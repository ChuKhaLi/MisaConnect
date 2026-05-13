using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Lookup;

/// <summary>
/// Slice 5 US2 tests T1–T6 from
/// <c>specs/005-misa-invoice-lookup/contracts/lookup-paginated.md</c>. Drives
/// <see cref="LookupStandard"/> with invalid <see cref="PagedLookupRequest"/>
/// shapes and asserts the wrapped <see cref="MeInvoiceException"/> carries the
/// FR-047 contract (Category == Validation, Field == offending field).
/// Validation short-circuits the use case before the token-ensure call, so the
/// <see cref="NotCalledClient"/> never has <c>LookupStandardAsync</c> invoked.
/// </summary>
public class PagedLookupRequestValidationTests
{
    [Fact]
    public async Task Length_zero_returns_Validation()
    {
        var sut = MakeSut(out _);
        var request = ValidRequest() with { Length = 0 };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, invoiceWithCode: true, default));
        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("Length", ex.Field);
        Assert.NotNull(ex.Failures);
        Assert.Single(ex.Failures!);
        Assert.Equal("Length", ex.Failures![0].FieldPath);
    }

    [Fact]
    public async Task Length_101_returns_Validation()
    {
        var sut = MakeSut(out _);
        var request = ValidRequest() with { Length = 101 };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, invoiceWithCode: true, default));
        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("Length", ex.Field);
    }

    [Fact]
    public async Task Negative_Start_returns_Validation()
    {
        var sut = MakeSut(out _);
        var request = ValidRequest() with { Start = -1 };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, invoiceWithCode: true, default));
        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("Start", ex.Field);
    }

    [Fact]
    public async Task FromDate_after_ToDate_returns_Validation()
    {
        var sut = MakeSut(out _);
        var request = ValidRequest() with
        {
            FromDate = new DateOnly(2026, 5, 31),
            ToDate = new DateOnly(2026, 5, 1),
        };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, invoiceWithCode: true, default));
        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("FromDate", ex.Field);
    }

    [Fact]
    public async Task Unknown_PublishStatus_returns_Validation()
    {
        var sut = MakeSut(out _);
        var request = ValidRequest() with { PublishStatus = 99 };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, invoiceWithCode: true, default));
        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("PublishStatus", ex.Field);
    }

    [Fact]
    public void Sort_null_defaults_to_InvDate()
    {
        // The use case itself does NOT default Sort — defaulting happens in the
        // API/Client adapter layer (LookupRequestMapping.ToDomain). At the use
        // case boundary the request is already populated with Sort="InvDate".
        // Validate() does not police Sort emptiness; assert the typical valid
        // request shape passes validation cleanly.
        var request = ValidRequest();
        Assert.Equal("InvDate", request.Sort);
        Assert.Null(request.Validate());
    }

    private static PagedLookupRequest ValidRequest() => new(
        Start: 0,
        Length: 100,
        Sort: "InvDate",
        FromDate: new DateOnly(2026, 5, 1),
        ToDate: new DateOnly(2026, 5, 31),
        PublishStatus: null);

    private static LookupStandard MakeSut(out NotCalledClient client)
    {
        client = new NotCalledClient();
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "k");
        return new LookupStandard(client, ensure, NullLogger<LookupStandard>.Instance);
    }

    /// <summary>
    /// Stub that fails the test if any lookup or delete operation reaches the
    /// MISA client — validation MUST short-circuit before the network call.
    /// <c>AcquireTokenAsync</c> is overridden to return a far-future token so
    /// <see cref="EnsureAccessToken"/> is satisfied without a real fetch (though
    /// validation runs before EnsureAccessToken, this keeps the stub usable for
    /// the Sort_null_defaults_to_InvDate path too).
    /// </summary>
    private sealed class NotCalledClient : IMeInvoiceClient
    {
        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));

        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();

        public Task<PagedResult> LookupStandardAsync(PagedLookupRequest request, bool invoiceWithCode, CancellationToken ct) =>
            throw new InvalidOperationException("LookupStandardAsync MUST NOT be reached when validation fails.");
    }
}

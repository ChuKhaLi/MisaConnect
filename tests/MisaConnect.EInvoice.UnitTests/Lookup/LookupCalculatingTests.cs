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
/// Spec contract test T14 plus the calculating-endpoint variants of T7–T13
/// from <c>specs/005-misa-invoice-lookup/contracts/lookup-paginated.md</c>.
/// Each test drives <see cref="LookupCalculating"/> with a configured
/// <see cref="MethodTrackingStub"/> and asserts the
/// <see cref="PagedResult"/> (or surfaced <see cref="MeInvoiceException"/>)
/// matches the FR-046/FR-047 contract row.
/// </summary>
public class LookupCalculatingTests
{
    [Fact]
    public async Task Targets_calculating_endpoint()
    {
        // T14 — invoking LookupCalculating MUST route to
        // IMeInvoiceClient.LookupCalculatingAsync and MUST NOT touch the
        // standard variant. This locks in the FR-046 disjoint-operation rule.
        var stub = new MethodTrackingStub(EmptyPage(NewRequest()));
        var sut = MakeSut(stub);

        _ = await sut.ExecuteAsync(NewRequest(), invoiceWithCode: true, default);

        Assert.True(stub.LookupCalculatingAsyncCalled);
        Assert.False(stub.LookupStandardAsyncCalled);
    }

    [Fact]
    public async Task Empty_page_returns_zero_returnedCount()
    {
        var stub = new MethodTrackingStub(
            new PagedResult(Array.Empty<InvoiceSnapshot>(), Start: 0, Length: 100, ReturnedCount: 0));
        var sut = MakeSut(stub);

        var result = await sut.ExecuteAsync(NewRequest(), invoiceWithCode: true, default);

        Assert.Equal(0, result.ReturnedCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Partial_page_returns_returnedCount_lt_Length()
    {
        var snapshots = MakeSnapshots(17);
        var stub = new MethodTrackingStub(
            new PagedResult(snapshots, Start: 0, Length: 100, ReturnedCount: 17));
        var sut = MakeSut(stub);

        var result = await sut.ExecuteAsync(NewRequest(), invoiceWithCode: true, default);

        Assert.Equal(17, result.ReturnedCount);
        Assert.True(result.ReturnedCount < 100);
    }

    [Fact]
    public async Task Full_page_returns_returnedCount_eq_Length()
    {
        var snapshots = MakeSnapshots(100);
        var stub = new MethodTrackingStub(
            new PagedResult(snapshots, Start: 0, Length: 100, ReturnedCount: 100));
        var sut = MakeSut(stub);

        var result = await sut.ExecuteAsync(NewRequest(), invoiceWithCode: true, default);

        Assert.Equal(100, result.ReturnedCount);
        Assert.Equal(100, result.Items.Count);
    }

    [Fact]
    public async Task AuthFailed_after_retry_exhausted_throws()
    {
        // T10 (calculating variant). The retry-exhausted AuthFailed bubbles
        // up unchanged from the port; the use case MUST NOT swallow it.
        var stub = new MethodTrackingStub(
            new MeInvoiceException(MeInvoiceErrorCategory.Authentication, "UnAuthorize"));
        var sut = MakeSut(stub);

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(
            () => sut.ExecuteAsync(NewRequest(), invoiceWithCode: true, default));

        Assert.Equal(MeInvoiceErrorCategory.Authentication, ex.Category);
    }

    [Fact]
    public async Task Length_zero_returns_Validation()
    {
        var stub = new MethodTrackingStub(EmptyPage(NewRequest()));
        var sut = MakeSut(stub);
        var request = NewRequest() with { Length = 0 };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(
            () => sut.ExecuteAsync(request, invoiceWithCode: true, default));

        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("Length", ex.Field);
        Assert.False(stub.LookupCalculatingAsyncCalled, "Validation must short-circuit before any MISA call.");
    }

    [Fact]
    public async Task Negative_Start_returns_Validation()
    {
        var stub = new MethodTrackingStub(EmptyPage(NewRequest()));
        var sut = MakeSut(stub);
        var request = NewRequest() with { Start = -1 };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(
            () => sut.ExecuteAsync(request, invoiceWithCode: true, default));

        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("Start", ex.Field);
        Assert.False(stub.LookupCalculatingAsyncCalled);
    }

    [Fact]
    public async Task Unknown_PublishStatus_returns_Validation()
    {
        var stub = new MethodTrackingStub(EmptyPage(NewRequest()));
        var sut = MakeSut(stub);
        var request = NewRequest() with { PublishStatus = 99 };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(
            () => sut.ExecuteAsync(request, invoiceWithCode: true, default));

        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("PublishStatus", ex.Field);
        Assert.False(stub.LookupCalculatingAsyncCalled);
    }

    [Fact]
    public async Task PublishStatus_filter_forwarded_to_MISA()
    {
        // T13 (calculating variant). The PublishStatus discriminator must
        // pass through the use case unchanged so MISA can apply the filter.
        var request = NewRequest() with { PublishStatus = 4 };
        var stub = new MethodTrackingStub(EmptyPage(request));
        var sut = MakeSut(stub);

        _ = await sut.ExecuteAsync(request, invoiceWithCode: true, default);

        Assert.NotNull(stub.CapturedRequest);
        Assert.Equal(4, stub.CapturedRequest!.PublishStatus);
    }

    // === helpers ===

    private static PagedLookupRequest NewRequest() =>
        new(Start: 0,
            Length: 100,
            Sort: "InvDate",
            FromDate: new DateOnly(2026, 5, 1),
            ToDate: new DateOnly(2026, 5, 31),
            PublishStatus: null);

    private static PagedResult EmptyPage(PagedLookupRequest request) =>
        new(Array.Empty<InvoiceSnapshot>(), request.Start, request.Length, 0);

    private static LookupCalculating MakeSut(MethodTrackingStub client)
    {
        var ensure = new EnsureAccessToken(
            client,
            new InMemoryTokenCache(),
            new SystemClock(TimeProvider.System),
            () => "k");
        return new LookupCalculating(client, ensure, NullLogger<LookupCalculating>.Instance);
    }

    private static IReadOnlyList<InvoiceSnapshot> MakeSnapshots(int count)
    {
        var list = new List<InvoiceSnapshot>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new InvoiceSnapshot(
                RefId: RefId.From($"r{i}"),
                InvoiceTemplateID: null, InvSeries: null, InvDate: null, InvNo: null,
                AccountObjectTaxCode: null, AccountObjectName: null,
                TotalSaleAmount: null, TotalVATAmount: null, TotalAmount: null,
                TotalSaleAmountOC: null, TotalVATAmountOC: null, TotalAmountOC: null,
                RawEInvoiceStatus: 1, RawPublishStatus: 0, Status: InvoiceStatus.Draft,
                OrgRefID: null, CreatedDate: null, ModifiedDate: null));
        }
        return list;
    }

    /// <summary>
    /// Stub that tracks which of the two paged-lookup port methods was
    /// invoked and captures the inbound request for cross-checking. Uses the
    /// default-interface-method behaviour of <see cref="IMeInvoiceClient"/>
    /// for the unused operations (NotImplementedException).
    /// </summary>
    private sealed class MethodTrackingStub : IMeInvoiceClient
    {
        private readonly PagedResult? _calculatingResponse;
        private readonly MeInvoiceException? _calculatingException;

        public bool LookupCalculatingAsyncCalled { get; private set; }
        public bool LookupStandardAsyncCalled { get; private set; }
        public PagedLookupRequest? CapturedRequest { get; private set; }

        public MethodTrackingStub(PagedResult response)
        {
            _calculatingResponse = response;
        }

        public MethodTrackingStub(MeInvoiceException exception)
        {
            _calculatingException = exception;
        }

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("stub-token", DateTimeOffset.UtcNow.AddDays(1)));

        // Slice 1/2/4 operations are not exercised by LookupCalculating; the
        // interface doesn't provide default implementations for them, so we
        // throw NotImplementedException to make any accidental invocation
        // loudly visible during a test run.
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<PagedResult> LookupStandardAsync(
            PagedLookupRequest request,
            bool invoiceWithCode,
            CancellationToken ct)
        {
            LookupStandardAsyncCalled = true;
            return Task.FromResult(new PagedResult(Array.Empty<InvoiceSnapshot>(), request.Start, request.Length, 0));
        }

        public Task<PagedResult> LookupCalculatingAsync(
            PagedLookupRequest request,
            bool invoiceWithCode,
            CancellationToken ct)
        {
            LookupCalculatingAsyncCalled = true;
            CapturedRequest = request;
            if (_calculatingException is not null) throw _calculatingException;
            return Task.FromResult(_calculatingResponse!);
        }
    }
}

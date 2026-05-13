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
/// Slice 5 US2 tests T7–T13 + T15 from
/// <c>specs/005-misa-invoice-lookup/contracts/lookup-paginated.md</c>. Drives
/// <see cref="LookupStandard"/> with stubbed <see cref="IMeInvoiceClient"/>
/// responses to assert ReturnedCount semantics (R-LU-04), retry-exhaustion
/// exception re-throw, filter forwarding, and endpoint routing (FR-046).
/// </summary>
public class LookupStandardTests
{
    [Fact]
    public async Task Empty_page_returns_zero_returnedCount()
    {
        var stub = new CapturingStub(new PagedResult(Array.Empty<InvoiceSnapshot>(), Start: 0, Length: 100, ReturnedCount: 0));
        var sut = MakeSut(stub);

        var result = await sut.ExecuteAsync(NewRequest(length: 100), invoiceWithCode: true, default);

        Assert.Equal(0, result.ReturnedCount);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Start);
        Assert.Equal(100, result.Length);
    }

    [Fact]
    public async Task Partial_page_returns_returnedCount_lt_Length()
    {
        var snapshots = Enumerable.Range(0, 17).Select(i => NewSnapshot($"r{i:D2}")).ToArray();
        var stub = new CapturingStub(new PagedResult(snapshots, Start: 0, Length: 100, ReturnedCount: 17));
        var sut = MakeSut(stub);

        var result = await sut.ExecuteAsync(NewRequest(length: 100), invoiceWithCode: true, default);

        Assert.Equal(17, result.ReturnedCount);
        Assert.Equal(17, result.Items.Count);
        Assert.True(result.ReturnedCount < result.Length);
    }

    [Fact]
    public async Task Full_page_returns_returnedCount_eq_Length()
    {
        var snapshots = Enumerable.Range(0, 100).Select(i => NewSnapshot($"r{i:D3}")).ToArray();
        var stub = new CapturingStub(new PagedResult(snapshots, Start: 0, Length: 100, ReturnedCount: 100));
        var sut = MakeSut(stub);

        var result = await sut.ExecuteAsync(NewRequest(length: 100), invoiceWithCode: true, default);

        Assert.Equal(100, result.ReturnedCount);
        Assert.Equal(result.Length, result.ReturnedCount);
    }

    [Fact]
    public async Task AuthFailed_after_retry_exhausted_throws()
    {
        var stub = new CapturingStub(new MeInvoiceException(MeInvoiceErrorCategory.Authentication, "UnAuthorize"));
        var sut = MakeSut(stub);

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(NewRequest(), invoiceWithCode: true, default));
        Assert.Equal(MeInvoiceErrorCategory.Authentication, ex.Category);
        Assert.Equal("UnAuthorize", ex.RawErrorCode);
    }

    [Fact]
    public async Task MisaThrottled_after_retry_exhausted_throws()
    {
        var stub = new CapturingStub(new MeInvoiceException(MeInvoiceErrorCategory.MisaThrottled, "Throttled"));
        var sut = MakeSut(stub);

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(NewRequest(), invoiceWithCode: true, default));
        Assert.Equal(MeInvoiceErrorCategory.MisaThrottled, ex.Category);
    }

    [Fact]
    public async Task TransportFailed_after_retry_exhausted_throws()
    {
        var stub = new CapturingStub(new MeInvoiceException(MeInvoiceErrorCategory.TransportFailed, "ConnectionRefused"));
        var sut = MakeSut(stub);

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(NewRequest(), invoiceWithCode: true, default));
        Assert.Equal(MeInvoiceErrorCategory.TransportFailed, ex.Category);
    }

    [Fact]
    public async Task PublishStatus_filter_forwarded_to_MISA()
    {
        var stub = new CapturingStub(new PagedResult(Array.Empty<InvoiceSnapshot>(), 0, 100, 0));
        var sut = MakeSut(stub);

        await sut.ExecuteAsync(NewRequest(publishStatus: 0), invoiceWithCode: true, default);

        Assert.NotNull(stub.LastRequest);
        Assert.Equal(0, stub.LastRequest!.PublishStatus);
    }

    [Fact]
    public async Task Targets_standard_endpoint()
    {
        var stub = new MethodTrackingStub();
        var sut = MakeSut(stub);

        await sut.ExecuteAsync(NewRequest(), invoiceWithCode: true, default);

        Assert.Equal(1, stub.StandardCalls);
        Assert.Equal(0, stub.CalculatingCalls);
    }

    private static PagedLookupRequest NewRequest(int start = 0, int length = 100, int? publishStatus = null) => new(
        Start: start,
        Length: length,
        Sort: "InvDate",
        FromDate: new DateOnly(2026, 5, 1),
        ToDate: new DateOnly(2026, 5, 31),
        PublishStatus: publishStatus);

    private static InvoiceSnapshot NewSnapshot(string refIdValue) => new(
        RefId: RefId.From(refIdValue),
        InvoiceTemplateID: null,
        InvSeries: null,
        InvDate: null,
        InvNo: null,
        AccountObjectTaxCode: null,
        AccountObjectName: null,
        TotalSaleAmount: null,
        TotalVATAmount: null,
        TotalAmount: null,
        TotalSaleAmountOC: null,
        TotalVATAmountOC: null,
        TotalAmountOC: null,
        RawEInvoiceStatus: 1,
        RawPublishStatus: 0,
        Status: InvoiceStatus.Draft,
        OrgRefID: null,
        CreatedDate: null,
        ModifiedDate: null);

    private static LookupStandard MakeSut(IMeInvoiceClient client)
    {
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "k");
        return new LookupStandard(client, ensure, NullLogger<LookupStandard>.Instance);
    }

    private sealed class CapturingStub : IMeInvoiceClient
    {
        private readonly PagedResult? _result;
        private readonly MeInvoiceException? _exception;

        public PagedLookupRequest? LastRequest { get; private set; }

        public CapturingStub(PagedResult result) => _result = result;
        public CapturingStub(MeInvoiceException exception) => _exception = exception;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));

        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();

        public Task<PagedResult> LookupStandardAsync(PagedLookupRequest request, bool invoiceWithCode, CancellationToken ct)
        {
            LastRequest = request;
            if (_exception is not null) throw _exception;
            return Task.FromResult(_result!);
        }
    }

    private sealed class MethodTrackingStub : IMeInvoiceClient
    {
        public int StandardCalls { get; private set; }
        public int CalculatingCalls { get; private set; }

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));

        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();

        public Task<PagedResult> LookupStandardAsync(PagedLookupRequest request, bool invoiceWithCode, CancellationToken ct)
        {
            StandardCalls++;
            return Task.FromResult(new PagedResult(Array.Empty<InvoiceSnapshot>(), request.Start, request.Length, 0));
        }

        public Task<PagedResult> LookupCalculatingAsync(PagedLookupRequest request, bool invoiceWithCode, CancellationToken ct)
        {
            CalculatingCalls++;
            return Task.FromResult(new PagedResult(Array.Empty<InvoiceSnapshot>(), request.Start, request.Length, 0));
        }
    }
}

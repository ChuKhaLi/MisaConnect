using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Lookup;

/// <summary>
/// Spec contract tests T1–T5, T15–T16 from
/// <c>contracts/lookup-by-refid.md</c>. Drives <see cref="LookupByRefIds"/>
/// with stubbed <see cref="IMeInvoiceClient"/> responses and asserts the
/// per-RefID outcomes match FR-042 / FR-044 / FR-047 / FR-050 semantics.
/// </summary>
public class LookupByRefIdsTests
{
    [Fact]
    public async Task Empty_list_returns_Validation_outcome()
    {
        var stub = new StubClient();
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(Array.Empty<RefId>(), InvoiceWithCode: true),
            default);

        Assert.Equal(LookupBatchStatus.Validation, outcome.Status);
        Assert.Null(outcome.Outcomes);
        Assert.Equal(0, stub.LookupCalls);
    }

    [Fact]
    public async Task Single_chunk_returns_Found_per_RefID()
    {
        var a = RefId.From("a");
        var b = RefId.From("b");
        var c = RefId.From("c");
        var stub = new StubClient(refIds => new LookupByRefIdChunkResult(
            new[] { Snapshot(a), Snapshot(b), Snapshot(c) },
            ErrorCode: null,
            ErrorMessage: null));
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(new[] { a, b, c }, InvoiceWithCode: true),
            default);

        Assert.Equal(LookupBatchStatus.Completed, outcome.Status);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(3, outcome.Outcomes!.Count);
        Assert.All(outcome.Outcomes, o => Assert.Equal(LookupStatus.Found, o.Status));
        Assert.Equal("a", outcome.Outcomes[0].RefId.Value);
        Assert.Equal("b", outcome.Outcomes[1].RefId.Value);
        Assert.Equal("c", outcome.Outcomes[2].RefId.Value);
        Assert.All(outcome.Outcomes, o => Assert.NotNull(o.Snapshot));
    }

    [Fact]
    public async Task RefID_missing_from_response_returns_NotFound()
    {
        var a = RefId.From("a");
        var b = RefId.From("b");
        var c = RefId.From("c");
        // MISA returns only A and C — B should surface as NotFound in slot 1.
        var stub = new StubClient(refIds => new LookupByRefIdChunkResult(
            new[] { Snapshot(a), Snapshot(c) },
            ErrorCode: null,
            ErrorMessage: null));
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(new[] { a, b, c }, InvoiceWithCode: true),
            default);

        Assert.Equal(LookupBatchStatus.Completed, outcome.Status);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(3, outcome.Outcomes!.Count);
        Assert.Equal(LookupStatus.Found, outcome.Outcomes[0].Status);
        Assert.Equal(LookupStatus.NotFound, outcome.Outcomes[1].Status);
        Assert.Equal("b", outcome.Outcomes[1].RefId.Value);
        Assert.Null(outcome.Outcomes[1].Snapshot);
        Assert.Equal(LookupStatus.Found, outcome.Outcomes[2].Status);
    }

    [Fact]
    public async Task Order_preserved_when_MISA_returns_out_of_order()
    {
        var a = RefId.From("a");
        var b = RefId.From("b");
        var c = RefId.From("c");
        // MISA returns snapshots in [C, A, B] order; use case must reorder by input.
        var stub = new StubClient(refIds => new LookupByRefIdChunkResult(
            new[] { Snapshot(c), Snapshot(a), Snapshot(b) },
            ErrorCode: null,
            ErrorMessage: null));
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(new[] { a, b, c }, InvoiceWithCode: true),
            default);

        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(3, outcome.Outcomes!.Count);
        Assert.Equal("a", outcome.Outcomes[0].RefId.Value);
        Assert.Equal("b", outcome.Outcomes[1].RefId.Value);
        Assert.Equal("c", outcome.Outcomes[2].RefId.Value);
        Assert.All(outcome.Outcomes, o => Assert.Equal(LookupStatus.Found, o.Status));
    }

    [Fact]
    public async Task AuthFailed_after_token_refresh_exhausted()
    {
        var a = RefId.From("a");
        var b = RefId.From("b");
        var stub = new StubClient(
            new MeInvoiceException(MeInvoiceErrorCategory.Authentication, "UnAuthorize"));
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(new[] { a, b }, InvoiceWithCode: true),
            default);

        Assert.Equal(LookupBatchStatus.Completed, outcome.Status);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(2, outcome.Outcomes!.Count);
        Assert.All(outcome.Outcomes, o =>
        {
            Assert.Equal(LookupStatus.AuthFailed, o.Status);
            Assert.Equal("UnAuthorize", o.RawErrorCode);
            Assert.Null(o.Snapshot);
        });
    }

    [Fact]
    public async Task MisaUnknown_for_unmapped_errorCode_per_RefID()
    {
        var a = RefId.From("a");
        var b = RefId.From("b");
        var stub = new StubClient(refIds => new LookupByRefIdChunkResult(
            Array.Empty<InvoiceSnapshot>(),
            ErrorCode: "FuturePagingError",
            ErrorMessage: null));
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(new[] { a, b }, InvoiceWithCode: true),
            default);

        Assert.Equal(LookupBatchStatus.Completed, outcome.Status);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(2, outcome.Outcomes!.Count);
        Assert.All(outcome.Outcomes, o =>
        {
            Assert.Equal(LookupStatus.MisaUnknown, o.Status);
            Assert.Equal("FuturePagingError", o.RawErrorCode);
            Assert.Null(o.Snapshot);
        });
    }

    private static LookupByRefIds MakeSut(StubClient client)
    {
        var ensure = new EnsureAccessToken(
            client,
            new InMemoryTokenCache(),
            new SystemClock(TimeProvider.System),
            () => "k");
        return new LookupByRefIds(
            client,
            ensure,
            new StubDeleteOptions(includeRawErrorMessage: false),
            NullLogger<LookupByRefIds>.Instance);
    }

    private static InvoiceSnapshot Snapshot(RefId refId) => new(
        RefId: refId,
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

    private sealed class StubDeleteOptions : IDeleteOptionsAccessor
    {
        public StubDeleteOptions(bool includeRawErrorMessage)
            => IncludeRawErrorMessage = includeRawErrorMessage;
        public bool IncludeRawErrorMessage { get; }
    }

    private sealed class StubClient : IMeInvoiceClient
    {
        private readonly Func<IReadOnlyList<RefId>, LookupByRefIdChunkResult>? _factory;
        private readonly MeInvoiceException? _exception;

        public int LookupCalls { get; private set; }

        public StubClient() { }
        public StubClient(Func<IReadOnlyList<RefId>, LookupByRefIdChunkResult> factory)
            => _factory = factory;
        public StubClient(MeInvoiceException exception) => _exception = exception;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct)
            => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();

        public Task<LookupByRefIdChunkResult> LookupByRefIdAsync(
            IReadOnlyList<RefId> refIds,
            bool invoiceWithCode,
            CancellationToken ct)
        {
            LookupCalls++;
            if (_exception is not null) throw _exception;
            if (_factory is not null) return Task.FromResult(_factory(refIds));
            throw new InvalidOperationException("StubClient was not configured for a lookup response.");
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Lookup;

/// <summary>
/// Spec contract tests T6–T8 from <c>contracts/lookup-by-refid.md</c> —
/// FR-044 transparent chunking at MISA's 50-RefID per-call cap. Asserts
/// chunk count and order-preservation across the cardinal boundaries.
/// </summary>
public class LookupByRefIdChunkingTests
{
    [Fact]
    public async Task Exactly_50_RefIDs_uses_one_chunk()
    {
        var input = MakeRefIds(50);
        var stub = new EchoStubClient();
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(input, InvoiceWithCode: true),
            default);

        Assert.Equal(1, stub.LookupCalls);
        Assert.Equal(LookupBatchStatus.Completed, outcome.Status);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(50, outcome.Outcomes!.Count);
        Assert.All(outcome.Outcomes, o => Assert.Equal(LookupStatus.Found, o.Status));
    }

    [Fact]
    public async Task Exactly_51_RefIDs_uses_two_chunks()
    {
        var input = MakeRefIds(51);
        var stub = new EchoStubClient();
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(input, InvoiceWithCode: true),
            default);

        Assert.Equal(2, stub.LookupCalls);
        Assert.Equal(50, stub.ChunkSizes[0]);
        Assert.Equal(1, stub.ChunkSizes[1]);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(51, outcome.Outcomes!.Count);
        Assert.All(outcome.Outcomes, o => Assert.Equal(LookupStatus.Found, o.Status));
    }

    [Fact]
    public async Task Hundred_thirty_seven_RefIDs_uses_three_chunks()
    {
        var input = MakeRefIds(137);
        var stub = new EchoStubClient();
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(input, InvoiceWithCode: true),
            default);

        Assert.Equal(3, stub.LookupCalls);
        Assert.Equal(50, stub.ChunkSizes[0]);
        Assert.Equal(50, stub.ChunkSizes[1]);
        Assert.Equal(37, stub.ChunkSizes[2]);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(137, outcome.Outcomes!.Count);

        // Order-preservation across chunk boundaries.
        for (var i = 0; i < input.Count; i++)
        {
            Assert.Equal(input[i].Value, outcome.Outcomes![i].RefId.Value);
            Assert.Equal(LookupStatus.Found, outcome.Outcomes![i].Status);
        }
    }

    private static IReadOnlyList<RefId> MakeRefIds(int count)
    {
        var list = new List<RefId>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(RefId.From($"r-{i:D4}"));
        }
        return list;
    }

    private static LookupByRefIds MakeSut(EchoStubClient client)
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

    private static InvoiceSnapshot MakeSnapshot(RefId refId) => new(
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

    private sealed class EchoStubClient : IMeInvoiceClient
    {
        public int LookupCalls { get; private set; }
        public List<int> ChunkSizes { get; } = new();

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
            ChunkSizes.Add(refIds.Count);
            var snapshots = refIds.Select(MakeSnapshot).ToArray();
            return Task.FromResult(new LookupByRefIdChunkResult(snapshots, null, null));
        }
    }
}

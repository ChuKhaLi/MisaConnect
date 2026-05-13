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
/// Spec contract tests T9–T14 from <c>contracts/lookup-by-refid.md</c> —
/// FR-044 cascade rule (R-LU-03):
/// <list type="bullet">
///   <item>Terminal categories (<see cref="LookupStatus.AuthFailed"/>,
///   <see cref="LookupStatus.Configuration"/>) short-circuit remaining
///   chunks — the per-chunk failure category is spread to every RefID in
///   every subsequent chunk.</item>
///   <item>Transient categories (<see cref="LookupStatus.MisaThrottled"/>,
///   <see cref="LookupStatus.MisaUnavailable"/>,
///   <see cref="LookupStatus.TransportFailed"/>,
///   <see cref="LookupStatus.MisaUnknown"/>) continue with remaining chunks,
///   each with a fresh retry budget.</item>
/// </list>
/// </summary>
public class LookupByRefIdCascadeTests
{
    [Fact]
    public async Task AuthFailed_short_circuits()
    {
        var input = MakeRefIds(137); // 50 + 50 + 37 = 3 chunks
        var stub = new CascadeStubClient((idx, refIds) =>
        {
            if (idx == 1) return SuccessForChunk(refIds);
            if (idx == 2) return new LookupByRefIdChunkResult(
                Array.Empty<InvoiceSnapshot>(),
                ErrorCode: "UnAuthorize",
                ErrorMessage: null);
            // chunk 3 should never be called
            throw new InvalidOperationException("Chunk 3 should be short-circuited by AuthFailed.");
        });
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(input, InvoiceWithCode: true),
            default);

        Assert.Equal(2, stub.LookupCalls);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(137, outcome.Outcomes!.Count);
        // First 50 RefIDs surface as Found.
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(LookupStatus.Found, outcome.Outcomes![i].Status);
        }
        // Remaining 87 (50 from chunk 2 + 37 from short-circuited chunk 3) surface as AuthFailed.
        for (var i = 50; i < 137; i++)
        {
            Assert.Equal(LookupStatus.AuthFailed, outcome.Outcomes![i].Status);
            Assert.Equal("UnAuthorize", outcome.Outcomes![i].RawErrorCode);
            Assert.Null(outcome.Outcomes![i].Snapshot);
            // Order preservation: each error outcome carries the caller's input RefID.
            Assert.Equal(input[i].Value, outcome.Outcomes![i].RefId.Value);
        }
    }

    [Fact]
    public async Task Configuration_short_circuits()
    {
        var input = MakeRefIds(137);
        var stub = new CascadeStubClient((idx, refIds) =>
        {
            if (idx == 1) return SuccessForChunk(refIds);
            if (idx == 2) return new LookupByRefIdChunkResult(
                Array.Empty<InvoiceSnapshot>(),
                ErrorCode: "InvalidAppID",
                ErrorMessage: null);
            throw new InvalidOperationException("Chunk 3 should be short-circuited by Configuration.");
        });
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(input, InvoiceWithCode: true),
            default);

        Assert.Equal(2, stub.LookupCalls);
        Assert.NotNull(outcome.Outcomes);
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(LookupStatus.Found, outcome.Outcomes![i].Status);
        }
        for (var i = 50; i < 137; i++)
        {
            Assert.Equal(LookupStatus.Configuration, outcome.Outcomes![i].Status);
            Assert.Equal("InvalidAppID", outcome.Outcomes![i].RawErrorCode);
        }
    }

    [Fact]
    public async Task MisaThrottled_continues()
    {
        var input = MakeRefIds(137);
        var stub = new CascadeStubClient((idx, refIds) =>
        {
            if (idx == 1) return SuccessForChunk(refIds);
            if (idx == 2) throw new MeInvoiceException(MeInvoiceErrorCategory.MisaThrottled, "Throttled");
            if (idx == 3) return SuccessForChunk(refIds);
            throw new InvalidOperationException($"Unexpected chunk index {idx}.");
        });
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(input, InvoiceWithCode: true),
            default);

        Assert.Equal(3, stub.LookupCalls); // chunk 3 IS called
        Assert.NotNull(outcome.Outcomes);
        // chunks 1 and 3 succeed; chunk 2 RefIDs surface MisaThrottled.
        for (var i = 0; i < 50; i++)
            Assert.Equal(LookupStatus.Found, outcome.Outcomes![i].Status);
        for (var i = 50; i < 100; i++)
        {
            Assert.Equal(LookupStatus.MisaThrottled, outcome.Outcomes![i].Status);
            Assert.Equal("Throttled", outcome.Outcomes![i].RawErrorCode);
        }
        for (var i = 100; i < 137; i++)
            Assert.Equal(LookupStatus.Found, outcome.Outcomes![i].Status);
    }

    [Fact]
    public async Task MisaUnavailable_continues()
    {
        var input = MakeRefIds(137);
        var stub = new CascadeStubClient((idx, refIds) =>
        {
            if (idx == 1) return SuccessForChunk(refIds);
            if (idx == 2) throw new MeInvoiceException(MeInvoiceErrorCategory.MisaUnavailable, "ServiceUnavailable");
            if (idx == 3) return SuccessForChunk(refIds);
            throw new InvalidOperationException($"Unexpected chunk index {idx}.");
        });
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(input, InvoiceWithCode: true),
            default);

        Assert.Equal(3, stub.LookupCalls);
        Assert.NotNull(outcome.Outcomes);
        for (var i = 50; i < 100; i++)
        {
            Assert.Equal(LookupStatus.MisaUnavailable, outcome.Outcomes![i].Status);
            Assert.Equal("ServiceUnavailable", outcome.Outcomes![i].RawErrorCode);
        }
        for (var i = 100; i < 137; i++)
            Assert.Equal(LookupStatus.Found, outcome.Outcomes![i].Status);
    }

    [Fact]
    public async Task TransportFailed_continues()
    {
        var input = MakeRefIds(137);
        var stub = new CascadeStubClient((idx, refIds) =>
        {
            if (idx == 1) return SuccessForChunk(refIds);
            if (idx == 2) throw new MeInvoiceException(MeInvoiceErrorCategory.TransportFailed, "ConnectionRefused");
            if (idx == 3) return SuccessForChunk(refIds);
            throw new InvalidOperationException($"Unexpected chunk index {idx}.");
        });
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(input, InvoiceWithCode: true),
            default);

        Assert.Equal(3, stub.LookupCalls);
        Assert.NotNull(outcome.Outcomes);
        for (var i = 50; i < 100; i++)
        {
            Assert.Equal(LookupStatus.TransportFailed, outcome.Outcomes![i].Status);
            Assert.Equal("ConnectionRefused", outcome.Outcomes![i].RawErrorCode);
        }
        for (var i = 100; i < 137; i++)
            Assert.Equal(LookupStatus.Found, outcome.Outcomes![i].Status);
    }

    [Fact]
    public async Task MisaUnknown_continues()
    {
        var input = MakeRefIds(137);
        var stub = new CascadeStubClient((idx, refIds) =>
        {
            if (idx == 1) return SuccessForChunk(refIds);
            if (idx == 2) return new LookupByRefIdChunkResult(
                Array.Empty<InvoiceSnapshot>(),
                ErrorCode: "FuturePagingError",
                ErrorMessage: null);
            if (idx == 3) return SuccessForChunk(refIds);
            throw new InvalidOperationException($"Unexpected chunk index {idx}.");
        });
        var sut = MakeSut(stub);

        var outcome = await sut.ExecuteAsync(
            new LookupByRefIdRequest(input, InvoiceWithCode: true),
            default);

        Assert.Equal(3, stub.LookupCalls);
        Assert.NotNull(outcome.Outcomes);
        for (var i = 50; i < 100; i++)
        {
            Assert.Equal(LookupStatus.MisaUnknown, outcome.Outcomes![i].Status);
            Assert.Equal("FuturePagingError", outcome.Outcomes![i].RawErrorCode);
        }
        for (var i = 100; i < 137; i++)
            Assert.Equal(LookupStatus.Found, outcome.Outcomes![i].Status);
    }

    private static LookupByRefIdChunkResult SuccessForChunk(IReadOnlyList<RefId> chunk)
    {
        var snapshots = chunk.Select(MakeSnapshot).ToArray();
        return new LookupByRefIdChunkResult(snapshots, null, null);
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

    private static LookupByRefIds MakeSut(CascadeStubClient client)
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

    private sealed class CascadeStubClient : IMeInvoiceClient
    {
        private readonly Func<int, IReadOnlyList<RefId>, LookupByRefIdChunkResult> _factory;
        public int LookupCalls { get; private set; }

        public CascadeStubClient(Func<int, IReadOnlyList<RefId>, LookupByRefIdChunkResult> factory)
            => _factory = factory;

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
            // Capture index BEFORE calling _factory so a throw is attributed correctly.
            var idx = LookupCalls;
            return Task.FromResult(_factory(idx, refIds));
        }
    }
}

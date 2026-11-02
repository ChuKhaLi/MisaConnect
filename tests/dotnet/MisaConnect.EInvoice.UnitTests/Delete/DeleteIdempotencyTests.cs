using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Delete;

/// <summary>
/// Slice 2 T044 (test T15) — FR-037 idempotency: a second
/// <c>DeleteDraftAsync</c> invocation on the same <c>RefID</c> must NOT throw
/// and must NOT produce a duplicate side effect. Sequence the mock to return
/// success on the first call, then <c>InvalidTransactionID + không tồn tại</c>
/// on the second (the spec's expected MISA behaviour); assert the second
/// outcome is <c>NotFound</c>.
/// </summary>
public class DeleteIdempotencyTests
{
    [Fact]
    public async Task Second_delete_returns_NotFound()
    {
        var stub = new SequencedStubClient(new[]
        {
            new DeleteResponse(Success: true, ErrorCode: null, ErrorMessage: null),
            new DeleteResponse(Success: false, ErrorCode: "InvalidTransactionID", ErrorMessage: "RefID không tồn tại."),
        });
        var sut = MakeSut(stub);

        var first = await sut.ExecuteAsync(NewRequest(), default);
        var second = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.Deleted, first.Status);
        Assert.Equal(DeleteDraftStatus.NotFound, second.Status);
        Assert.Equal("InvalidTransactionID", second.RawErrorCode);
    }

    private static DeleteDraftRequest NewRequest() =>
        new(RefId.From("idemp-1"), InvoiceWithCode: true);

    private static DeleteDraftInvoice MakeSut(SequencedStubClient client)
    {
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "k");
        return new DeleteDraftInvoice(
            client,
            ensure,
            new InvalidTransactionDisambiguator(),
            new StubDeleteOptions(false),
            NullLogger<DeleteDraftInvoice>.Instance);
    }

    private sealed class StubDeleteOptions : IDeleteOptionsAccessor
    {
        public StubDeleteOptions(bool include) => IncludeRawErrorMessage = include;
        public bool IncludeRawErrorMessage { get; }
    }

    private sealed class SequencedStubClient : IMeInvoiceClient
    {
        private readonly Queue<DeleteResponse> _queue;

        public SequencedStubClient(IEnumerable<DeleteResponse> responses) => _queue = new Queue<DeleteResponse>(responses);

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct)
        {
            return Task.FromResult(_queue.Dequeue());
        }
    }
}

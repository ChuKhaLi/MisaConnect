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
/// Slice 2 T063 — verifies <see cref="IDeleteOptionsAccessor.IncludeRawErrorMessage"/>
/// gates whether MISA's raw <c>ErrorMessage</c> text flows into the caller-facing
/// <see cref="DeleteDraftOutcome.Message"/> field.
/// </summary>
public class DeleteOptionsFlagTests
{
    [Fact]
    public async Task Default_strips_message_from_outcome()
    {
        var stub = new StubClient(new DeleteResponse(false, "InvalidTransactionID", "Hóa đơn đã phát hành nên không thể xóa."));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.NotDeletable, outcome.Status);
        Assert.NotNull(outcome.RawErrorCode);
        Assert.Null(outcome.Message);
    }

    [Fact]
    public async Task Flag_on_includes_message_in_outcome()
    {
        var stub = new StubClient(new DeleteResponse(false, "InvalidTransactionID", "Hóa đơn đã phát hành nên không thể xóa."));
        var sut = MakeSut(stub, includeMessage: true);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.NotDeletable, outcome.Status);
        Assert.Equal("Hóa đơn đã phát hành nên không thể xóa.", outcome.Message);
    }

    [Fact]
    public async Task Flag_off_strips_message_on_MisaUnknown_too()
    {
        var stub = new StubClient(new DeleteResponse(false, "NewlyDiscoveredCode", "Something detailed."));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.MisaUnknown, outcome.Status);
        Assert.Null(outcome.Message);
    }

    [Fact]
    public async Task Flag_on_includes_message_on_MisaUnknown()
    {
        var stub = new StubClient(new DeleteResponse(false, "NewlyDiscoveredCode", "Something detailed."));
        var sut = MakeSut(stub, includeMessage: true);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.MisaUnknown, outcome.Status);
        Assert.Equal("Something detailed.", outcome.Message);
    }

    private static DeleteDraftRequest NewRequest() => new(RefId.From("r1"), InvoiceWithCode: true);

    private static DeleteDraftInvoice MakeSut(StubClient client, bool includeMessage)
    {
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "k");
        return new DeleteDraftInvoice(
            client,
            ensure,
            new InvalidTransactionDisambiguator(),
            new StubDeleteOptions(includeMessage),
            NullLogger<DeleteDraftInvoice>.Instance);
    }

    private sealed class StubDeleteOptions : IDeleteOptionsAccessor
    {
        public StubDeleteOptions(bool include) => IncludeRawErrorMessage = include;
        public bool IncludeRawErrorMessage { get; }
    }

    private sealed class StubClient : IMeInvoiceClient
    {
        private readonly DeleteResponse _response;
        public StubClient(DeleteResponse r) => _response = r;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => Task.FromResult(_response);
    }
}

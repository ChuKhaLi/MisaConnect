using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.UseCases;

public class ListTicketTemplatesTests
{
    private static readonly TicketTemplate Active = new("tt-1", "Vé xe", "5C26GAA", true, 1, 5, "Vận tải", false, true);
    private static readonly TicketTemplate Inactive = new("tt-2", "Vé cũ", "5C25GAA", false, 1, 5, null, true, false);

    [Fact]
    public async Task Validation_rejects_negative_start()
    {
        var stub = new Stub([Active]);
        var sut = MakeSut(stub);

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() =>
            sut.ExecuteAsync(new TicketTemplateListRequest(Start: -1), default));

        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("Start", ex.Field);
        Assert.Equal(0, stub.TokenCalls);
        Assert.Null(stub.LastRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Validation_rejects_length_out_of_range(int length)
    {
        var stub = new Stub([Active]);
        var sut = MakeSut(stub);

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() =>
            sut.ExecuteAsync(new TicketTemplateListRequest(Length: length), default));

        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("Length", ex.Field);
        Assert.Null(stub.LastRequest);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 100)]
    public async Task Validation_accepts_bounds(int start, int length)
    {
        var stub = new Stub([Active]);
        var sut = MakeSut(stub);
        var request = new TicketTemplateListRequest(InvoiceWithCode: false, Start: start, Length: length, FromCashRegister: true);

        await sut.ExecuteAsync(request, default);

        Assert.Equal(request, stub.LastRequest);
    }

    [Fact]
    public void Request_defaults_match_MISA_docs()
    {
        var r = new TicketTemplateListRequest();
        Assert.True(r.InvoiceWithCode);
        Assert.Equal(0, r.Start);
        Assert.Equal(20, r.Length);
        Assert.False(r.FromCashRegister);
        Assert.Null(r.Validate());
    }

    [Fact]
    public async Task Returns_rows_unfiltered_including_inactive()
    {
        var sut = MakeSut(new Stub([Active, Inactive]));

        var result = await sut.ExecuteAsync(new TicketTemplateListRequest(), default);

        Assert.Equal(["tt-1", "tt-2"], result.Select(t => t.TicketTemplateID));
    }

    [Fact]
    public async Task Ensures_token_before_calling_client()
    {
        var stub = new Stub([Active]);
        var sut = MakeSut(stub);

        await sut.ExecuteAsync(new TicketTemplateListRequest(), default);

        Assert.Equal(1, stub.TokenCalls);
        Assert.True(stub.TokenBeforeList);
    }

    [Fact]
    public async Task Port_without_override_throws_NotImplemented()
    {
        var sut = MakeSut(new LegacyStub());

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            sut.ExecuteAsync(new TicketTemplateListRequest(), default));
    }

    private static ListTicketTemplates MakeSut(IMeInvoiceClient client)
    {
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(),
            new SystemClock(TimeProvider.System), () => "key");
        return new ListTicketTemplates(client, ensure);
    }

    private class LegacyStub : IMeInvoiceClient
    {
        public int TokenCalls { get; private set; }

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct)
        {
            TokenCalls++;
            return Task.FromResult(new AccessToken("tok", DateTimeOffset.UtcNow.AddDays(1)));
        }

        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class Stub : LegacyStub, IMeInvoiceClient
    {
        private readonly IReadOnlyList<TicketTemplate> _rows;
        public Stub(IReadOnlyList<TicketTemplate> rows) => _rows = rows;

        public TicketTemplateListRequest? LastRequest { get; private set; }
        public bool TokenBeforeList { get; private set; }

        public Task<IReadOnlyList<TicketTemplate>> ListTicketTemplatesAsync(TicketTemplateListRequest request, CancellationToken ct)
        {
            TokenBeforeList = TokenCalls > 0;
            LastRequest = request;
            return Task.FromResult(_rows);
        }
    }
}

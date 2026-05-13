using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Application.Validation;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using MisaConnect.EInvoice.TestSupport.Fixtures;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Amendments;

public class IssueReplacementInvoiceTests
{
    private static readonly Template DefaultTemplate = new("ipt-1", "1C25MNQ", "Default", null, 1, true, true, false);

    private static readonly OriginalInvoiceReference ValidOriginalRef = new(
        OrgRefID: "orig-001",
        OrgInvNo: "00000123",
        OrgInvTemplateNo: "1",
        OrgInvSeries: "C26TAA",
        OrgInvDate: new DateOnly(2026, 5, 10));

    [Fact]
    public async Task Issues_replacement_with_EInvoiceStatus_3()
    {
        var stub = new ReplayingStubClient();
        var sut = MakeSut(stub);
        var invoice = SampleInvoices.TypicalVatInvoice();
        var request = new ReplacementRequest(invoice, ValidOriginalRef, "Sửa thông tin người mua", InvoiceWithCode: true);

        var result = await sut.ExecuteAsync(request, default);

        Assert.Equal(SaveOutcome.Success, result.Outcome);
        Assert.Equal(invoice.RefId.Value, result.RefId.Value);
        Assert.Equal("3", stub.LastEInvoiceStatus);
        Assert.NotNull(stub.LastOriginalRef);
        Assert.Equal("orig-001", stub.LastOriginalRef!.OrgRefID);
        Assert.Equal("00000123", stub.LastOriginalRef.OrgInvNo);
        Assert.Equal("1", stub.LastOriginalRef.OrgInvTemplateNo);
        Assert.Equal("C26TAA", stub.LastOriginalRef.OrgInvSeries);
        Assert.Equal(new DateOnly(2026, 5, 10), stub.LastOriginalRef.OrgInvDate);
        Assert.Equal("Sửa thông tin người mua", stub.LastChangeReason);
    }

    [Fact]
    public async Task Duplicate_fresh_RefID_maps_to_DuplicateOrUniqueness_not_Replacement()
    {
        var invoice = SampleInvoices.TypicalVatInvoice();
        var stub = new ReplayingStubClient
        {
            NextResult = new SaveResult(
                invoice.RefId,
                SaveOutcome.Error,
                new MeInvoiceErrorCode(MeInvoiceErrorCategory.DuplicateOrUniqueness, "DuplicateInvoiceRefID")),
        };
        var sut = MakeSut(stub);
        var request = new ReplacementRequest(invoice, ValidOriginalRef, "Sửa thông tin", InvoiceWithCode: true);

        var result = await sut.ExecuteAsync(request, default);

        Assert.Equal(SaveOutcome.Error, result.Outcome);
        Assert.Equal(MeInvoiceErrorCategory.DuplicateOrUniqueness, result.Error!.Category);
        Assert.NotEqual(MeInvoiceErrorCategory.Replacement, result.Error.Category);
    }

    [Fact]
    public async Task Caller_omits_RefId_service_generates_GUID_and_propagates()
    {
        var stub = new ReplayingStubClient();
        var sut = MakeSut(stub);
        var invoice = SampleInvoices.TypicalVatInvoice() with { RefId = default };
        var request = new ReplacementRequest(invoice, ValidOriginalRef, "Sửa", InvoiceWithCode: true);

        var result = await sut.ExecuteAsync(request, default);

        Assert.Equal(SaveOutcome.Success, result.Outcome);
        Assert.NotEmpty(result.RefId.Value);
        Assert.NotEmpty(stub.LastInvoice!.RefId.Value);
    }

    private static IssueReplacementInvoice MakeSut(IMeInvoiceClient client)
    {
        var resolver = new StubResolver(new TemplateResolution.Resolved(DefaultTemplate));
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "key");
        return new IssueReplacementInvoice(client, ensure, new InvoiceValidator(), resolver, new GuidRefIdGenerator());
    }

    internal sealed class ReplayingStubClient : IMeInvoiceClient
    {
        public Invoice? LastInvoice { get; private set; }
        public OriginalInvoiceReference? LastOriginalRef { get; private set; }
        public string? LastChangeReason { get; private set; }
        public string? LastEInvoiceStatus { get; private set; }
        public SaveResult? NextResult { get; set; }

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Template>>(new[] { DefaultTemplate });
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();

        public Task<SaveResult> IssueReplacementAsync(
            Invoice invoice,
            OriginalInvoiceReference originalRef,
            string changeReason,
            bool invoiceWithCode,
            CancellationToken ct)
        {
            LastInvoice = invoice;
            LastOriginalRef = originalRef;
            LastChangeReason = changeReason;
            LastEInvoiceStatus = "3";
            return Task.FromResult(NextResult ?? new SaveResult(invoice.RefId, SaveOutcome.Success));
        }
    }

    internal sealed class StubResolver : ITemplateResolver
    {
        private readonly TemplateResolution _result;
        public StubResolver(TemplateResolution result) => _result = result;
        public Task<TemplateResolution> ResolveAsync(TemplateRef? callerSupplied, bool invoiceWithCode, CancellationToken ct) =>
            Task.FromResult(_result);
    }
}

using System.Reflection;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Application.Validation;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using MisaConnect.EInvoice.TestSupport.Builders;
using MisaConnect.EInvoice.TestSupport.Fixtures;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Amendments;

public class IssueAdjustmentInvoiceTests
{
    private static readonly Template DefaultTemplate = new("ipt-1", "1C25MNQ", "Default", null, 1, true, true, false);

    private static readonly OriginalInvoiceReference ValidOriginalRef = new(
        OrgRefID: "orig-002",
        OrgInvNo: "00000124",
        OrgInvTemplateNo: "1",
        OrgInvSeries: "C26TAA",
        OrgInvDate: new DateOnly(2026, 5, 10));

    [Fact]
    public async Task Issues_adjustment_with_increase_delta_and_EInvoiceStatus_4()
    {
        var stub = new ReplayingStubClient();
        var sut = MakeSut(stub);
        var invoice = MakeDeltaInvoice(amount: 100_000m);
        var request = new AdjustmentRequest(invoice, ValidOriginalRef, "Bổ sung phụ thu", InvoiceWithCode: true);

        var result = await sut.ExecuteAsync(request, default);

        Assert.Equal(SaveOutcome.Success, result.Outcome);
        Assert.Equal(invoice.RefId.Value, result.RefId.Value);
        Assert.Equal("4", stub.LastEInvoiceStatus);
        Assert.Equal(100_000m, stub.LastInvoice!.Totals.TotalAmount);
        Assert.Equal("Bổ sung phụ thu", stub.LastChangeReason);
    }

    [Fact]
    public async Task Issues_adjustment_with_decrease_delta_and_EInvoiceStatus_4()
    {
        var stub = new ReplayingStubClient();
        var sut = MakeSut(stub);
        var invoice = MakeDeltaInvoice(amount: -55_000m);
        var request = new AdjustmentRequest(invoice, ValidOriginalRef, "Giảm trừ phụ thu", InvoiceWithCode: true);

        var result = await sut.ExecuteAsync(request, default);

        Assert.Equal(SaveOutcome.Success, result.Outcome);
        Assert.Equal("4", stub.LastEInvoiceStatus);
        Assert.Equal(-55_000m, stub.LastInvoice!.Totals.TotalAmount);
    }

    [Fact]
    public async Task Issues_adjustment_with_zero_net_descriptive_delta_and_EInvoiceStatus_4()
    {
        var stub = new ReplayingStubClient();
        var sut = MakeSut(stub);
        var invoice = MakeDeltaInvoice(amount: 0m);
        var request = new AdjustmentRequest(invoice, ValidOriginalRef, "Đính chính mô tả", InvoiceWithCode: true);

        var result = await sut.ExecuteAsync(request, default);

        Assert.Equal(SaveOutcome.Success, result.Outcome);
        Assert.Equal("4", stub.LastEInvoiceStatus);
        Assert.Equal(0m, stub.LastInvoice!.Totals.TotalAmount);
    }

    [Fact]
    public void Issues_adjustment_does_not_enforce_direction_enum()
    {
        // Compile-time guard: the execute method signature must NOT carry a
        // parameter whose type name looks like an adjustment direction enum.
        var method = typeof(IssueAdjustmentInvoice).GetMethod(
            nameof(IssueAdjustmentInvoice.ExecuteAsync),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        foreach (var p in method!.GetParameters())
        {
            Assert.False(
                p.Name?.Equals("direction", StringComparison.OrdinalIgnoreCase) ?? false,
                $"Parameter '{p.Name}' should not exist — adjustment direction is delta-only (research R-AM-20).");
            var typeName = p.ParameterType.Name;
            Assert.DoesNotContain("Direction", typeName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AdjustmentKind", typeName, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Duplicate_fresh_RefID_maps_to_DuplicateOrUniqueness_not_Replacement()
    {
        var invoice = MakeDeltaInvoice(amount: 50_000m);
        var stub = new ReplayingStubClient
        {
            NextResult = new SaveResult(
                invoice.RefId,
                SaveOutcome.Error,
                new MeInvoiceErrorCode(MeInvoiceErrorCategory.DuplicateOrUniqueness, "DuplicateInvoiceRefID")),
        };
        var sut = MakeSut(stub);
        var request = new AdjustmentRequest(invoice, ValidOriginalRef, "Bổ sung phụ thu", InvoiceWithCode: true);

        var result = await sut.ExecuteAsync(request, default);

        Assert.Equal(MeInvoiceErrorCategory.DuplicateOrUniqueness, result.Error!.Category);
        Assert.NotEqual(MeInvoiceErrorCategory.Replacement, result.Error.Category);
    }

    private static Invoice MakeDeltaInvoice(decimal amount)
    {
        // Build a minimal-shape delta invoice. The slice 1 validator accepts
        // negative and zero totals; the service does not enforce direction.
        var line = new InvoiceLine(
            InventoryItemType: InventoryItemType.Goods,
            SortOrder: 1,
            Description: "Điều chỉnh phụ thu",
            UnitName: "Lần",
            Quantity: 1m,
            UnitPrice: amount,
            AmountOC: amount,
            Amount: amount,
            AmountWithoutVATOC: amount,
            AmountWithoutVAT: amount,
            VatRate: VatRate.Zero,
            VATAmountOC: 0m,
            VATAmount: 0m,
            SortOrderView: 1);

        var totals = new InvoiceTotals(
            TotalSaleAmountOC: amount,
            TotalSaleAmount: amount,
            TotalDiscountAmountOC: 0m,
            TotalDiscountAmount: 0m,
            TotalAmountWithoutVATOC: amount,
            TotalAmountWithoutVAT: amount,
            TotalVATAmountOC: 0m,
            TotalVATAmount: 0m,
            TotalAmountOC: amount,
            TotalAmount: amount,
            TotalAmountInWords: "Điều chỉnh");

        return new InvoiceBuilder()
            .WithRefId(RefId.NewGuid())
            .AddLine(line)
            .WithTotals(totals)
            .Build();
    }

    private static IssueAdjustmentInvoice MakeSut(IMeInvoiceClient client)
    {
        var resolver = new StubResolver(new TemplateResolution.Resolved(DefaultTemplate));
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "key");
        return new IssueAdjustmentInvoice(client, ensure, new InvoiceValidator(), resolver, new GuidRefIdGenerator());
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

        public Task<SaveResult> IssueAdjustmentAsync(
            Invoice invoice,
            OriginalInvoiceReference originalRef,
            string changeReason,
            bool invoiceWithCode,
            CancellationToken ct)
        {
            LastInvoice = invoice;
            LastOriginalRef = originalRef;
            LastChangeReason = changeReason;
            LastEInvoiceStatus = "4";
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

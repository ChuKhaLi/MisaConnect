using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.TestSupport.Builders;

public sealed class InvoiceBuilder
{
    private RefId _refId = default;
    private TemplateRef? _template;
    private DateOnly _invDate = new(2026, 5, 11);
    private DateTimeOffset _created = new(2026, 5, 11, 0, 0, 0, TimeSpan.Zero);
    private DateTimeOffset _modified = new(2026, 5, 11, 0, 0, 0, TimeSpan.Zero);
    private string _currency = "VND";
    private decimal _exchangeRate = 1m;
    private string _paymentMethod = "TM";
    private BuyerType _buyerType = BuyerType.Organization;
    private BuyerInfo _buyer = new("Công ty TNHH Mua hàng", TaxCode: "0123456789");
    private readonly List<InvoiceLine> _lines = new();
    private InvoiceTotals? _totals;
    private Dictionary<int, string>? _customFields;

    public InvoiceBuilder WithRefId(RefId refId) { _refId = refId; return this; }
    public InvoiceBuilder WithRefId(string value) { _refId = RefId.From(value); return this; }
    public InvoiceBuilder WithTemplate(TemplateRef? template) { _template = template; return this; }
    public InvoiceBuilder WithInvDate(DateOnly d) { _invDate = d; return this; }
    public InvoiceBuilder WithCreatedDate(DateTimeOffset d) { _created = d; return this; }
    public InvoiceBuilder WithModifiedDate(DateTimeOffset d) { _modified = d; return this; }
    public InvoiceBuilder WithCurrency(string c) { _currency = c; return this; }
    public InvoiceBuilder WithExchangeRate(decimal r) { _exchangeRate = r; return this; }
    public InvoiceBuilder WithPaymentMethod(string p) { _paymentMethod = p; return this; }
    public InvoiceBuilder WithBuyerType(BuyerType t) { _buyerType = t; return this; }
    public InvoiceBuilder WithBuyer(BuyerInfo b) { _buyer = b; return this; }
    public InvoiceBuilder AddLine(InvoiceLine line) { _lines.Add(line); return this; }
    public InvoiceBuilder ClearLines() { _lines.Clear(); return this; }
    public InvoiceBuilder WithTotals(InvoiceTotals t) { _totals = t; return this; }
    public InvoiceBuilder WithCustomField(int index, string value)
    {
        _customFields ??= new Dictionary<int, string>();
        _customFields[index] = value;
        return this;
    }

    public Invoice Build() => new(
        RefId: _refId,
        Template: _template,
        InvDate: _invDate,
        CreatedDate: _created,
        ModifiedDate: _modified,
        Currency: _currency,
        ExchangeRate: _exchangeRate,
        PaymentMethod: _paymentMethod,
        BuyerType: _buyerType,
        Buyer: _buyer,
        Lines: _lines.ToArray(),
        Totals: _totals ?? throw new InvalidOperationException("Totals required."),
        CustomFields: _customFields);
}

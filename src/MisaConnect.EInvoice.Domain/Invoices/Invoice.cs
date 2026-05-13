namespace MisaConnect.EInvoice.Domain.Invoices;

public sealed record Invoice(
    RefId RefId,
    TemplateRef? Template,
    DateOnly InvDate,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate,
    string Currency,
    decimal ExchangeRate,
    string PaymentMethod,
    BuyerType BuyerType,
    BuyerInfo Buyer,
    IReadOnlyList<InvoiceLine> Lines,
    InvoiceTotals Totals,
    IReadOnlyDictionary<int, string>? CustomFields = null);

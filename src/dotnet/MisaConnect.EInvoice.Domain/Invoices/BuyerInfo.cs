namespace MisaConnect.EInvoice.Domain.Invoices;

public sealed record BuyerInfo(
    string Name,
    string? TaxCode = null,
    string? Address = null,
    string? ContactName = null,
    string? Email = null,
    string? ReceiverName = null,
    string? Mobile = null,
    string? BankAccount = null,
    string? BankName = null);

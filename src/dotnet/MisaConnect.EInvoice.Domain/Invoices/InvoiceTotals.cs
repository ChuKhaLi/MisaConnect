namespace MisaConnect.EInvoice.Domain.Invoices;

public sealed record InvoiceTotals(
    decimal TotalSaleAmountOC,
    decimal TotalSaleAmount,
    decimal TotalDiscountAmountOC,
    decimal TotalDiscountAmount,
    decimal TotalAmountWithoutVATOC,
    decimal TotalAmountWithoutVAT,
    decimal TotalVATAmountOC,
    decimal TotalVATAmount,
    decimal TotalAmountOC,
    decimal TotalAmount,
    string TotalAmountInWords);

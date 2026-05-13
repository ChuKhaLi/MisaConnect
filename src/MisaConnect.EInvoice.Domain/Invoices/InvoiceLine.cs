namespace MisaConnect.EInvoice.Domain.Invoices;

public sealed record InvoiceLine(
    InventoryItemType InventoryItemType,
    int SortOrder,
    string Description,
    string UnitName,
    decimal Quantity,
    decimal UnitPrice,
    decimal AmountOC,
    decimal Amount,
    decimal AmountWithoutVATOC,
    decimal AmountWithoutVAT,
    VatRate? VatRate = null,
    decimal? VATAmountOC = null,
    decimal? VATAmount = null,
    int? SortOrderView = null,
    decimal? DiscountRate = null,
    decimal? DiscountAmountOC = null,
    decimal? DiscountAmount = null);

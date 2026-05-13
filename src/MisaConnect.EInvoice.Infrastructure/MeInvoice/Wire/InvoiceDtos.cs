namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Wire;

internal sealed record TaxRateInfoDto(
    string? VATRateName,
    int? VATRate);

internal sealed record InvoiceDetailDto(
    int InventoryItemType,
    int? SortOrder,
    int? SortOrderView,
    string? Description,
    string? UnitName,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? AmountOC,
    decimal? Amount,
    decimal? AmountWithoutVATOC,
    decimal? AmountWithoutVAT,
    decimal? VATAmountOC,
    decimal? VATAmount,
    string? VATRateName,
    int? VATRate,
    decimal? DiscountRate,
    decimal? DiscountAmountOC,
    decimal? DiscountAmount);

/// <summary>
/// Outgoing invoice body for <c>/insert</c> and <c>/preview</c>. Field
/// names follow MISA's CURL examples exactly: <c>InvoiceTemplateID</c>
/// (not <c>IPTemplateID</c>) and <c>InvoiceDetails</c> (plural). MISA
/// validates these names strictly and rejects with
/// <c>Invalid_InvoiceTemplateID</c> / <c>Invalid_InvoiceDetails</c> if
/// they are wrong.
/// </summary>
internal sealed record InvoiceDataDto(
    string RefID,
    string? InvoiceTemplateID,
    string? InvSeries,
    string? InvDate,
    string? CreatedDate,
    string? ModifiedDate,
    string? CurrencyCode,
    decimal? ExchangeRate,
    string? PaymentMethod,
    string? AccountObjectName,
    string? AccountObjectTaxCode,
    string? AccountObjectAddress,
    string? ContactName,
    string? ReceiverEmail,
    string? ReceiverName,
    string? ReceiverMobile,
    string? AccountObjectBankAccount,
    string? AccountObjectBankName,
    decimal? TotalSaleAmountOC,
    decimal? TotalSaleAmount,
    decimal? TotalDiscountAmountOC,
    decimal? TotalDiscountAmount,
    decimal? TotalAmountWithoutVATOC,
    decimal? TotalAmountWithoutVAT,
    decimal? TotalVATAmountOC,
    decimal? TotalVATAmount,
    decimal? TotalAmountOC,
    decimal? TotalAmount,
    string? TotalAmountInWords,
    string? EInvoiceStatus,
    string? CustomField1,
    string? CustomField2,
    string? CustomField3,
    // Slice 6 — amendment fields, populated only on replacement / adjustment paths.
    // JsonIgnoreCondition.WhenWritingNull on MeInvoiceJsonOptions.Wire keeps them
    // off the wire for slice 1 SaveDraft / Preview calls.
    string? OrgRefID,
    string? OrgInvNo,
    string? OrgInvTemplateNo,
    string? OrgInvSeries,
    string? OrgInvDate,
    string? ChangeReason,
    IReadOnlyList<InvoiceDetailDto> InvoiceDetails);

/// <summary>
/// Infrastructure-internal mirror of the Application-layer
/// <c>OriginalInvoiceReference</c> with the date pre-formatted as a string
/// (<c>yyyy-MM-dd</c>) per MISA's <c>InvDate</c> convention.
/// </summary>
internal sealed record OriginalInvoiceReferenceWire(
    string OrgRefID,
    string OrgInvNo,
    string OrgInvTemplateNo,
    string OrgInvSeries,
    string OrgInvDate);

internal sealed record InsertSuccessEntryDto(string? RefID);

internal sealed record InsertErrorEntryDto(string? RefID, string? ErrorCode, string? ErrorMessage);

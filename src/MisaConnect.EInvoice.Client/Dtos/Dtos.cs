namespace MisaConnect.EInvoice.Client.Dtos;

public sealed record TemplateDto(
    string IPTemplateID,
    string InvSeries,
    string TemplateName,
    string? InvTemplateNo,
    int TemplateType,
    bool UsesTaxAuthorityCode,
    bool IsActive,
    bool IsMoreVATRate);

public sealed record TemplateRefDto(string IPTemplateID, string InvSeries);

public sealed record BuyerInfoDto(
    string Name,
    string? TaxCode = null,
    string? Address = null,
    string? ContactName = null,
    string? Email = null,
    string? ReceiverName = null,
    string? Mobile = null,
    string? BankAccount = null,
    string? BankName = null);

public sealed record InvoiceLineDto(
    int InventoryItemType,
    int SortOrder,
    string Description,
    string UnitName,
    decimal Quantity,
    decimal UnitPrice,
    decimal AmountOC,
    decimal Amount,
    decimal AmountWithoutVATOC,
    decimal AmountWithoutVAT,
    string? VatRateName = null,
    decimal? VATAmountOC = null,
    decimal? VATAmount = null,
    int? SortOrderView = null,
    decimal? DiscountRate = null,
    decimal? DiscountAmountOC = null,
    decimal? DiscountAmount = null);

public sealed record InvoiceTotalsDto(
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

public sealed record InvoiceDto(
    string? RefId,
    TemplateRefDto? Template,
    DateOnly InvDate,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate,
    string Currency,
    decimal ExchangeRate,
    string PaymentMethod,
    int BuyerType,
    BuyerInfoDto Buyer,
    IReadOnlyList<InvoiceLineDto> Lines,
    InvoiceTotalsDto Totals,
    IReadOnlyDictionary<int, string>? CustomFields = null);

public enum SaveOutcomeDto { Success, Error }

public sealed record ValidationFailureDto(string FieldPath, string Message, string? RuleId);

public sealed record SaveResultDto(
    string RefId,
    SaveOutcomeDto Outcome,
    string? ErrorCategory = null,
    string? RawErrorCode = null,
    string? ErrorMessage = null,
    IReadOnlyList<ValidationFailureDto>? Failures = null);

public sealed record PdfDocumentDto(byte[] Content, string ContentType = "application/pdf");

// === Slice 5: invoice lookup DTOs ===

public sealed record PagedLookupRequestDto(
    int Start,
    int Length,
    string? Sort,
    DateOnly FromDate,
    DateOnly ToDate,
    int? PublishStatus);

public sealed record InvoiceSnapshotDto(
    string RefId,
    string? InvoiceTemplateID,
    string? InvSeries,
    DateTime? InvDate,
    string? InvNo,
    string? AccountObjectTaxCode,
    string? AccountObjectName,
    decimal? TotalSaleAmount,
    decimal? TotalVATAmount,
    decimal? TotalAmount,
    decimal? TotalSaleAmountOC,
    decimal? TotalVATAmountOC,
    decimal? TotalAmountOC,
    int? RawEInvoiceStatus,
    int? RawPublishStatus,
    string Status,
    string? OrgRefID,
    DateTimeOffset? CreatedDate,
    DateTimeOffset? ModifiedDate);

public sealed record LookupOutcomeDto(
    string RefId,
    string Status,
    InvoiceSnapshotDto? Snapshot,
    string? ErrorCode,
    string? Message);

public sealed record LookupBatchOutcomeDto(
    string Status,
    IReadOnlyList<LookupOutcomeDto>? Outcomes,
    string? Reason);

public sealed record PagedLookupResultDto(
    IReadOnlyList<InvoiceSnapshotDto> Items,
    int Start,
    int Length,
    int ReturnedCount);

// === Slice 6: invoice amendment DTOs ===

public sealed record OriginalInvoiceReferenceDto(
    string OrgRefID,
    string OrgInvNo,
    string OrgInvTemplateNo,
    string OrgInvSeries,
    DateOnly OrgInvDate);

public sealed record ReplacementRequestDto(
    InvoiceDto Invoice,
    OriginalInvoiceReferenceDto OriginalRef,
    string ChangeReason,
    bool InvoiceWithCode = true);

public sealed record AdjustmentRequestDto(
    InvoiceDto Invoice,
    OriginalInvoiceReferenceDto OriginalRef,
    string ChangeReason,
    bool InvoiceWithCode = true);

public sealed record AmendmentResultDto(
    string RefId,
    SaveOutcomeDto Outcome,
    string? ErrorCategory = null,
    string? RawErrorCode = null,
    string? ErrorMessage = null,
    IReadOnlyList<ValidationFailureDto>? Failures = null,
    string? OrgRefId = null);

using System.Text.Json;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Wire;

/// <summary>
/// Response envelope for MISA's lookup endpoints. Same shape as the slice 1
/// <see cref="MeInvoiceEnvelopeDto"/> except <c>data</c> is a
/// <see cref="JsonElement"/> rather than a stringified-JSON
/// <see cref="string"/> — the lookup endpoints return <c>data</c> as a
/// direct JSON array per R-LU-16.
/// </summary>
internal sealed record LookupEnvelopeDto(
    bool success,
    JsonElement? data,
    JsonElement? error,
    JsonElement? errorCode);

/// <summary>
/// Incoming snapshot shape from MISA's <c>/webapp/getlist</c>,
/// <c>/webapp/paging</c>, and <c>/webapp/paging/calculating</c> endpoints
/// (R-LU-16). String types on the status / date fields match MISA's
/// captured cURL exactly; the mapper coerces them to strongly-typed
/// values per R-LU-20.
/// </summary>
internal sealed record InvoiceDataLookupDto(
    string? RefID,
    string? InvoiceTemplateID,
    string? InvSeries,
    string? InvDate,
    string? InvNo,
    string? AccountObjectTaxCode,
    string? AccountObjectName,
    decimal? TotalSaleAmount,
    decimal? TotalVATAmount,
    decimal? TotalAmount,
    decimal? TotalSaleAmountOC,
    decimal? TotalVATAmountOC,
    decimal? TotalAmountOC,
    string? EInvoiceStatus,
    string? PublishStatus,
    string? OrgRefID,
    string? CreatedDate,
    string? ModifiedDate);

/// <summary>
/// Outbound paged-request body for <c>/webapp/paging</c> and
/// <c>/webapp/paging/calculating</c>. PublishStatus is serialised as a
/// string to match MISA's captured cURL exactly (R-LU-20).
/// </summary>
internal sealed record PagedLookupRequestDto(
    int Start,
    int Length,
    string Sort,
    string FromDate,
    string ToDate,
    string? PublishStatus);

namespace MisaConnect.EInvoice.Application.Abstractions;

/// <summary>
/// Flat Application-level shape of MISA's <c>/webapp/delete</c> response that
/// the use case consumes. Other v2-envelope fields (descriptionErrorCode,
/// errors, data, customData) are parsed and discarded by the infrastructure
/// adapter; widen this record if a future slice needs them.
/// </summary>
public sealed record DeleteResponse(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage);

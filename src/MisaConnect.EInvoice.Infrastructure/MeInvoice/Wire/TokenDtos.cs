using System.Text.Json;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Wire;

internal sealed record TokenRequestDto(string taxcode, string username, string password);

/// <summary>
/// Inner payload after unwrapping the stringified-JSON sitting in
/// <see cref="MeInvoiceEnvelopeDto.data"/>.
/// </summary>
internal sealed record TokenInnerDto(string access_token);

/// <summary>
/// Universal MISA response envelope. <c>data</c> is a string — either
/// stringified-JSON (token, templates, insert) or a raw base64 PDF
/// (preview, viewrefid). <c>error</c> is sometimes a plain string,
/// sometimes stringified-JSON, sometimes null. <c>errorCode</c> is
/// sometimes a string, sometimes an array, sometimes absent.
/// </summary>
internal sealed record MeInvoiceEnvelopeDto(
    bool success,
    string? data,
    JsonElement? error,
    JsonElement? errorCode);

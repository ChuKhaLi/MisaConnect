using System.Text.Json;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Wire;

/// <summary>
/// MISA's <c>/webapp/delete</c> response envelope. Mirrors the v2 envelope
/// fields (<c>success</c>, <c>errorCode</c>, etc.) plus the top-level
/// <c>ErrorMessage</c> string that delete returns when <c>success == false</c>.
/// The <c>errorCode</c> shape is a <see cref="JsonElement"/> because MISA
/// sometimes serialises the value as a string, sometimes as an array.
/// </summary>
internal sealed record DeleteResponseDto(
    bool success,
    JsonElement? errorCode,
    string? ErrorMessage,
    string? descriptionErrorCode);

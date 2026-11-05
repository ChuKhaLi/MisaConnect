using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MisaConnect.ESign.Client.Dtos.Webhook;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Client.Webhook;

/// <summary>
/// Converts the consumer-facing <see cref="WebhookEnvelopeDto"/> into the domain
/// <see cref="WebhookEnvelope"/> without ever throwing on bad input.
/// </summary>
internal static class WebhookEnvelopeParser
{
    /// <summary>
    /// Tries to parse <paramref name="dto"/>. On failure <paramref name="malformedField"/> is the
    /// offending JSON field name (never a value), which is safe to log.
    /// </summary>
    public static bool TryParse(
        WebhookEnvelopeDto dto,
        [NotNullWhen(true)] out WebhookEnvelope? envelope,
        [NotNullWhen(false)] out string? malformedField)
    {
        envelope = null;

        if (string.IsNullOrWhiteSpace(dto.MessageId)) { malformedField = "messageId"; return false; }
        if (string.IsNullOrWhiteSpace(dto.ClientId)) { malformedField = "clientId"; return false; }
        if (string.IsNullOrWhiteSpace(dto.TransactionId)) { malformedField = "transactionId"; return false; }

        WebhookStatus status;
        if (string.Equals(dto.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)) status = WebhookStatus.Success;
        else if (string.Equals(dto.Status, "FAILED", StringComparison.OrdinalIgnoreCase)) status = WebhookStatus.Failed;
        else if (string.Equals(dto.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) status = WebhookStatus.Cancelled;
        else { malformedField = "status"; return false; }

        var signatures = new List<WebhookSignature>();
        if (dto.Signatures is not null)
        {
            foreach (var s in dto.Signatures)
            {
                if (s is null) { malformedField = "signatures"; return false; }
                signatures.Add(new WebhookSignature(s.DocumentId, s.Signature));
            }
        }

        IReadOnlyDictionary<string, JsonElement>? extra = dto.ExtraData is null
            ? null
            : new ReadOnlyDictionary<string, JsonElement>(dto.ExtraData);

        envelope = new WebhookEnvelope(
            MessageId: dto.MessageId,
            ClientId: dto.ClientId,
            ExtraData: extra,
            Status: status,
            ErrorCode: dto.ErrorCode,
            TransactionId: dto.TransactionId,
            Signatures: signatures);
        malformedField = null;
        return true;
    }
}

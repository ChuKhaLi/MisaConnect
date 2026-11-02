using System.Text.Json;

namespace MisaConnect.ESign.Domain.Webhook;

public sealed record WebhookEnvelope(
    string MessageId,
    string ClientId,
    IReadOnlyDictionary<string, JsonElement>? ExtraData,
    WebhookStatus Status,
    string? ErrorCode,
    string TransactionId,
    IReadOnlyList<WebhookSignature> Signatures);

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Client.Dtos.Webhook;

/// <summary>
/// Consumer-deserializable form of MISA's inbound webhook envelope (§3.8 / §4.9).
/// CamelCase via <see cref="JsonPropertyNameAttribute"/>. Pass into
/// <c>IMisaESignClient.HandleWebhookAsync</c>.
/// </summary>
public sealed record WebhookEnvelopeDto(
    [property: JsonPropertyName("messageId")] string MessageId,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("extraData")] Dictionary<string, JsonElement>? ExtraData,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("transactionId")] string TransactionId,
    [property: JsonPropertyName("signatures")] List<WebhookSignatureDto> Signatures);

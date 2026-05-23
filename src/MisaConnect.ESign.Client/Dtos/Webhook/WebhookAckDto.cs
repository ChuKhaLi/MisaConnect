using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Client.Dtos.Webhook;

public sealed record WebhookAckDto(
    [property: JsonPropertyName("errorCode")] string ErrorCode,
    [property: JsonPropertyName("devMsg")] string DevMsg,
    [property: JsonPropertyName("userMsg")] string UserMsg);

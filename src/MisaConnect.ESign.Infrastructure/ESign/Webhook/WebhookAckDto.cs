using System.Text.Json.Serialization;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Infrastructure.ESign.Webhook;

internal sealed record WebhookAckDto(
    [property: JsonPropertyName("errorCode")] string ErrorCode,
    [property: JsonPropertyName("devMsg")] string DevMsg,
    [property: JsonPropertyName("userMsg")] string UserMsg)
{
    public static WebhookAckDto FromDomain(WebhookAck ack) =>
        new(ack.ErrorCode, ack.DevMsg, ack.UserMsg);
}

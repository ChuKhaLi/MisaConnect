using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.Sessions;

public sealed record SigningSessionCachedSuccess(
    string TriggeringMessageId,
    WebhookAck Ack,
    byte[] SignedBytes);

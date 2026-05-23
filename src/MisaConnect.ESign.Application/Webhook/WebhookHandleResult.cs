using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.Webhook;

public sealed record WebhookHandleResult(WebhookAck Ack, WebhookOutcome Outcome);

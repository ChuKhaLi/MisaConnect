namespace MisaConnect.ESign.Client.Dtos.Webhook;

public sealed record WebhookHandleResultDto(WebhookAckDto Ack, WebhookOutcomeDto Outcome);

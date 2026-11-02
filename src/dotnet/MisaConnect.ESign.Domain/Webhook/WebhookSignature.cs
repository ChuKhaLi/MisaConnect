namespace MisaConnect.ESign.Domain.Webhook;

public sealed record WebhookSignature(string DocumentId, string Signature);

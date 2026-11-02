namespace MisaConnect.ESign.Domain.Webhook;

public enum WebhookStatus : byte
{
    Success = 1,
    Failed = 2,
    Cancelled = 3,
}

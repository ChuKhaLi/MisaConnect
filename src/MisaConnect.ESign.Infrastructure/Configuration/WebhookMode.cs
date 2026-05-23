namespace MisaConnect.ESign.Infrastructure.Configuration;

public enum WebhookMode : byte
{
    Polling = 1,
    Webhook = 2,
    Both = 3,
}

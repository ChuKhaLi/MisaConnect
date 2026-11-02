namespace MisaConnect.ESign.Infrastructure.Configuration;

public sealed class MisaESignWebhookSessionOptions
{
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(24);
}

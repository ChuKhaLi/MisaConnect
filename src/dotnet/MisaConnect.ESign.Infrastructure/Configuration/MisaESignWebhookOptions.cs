namespace MisaConnect.ESign.Infrastructure.Configuration;

public sealed class MisaESignWebhookOptions
{
    public WebhookMode Mode { get; set; } = WebhookMode.Both;
    public MisaESignWebhookSessionOptions Session { get; set; } = new();
    public string? Path { get; set; } = "/esign/webhook";
    public string? Secret { get; set; }
    public string[]? AllowedIps { get; set; }
}

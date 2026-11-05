namespace MisaConnect.ESign.Infrastructure.Configuration;

public sealed class MisaESignWebhookOptions
{
    public WebhookMode Mode { get; set; } = WebhookMode.Both;
    public MisaESignWebhookSessionOptions Session { get; set; } = new();
    public string? Path { get; set; } = "/esign/webhook";
    public string? Secret { get; set; }
    public string[]? AllowedIps { get; set; }

    /// <summary>
    /// When <c>true</c> (the 2.x default), webhook validation failures (malformed envelope, client id
    /// mismatch, unknown transaction, incomplete or mismatched success) are also delivered to the
    /// registered <c>IWebhookDeliveryHook</c> as a failure outcome. Set to <c>false</c> to answer
    /// them with an ACK only and never call the hook.
    /// <para>
    /// The default stays <c>true</c> in 2.x so a minor release does not silently change behaviour.
    /// It flips to <c>false</c> in 3.0: a public webhook endpoint lets anyone trigger hook calls
    /// with forged payloads.
    /// </para>
    /// </summary>
    public bool DeliverValidationFailuresToHook { get; set; } = true;
}

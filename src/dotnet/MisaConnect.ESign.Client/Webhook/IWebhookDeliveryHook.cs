using MisaConnect.ESign.Client.Dtos.Webhook;

namespace MisaConnect.ESign.Client.Webhook;

/// <summary>
/// Consumer-implementable callback that the SDK invokes with the typed
/// <see cref="WebhookOutcomeDto"/> after each webhook delivery's finalize attempt.
/// Register via <c>services.AddSingleton&lt;IWebhookDeliveryHook, MyImpl&gt;()</c>
/// BEFORE <c>services.AddMisaConnectESign(...)</c>; the SDK uses
/// <c>TryAddSingleton</c> for the no-op default, so the consumer's registration wins.
/// </summary>
public interface IWebhookDeliveryHook
{
    Task DeliverAsync(WebhookOutcomeDto outcome, CancellationToken ct);
}

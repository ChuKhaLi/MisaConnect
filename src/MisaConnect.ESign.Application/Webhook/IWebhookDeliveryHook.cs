using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.Webhook;

/// <summary>
/// Consumer-implementable callback that the SDK invokes once per webhook delivery
/// after finalize attempts complete (success, terminal failure, or post-finalize
/// failure). Default DI registration is a no-op logging implementation; consumers
/// register their own <c>TryAddSingleton</c> before <c>AddMisaConnectESign</c>.
/// </summary>
public interface IWebhookDeliveryHook
{
    Task DeliverAsync(WebhookOutcome outcome, CancellationToken ct);
}

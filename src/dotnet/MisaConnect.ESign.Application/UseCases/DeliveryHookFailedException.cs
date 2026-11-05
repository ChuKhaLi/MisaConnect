using System.Runtime.ExceptionServices;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.UseCases;

/// <summary>
/// Marks an exception thrown by the integrator's <see cref="IWebhookDeliveryHook"/> while delivering a
/// success or terminal outcome, so <see cref="HandleWebhook"/>'s catch-all can rethrow the original
/// exception unchanged instead of converting it to a <c>webhook.finalize_failed</c> ACK.
/// </summary>
internal sealed class DeliveryHookFailedException : Exception
{
    private DeliveryHookFailedException(Exception inner)
        : base("Webhook delivery hook threw.", inner)
    {
    }

    /// <summary>Delivers <paramref name="outcome"/>, wrapping any hook exception for later rethrow.</summary>
    public static async Task DeliverAsync(IWebhookDeliveryHook hook, WebhookOutcome outcome, CancellationToken ct)
    {
        try
        {
            await hook.DeliverAsync(outcome, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new DeliveryHookFailedException(ex);
        }
    }

    /// <summary>Rethrows the original hook exception with its original type and stack.</summary>
    public void RethrowInner() => ExceptionDispatchInfo.Capture(InnerException!).Throw();
}

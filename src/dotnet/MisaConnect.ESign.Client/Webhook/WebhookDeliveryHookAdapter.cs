using MisaConnect.ESign.Client.Dtos.Webhook;
using ApplicationHook = MisaConnect.ESign.Application.Webhook.IWebhookDeliveryHook;
using DomainOutcome = MisaConnect.ESign.Domain.Webhook.WebhookOutcome;

namespace MisaConnect.ESign.Client.Webhook;

/// <summary>
/// Bridges the SDK's Application-layer delivery hook to the consumer-facing
/// Client-layer <see cref="IWebhookDeliveryHook"/>. Translates Domain
/// <see cref="DomainOutcome"/> into <see cref="WebhookOutcomeDto"/> so consumers
/// don't need to add a <c>using MisaConnect.ESign.Domain.Webhook</c>.
/// </summary>
internal sealed class WebhookDeliveryHookAdapter : ApplicationHook
{
    private readonly IWebhookDeliveryHook _consumerHook;

    public WebhookDeliveryHookAdapter(IWebhookDeliveryHook consumerHook)
    {
        _consumerHook = consumerHook;
    }

    public Task DeliverAsync(DomainOutcome outcome, CancellationToken ct)
    {
        WebhookOutcomeDto dto = outcome switch
        {
            DomainOutcome.SuccessWithSignedBytes s => new WebhookOutcomeDto.SuccessWithSignedBytes(
                s.TransactionId, s.Format, s.SignedBytes, s.CorrelationId),
            DomainOutcome.FailureWithError f => new WebhookOutcomeDto.FailureWithError(
                f.TransactionId, f.Format, f.Category.ToString(), f.MisaErrorCode, f.CorrelationId),
            DomainOutcome.TerminalWithoutFinalize t => new WebhookOutcomeDto.TerminalWithoutFinalize(
                t.TransactionId, t.Format, (WebhookStatusDto)(byte)t.Status, t.MisaErrorCode, t.CorrelationId),
            _ => throw new InvalidOperationException($"Unknown WebhookOutcome variant: {outcome.GetType().Name}"),
        };
        return _consumerHook.DeliverAsync(dto, ct);
    }
}

using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.Webhook;

internal sealed class NullWebhookDeliveryHook : IWebhookDeliveryHook
{
    private readonly ILogger<NullWebhookDeliveryHook> _logger;

    public NullWebhookDeliveryHook(ILogger<NullWebhookDeliveryHook> logger)
    {
        _logger = logger;
    }

    public Task DeliverAsync(WebhookOutcome outcome, CancellationToken ct)
    {
        switch (outcome)
        {
            case WebhookOutcome.SuccessWithSignedBytes s:
                _logger.LogInformation(
                    "Webhook delivery (no consumer hook registered): SUCCESS transactionId={TransactionId} format={Format} signedByteCount={SignedByteCount} correlationId={CorrelationId}",
                    s.TransactionId, s.Format, s.SignedBytes.Length, s.CorrelationId);
                break;
            case WebhookOutcome.FailureWithError f:
                _logger.LogInformation(
                    "Webhook delivery (no consumer hook registered): FAILURE transactionId={TransactionId} category={Category} correlationId={CorrelationId}",
                    f.TransactionId ?? "<none>", f.Category, f.CorrelationId);
                break;
            case WebhookOutcome.TerminalWithoutFinalize t:
                _logger.LogInformation(
                    "Webhook delivery (no consumer hook registered): TERMINAL transactionId={TransactionId} status={Status} correlationId={CorrelationId}",
                    t.TransactionId, t.Status, t.CorrelationId);
                break;
        }
        return Task.CompletedTask;
    }
}

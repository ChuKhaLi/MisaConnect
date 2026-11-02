using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Client.Dtos.Webhook;

namespace MisaConnect.ESign.Client.Webhook;

internal sealed class NullClientWebhookDeliveryHook : IWebhookDeliveryHook
{
    private readonly ILogger<NullClientWebhookDeliveryHook> _logger;

    public NullClientWebhookDeliveryHook(ILogger<NullClientWebhookDeliveryHook> logger)
    {
        _logger = logger;
    }

    public Task DeliverAsync(WebhookOutcomeDto outcome, CancellationToken ct)
    {
        switch (outcome)
        {
            case WebhookOutcomeDto.SuccessWithSignedBytes s:
                _logger.LogInformation(
                    "Webhook outcome (no consumer hook): SUCCESS transactionId={TransactionId} format={Format} signedByteCount={SignedByteCount} correlationId={CorrelationId}",
                    s.TransactionId, s.Format, s.SignedBytes.Length, s.CorrelationId);
                break;
            case WebhookOutcomeDto.FailureWithError f:
                _logger.LogInformation(
                    "Webhook outcome (no consumer hook): FAILURE transactionId={TransactionId} category={Category} correlationId={CorrelationId}",
                    f.TransactionId ?? "<none>", f.CategoryName, f.CorrelationId);
                break;
            case WebhookOutcomeDto.TerminalWithoutFinalize t:
                _logger.LogInformation(
                    "Webhook outcome (no consumer hook): TERMINAL transactionId={TransactionId} status={Status} correlationId={CorrelationId}",
                    t.TransactionId, t.Status, t.CorrelationId);
                break;
        }
        return Task.CompletedTask;
    }
}

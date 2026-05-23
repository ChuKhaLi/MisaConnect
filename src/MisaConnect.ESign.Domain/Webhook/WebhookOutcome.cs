using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Domain.Webhook;

public abstract record WebhookOutcome(string CorrelationId)
{
    public sealed record SuccessWithSignedBytes(
        string TransactionId,
        DocumentFormat Format,
        byte[] SignedBytes,
        string CorrelationId) : WebhookOutcome(CorrelationId);

    public sealed record FailureWithError(
        string? TransactionId,
        DocumentFormat Format,
        WebhookValidationCategory Category,
        string? MisaErrorCode,
        string CorrelationId) : WebhookOutcome(CorrelationId);

    public sealed record TerminalWithoutFinalize(
        string TransactionId,
        DocumentFormat Format,
        WebhookStatus Status,
        string? MisaErrorCode,
        string CorrelationId) : WebhookOutcome(CorrelationId);
}

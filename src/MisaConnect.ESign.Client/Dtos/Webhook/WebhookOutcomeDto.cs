using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Client.Dtos.Webhook;

public abstract record WebhookOutcomeDto(string CorrelationId)
{
    public sealed record SuccessWithSignedBytes(
        string TransactionId,
        DocumentFormat Format,
        byte[] SignedBytes,
        string CorrelationId) : WebhookOutcomeDto(CorrelationId);

    public sealed record FailureWithError(
        string? TransactionId,
        DocumentFormat Format,
        string CategoryName,
        string? MisaErrorCode,
        string CorrelationId) : WebhookOutcomeDto(CorrelationId);

    public sealed record TerminalWithoutFinalize(
        string TransactionId,
        DocumentFormat Format,
        WebhookStatusDto Status,
        string? MisaErrorCode,
        string CorrelationId) : WebhookOutcomeDto(CorrelationId);
}

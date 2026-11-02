using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Domain.Errors;

public abstract class WebhookValidationException : ESignException
{
    protected WebhookValidationException(
        WebhookValidationCategory category,
        string correlationId,
        string? matchedTransactionId,
        DocumentFormat format,
        string message)
        : base(ESignErrorCategory.WebhookValidation, rawCode: null, detail: message, correlationId, inner: null, format: format)
    {
        WebhookCategory = category;
        MatchedTransactionId = matchedTransactionId;
    }

    public WebhookValidationCategory WebhookCategory { get; }
    public string? MatchedTransactionId { get; }
}

public sealed class MalformedEnvelopeException : WebhookValidationException
{
    public MalformedEnvelopeException(string correlationId, string message)
        : base(WebhookValidationCategory.MalformedEnvelope, correlationId, matchedTransactionId: null, DocumentFormat.Unknown, message)
    {
    }
}

public sealed class ClientIdMismatchException : WebhookValidationException
{
    public ClientIdMismatchException(string correlationId, string message)
        : base(WebhookValidationCategory.ClientIdMismatch, correlationId, matchedTransactionId: null, DocumentFormat.Unknown, message)
    {
    }
}

public sealed class UnknownTransactionException : WebhookValidationException
{
    public UnknownTransactionException(string correlationId, string transactionId, string message)
        : base(WebhookValidationCategory.UnknownTransaction, correlationId, matchedTransactionId: transactionId, DocumentFormat.Unknown, message)
    {
    }
}

public sealed class IncompleteSuccessEnvelopeException : WebhookValidationException
{
    public IncompleteSuccessEnvelopeException(string correlationId, string transactionId, DocumentFormat format, string message)
        : base(WebhookValidationCategory.IncompleteSuccessEnvelope, correlationId, matchedTransactionId: transactionId, format, message)
    {
    }
}

public sealed class DocumentIdMismatchException : WebhookValidationException
{
    public DocumentIdMismatchException(string correlationId, string transactionId, DocumentFormat format, string message)
        : base(WebhookValidationCategory.DocumentIdMismatch, correlationId, matchedTransactionId: transactionId, format, message)
    {
    }
}

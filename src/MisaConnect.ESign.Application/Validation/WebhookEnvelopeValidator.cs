using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.Validation;

public sealed class WebhookEnvelopeValidator
{
    public void AssertClientIdMatch(WebhookEnvelope envelope, string configuredClientId, string correlationId)
    {
        if (!StringComparer.Ordinal.Equals(envelope.ClientId, configuredClientId))
        {
            throw new ClientIdMismatchException(
                correlationId,
                $"clientId '{envelope.ClientId}' does not match configured ClientId");
        }
    }

    public void AssertSuccessShapeIsValid(WebhookEnvelope envelope, SigningSession session, string correlationId)
    {
        if (envelope.Signatures.Count == 0)
        {
            throw new IncompleteSuccessEnvelopeException(
                correlationId,
                envelope.TransactionId,
                session.Format,
                "status=SUCCESS but signatures[] is empty");
        }
        var recordedIds = new HashSet<string>(session.RecordedDocumentIds, StringComparer.Ordinal);
        foreach (var sig in envelope.Signatures)
        {
            if (!recordedIds.Contains(sig.DocumentId))
            {
                throw new DocumentIdMismatchException(
                    correlationId,
                    envelope.TransactionId,
                    session.Format,
                    $"signatures[].documentId '{sig.DocumentId}' not in recorded session documents");
            }
        }
    }
}

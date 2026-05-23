namespace MisaConnect.ESign.Domain.Webhook;

public enum WebhookValidationCategory : byte
{
    None = 0,
    MalformedEnvelope = 1,
    ClientIdMismatch = 2,
    UnknownTransaction = 3,
    IncompleteSuccessEnvelope = 4,
    DocumentIdMismatch = 5,
}

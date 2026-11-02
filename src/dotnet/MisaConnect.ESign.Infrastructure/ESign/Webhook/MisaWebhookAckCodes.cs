namespace MisaConnect.ESign.Infrastructure.ESign.Webhook;

internal static class MisaWebhookAckCodes
{
    public const string Success = "0";
    public const string MalformedEnvelope = "webhook.malformed";
    public const string ClientIdMismatch = "webhook.client_id_mismatch";
    public const string UnknownTransaction = "webhook.unknown_transaction";
    public const string IncompleteSuccessEnvelope = "webhook.incomplete_success";
    public const string DocumentIdMismatch = "webhook.document_id_mismatch";
    public const string FinalizeFailed = "webhook.finalize_failed";
}

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Infrastructure.ESign.Webhook;

internal sealed record WebhookEnvelopeDto(
    [property: JsonPropertyName("messageId")] string? MessageId,
    [property: JsonPropertyName("clientId")] string? ClientId,
    [property: JsonPropertyName("extraData")] Dictionary<string, JsonElement>? ExtraData,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("transactionId")] string? TransactionId,
    [property: JsonPropertyName("signatures")] List<WebhookSignatureDto>? Signatures)
{
    public WebhookEnvelope ToDomain(string correlationId)
    {
        if (string.IsNullOrEmpty(MessageId))
        {
            throw new MalformedEnvelopeException(correlationId, "Webhook envelope is missing messageId.");
        }
        if (string.IsNullOrEmpty(ClientId))
        {
            throw new MalformedEnvelopeException(correlationId, "Webhook envelope is missing clientId.");
        }
        if (string.IsNullOrEmpty(TransactionId))
        {
            throw new MalformedEnvelopeException(correlationId, "Webhook envelope is missing transactionId.");
        }
        var status = ParseStatus(Status, correlationId);

        var signatures = (Signatures ?? new List<WebhookSignatureDto>())
            .Select(s => new WebhookSignature(s.DocumentId ?? string.Empty, s.Signature ?? string.Empty))
            .ToList();

        IReadOnlyDictionary<string, JsonElement>? extra = ExtraData is null
            ? null
            : new ReadOnlyDictionary<string, JsonElement>(ExtraData);

        return new WebhookEnvelope(
            MessageId: MessageId,
            ClientId: ClientId,
            ExtraData: extra,
            Status: status,
            ErrorCode: ErrorCode,
            TransactionId: TransactionId,
            Signatures: signatures);
    }

    private static WebhookStatus ParseStatus(string? value, string correlationId) => value switch
    {
        "SUCCESS" => WebhookStatus.Success,
        "FAILED" => WebhookStatus.Failed,
        "CANCELLED" => WebhookStatus.Cancelled,
        _ => throw new MalformedEnvelopeException(correlationId, $"Unknown webhook status '{value ?? "<null>"}'."),
    };
}

internal sealed record WebhookSignatureDto(
    [property: JsonPropertyName("documentId")] string? DocumentId,
    [property: JsonPropertyName("signature")] string? Signature);

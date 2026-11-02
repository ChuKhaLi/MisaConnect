using System.Net;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.Errors;

/// <summary>
/// Translates a MISA <see cref="ResponseError"/> envelope (or its absence) into
/// a typed <see cref="ESignException"/> subclass per the slice-1 / slice-3 error
/// mapping contract. The mapper is endpoint-aware so the same MISA-side code
/// lands on the right typed exception (e.g. "InvalidCertificate" on hash vs.
/// on attachment); slice 3 adds the <paramref name="requestedFormat"/>
/// parameter so every typed exception also carries the document format the
/// consumer requested.
/// </summary>
public static class ESignErrorMapper
{
    public const string EndpointLogin = "api/auth/api/v1/auth/login-api";
    public const string EndpointRefresh = "webdev/api/auth/api/v1/auth/refreshtoken";
    public const string EndpointCertificates = "external/esrm/service/general/api/v1/Certificates/by-userId";
    public const string EndpointHash = "external/esrm/service/document/api/v1/documents/hash";
    public const string EndpointSignHash = "external/esrm/service/signing/api/v1/Signing/hash";
    public const string EndpointSignStatus = "external/esrm/service/signing/api/v1/Signing/status";
    public const string EndpointAttachment = "external/esrm/service/document/api/v1/documents/attachment";

    public static ESignException Map(
        string endpoint,
        int statusCode,
        ResponseError? envelope,
        string correlationId,
        bool includeRawErrorMessage = false,
        string? transactionId = null,
        int? attemptCount = null,
        HttpStatusCode? lastStatusCode = null,
        string? userName = null,
        DocumentFormat requestedFormat = DocumentFormat.Pdf)
        => Map(
            endpoint,
            statusCode,
            envelope,
            correlationId,
            includeRawErrorMessage,
            transactionId,
            attemptCount,
            lastStatusCode,
            userName,
            requestedFormat,
            validationFailuresDetail: null);

    /// <summary>
    /// Slice 007 overload: <paramref name="validationFailuresDetail"/> is a
    /// pre-rendered, sanitized string of MISA's per-property failures. It feeds
    /// only the human-readable detail (under <paramref name="includeRawErrorMessage"/>)
    /// and is never used for error-code synthesis, so synthesized codes are
    /// unchanged. Internal so the public surface stays stable (patch release).
    /// </summary>
    internal static ESignException Map(
        string endpoint,
        int statusCode,
        ResponseError? envelope,
        string correlationId,
        bool includeRawErrorMessage,
        string? transactionId,
        int? attemptCount,
        HttpStatusCode? lastStatusCode,
        string? userName,
        DocumentFormat requestedFormat,
        string? validationFailuresDetail)
    {
        var rawCode = envelope?.ErrorCode;
        var detail = BuildDetail(endpoint, rawCode, envelope, includeRawErrorMessage, validationFailuresDetail);

        if (statusCode == (int)HttpStatusCode.Unauthorized && !IsAuthEndpoint(endpoint))
        {
            return new AuthenticationFailedException(rawCode, detail, correlationId, requires2FA: false, format: requestedFormat);
        }

        if (IsTransportFailure(statusCode))
        {
            return new ESignTransportException(
                lastStatusCode: lastStatusCode ?? (HttpStatusCode)statusCode,
                attemptCount: attemptCount ?? 1,
                detail: detail,
                correlationId: correlationId,
                format: requestedFormat);
        }

        return endpoint switch
        {
            EndpointLogin => MapLogin(rawCode, detail, correlationId, userName ?? string.Empty, requestedFormat),
            EndpointRefresh => new AuthenticationFailedException(rawCode, detail, correlationId, requires2FA: false, format: requestedFormat),
            EndpointCertificates => new ESignGeneralException(ESignErrorCategory.CertificateLookupFailed, rawCode ?? "EmptyErrorCode", detail, correlationId, format: requestedFormat),
            EndpointHash => MapHash(rawCode, envelope, detail, correlationId, requestedFormat),
            EndpointSignHash => MapSignHash(rawCode, envelope, detail, correlationId, requestedFormat),
            EndpointSignStatus => new ESignGeneralException(ESignErrorCategory.StatusLookupFailed, rawCode ?? "EmptyErrorCode", detail, correlationId, format: requestedFormat),
            EndpointAttachment => MapAttachment(rawCode, envelope, detail, correlationId, requestedFormat),
            _ => new ESignGeneralException(ESignErrorCategory.MisaUnknown, rawCode ?? "EmptyErrorCode", detail, correlationId, format: requestedFormat),
        };
    }

    private static bool IsAuthEndpoint(string endpoint) =>
        endpoint == EndpointLogin || endpoint == EndpointRefresh;

    private static bool IsTransportFailure(int statusCode) =>
        statusCode == 429 || statusCode >= 500;

    private static AuthenticationFailedException MapLogin(string? rawCode, string detail, string correlationId, string userName, DocumentFormat requestedFormat)
    {
        var requires2FA = string.Equals(rawCode, "122", StringComparison.OrdinalIgnoreCase);
        return new AuthenticationFailedException(
            rawCode,
            detail,
            correlationId,
            requires2FA,
            username: requires2FA ? userName : string.Empty,
            format: requestedFormat);
    }

    private static ESignException MapHash(string? rawCode, ResponseError? envelope, string detail, string correlationId, DocumentFormat requestedFormat)
    {
        var synthesized = SynthesizeHashCodeForFormat(rawCode, envelope, requestedFormat);
        return new ESignGeneralException(ESignErrorCategory.HashRejected, synthesized, detail, correlationId, format: requestedFormat);
    }

    private static ESignException MapSignHash(string? rawCode, ResponseError? envelope, string detail, string correlationId, DocumentFormat requestedFormat)
    {
        var requiresSetup = ContainsAny(envelope?.DevMsg, "not connected", "not set up", "remote signing account") ||
                            ContainsAny(envelope?.UserMsg, "chưa kết nối", "chưa thiết lập");
        return new SignRejectedException(rawCode, detail, correlationId, requiresUserCertSetup: requiresSetup, format: requestedFormat);
    }

    private static ESignException MapAttachment(string? rawCode, ResponseError? envelope, string detail, string correlationId, DocumentFormat requestedFormat)
    {
        var synthesized = SynthesizeAttachmentCodeForFormat(rawCode, envelope, requestedFormat);
        return new ESignGeneralException(ESignErrorCategory.AttachmentRejected, synthesized, detail, correlationId, format: requestedFormat);
    }

    private static string SynthesizeHashCodeForFormat(string? rawCode, ResponseError? envelope, DocumentFormat requestedFormat)
    {
        var probe = CombinedProbe(rawCode, envelope);
        if (probe is null) return "EmptyErrorCode";

        if (requestedFormat == DocumentFormat.Xml &&
            Contains(probe, "xml") &&
            (Contains(probe, "malformed") || Contains(probe, "invalid") || Contains(probe, "không hợp lệ")))
        {
            return "InvalidXmlInput";
        }
        if (Contains(probe, "unsupported") || Contains(probe, "variant") || Contains(probe, "format not supported"))
        {
            return "UnsupportedDocumentVariant";
        }
        return SynthesizeHashCode(rawCode, envelope);
    }

    private static string SynthesizeAttachmentCodeForFormat(string? rawCode, ResponseError? envelope, DocumentFormat requestedFormat)
    {
        var probe = CombinedProbe(rawCode, envelope);
        if (probe is null) return "EmptyErrorCode";

        if ((requestedFormat == DocumentFormat.Word || requestedFormat == DocumentFormat.Excel) &&
            (Contains(probe, "mainDom") || Contains(probe, "main dom") || Contains(probe, "missing main")))
        {
            return "MissingMainDom";
        }
        if ((requestedFormat == DocumentFormat.Xml || requestedFormat == DocumentFormat.Word || requestedFormat == DocumentFormat.Excel) &&
            (Contains(probe, "signatureId") || Contains(probe, "signature id") || Contains(probe, "missing signature")))
        {
            return "MissingSignatureId";
        }
        if (Contains(probe, "unsupported") || Contains(probe, "variant") || Contains(probe, "format not supported"))
        {
            return "UnsupportedDocumentVariant";
        }
        return SynthesizeAttachmentCode(rawCode, envelope);
    }

    private static string? CombinedProbe(string? rawCode, ResponseError? envelope)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(rawCode)) parts.Add(rawCode);
        if (!string.IsNullOrEmpty(envelope?.DevMsg)) parts.Add(envelope!.DevMsg!);
        if (!string.IsNullOrEmpty(envelope?.UserMsg)) parts.Add(envelope!.UserMsg!);
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string SynthesizeHashCode(string? rawCode, ResponseError? envelope)
    {
        var probe = rawCode ?? envelope?.DevMsg ?? envelope?.UserMsg;
        if (probe is null) return "EmptyErrorCode";
        if (Contains(probe, "Cert") || Contains(probe, "Certificate")) return "InvalidCertificate";
        if (Contains(probe, "Hash") || Contains(probe, "Digest")) return "InvalidHash";
        if (Contains(probe, "File") || Contains(probe, "Document")) return "InvalidDocument";
        if (Contains(probe, "Signature")) return "InvalidSignatureInfo";
        return rawCode ?? "EmptyErrorCode";
    }

    private static string SynthesizeAttachmentCode(string? rawCode, ResponseError? envelope)
    {
        var probe = rawCode ?? envelope?.DevMsg ?? envelope?.UserMsg;
        if (probe is null) return "EmptyErrorCode";
        if (Contains(probe, "Signature")) return "InvalidSignature";
        if (Contains(probe, "Cert")) return "InvalidCertificate";
        if (Contains(probe, "Doc") || Contains(probe, "Hash")) return "InvalidHashInputs";
        return rawCode ?? "EmptyErrorCode";
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool ContainsAny(string? haystack, params string[] needles)
    {
        if (haystack is null) return false;
        foreach (var n in needles)
        {
            if (haystack.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static string BuildDetail(
        string endpoint,
        string? rawCode,
        ResponseError? envelope,
        bool includeRawErrorMessage,
        string? validationFailuresDetail = null)
    {
        var summary = $"MISA returned errorCode={rawCode ?? "<none>"} on {endpoint}.";
        if (!includeRawErrorMessage) return summary;

        var parts = new List<string>(4) { summary };
        if (envelope is not null)
        {
            if (!string.IsNullOrWhiteSpace(envelope.UserMsg)) parts.Add($"userMsg={envelope.UserMsg}");
            if (!string.IsNullOrWhiteSpace(envelope.DevMsg)) parts.Add($"devMsg={envelope.DevMsg}");
        }
        if (!string.IsNullOrWhiteSpace(validationFailuresDetail)) parts.Add(validationFailuresDetail);
        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Per [contracts/error-mapping.md A.11], maps a typed webhook-validation
    /// failure to the namespaced ACK <c>errorCode</c> the sample API returns to
    /// MISA. Wire-side success uses <c>"0"</c>; failures use the
    /// <c>"webhook.&lt;category&gt;"</c> codes.
    /// </summary>
    public static WebhookAck MapWebhookValidationToAck(WebhookValidationException ex)
    {
        return ex.WebhookCategory switch
        {
            WebhookValidationCategory.MalformedEnvelope => WebhookAck.Failure(
                "webhook.malformed", "Inbound envelope failed shape validation.", "Webhook payload was malformed."),
            WebhookValidationCategory.ClientIdMismatch => WebhookAck.Failure(
                "webhook.client_id_mismatch", "clientId did not match configured value.", "Webhook clientId mismatch."),
            WebhookValidationCategory.UnknownTransaction => WebhookAck.Failure(
                "webhook.unknown_transaction", "No session recorded for that transactionId.", "Webhook transaction not recognized."),
            WebhookValidationCategory.IncompleteSuccessEnvelope => WebhookAck.Failure(
                "webhook.incomplete_success", "status=SUCCESS but signatures[] was empty or invalid.", "Webhook payload was incomplete."),
            WebhookValidationCategory.DocumentIdMismatch => WebhookAck.Failure(
                "webhook.document_id_mismatch", "signatures[].documentId did not match session.", "Webhook document mismatch."),
            _ => WebhookAck.Failure(
                "webhook.malformed", "Unspecified webhook validation failure.", "Webhook payload was invalid."),
        };
    }

    public static ESignException MapStatusTerminal(
        string transactionId,
        SignStatus status,
        string? rawCode,
        string? errorDescription,
        string correlationId,
        DocumentFormat requestedFormat = DocumentFormat.Pdf)
    {
        var detail = status switch
        {
            SignStatus.FAILED => $"Signing/status returned FAILED (errorCode={rawCode ?? "<none>"}).",
            SignStatus.CANCELLED => $"Signing/status returned CANCELLED (errorCode={rawCode ?? "<none>"}).",
            _ => $"Signing/status returned an unknown status (errorCode={rawCode ?? "UnknownStatus"}).",
        };
        if (!string.IsNullOrEmpty(errorDescription))
        {
            detail += $" {errorDescription}";
        }
        return new SignTerminalStateException(status, transactionId, rawCode, detail, correlationId, format: requestedFormat);
    }
}

using System.Net;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.Errors;

/// <summary>
/// Translates a MISA <see cref="ResponseError"/> envelope (or its absence) into
/// a typed <see cref="ESignException"/> subclass per the slice-1 error mapping
/// contract. The mapper is endpoint-aware so the same MISA-side code lands on
/// the right typed exception (e.g. "InvalidCertificate" on hash vs. on
/// attachment).
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
        HttpStatusCode? lastStatusCode = null)
    {
        var rawCode = envelope?.ErrorCode;
        var detail = BuildDetail(endpoint, rawCode, envelope, includeRawErrorMessage);

        if (statusCode == (int)HttpStatusCode.Unauthorized && !IsAuthEndpoint(endpoint))
        {
            return new AuthenticationFailedException(rawCode, detail, correlationId, requires2FA: false);
        }

        if (IsTransportFailure(statusCode))
        {
            return new ESignTransportException(
                lastStatusCode: lastStatusCode ?? (HttpStatusCode)statusCode,
                attemptCount: attemptCount ?? 1,
                detail: detail,
                correlationId: correlationId);
        }

        return endpoint switch
        {
            EndpointLogin => MapLogin(rawCode, detail, correlationId),
            EndpointRefresh => new AuthenticationFailedException(rawCode, detail, correlationId, requires2FA: false),
            EndpointCertificates => new ESignGeneralException(ESignErrorCategory.CertificateLookupFailed, rawCode ?? "EmptyErrorCode", detail, correlationId),
            EndpointHash => MapHash(rawCode, envelope, detail, correlationId),
            EndpointSignHash => MapSignHash(rawCode, envelope, detail, correlationId),
            EndpointSignStatus => new ESignGeneralException(ESignErrorCategory.StatusLookupFailed, rawCode ?? "EmptyErrorCode", detail, correlationId),
            EndpointAttachment => MapAttachment(rawCode, envelope, detail, correlationId),
            _ => new ESignGeneralException(ESignErrorCategory.MisaUnknown, rawCode ?? "EmptyErrorCode", detail, correlationId),
        };
    }

    private static bool IsAuthEndpoint(string endpoint) =>
        endpoint == EndpointLogin || endpoint == EndpointRefresh;

    private static bool IsTransportFailure(int statusCode) =>
        statusCode == 429 || statusCode >= 500;

    private static AuthenticationFailedException MapLogin(string? rawCode, string detail, string correlationId)
    {
        var requires2FA = string.Equals(rawCode, "122", StringComparison.OrdinalIgnoreCase);
        return new AuthenticationFailedException(rawCode, detail, correlationId, requires2FA);
    }

    private static ESignException MapHash(string? rawCode, ResponseError? envelope, string detail, string correlationId)
    {
        var synthesized = SynthesizeHashCode(rawCode, envelope);
        return new ESignGeneralException(ESignErrorCategory.HashRejected, synthesized, detail, correlationId);
    }

    private static ESignException MapSignHash(string? rawCode, ResponseError? envelope, string detail, string correlationId)
    {
        var requiresSetup = ContainsAny(envelope?.DevMsg, "not connected", "not set up", "remote signing account") ||
                            ContainsAny(envelope?.UserMsg, "chưa kết nối", "chưa thiết lập");
        return new SignRejectedException(rawCode, detail, correlationId, requiresUserCertSetup: requiresSetup);
    }

    private static ESignException MapAttachment(string? rawCode, ResponseError? envelope, string detail, string correlationId)
    {
        var synthesized = SynthesizeAttachmentCode(rawCode, envelope);
        return new ESignGeneralException(ESignErrorCategory.AttachmentRejected, synthesized, detail, correlationId);
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

    private static string BuildDetail(string endpoint, string? rawCode, ResponseError? envelope, bool includeRawErrorMessage)
    {
        var summary = $"MISA returned errorCode={rawCode ?? "<none>"} on {endpoint}.";
        if (!includeRawErrorMessage || envelope is null) return summary;

        var parts = new List<string>(3) { summary };
        if (!string.IsNullOrWhiteSpace(envelope.UserMsg)) parts.Add($"userMsg={envelope.UserMsg}");
        if (!string.IsNullOrWhiteSpace(envelope.DevMsg)) parts.Add($"devMsg={envelope.DevMsg}");
        return string.Join(" | ", parts);
    }

    public static ESignException MapStatusTerminal(
        string transactionId,
        SignStatus status,
        string? rawCode,
        string? errorDescription,
        string correlationId)
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
        return new SignTerminalStateException(status, transactionId, rawCode, detail, correlationId);
    }
}

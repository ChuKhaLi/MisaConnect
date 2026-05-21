using System.Net;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Application.Errors;

/// <summary>
/// Translates MISA responses on <c>/two-factor-auth</c> and
/// <c>/resend-otp-auth</c> into the typed slice-2 outputs. Two-layered for
/// <c>/two-factor-auth</c>: canonical errorCode dispatch first, then a
/// substring keyword fallback over <c>errorCode + devMsg + userMsg</c>. The
/// resend endpoint returns a typed result instead of throwing.
/// </summary>
public static class OtpErrorMapper
{
    public const string EndpointTwoFactorAuth = "api/auth/api/v1/auth/two-factor-auth";
    public const string EndpointResendOtp = "webdev/api/auth/api/v1/auth/resend-otp-auth";

    public static ESignException MapTwoFactor(
        int statusCode,
        ResponseError? envelope,
        string correlationId,
        bool includeRawErrorMessage = false,
        HttpStatusCode? lastStatusCode = null,
        int? attemptCount = null,
        string? userName = null)
    {
        if (IsTransportFailure(statusCode))
        {
            return new ESignTransportException(
                lastStatusCode: lastStatusCode ?? (HttpStatusCode)statusCode,
                attemptCount: attemptCount ?? 1,
                detail: BuildDetail(EndpointTwoFactorAuth, envelope?.ErrorCode, envelope, includeRawErrorMessage),
                correlationId: correlationId);
        }

        var rawCode = envelope?.ErrorCode;
        var detail = BuildDetail(EndpointTwoFactorAuth, rawCode, envelope, includeRawErrorMessage);

        if (!string.IsNullOrEmpty(rawCode))
        {
            if (string.Equals(rawCode, "122", StringComparison.OrdinalIgnoreCase))
            {
                return new AuthenticationFailedException(
                    rawCode,
                    detail,
                    correlationId,
                    requires2FA: true,
                    username: userName ?? string.Empty);
            }
            if (string.Equals(rawCode, "1001", StringComparison.OrdinalIgnoreCase))
            {
                return new InvalidOtpException(rawCode, detail, correlationId);
            }
            if (string.Equals(rawCode, "1002", StringComparison.OrdinalIgnoreCase))
            {
                return new ExpiredOtpException(rawCode, detail, correlationId);
            }
            if (string.Equals(rawCode, "1003", StringComparison.OrdinalIgnoreCase))
            {
                return new ExhaustedOtpAttemptsException(rawCode, detail, correlationId);
            }
        }

        var probe = ((rawCode ?? string.Empty)
            + " " + (envelope?.DevMsg ?? string.Empty)
            + " " + (envelope?.UserMsg ?? string.Empty))
            .ToLowerInvariant();

        if (ContainsAny(probe, InvalidOtpKeywords))
        {
            return new InvalidOtpException(rawCode, detail, correlationId);
        }
        if (ContainsAny(probe, ExpiredOtpKeywords))
        {
            return new ExpiredOtpException(rawCode, detail, correlationId);
        }
        if (ContainsAny(probe, ExhaustedOtpKeywords))
        {
            return new ExhaustedOtpAttemptsException(rawCode, detail, correlationId);
        }

        return new OtpRejectedException(rawCode, detail, correlationId);
    }

    public static OtpResendResult MapResendResult(
        int statusCode,
        ResponseError? envelope,
        string correlationId,
        bool includeRawErrorMessage = false)
    {
        _ = includeRawErrorMessage;
        var success = statusCode is >= 200 and < 300 && envelope is null or { Error: false };
        if (success)
        {
            return new OtpResendResult(
                Success: true,
                RawCode: null,
                UserMsg: null,
                DevMsg: null,
                CorrelationId: correlationId);
        }

        if (envelope is null)
        {
            return new OtpResendResult(
                Success: false,
                RawCode: "EmptyErrorCode",
                UserMsg: null,
                DevMsg: null,
                CorrelationId: correlationId);
        }

        return new OtpResendResult(
            Success: false,
            RawCode: envelope.ErrorCode,
            UserMsg: envelope.UserMsg,
            DevMsg: envelope.DevMsg,
            CorrelationId: correlationId);
    }

    private static readonly string[] InvalidOtpKeywords = new[]
    {
        "invalid otp",
        "wrong otp",
        "otp incorrect",
        "otp wrong",
        "sai mã",
        "không đúng",
        "otp không hợp lệ",
    };

    private static readonly string[] ExpiredOtpKeywords = new[]
    {
        "expired",
        "hết hạn",
        "quá hạn",
        "timed out",
    };

    private static readonly string[] ExhaustedOtpKeywords = new[]
    {
        "max attempts",
        "exceeded",
        "vượt quá số lần",
        "too many attempts",
        "locked",
    };

    private static bool ContainsAny(string haystack, string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static bool IsTransportFailure(int statusCode) =>
        statusCode == 429 || statusCode >= 500;

    private static string BuildDetail(string endpoint, string? rawCode, ResponseError? envelope, bool includeRawErrorMessage)
    {
        var summary = $"MISA returned errorCode={rawCode ?? "<none>"} on {endpoint}.";
        if (!includeRawErrorMessage || envelope is null) return summary;

        var parts = new List<string>(3) { summary };
        if (!string.IsNullOrWhiteSpace(envelope.UserMsg)) parts.Add($"userMsg={envelope.UserMsg}");
        if (!string.IsNullOrWhiteSpace(envelope.DevMsg)) parts.Add($"devMsg={envelope.DevMsg}");
        return string.Join(" | ", parts);
    }
}

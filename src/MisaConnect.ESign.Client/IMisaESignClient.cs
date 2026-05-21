using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Client;

public interface IMisaESignClient
{
    /// <summary>
    /// Sign a PDF end-to-end via MISA eSign RemoteSigning. Orchestrates login
    /// (or cached-token reuse), certificate selection, server-side hashing,
    /// signing, status polling, and signature attachment. Returns the signed
    /// PDF bytes.
    /// </summary>
    Task<SignPdfResultDto> SignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Complete an in-progress 2FA challenge. The consumer must have caught an
    /// <see cref="AuthenticationFailedException"/> with
    /// <see cref="AuthenticationFailedException.Requires2FA"/> == true from a
    /// preceding call (typically <see cref="SignPdfAsync"/>); the userName from
    /// that challenge is captured automatically. On success, the SDK caches the
    /// returned tokens and the next call resumes signing without further 2FA
    /// prompts.
    /// </summary>
    /// <exception cref="InvalidOperationException">No pending 2FA challenge captured for the current execution context.</exception>
    /// <exception cref="InvalidOtpException">The submitted OTP value is wrong.</exception>
    /// <exception cref="ExpiredOtpException">The submitted OTP value is stale.</exception>
    /// <exception cref="ExhaustedOtpAttemptsException">MISA refused further attempts on this challenge.</exception>
    /// <exception cref="OtpRejectedException">Any other typed OTP rejection.</exception>
    /// <exception cref="AuthenticationFailedException">MISA re-issued the 2FA challenge on the /two-factor-auth response itself — the consumer must restart from a fresh /login-api call by retrying SignPdfAsync.</exception>
    /// <exception cref="ESignTransportException">Transport-retry budget exhausted.</exception>
    Task SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken ct = default);

    /// <summary>
    /// Ask MISA to redeliver the OTP for the in-progress 2FA challenge. The
    /// userName from the captured challenge is threaded into the request body.
    /// Defaults <paramref name="language"/> to
    /// <c>MisaESignOptions.Otp.DefaultResendLanguage</c> (which defaults to
    /// "en-US"). The SDK does NOT validate the language value client-side.
    /// </summary>
    /// <remarks>
    /// Resend failures returned by MISA in a typed response envelope surface
    /// as <c>OtpResendResultDto { Success = false, ... }</c> — this method does
    /// NOT throw on documented MISA rejections. Transport failures DO throw
    /// <see cref="ESignTransportException"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No pending 2FA challenge captured for the current execution context.</exception>
    /// <exception cref="ESignTransportException">Transport-retry budget exhausted.</exception>
    Task<OtpResendResultDto> ResendOtpAsync(string? language = null, CancellationToken ct = default);
}

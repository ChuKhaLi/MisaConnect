namespace MisaConnect.ESign.Application.Abstractions;

public interface IOtpProvider
{
    /// <summary>
    /// Produce an <see cref="OtpSubmission"/> for a captured 2FA challenge.
    /// Called by <c>SignPdf</c> when it encounters a 2FA-required signal AND
    /// the consumer has registered an <see cref="IOtpProvider"/>.
    /// </summary>
    Task<OtpSubmission> ProvideAsync(OtpChallenge challenge, CancellationToken ct);

    /// <summary>
    /// Request OTP re-delivery for a captured 2FA challenge. Called by the SDK
    /// when a transparent-flow provider needs a fresh OTP. Implementations that
    /// don't support resend may throw <see cref="NotSupportedException"/>; the
    /// SDK surfaces that to the consumer as-is.
    /// </summary>
    Task<OtpResendResult> RequestResendAsync(OtpChallenge challenge, string? language, CancellationToken ct);
}

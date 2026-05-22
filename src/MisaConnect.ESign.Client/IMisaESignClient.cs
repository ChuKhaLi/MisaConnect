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
    /// Sign an XML document end-to-end via MISA eSign RemoteSigning. Same
    /// orchestration as <see cref="SignPdfAsync"/>. The request DTO accepts
    /// either <see cref="SignXmlRequestDto.Xml"/> (string — primary; matches
    /// MISA's "text content" semantics for FileToSign per §4.1.2) or
    /// <see cref="SignXmlRequestDto.XmlUtf8Bytes"/> (UTF-8-decoded by the SDK).
    /// Returns the signed XML bytes (UTF-8 encoded) in
    /// <see cref="SignXmlResultDto.SignedXml"/>.
    /// </summary>
    /// <exception cref="ESignGeneralException">Validation failure, hash rejection, attachment rejection (Format == Xml).</exception>
    /// <exception cref="ESignTransportException">Transport-retry budget exhausted (Format == Xml).</exception>
    /// <exception cref="AuthenticationFailedException">Cached token invalid; if Requires2FA, see <see cref="SignInWithOtpAsync"/> (Format == Xml).</exception>
    Task<SignXmlResultDto> SignXmlAsync(SignXmlRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Sign a Word (OOXML .docx) document end-to-end. Mirrors
    /// <see cref="SignPdfAsync"/>; uses MISA's <c>wordDocs</c> per-format array
    /// on <c>/documents/hash</c> and <c>/documents/attachment</c> per
    /// §4.1.1 / §4.6 / §4.15.
    /// </summary>
    Task<SignWordResultDto> SignWordAsync(SignWordRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Sign an Excel (OOXML .xlsx) document end-to-end. Mirrors
    /// <see cref="SignPdfAsync"/>; uses MISA's <c>excelDocs</c> per-format
    /// array per §4.1.1 / §4.6 / §4.15.
    /// </summary>
    Task<SignExcelResultDto> SignExcelAsync(SignExcelRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Complete an in-progress 2FA challenge. The consumer must have caught an
    /// <see cref="AuthenticationFailedException"/> with
    /// <see cref="AuthenticationFailedException.Requires2FA"/> == true from a
    /// preceding facade call; the userName from that challenge is captured
    /// automatically. On success, the SDK caches the returned tokens and the
    /// next call resumes signing without further 2FA prompts.
    /// </summary>
    /// <exception cref="InvalidOperationException">No pending 2FA challenge captured for the current execution context.</exception>
    /// <exception cref="InvalidOtpException">The submitted OTP value is wrong.</exception>
    /// <exception cref="ExpiredOtpException">The submitted OTP value is stale.</exception>
    /// <exception cref="ExhaustedOtpAttemptsException">MISA refused further attempts on this challenge.</exception>
    /// <exception cref="OtpRejectedException">Any other typed OTP rejection.</exception>
    /// <exception cref="AuthenticationFailedException">MISA re-issued the 2FA challenge.</exception>
    /// <exception cref="ESignTransportException">Transport-retry budget exhausted.</exception>
    Task SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken ct = default);

    /// <summary>
    /// Ask MISA to redeliver the OTP for the in-progress 2FA challenge.
    /// </summary>
    /// <exception cref="InvalidOperationException">No pending 2FA challenge captured for the current execution context.</exception>
    /// <exception cref="ESignTransportException">Transport-retry budget exhausted.</exception>
    Task<OtpResendResultDto> ResendOtpAsync(string? language = null, CancellationToken ct = default);
}

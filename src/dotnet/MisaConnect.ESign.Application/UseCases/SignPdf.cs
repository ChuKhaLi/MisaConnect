using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

/// <summary>
/// Intermediate result of the pre-/Signing/status half of a sign orchestration.
/// Slice 4 extracts this so the webhook-mode <c>BeginSignPdf</c> use case can
/// share the same plumbing with the polling-mode <c>SignPdf</c>. FR-089 byte-
/// identical regression is preserved because <see cref="SignPdf"/> still
/// composes this helper + poll + attach.
/// </summary>
internal sealed record BeginSignPdfCoreResult(AccessToken Token, Certificate Cert, PdfHashOutput Hash, SignTransaction Transaction);

public sealed class SignPdf
{
    private readonly EnsureAccessToken _ensureToken;
    private readonly ListActiveCertificates _listCerts;
    private readonly ICertificateSelector _certSelector;
    private readonly HashPdfDocument _hashPdf;
    private readonly SubmitSignHash _submitSignHash;
    private readonly PollSignStatus _pollStatus;
    private readonly AttachSignature _attachSignature;
    private readonly ISystemClock _clock;
    private readonly SignPdfRequestValidator _validator;
    private readonly Func<TimeSpan> _intervalAccessor;
    private readonly Func<TimeSpan> _totalTimeoutAccessor;
    private readonly IOtpProvider? _otpProvider;
    private readonly ExchangeOtp? _exchangeOtp;
    private readonly OtpSubmissionValidator? _otpSubmissionValidator;
    private readonly ICorrelationIdAccessor? _correlation;

    public SignPdf(
        EnsureAccessToken ensureToken,
        ListActiveCertificates listCerts,
        ICertificateSelector certSelector,
        HashPdfDocument hashPdf,
        SubmitSignHash submitSignHash,
        PollSignStatus pollStatus,
        AttachSignature attachSignature,
        ISystemClock clock,
        SignPdfRequestValidator validator,
        Func<TimeSpan> intervalAccessor,
        Func<TimeSpan> totalTimeoutAccessor,
        IOtpProvider? otpProvider = null,
        ExchangeOtp? exchangeOtp = null,
        OtpSubmissionValidator? otpSubmissionValidator = null,
        ICorrelationIdAccessor? correlation = null)
    {
        _ensureToken = ensureToken;
        _listCerts = listCerts;
        _certSelector = certSelector;
        _hashPdf = hashPdf;
        _submitSignHash = submitSignHash;
        _pollStatus = pollStatus;
        _attachSignature = attachSignature;
        _clock = clock;
        _validator = validator;
        _intervalAccessor = intervalAccessor;
        _totalTimeoutAccessor = totalTimeoutAccessor;
        _otpProvider = otpProvider;
        _exchangeOtp = exchangeOtp;
        _otpSubmissionValidator = otpSubmissionValidator;
        _correlation = correlation;
    }

    public Task<SignPdfWorkResult> ExecuteAsync(SignPdfWorkRequest request, CancellationToken ct) =>
        ExecuteCoreAsync(request, allowOtpRecursion: _otpProvider is not null && _exchangeOtp is not null, ct);

    private async Task<SignPdfWorkResult> ExecuteCoreAsync(
        SignPdfWorkRequest request,
        bool allowOtpRecursion,
        CancellationToken ct)
    {
        try
        {
            return await ExecuteSignAsync(request, ct).ConfigureAwait(false);
        }
        catch (AuthenticationFailedException ex) when (allowOtpRecursion && ex.Requires2FA && _otpProvider is not null && _exchangeOtp is not null)
        {
            var challenge = new OtpChallenge(
                UserName: ex.Username,
                CorrelationId: _correlation?.Current ?? ex.CorrelationId);

            var submission = await _otpProvider.ProvideAsync(challenge, ct).ConfigureAwait(false);
            _otpSubmissionValidator?.Validate(submission);

            await _exchangeOtp.ExecuteAsync(ex.Username, submission, ct).ConfigureAwait(false);

            return await ExecuteCoreAsync(request, allowOtpRecursion: false, ct).ConfigureAwait(false);
        }
    }

    internal async Task<BeginSignPdfCoreResult> BeginSignPdfCoreAsync(SignPdfWorkRequest request, CancellationToken ct)
    {
        _validator.Validate(request);

        var token = await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);

        var activeCerts = await _listCerts.ExecuteAsync(token.Value, ct, DocumentFormat.Pdf).ConfigureAwait(false);
        var cert = await _certSelector.SelectAsync(activeCerts, ct).ConfigureAwait(false);

        var hash = await _hashPdf.ExecuteAsync(
            accessToken: token.Value,
            cert: cert,
            pdfBytes: request.Pdf.Bytes,
            documentId: request.DocumentId,
            signatureInfo: request.SignatureInfo,
            ct: ct).ConfigureAwait(false);

        var transaction = await _submitSignHash.ExecuteAsync(
            accessToken: token.Value,
            cert: cert,
            userId: token.UserId,
            dataToBeDisplayed: request.DataToBeDisplayed,
            hash: hash.ToSignHashInput(),
            documentName: request.DocumentName,
            ct: ct).ConfigureAwait(false);

        return new BeginSignPdfCoreResult(token, cert, hash, transaction);
    }

    private async Task<SignPdfWorkResult> ExecuteSignAsync(SignPdfWorkRequest request, CancellationToken ct)
    {
        var core = await BeginSignPdfCoreAsync(request, ct).ConfigureAwait(false);

        var snapshot = await _pollStatus.ExecuteAsync(
            accessToken: core.Token.Value,
            transactionId: core.Transaction.TransactionId,
            interval: _intervalAccessor(),
            totalTimeout: _totalTimeoutAccessor(),
            ct: ct,
            format: DocumentFormat.Pdf).ConfigureAwait(false);

        var signedBytes = await _attachSignature.ExecuteAsync(
            accessToken: core.Token.Value,
            cert: core.Cert,
            hash: core.Hash,
            signatureData: snapshot.FirstSignatureData ?? string.Empty,
            ct: ct).ConfigureAwait(false);

        return new SignPdfWorkResult(
            SignedPdf: new SignedDocument(signedBytes),
            TransactionId: core.Transaction.TransactionId,
            CertificateKeyAlias: core.Cert.KeyAlias,
            CompletedAtUtc: _clock.UtcNow);
    }
}

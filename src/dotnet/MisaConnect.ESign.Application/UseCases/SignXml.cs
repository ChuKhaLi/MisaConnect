using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

internal sealed record BeginSignXmlCoreResult(AccessToken Token, Certificate Cert, XmlHashOutput Hash, SignTransaction Transaction);

public sealed class SignXml
{
    private readonly EnsureAccessToken _ensureToken;
    private readonly ListActiveCertificates _listCerts;
    private readonly ICertificateSelector _certSelector;
    private readonly HashXmlDocument _hashXml;
    private readonly SubmitSignHash _submitSignHash;
    private readonly PollSignStatus _pollStatus;
    private readonly AttachSignatureToXml _attachSignature;
    private readonly ISystemClock _clock;
    private readonly SignXmlRequestValidator _validator;
    private readonly Func<TimeSpan> _intervalAccessor;
    private readonly Func<TimeSpan> _totalTimeoutAccessor;
    private readonly IOtpProvider? _otpProvider;
    private readonly ExchangeOtp? _exchangeOtp;
    private readonly OtpSubmissionValidator? _otpSubmissionValidator;
    private readonly ICorrelationIdAccessor? _correlation;

    public SignXml(
        EnsureAccessToken ensureToken,
        ListActiveCertificates listCerts,
        ICertificateSelector certSelector,
        HashXmlDocument hashXml,
        SubmitSignHash submitSignHash,
        PollSignStatus pollStatus,
        AttachSignatureToXml attachSignature,
        ISystemClock clock,
        SignXmlRequestValidator validator,
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
        _hashXml = hashXml;
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

    public Task<SignXmlWorkResult> ExecuteAsync(SignXmlWorkRequest request, CancellationToken ct) =>
        ExecuteCoreAsync(request, allowOtpRecursion: _otpProvider is not null && _exchangeOtp is not null, ct);

    private async Task<SignXmlWorkResult> ExecuteCoreAsync(
        SignXmlWorkRequest request,
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

    internal async Task<BeginSignXmlCoreResult> BeginSignXmlCoreAsync(SignXmlWorkRequest request, CancellationToken ct)
    {
        _validator.Validate(request);

        var token = await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);

        var activeCerts = await _listCerts.ExecuteAsync(token.Value, ct, DocumentFormat.Xml).ConfigureAwait(false);
        var cert = await _certSelector.SelectAsync(activeCerts, ct).ConfigureAwait(false);

        var hash = await _hashXml.ExecuteAsync(
            accessToken: token.Value,
            cert: cert,
            xmlContent: request.Xml,
            documentId: request.DocumentId,
            signatureContext: request.SignatureContext,
            ct: ct).ConfigureAwait(false);

        var transaction = await _submitSignHash.ExecuteAsync(
            accessToken: token.Value,
            cert: cert,
            userId: token.UserId,
            dataToBeDisplayed: request.DataToBeDisplayed,
            hash: hash.ToSignHashInput(),
            documentName: request.DocumentName,
            ct: ct).ConfigureAwait(false);

        return new BeginSignXmlCoreResult(token, cert, hash, transaction);
    }

    private async Task<SignXmlWorkResult> ExecuteSignAsync(SignXmlWorkRequest request, CancellationToken ct)
    {
        var core = await BeginSignXmlCoreAsync(request, ct).ConfigureAwait(false);

        var snapshot = await _pollStatus.ExecuteAsync(
            accessToken: core.Token.Value,
            transactionId: core.Transaction.TransactionId,
            interval: _intervalAccessor(),
            totalTimeout: _totalTimeoutAccessor(),
            ct: ct,
            format: DocumentFormat.Xml).ConfigureAwait(false);

        var signedBytes = await _attachSignature.ExecuteAsync(
            accessToken: core.Token.Value,
            cert: core.Cert,
            hash: core.Hash,
            signatureData: snapshot.FirstSignatureData ?? string.Empty,
            ct: ct).ConfigureAwait(false);

        return new SignXmlWorkResult(
            SignedXml: new SignedDocument(signedBytes),
            TransactionId: core.Transaction.TransactionId,
            CertificateKeyAlias: core.Cert.KeyAlias,
            CompletedAtUtc: _clock.UtcNow);
    }
}

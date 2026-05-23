using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

internal sealed record BeginSignWordCoreResult(AccessToken Token, Certificate Cert, WordExcelHashOutput Hash, SignTransaction Transaction);

public sealed class SignWord
{
    private readonly EnsureAccessToken _ensureToken;
    private readonly ListActiveCertificates _listCerts;
    private readonly ICertificateSelector _certSelector;
    private readonly HashWordDocument _hashWord;
    private readonly SubmitSignHash _submitSignHash;
    private readonly PollSignStatus _pollStatus;
    private readonly AttachSignatureToWordExcel _attachSignature;
    private readonly ISystemClock _clock;
    private readonly SignWordRequestValidator _validator;
    private readonly Func<TimeSpan> _intervalAccessor;
    private readonly Func<TimeSpan> _totalTimeoutAccessor;
    private readonly IOtpProvider? _otpProvider;
    private readonly ExchangeOtp? _exchangeOtp;
    private readonly OtpSubmissionValidator? _otpSubmissionValidator;
    private readonly ICorrelationIdAccessor? _correlation;

    public SignWord(
        EnsureAccessToken ensureToken,
        ListActiveCertificates listCerts,
        ICertificateSelector certSelector,
        HashWordDocument hashWord,
        SubmitSignHash submitSignHash,
        PollSignStatus pollStatus,
        AttachSignatureToWordExcel attachSignature,
        ISystemClock clock,
        SignWordRequestValidator validator,
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
        _hashWord = hashWord;
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

    public Task<SignWordWorkResult> ExecuteAsync(SignWordWorkRequest request, CancellationToken ct) =>
        ExecuteCoreAsync(request, allowOtpRecursion: _otpProvider is not null && _exchangeOtp is not null, ct);

    private async Task<SignWordWorkResult> ExecuteCoreAsync(
        SignWordWorkRequest request,
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

    internal async Task<BeginSignWordCoreResult> BeginSignWordCoreAsync(SignWordWorkRequest request, CancellationToken ct)
    {
        _validator.Validate(request);

        var token = await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);

        var activeCerts = await _listCerts.ExecuteAsync(token.Value, ct, DocumentFormat.Word).ConfigureAwait(false);
        var cert = await _certSelector.SelectAsync(activeCerts, ct).ConfigureAwait(false);

        var hash = await _hashWord.ExecuteAsync(
            accessToken: token.Value,
            cert: cert,
            wordBytes: request.Word,
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

        return new BeginSignWordCoreResult(token, cert, hash, transaction);
    }

    private async Task<SignWordWorkResult> ExecuteSignAsync(SignWordWorkRequest request, CancellationToken ct)
    {
        var core = await BeginSignWordCoreAsync(request, ct).ConfigureAwait(false);

        var snapshot = await _pollStatus.ExecuteAsync(
            accessToken: core.Token.Value,
            transactionId: core.Transaction.TransactionId,
            interval: _intervalAccessor(),
            totalTimeout: _totalTimeoutAccessor(),
            ct: ct,
            format: DocumentFormat.Word).ConfigureAwait(false);

        var signedBytes = await _attachSignature.ExecuteAsync(
            accessToken: core.Token.Value,
            cert: core.Cert,
            hash: core.Hash,
            signatureData: snapshot.FirstSignatureData ?? string.Empty,
            format: DocumentFormat.Word,
            ct: ct).ConfigureAwait(false);

        return new SignWordWorkResult(
            SignedWord: new SignedDocument(signedBytes),
            TransactionId: core.Transaction.TransactionId,
            CertificateKeyAlias: core.Cert.KeyAlias,
            CompletedAtUtc: _clock.UtcNow);
    }
}

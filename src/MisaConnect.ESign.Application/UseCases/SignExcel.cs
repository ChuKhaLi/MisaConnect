using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class SignExcel
{
    private readonly EnsureAccessToken _ensureToken;
    private readonly ListActiveCertificates _listCerts;
    private readonly ICertificateSelector _certSelector;
    private readonly HashExcelDocument _hashExcel;
    private readonly SubmitSignHash _submitSignHash;
    private readonly PollSignStatus _pollStatus;
    private readonly AttachSignatureToWordExcel _attachSignature;
    private readonly ISystemClock _clock;
    private readonly SignExcelRequestValidator _validator;
    private readonly Func<TimeSpan> _intervalAccessor;
    private readonly Func<TimeSpan> _totalTimeoutAccessor;
    private readonly IOtpProvider? _otpProvider;
    private readonly ExchangeOtp? _exchangeOtp;
    private readonly OtpSubmissionValidator? _otpSubmissionValidator;
    private readonly ICorrelationIdAccessor? _correlation;

    public SignExcel(
        EnsureAccessToken ensureToken,
        ListActiveCertificates listCerts,
        ICertificateSelector certSelector,
        HashExcelDocument hashExcel,
        SubmitSignHash submitSignHash,
        PollSignStatus pollStatus,
        AttachSignatureToWordExcel attachSignature,
        ISystemClock clock,
        SignExcelRequestValidator validator,
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
        _hashExcel = hashExcel;
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

    public Task<SignExcelWorkResult> ExecuteAsync(SignExcelWorkRequest request, CancellationToken ct) =>
        ExecuteCoreAsync(request, allowOtpRecursion: _otpProvider is not null && _exchangeOtp is not null, ct);

    private async Task<SignExcelWorkResult> ExecuteCoreAsync(
        SignExcelWorkRequest request,
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

    private async Task<SignExcelWorkResult> ExecuteSignAsync(SignExcelWorkRequest request, CancellationToken ct)
    {
        _validator.Validate(request);

        var token = await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);

        var activeCerts = await _listCerts.ExecuteAsync(token.Value, ct, DocumentFormat.Excel).ConfigureAwait(false);
        var cert = await _certSelector.SelectAsync(activeCerts, ct).ConfigureAwait(false);

        var hash = await _hashExcel.ExecuteAsync(
            accessToken: token.Value,
            cert: cert,
            excelBytes: request.Excel,
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

        var snapshot = await _pollStatus.ExecuteAsync(
            accessToken: token.Value,
            transactionId: transaction.TransactionId,
            interval: _intervalAccessor(),
            totalTimeout: _totalTimeoutAccessor(),
            ct: ct,
            format: DocumentFormat.Excel).ConfigureAwait(false);

        var signedBytes = await _attachSignature.ExecuteAsync(
            accessToken: token.Value,
            cert: cert,
            hash: hash,
            signatureData: snapshot.FirstSignatureData ?? string.Empty,
            format: DocumentFormat.Excel,
            ct: ct).ConfigureAwait(false);

        return new SignExcelWorkResult(
            SignedExcel: new SignedDocument(signedBytes),
            TransactionId: transaction.TransactionId,
            CertificateKeyAlias: cert.KeyAlias,
            CompletedAtUtc: _clock.UtcNow);
    }
}

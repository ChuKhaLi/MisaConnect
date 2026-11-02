using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.UseCases;

/// <summary>
/// Webhook-mode initiation use case for PDF. Runs login → cert select → hash →
/// submit; registers a <see cref="SigningSession"/> AFTER <c>/Signing/hash</c>
/// returns per FR-076 / research R-6. Returns the MISA-issued
/// <see cref="BeginResult"/> without polling — the webhook handler completes
/// the flow when MISA pushes the signed-document envelope.
/// </summary>
public sealed class BeginSignPdf
{
    private readonly EnsureAccessToken _ensureToken;
    private readonly ListActiveCertificates _listCerts;
    private readonly ICertificateSelector _certSelector;
    private readonly HashPdfDocument _hashPdf;
    private readonly SubmitSignHash _submitSignHash;
    private readonly ISigningSessionStore _sessionStore;
    private readonly ISystemClock _clock;
    private readonly SignPdfRequestValidator _validator;
    private readonly Func<string> _clientIdAccessor;
    private readonly Func<TimeSpan> _ttlAccessor;
    private readonly IOtpProvider? _otpProvider;
    private readonly ExchangeOtp? _exchangeOtp;
    private readonly OtpSubmissionValidator? _otpSubmissionValidator;
    private readonly ICorrelationIdAccessor? _correlation;
    private readonly ILogger<BeginSignPdf> _logger;

    public BeginSignPdf(
        EnsureAccessToken ensureToken,
        ListActiveCertificates listCerts,
        ICertificateSelector certSelector,
        HashPdfDocument hashPdf,
        SubmitSignHash submitSignHash,
        ISigningSessionStore sessionStore,
        ISystemClock clock,
        SignPdfRequestValidator validator,
        Func<string> clientIdAccessor,
        Func<TimeSpan> ttlAccessor,
        ILogger<BeginSignPdf> logger,
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
        _sessionStore = sessionStore;
        _clock = clock;
        _validator = validator;
        _clientIdAccessor = clientIdAccessor;
        _ttlAccessor = ttlAccessor;
        _otpProvider = otpProvider;
        _exchangeOtp = exchangeOtp;
        _otpSubmissionValidator = otpSubmissionValidator;
        _correlation = correlation;
        _logger = logger;
    }

    public Task<BeginResult> ExecuteAsync(SignPdfWorkRequest request, CancellationToken ct) =>
        ExecuteCoreAsync(request, allowOtpRecursion: _otpProvider is not null && _exchangeOtp is not null, ct);

    private async Task<BeginResult> ExecuteCoreAsync(SignPdfWorkRequest request, bool allowOtpRecursion, CancellationToken ct)
    {
        try
        {
            return await ExecuteInnerAsync(request, ct).ConfigureAwait(false);
        }
        catch (AuthenticationFailedException ex) when (allowOtpRecursion && ex.Requires2FA && _otpProvider is not null && _exchangeOtp is not null)
        {
            var challenge = new OtpChallenge(UserName: ex.Username, CorrelationId: _correlation?.Current ?? ex.CorrelationId);
            var submission = await _otpProvider.ProvideAsync(challenge, ct).ConfigureAwait(false);
            _otpSubmissionValidator?.Validate(submission);
            await _exchangeOtp.ExecuteAsync(ex.Username, submission, ct).ConfigureAwait(false);
            return await ExecuteCoreAsync(request, allowOtpRecursion: false, ct).ConfigureAwait(false);
        }
    }

    private async Task<BeginResult> ExecuteInnerAsync(SignPdfWorkRequest request, CancellationToken ct)
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

        var session = new SigningSession(
            ClientId: _clientIdAccessor(),
            TransactionId: transaction.TransactionId,
            Format: DocumentFormat.Pdf,
            HashPayload: new PerFormatHashPayload.Pdf(hash),
            RecordedDocumentIds: new[] { request.DocumentId },
            CreatedAtUtc: _clock.UtcNow,
            Ttl: _ttlAccessor(),
            ObservedMessageIds: new HashSet<string>(StringComparer.Ordinal),
            CachedSuccess: null);

        await _sessionStore.RegisterAsync(session, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "BeginSignPdf registered session for transactionId {TransactionId} format=Pdf correlationId={CorrelationId}",
            transaction.TransactionId,
            _correlation?.Current ?? string.Empty);

        return new BeginResult(transaction.TransactionId, DocumentFormat.Pdf);
    }
}

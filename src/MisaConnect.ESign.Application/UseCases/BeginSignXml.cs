using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class BeginSignXml
{
    private readonly EnsureAccessToken _ensureToken;
    private readonly ListActiveCertificates _listCerts;
    private readonly ICertificateSelector _certSelector;
    private readonly HashXmlDocument _hashXml;
    private readonly SubmitSignHash _submitSignHash;
    private readonly ISigningSessionStore _sessionStore;
    private readonly ISystemClock _clock;
    private readonly SignXmlRequestValidator _validator;
    private readonly Func<string> _clientIdAccessor;
    private readonly Func<TimeSpan> _ttlAccessor;
    private readonly IOtpProvider? _otpProvider;
    private readonly ExchangeOtp? _exchangeOtp;
    private readonly OtpSubmissionValidator? _otpSubmissionValidator;
    private readonly ICorrelationIdAccessor? _correlation;
    private readonly ILogger<BeginSignXml> _logger;

    public BeginSignXml(
        EnsureAccessToken ensureToken,
        ListActiveCertificates listCerts,
        ICertificateSelector certSelector,
        HashXmlDocument hashXml,
        SubmitSignHash submitSignHash,
        ISigningSessionStore sessionStore,
        ISystemClock clock,
        SignXmlRequestValidator validator,
        Func<string> clientIdAccessor,
        Func<TimeSpan> ttlAccessor,
        ILogger<BeginSignXml> logger,
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

    public Task<BeginResult> ExecuteAsync(SignXmlWorkRequest request, CancellationToken ct) =>
        ExecuteCoreAsync(request, _otpProvider is not null && _exchangeOtp is not null, ct);

    private async Task<BeginResult> ExecuteCoreAsync(SignXmlWorkRequest request, bool allowOtpRecursion, CancellationToken ct)
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
            return await ExecuteCoreAsync(request, false, ct).ConfigureAwait(false);
        }
    }

    private async Task<BeginResult> ExecuteInnerAsync(SignXmlWorkRequest request, CancellationToken ct)
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

        var session = new SigningSession(
            ClientId: _clientIdAccessor(),
            TransactionId: transaction.TransactionId,
            Format: DocumentFormat.Xml,
            HashPayload: new PerFormatHashPayload.Xml(hash),
            RecordedDocumentIds: new[] { request.DocumentId },
            CreatedAtUtc: _clock.UtcNow,
            Ttl: _ttlAccessor(),
            ObservedMessageIds: new HashSet<string>(StringComparer.Ordinal),
            CachedSuccess: null);

        await _sessionStore.RegisterAsync(session, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "BeginSignXml registered session for transactionId {TransactionId} format=Xml correlationId={CorrelationId}",
            transaction.TransactionId,
            _correlation?.Current ?? string.Empty);

        return new BeginResult(transaction.TransactionId, DocumentFormat.Xml);
    }
}

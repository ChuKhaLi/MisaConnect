using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.UseCases;

/// <summary>
/// Webhook finalize. The inbound envelope is only a trigger: MISA's
/// <c>Signing/status</c> decides the outcome. Under the per-(clientId, transactionId)
/// finalize lock:
/// <list type="number">
/// <item>re-read the session (evicted → <see cref="UnknownTransactionException"/>);</item>
/// <item>a cached success or recorded terminal answers the delivery without calling MISA or the hook;</item>
/// <item>a SUCCESS envelope must still pass the shape checks;</item>
/// <item>in Dynamic mode, resolve the session principal's credentials (must match its clientId and userName);</item>
/// <item>ask MISA for the status: SUCCESS → attach MISA's own signature (never the envelope's), cache,
/// deliver, ACK success; FAILED/CANCELLED → record the terminal, deliver MISA's status, ACK success;
/// anything else → <c>webhook.finalize_failed</c>.</item>
/// </list>
/// Any failure in steps 4–5 (including a 2FA challenge — no OTP is requested here) answers
/// <c>webhook.finalize_failed</c>; nothing is cached and the hook is not called, so MISA's retry
/// can finish. Hook exceptions on success/terminal delivery propagate; the cache write precedes the hook.
/// </summary>
public sealed class FinalizeFromWebhook
{
    private static readonly WebhookEnvelopeValidator ShapeValidator = new();

    private readonly EnsureAccessToken _ensureToken;
    private readonly ListActiveCertificates _listCerts;
    private readonly ICertificateSelector _certSelector;
    private readonly AttachSignature _attachPdf;
    private readonly AttachSignatureToXml _attachXml;
    private readonly AttachSignatureToWordExcel _attachWordExcel;
    private readonly ISigningSessionStore _sessionStore;
    private readonly IFinalizeLockOwner _lockOwner;
    private readonly IWebhookDeliveryHook _deliveryHook;
    private readonly ILogger<FinalizeFromWebhook> _logger;
    private readonly Func<string> _successAckCodeAccessor;
    private readonly IMisaESignWireClient _wire;
    private readonly IWebhookCredentialsResolver _credentialsResolver;
    private readonly Func<MisaCredentials, IDisposable> _useCredentials;
    private readonly Func<bool> _isDynamicMode;

    public FinalizeFromWebhook(
        EnsureAccessToken ensureToken,
        ListActiveCertificates listCerts,
        ICertificateSelector certSelector,
        AttachSignature attachPdf,
        AttachSignatureToXml attachXml,
        AttachSignatureToWordExcel attachWordExcel,
        ISigningSessionStore sessionStore,
        IFinalizeLockOwner lockOwner,
        IWebhookDeliveryHook deliveryHook,
        ILogger<FinalizeFromWebhook> logger,
        Func<string> successAckCodeAccessor,
        IMisaESignWireClient wire,
        IWebhookCredentialsResolver credentialsResolver,
        Func<MisaCredentials, IDisposable> useCredentials,
        Func<bool> isDynamicMode)
    {
        _ensureToken = ensureToken;
        _listCerts = listCerts;
        _certSelector = certSelector;
        _attachPdf = attachPdf;
        _attachXml = attachXml;
        _attachWordExcel = attachWordExcel;
        _sessionStore = sessionStore;
        _lockOwner = lockOwner;
        _deliveryHook = deliveryHook;
        _logger = logger;
        _successAckCodeAccessor = successAckCodeAccessor;
        _wire = wire;
        _credentialsResolver = credentialsResolver;
        _useCredentials = useCredentials;
        _isDynamicMode = isDynamicMode;
    }

    public async Task<WebhookHandleResult> RunAsync(SigningSession session, WebhookEnvelope envelope, string correlationId, CancellationToken ct)
    {
        await using var _ = await _lockOwner.AcquireFinalizeLockAsync(envelope.ClientId, envelope.TransactionId, ct).ConfigureAwait(false);

        var refreshed = await _sessionStore.TryGetByTransactionIdAsync(envelope.ClientId, envelope.TransactionId, ct).ConfigureAwait(false)
            ?? throw new UnknownTransactionException(correlationId, envelope.TransactionId, "Session evicted while awaiting finalize lock");

        // State precedence: a settled transaction answers every later delivery, whatever its status.
        if (refreshed.CachedSuccess is { } cached)
        {
            _logger.LogInformation(
                "FinalizeFromWebhook short-circuit on cached success transactionId={TransactionId} correlationId={CorrelationId}",
                envelope.TransactionId, correlationId);
            return new WebhookHandleResult(
                cached.Ack,
                new WebhookOutcome.SuccessWithSignedBytes(envelope.TransactionId, refreshed.Format, cached.SignedBytes, correlationId));
        }
        if (refreshed.CachedTerminal is { } recorded)
        {
            _logger.LogInformation(
                "FinalizeFromWebhook short-circuit on recorded terminal transactionId={TransactionId} status={Status} correlationId={CorrelationId}",
                envelope.TransactionId, recorded.Status, correlationId);
            return new WebhookHandleResult(
                recorded.Ack,
                new WebhookOutcome.TerminalWithoutFinalize(envelope.TransactionId, refreshed.Format, recorded.Status, recorded.MisaErrorCode, correlationId));
        }

        if (envelope.Status == WebhookStatus.Success)
        {
            ShapeValidator.AssertSuccessShapeIsValid(envelope, refreshed, correlationId);
        }

        Confirmed confirmed;
        try
        {
            confirmed = await ConfirmAndAttachAsync(refreshed, correlationId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            var rawCode = (ex as ESignException)?.RawCode;
            // Log the type and raw code only: exception messages may carry document or credential content.
            _logger.LogWarning(
                "FinalizeFromWebhook failure transactionId={TransactionId} format={Format} exType={ExType} rawCode={RawCode} correlationId={CorrelationId}",
                envelope.TransactionId, refreshed.Format, ex.GetType().Name, rawCode, correlationId);

            var failureAck = WebhookAck.Failure(
                "webhook.finalize_failed",
                "Finalize attempt failed; MISA may retry.",
                "Signing finalization failed; please wait for retry.");
            var failure = new WebhookOutcome.FailureWithError(
                envelope.TransactionId,
                refreshed.Format,
                WebhookValidationCategory.None,
                MisaErrorCode: rawCode,
                correlationId);
            return new WebhookHandleResult(failureAck, failure);
        }

        var ack = WebhookAck.Success(_successAckCodeAccessor());

        if (confirmed is Confirmed.Terminal terminal)
        {
            await _sessionStore.CacheTerminalAsync(
                envelope.ClientId, envelope.TransactionId, envelope.MessageId, ack, terminal.Status, terminal.MisaErrorCode, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "FinalizeFromWebhook terminal transactionId={TransactionId} status={Status} correlationId={CorrelationId}",
                envelope.TransactionId, terminal.Status, correlationId);

            var terminalOutcome = new WebhookOutcome.TerminalWithoutFinalize(
                envelope.TransactionId, refreshed.Format, terminal.Status, terminal.MisaErrorCode, correlationId);
            await DeliveryHookFailedException.DeliverAsync(_deliveryHook, terminalOutcome, ct).ConfigureAwait(false);
            return new WebhookHandleResult(ack, terminalOutcome);
        }

        var signedBytes = ((Confirmed.Signed)confirmed).SignedBytes;
        await _sessionStore.CacheSuccessAsync(envelope.ClientId, envelope.TransactionId, envelope.MessageId, ack, signedBytes, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "FinalizeFromWebhook success transactionId={TransactionId} format={Format} signedByteCount={SignedByteCount} correlationId={CorrelationId}",
            envelope.TransactionId, refreshed.Format, signedBytes.Length, correlationId);

        var outcome = new WebhookOutcome.SuccessWithSignedBytes(envelope.TransactionId, refreshed.Format, signedBytes, correlationId);
        await DeliveryHookFailedException.DeliverAsync(_deliveryHook, outcome, ct).ConfigureAwait(false);
        return new WebhookHandleResult(ack, outcome);
    }

    /// <summary>Resolves the principal (Dynamic mode), asks MISA for the status and, on SUCCESS, attaches MISA's signature.</summary>
    private async Task<Confirmed> ConfirmAndAttachAsync(SigningSession session, string correlationId, CancellationToken ct)
    {
        if (!_isDynamicMode())
        {
            return await ConfirmWithMisaAsync(session, correlationId, ct).ConfigureAwait(false);
        }

        if (session.UserName is null)
        {
            throw Failure("MissingPrincipal", "The signing session has no recorded user name; the webhook finalize principal cannot be resolved.", session, correlationId);
        }

        var resolved = await _credentialsResolver.ResolveAsync(session.ClientId, session.UserName, ct).ConfigureAwait(false)
            ?? throw Failure("NoWebhookCredentials", "No credentials were resolved for the webhook finalize principal.", session, correlationId);

        if (!StringComparer.Ordinal.Equals(resolved.ClientId, session.ClientId)
            || !StringComparer.Ordinal.Equals(resolved.UserName, session.UserName))
        {
            // A resolver must not be able to finalize another principal's transaction.
            throw Failure(
                "CredentialsMismatch",
                "The credentials resolved for the webhook finalize do not belong to the principal that began the transaction.",
                session,
                correlationId);
        }

        using (_useCredentials(resolved))
        {
            return await ConfirmWithMisaAsync(session, correlationId, ct).ConfigureAwait(false);
        }
    }

    private async Task<Confirmed> ConfirmWithMisaAsync(SigningSession session, string correlationId, CancellationToken ct)
    {
        var token = await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);
        var status = await _wire.GetSignStatusAsync(token.Value, session.TransactionId, ct).ConfigureAwait(false);

        switch (status.Status)
        {
            case SignStatus.FAILED:
                return new Confirmed.Terminal(WebhookStatus.Failed, status.ErrorCode);
            case SignStatus.CANCELLED:
                return new Confirmed.Terminal(WebhookStatus.Cancelled, status.ErrorCode);
            case SignStatus.SUCCESS:
                break;
            default:
                throw Failure("SignNotFinal", $"MISA Signing/status reports {status.Status}; the webhook cannot be finalized yet.", session, correlationId);
        }

        var signature = status.FirstSignatureData;
        if (string.IsNullOrEmpty(signature))
        {
            throw Failure("MissingSignature", "MISA Signing/status reports SUCCESS without a signature.", session, correlationId);
        }

        var activeCerts = await _listCerts.ExecuteAsync(token.Value, ct, session.Format).ConfigureAwait(false);
        var cert = await _certSelector.SelectAsync(activeCerts, ct).ConfigureAwait(false);

        byte[] signedBytes = session.Format switch
        {
            DocumentFormat.Pdf => await _attachPdf.ExecuteAsync(
                token.Value, cert, ((PerFormatHashPayload.Pdf)session.HashPayload).Output, signature, ct).ConfigureAwait(false),
            DocumentFormat.Xml => await _attachXml.ExecuteAsync(
                token.Value, cert, ((PerFormatHashPayload.Xml)session.HashPayload).Output, signature, ct).ConfigureAwait(false),
            DocumentFormat.Word => await _attachWordExcel.ExecuteAsync(
                token.Value, cert, ((PerFormatHashPayload.Word)session.HashPayload).Output, signature, DocumentFormat.Word, ct).ConfigureAwait(false),
            DocumentFormat.Excel => await _attachWordExcel.ExecuteAsync(
                token.Value, cert, ((PerFormatHashPayload.Excel)session.HashPayload).Output, signature, DocumentFormat.Excel, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported format on session: {session.Format}"),
        };
        return new Confirmed.Signed(signedBytes);
    }

    private static ESignGeneralException Failure(string rawCode, string detail, SigningSession session, string correlationId) =>
        new(ESignErrorCategory.WebhookFinalize, rawCode, detail, correlationId, format: session.Format);

    /// <summary>What MISA's <c>Signing/status</c> confirmed for the transaction.</summary>
    private abstract record Confirmed
    {
        public sealed record Signed(byte[] SignedBytes) : Confirmed;
        public sealed record Terminal(WebhookStatus Status, string? MisaErrorCode) : Confirmed;
    }
}

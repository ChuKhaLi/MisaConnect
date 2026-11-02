using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class FinalizeFromWebhook
{
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
        Func<string> successAckCodeAccessor)
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
    }

    public async Task<WebhookHandleResult> RunAsync(SigningSession session, WebhookEnvelope envelope, string correlationId, CancellationToken ct)
    {
        await using var _ = await _lockOwner.AcquireFinalizeLockAsync(envelope.ClientId, envelope.TransactionId, ct).ConfigureAwait(false);

        var refreshed = await _sessionStore.TryGetByTransactionIdAsync(envelope.ClientId, envelope.TransactionId, ct).ConfigureAwait(false)
            ?? throw new UnknownTransactionException(correlationId, envelope.TransactionId, "Session evicted while awaiting finalize lock");

        if (refreshed.CachedSuccess is { } cached)
        {
            _logger.LogInformation(
                "FinalizeFromWebhook short-circuit on cached success transactionId={TransactionId} correlationId={CorrelationId}",
                envelope.TransactionId, correlationId);
            return new WebhookHandleResult(
                cached.Ack,
                new WebhookOutcome.SuccessWithSignedBytes(envelope.TransactionId, refreshed.Format, cached.SignedBytes, correlationId));
        }

        var token = await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);
        var activeCerts = await _listCerts.ExecuteAsync(token.Value, ct, refreshed.Format).ConfigureAwait(false);
        var cert = await _certSelector.SelectAsync(activeCerts, ct).ConfigureAwait(false);

        var signature = envelope.Signatures[0].Signature;

        try
        {
            byte[] signedBytes = refreshed.Format switch
            {
                DocumentFormat.Pdf => await _attachPdf.ExecuteAsync(
                    token.Value, cert, ((PerFormatHashPayload.Pdf)refreshed.HashPayload).Output, signature, ct).ConfigureAwait(false),
                DocumentFormat.Xml => await _attachXml.ExecuteAsync(
                    token.Value, cert, ((PerFormatHashPayload.Xml)refreshed.HashPayload).Output, signature, ct).ConfigureAwait(false),
                DocumentFormat.Word => await _attachWordExcel.ExecuteAsync(
                    token.Value, cert, ((PerFormatHashPayload.Word)refreshed.HashPayload).Output, signature, DocumentFormat.Word, ct).ConfigureAwait(false),
                DocumentFormat.Excel => await _attachWordExcel.ExecuteAsync(
                    token.Value, cert, ((PerFormatHashPayload.Excel)refreshed.HashPayload).Output, signature, DocumentFormat.Excel, ct).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported format on session: {refreshed.Format}"),
            };

            var ack = WebhookAck.Success(_successAckCodeAccessor());
            await _sessionStore.CacheSuccessAsync(envelope.ClientId, envelope.TransactionId, envelope.MessageId, ack, signedBytes, ct).ConfigureAwait(false);

            var outcome = new WebhookOutcome.SuccessWithSignedBytes(envelope.TransactionId, refreshed.Format, signedBytes, correlationId);
            await _deliveryHook.DeliverAsync(outcome, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "FinalizeFromWebhook success transactionId={TransactionId} format={Format} signedByteCount={SignedByteCount} correlationId={CorrelationId}",
                envelope.TransactionId, refreshed.Format, signedBytes.Length, correlationId);

            return new WebhookHandleResult(ack, outcome);
        }
        catch (ESignException ex)
        {
            _logger.LogWarning(
                "FinalizeFromWebhook failure transactionId={TransactionId} format={Format} exType={ExType} correlationId={CorrelationId}",
                envelope.TransactionId, refreshed.Format, ex.GetType().Name, correlationId);

            var failureAck = WebhookAck.Failure(
                "webhook.finalize_failed",
                "Finalize attempt failed; MISA may retry.",
                "Signing finalization failed; please wait for retry.");

            var outcome = new WebhookOutcome.FailureWithError(
                envelope.TransactionId,
                refreshed.Format,
                WebhookValidationCategory.None,
                MisaErrorCode: ex.RawCode,
                correlationId);

            return new WebhookHandleResult(failureAck, outcome);
        }
    }
}

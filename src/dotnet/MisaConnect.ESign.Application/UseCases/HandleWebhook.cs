using System.ComponentModel;
using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Errors;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class HandleWebhook
{
    private readonly WebhookEnvelopeValidator _validator;
    private readonly ISigningSessionStore _sessionStore;
    private readonly FinalizeFromWebhook _finalizeFromWebhook;
    private readonly IWebhookDeliveryHook _deliveryHook;
    private readonly ICorrelationIdAccessor _correlationAccessor;
    private readonly Func<string> _configuredClientIdAccessor;
    private readonly Func<string> _successAckCodeAccessor;
    private readonly ILogger<HandleWebhook> _logger;
    private readonly Func<bool> _deliverValidationFailuresAccessor;

    /// <summary>
    /// The 2.4 constructor, kept for 2.4 source and binary compatibility. Validation failures
    /// are delivered to the hook (<c>DeliverValidationFailuresToHook = true</c>). Use cases are
    /// normally resolved through <c>AddMisaConnectESign</c>, not constructed directly.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public HandleWebhook(
        WebhookEnvelopeValidator validator,
        ISigningSessionStore sessionStore,
        FinalizeFromWebhook finalizeFromWebhook,
        IWebhookDeliveryHook deliveryHook,
        ICorrelationIdAccessor correlationAccessor,
        Func<string> configuredClientIdAccessor,
        Func<string> successAckCodeAccessor,
        ILogger<HandleWebhook> logger)
        : this(
            validator,
            sessionStore,
            finalizeFromWebhook,
            deliveryHook,
            correlationAccessor,
            configuredClientIdAccessor,
            successAckCodeAccessor,
            logger,
            deliverValidationFailuresAccessor: () => true)
    {
    }

    public HandleWebhook(
        WebhookEnvelopeValidator validator,
        ISigningSessionStore sessionStore,
        FinalizeFromWebhook finalizeFromWebhook,
        IWebhookDeliveryHook deliveryHook,
        ICorrelationIdAccessor correlationAccessor,
        Func<string> configuredClientIdAccessor,
        Func<string> successAckCodeAccessor,
        ILogger<HandleWebhook> logger,
        Func<bool> deliverValidationFailuresAccessor)
    {
        _validator = validator;
        _sessionStore = sessionStore;
        _finalizeFromWebhook = finalizeFromWebhook;
        _deliveryHook = deliveryHook;
        _correlationAccessor = correlationAccessor;
        _configuredClientIdAccessor = configuredClientIdAccessor;
        _successAckCodeAccessor = successAckCodeAccessor;
        _logger = logger;
        _deliverValidationFailuresAccessor = deliverValidationFailuresAccessor;
    }

    /// <summary>
    /// Answers an inbound envelope that failed shape parsing with a <c>webhook.malformed</c> ACK
    /// instead of throwing. <paramref name="malformedField"/> is a JSON field name, safe to log.
    /// </summary>
    public async Task<WebhookHandleResult> HandleMalformedAsync(string? transactionId, string malformedField, CancellationToken ct)
    {
        var correlationId = _correlationAccessor.Current ?? Guid.NewGuid().ToString("N");

        _logger.LogWarning(
            "HandleWebhook malformed envelope field={Field} correlationId={CorrelationId}",
            malformedField, correlationId);

        var ack = ESignErrorMapper.MapWebhookValidationToAck(
            new MalformedEnvelopeException(correlationId, $"Inbound webhook envelope field '{malformedField}' was missing or invalid."));
        var outcome = new WebhookOutcome.FailureWithError(
            transactionId ?? "",
            DocumentFormat.Unknown,
            WebhookValidationCategory.MalformedEnvelope,
            MisaErrorCode: null,
            correlationId);

        await DeliverFailureIfEnabledAsync(outcome, correlationId, ct).ConfigureAwait(false);
        return new WebhookHandleResult(ack, outcome);
    }

    private async Task DeliverFailureIfEnabledAsync(WebhookOutcome outcome, string correlationId, CancellationToken ct)
    {
        if (!_deliverValidationFailuresAccessor()) return;

        try
        {
            await _deliveryHook.DeliverAsync(outcome, ct).ConfigureAwait(false);
        }
        catch (Exception hookEx)
        {
            _logger.LogWarning(hookEx, "Webhook delivery hook threw during failure outcome dispatch correlationId={CorrelationId}", correlationId);
        }
    }

    public async Task<WebhookHandleResult> HandleAsync(WebhookEnvelope envelope, CancellationToken ct)
    {
        var correlationId = _correlationAccessor.Current ?? Guid.NewGuid().ToString("N");

        _logger.LogInformation(
            "HandleWebhook received envelope transactionId={TransactionId} status={Status} correlationId={CorrelationId}",
            envelope.TransactionId, envelope.Status, correlationId);

        DocumentFormat? format = null;
        try
        {
            // Dynamic mode has no configured ClientId: the session lookup by
            // (envelope.ClientId, transactionId) below is then the spoof check.
            var configuredClientId = _configuredClientIdAccessor();
            if (!string.IsNullOrWhiteSpace(configuredClientId))
            {
                _validator.AssertClientIdMatch(envelope, configuredClientId, correlationId);
            }

            var session = await _sessionStore.TryGetByTransactionIdAsync(envelope.ClientId, envelope.TransactionId, ct).ConfigureAwait(false);
            if (session is null)
            {
                throw new UnknownTransactionException(correlationId, envelope.TransactionId, "No session recorded for inbound transactionId.");
            }

            format = session.Format;
            await _sessionStore.RecordObservedMessageIdAsync(envelope.ClientId, envelope.TransactionId, envelope.MessageId, ct).ConfigureAwait(false);

            // The envelope is only a trigger: FinalizeFromWebhook applies state precedence
            // (cached success / recorded terminal), the SUCCESS shape checks, and MISA's
            // confirmed Signing/status under the finalize lock.
            return await _finalizeFromWebhook.RunAsync(session, envelope, correlationId, ct).ConfigureAwait(false);
        }
        catch (WebhookValidationException ex)
        {
            _logger.LogWarning(
                "HandleWebhook validation failure transactionId={TransactionId} category={Category} correlationId={CorrelationId}",
                envelope.TransactionId, ex.WebhookCategory, correlationId);

            var ack = ESignErrorMapper.MapWebhookValidationToAck(ex);
            var outcome = new WebhookOutcome.FailureWithError(
                ex.MatchedTransactionId ?? envelope.TransactionId,
                ex.Format,
                ex.WebhookCategory,
                MisaErrorCode: null,
                correlationId);

            await DeliverFailureIfEnabledAsync(outcome, correlationId, ct).ConfigureAwait(false);

            return new WebhookHandleResult(ack, outcome);
        }
        catch (DeliveryHookFailedException hookFailure)
        {
            // Integrator hook failures on success/terminal delivery propagate unchanged.
            hookFailure.RethrowInner();
            throw; // unreachable; satisfies definite-return analysis
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            // Never let an unexpected failure escape to the HTTP endpoint; answer with a retryable ACK.
            // Log the type only: exception messages may carry document or credential content.
            _logger.LogWarning(
                "HandleWebhook unexpected failure transactionId={TransactionId} exType={ExType} correlationId={CorrelationId}",
                envelope.TransactionId, ex.GetType().Name, correlationId);

            var failureAck = WebhookAck.Failure(
                "webhook.finalize_failed",
                "Finalize attempt failed; MISA may retry.",
                "Signing finalization failed; please wait for retry.");
            var failure = new WebhookOutcome.FailureWithError(
                envelope.TransactionId,
                format ?? DocumentFormat.Unknown,
                WebhookValidationCategory.None,
                MisaErrorCode: (ex as ESignException)?.RawCode,
                correlationId);

            return new WebhookHandleResult(failureAck, failure);
        }
    }
}

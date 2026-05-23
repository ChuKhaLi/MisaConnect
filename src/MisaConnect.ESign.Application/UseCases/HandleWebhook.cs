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

    public HandleWebhook(
        WebhookEnvelopeValidator validator,
        ISigningSessionStore sessionStore,
        FinalizeFromWebhook finalizeFromWebhook,
        IWebhookDeliveryHook deliveryHook,
        ICorrelationIdAccessor correlationAccessor,
        Func<string> configuredClientIdAccessor,
        Func<string> successAckCodeAccessor,
        ILogger<HandleWebhook> logger)
    {
        _validator = validator;
        _sessionStore = sessionStore;
        _finalizeFromWebhook = finalizeFromWebhook;
        _deliveryHook = deliveryHook;
        _correlationAccessor = correlationAccessor;
        _configuredClientIdAccessor = configuredClientIdAccessor;
        _successAckCodeAccessor = successAckCodeAccessor;
        _logger = logger;
    }

    public async Task<WebhookHandleResult> HandleAsync(WebhookEnvelope envelope, CancellationToken ct)
    {
        var correlationId = _correlationAccessor.Current ?? Guid.NewGuid().ToString("N");

        _logger.LogInformation(
            "HandleWebhook received envelope transactionId={TransactionId} status={Status} correlationId={CorrelationId}",
            envelope.TransactionId, envelope.Status, correlationId);

        try
        {
            _validator.AssertClientIdMatch(envelope, _configuredClientIdAccessor(), correlationId);

            var session = await _sessionStore.TryGetByTransactionIdAsync(envelope.ClientId, envelope.TransactionId, ct).ConfigureAwait(false);
            if (session is null)
            {
                throw new UnknownTransactionException(correlationId, envelope.TransactionId, "No session recorded for inbound transactionId.");
            }

            await _sessionStore.RecordObservedMessageIdAsync(envelope.ClientId, envelope.TransactionId, envelope.MessageId, ct).ConfigureAwait(false);

            if (envelope.Status is WebhookStatus.Failed or WebhookStatus.Cancelled)
            {
                _logger.LogInformation(
                    "HandleWebhook terminal-without-finalize transactionId={TransactionId} status={Status} correlationId={CorrelationId}",
                    envelope.TransactionId, envelope.Status, correlationId);

                var terminal = new WebhookOutcome.TerminalWithoutFinalize(
                    envelope.TransactionId, session.Format, envelope.Status, envelope.ErrorCode, correlationId);
                await _deliveryHook.DeliverAsync(terminal, ct).ConfigureAwait(false);

                return new WebhookHandleResult(WebhookAck.Success(_successAckCodeAccessor()), terminal);
            }

            _validator.AssertSuccessShapeIsValid(envelope, session, correlationId);

            if (session.CachedSuccess is { } cached)
            {
                _logger.LogInformation(
                    "HandleWebhook duplicate-delivery short-circuit transactionId={TransactionId} isMessageIdNew={IsMessageIdNew} correlationId={CorrelationId}",
                    envelope.TransactionId,
                    !session.ObservedMessageIds.Contains(envelope.MessageId),
                    correlationId);

                return new WebhookHandleResult(
                    cached.Ack,
                    new WebhookOutcome.SuccessWithSignedBytes(envelope.TransactionId, session.Format, cached.SignedBytes, correlationId));
            }

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

            try
            {
                await _deliveryHook.DeliverAsync(outcome, ct).ConfigureAwait(false);
            }
            catch (Exception hookEx)
            {
                _logger.LogWarning(hookEx, "Webhook delivery hook threw during failure outcome dispatch correlationId={CorrelationId}", correlationId);
            }

            return new WebhookHandleResult(ack, outcome);
        }
    }
}

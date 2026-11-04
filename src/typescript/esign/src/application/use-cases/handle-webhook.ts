import type { WebhookHandleResultDto, WebhookOutcomeDto } from '../../domain/dtos.js';
import {
  ClientIdMismatchException,
  isESignException,
  UnknownTransactionException,
  type WebhookValidationException,
} from '../../domain/errors.js';
import type { Logger, SigningSessionStore, WebhookDeliveryHook } from '../ports.js';
import { parseWebhookEnvelope, WEBHOOK_ACKS, type WebhookEnvelope } from '../webhook-envelope.js';
import type { WebhookCallContext } from './finalize-webhook.js';

export interface HandleWebhookDeps {
  store: SigningSessionStore;
  hook: WebhookDeliveryHook;
  /** Runs under the finalize lock: state precedence, MISA `Signing/status` confirmation, attach. */
  finalize: (envelope: WebhookEnvelope, ctx: WebhookCallContext) => Promise<WebhookHandleResultDto>;
  logger: Logger;
}

function ackFor(err: WebhookValidationException) {
  switch (err.webhookCategory) {
    case 'MalformedEnvelope':
    case 'ClientIdMismatch':
    case 'UnknownTransaction':
    case 'IncompleteSuccessEnvelope':
    case 'DocumentIdMismatch':
      return { ...WEBHOOK_ACKS[err.webhookCategory] };
    default:
      return { ...WEBHOOK_ACKS.otherValidation };
  }
}

/**
 * Webhook handling (INV §3, design §6 and §9.6, ruling R10). Never throws for malformed input: the
 * body is validated here and a bad shape answers `webhook.malformed`. Steps: parse → session by
 * the envelope's (clientId, transactionId) (none → `webhook.unknown_transaction`) → the session's
 * stored clientId must equal the envelope's (`webhook.client_id_mismatch`) → record messageId →
 * finalize. Everything after that — cached success / recorded terminal precedence, SUCCESS shape
 * checks, and acting on MISA's own `Signing/status` whatever the envelope claims — happens under
 * the finalize lock. A validation failure answers its ACK with a `FailureWithError` outcome and
 * invokes the hook, swallowing hook errors; hook errors on terminal and success outcomes
 * propagate (as .NET).
 */
export async function handleWebhook(
  deps: HandleWebhookDeps,
  body: unknown,
  ctx: WebhookCallContext,
): Promise<WebhookHandleResultDto> {
  const { correlationId } = ctx;
  let envelope: WebhookEnvelope | undefined;
  try {
    envelope = parseWebhookEnvelope(body, correlationId);
    const { clientId, transactionId } = envelope;
    deps.logger.info('Webhook received', { transactionId, status: envelope.status, correlationId });

    const session = await deps.store.get(clientId, transactionId);
    if (session === undefined) {
      throw new UnknownTransactionException({
        correlationId,
        transactionId,
        detail: 'No session recorded for inbound transactionId.',
      });
    }
    if (session.clientId !== clientId) {
      throw new ClientIdMismatchException({
        correlationId,
        detail: 'The envelope clientId does not match the clientId the transaction began with.',
      });
    }
    await deps.store.recordMessageId(clientId, transactionId, envelope.messageId);
    return await deps.finalize(envelope, ctx);
  } catch (caught) {
    // Brand check, not instanceof: a custom store may throw from another copy of the package.
    if (!isESignException(caught) || caught.category !== 'WebhookValidation') throw caught;
    const err = caught as WebhookValidationException;
    const transactionId =
      err.matchedTransactionId ??
      envelope?.transactionId ??
      (typeof body === 'object' &&
      body !== null &&
      typeof (body as Record<string, unknown>)['transactionId'] === 'string'
        ? ((body as Record<string, unknown>)['transactionId'] as string)
        : undefined);
    deps.logger.warn('Webhook validation failure', {
      category: err.webhookCategory,
      correlationId,
      ...(transactionId === undefined ? {} : { transactionId }),
    });
    const outcome: WebhookOutcomeDto = {
      kind: 'FailureWithError',
      ...(transactionId === undefined ? {} : { transactionId }),
      format: err.format,
      categoryName: err.webhookCategory,
      correlationId,
    };
    try {
      await deps.hook(outcome);
    } catch {
      deps.logger.warn('Webhook delivery hook threw during failure outcome dispatch', {
        correlationId,
      });
    }
    return { ack: ackFor(err), outcome };
  }
}

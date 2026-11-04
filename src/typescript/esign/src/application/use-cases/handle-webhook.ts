import type { WebhookHandleResultDto, WebhookOutcomeDto } from '../../domain/dtos.js';
import {
  ClientIdMismatchException,
  DocumentIdMismatchException,
  IncompleteSuccessEnvelopeException,
  isESignException,
  UnknownTransactionException,
  type WebhookValidationException,
} from '../../domain/errors.js';
import type { Logger, SigningSession, SigningSessionStore, WebhookDeliveryHook } from '../ports.js';
import { parseWebhookEnvelope, WEBHOOK_ACKS, type WebhookEnvelope } from '../webhook-envelope.js';
import { cachedResult, type WebhookCallContext } from './finalize-webhook.js';

export interface HandleWebhookDeps {
  store: SigningSessionStore;
  hook: WebhookDeliveryHook;
  decodeBase64: (text: string) => Uint8Array;
  finalize: (envelope: WebhookEnvelope, ctx: WebhookCallContext) => Promise<WebhookHandleResultDto>;
  logger: Logger;
}

function assertSuccessShape(
  envelope: WebhookEnvelope,
  session: SigningSession,
  correlationId: string,
): void {
  const base = { correlationId, transactionId: envelope.transactionId, format: session.format };
  if (envelope.signatures.length === 0) {
    throw new IncompleteSuccessEnvelopeException({
      ...base,
      detail: 'status=SUCCESS but signatures[] is empty',
    });
  }
  const recorded = new Set(session.recordedDocumentIds);
  for (const sig of envelope.signatures) {
    if (!recorded.has(sig.documentId)) {
      throw new DocumentIdMismatchException({
        ...base,
        detail: 'signatures[].documentId not in recorded session documents',
      });
    }
  }
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
 * Webhook handling (INV §3, design §6 and §9.6). Never throws for malformed input: the body is
 * validated here and a bad shape answers `webhook.malformed`. Steps: parse → session by the
 * envelope's (clientId, transactionId) (none → `webhook.unknown_transaction`) → the session's
 * stored clientId must equal the envelope's (`webhook.client_id_mismatch`) → record messageId →
 * FAILED/CANCELLED: hook `TerminalWithoutFinalize`, ACK "0" → SUCCESS: shape check, cached
 * success short-circuit (hook not re-invoked), else finalize. A validation failure answers its
 * ACK with a `FailureWithError` outcome and invokes the hook, swallowing hook errors; hook
 * errors on the terminal and success outcomes propagate (as .NET).
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

    if (envelope.status === 'Failed' || envelope.status === 'Cancelled') {
      const terminal: WebhookOutcomeDto = {
        kind: 'TerminalWithoutFinalize',
        transactionId,
        format: session.format,
        status: envelope.status,
        ...(envelope.errorCode === undefined ? {} : { misaErrorCode: envelope.errorCode }),
        correlationId,
      };
      await deps.hook(terminal);
      return { ack: { ...WEBHOOK_ACKS.success }, outcome: terminal };
    }

    assertSuccessShape(envelope, session, correlationId);
    if (session.cachedSuccess !== undefined) {
      deps.logger.info('Webhook duplicate delivery answered from the cached success', {
        transactionId,
        correlationId,
      });
      return cachedResult(
        { ...session, cachedSuccess: session.cachedSuccess },
        envelope,
        correlationId,
        deps.decodeBase64,
      );
    }
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

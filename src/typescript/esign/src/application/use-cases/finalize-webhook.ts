import type {
  MisaCredentials,
  WebhookHandleResultDto,
  WebhookOutcomeDto,
} from '../../domain/dtos.js';
import {
  DocumentIdMismatchException,
  ESignGeneralException,
  IncompleteSuccessEnvelopeException,
  isESignException,
  UnknownTransactionException,
} from '../../domain/errors.js';
import type { EsrmGateway } from '../esrm-gateway.js';
import type { Base64Encoder } from '../mapping/sign-request-mapper.js';
import type {
  CertificateSelector,
  Logger,
  SigningSession,
  SigningSessionStore,
  SigningTerminal,
  WebhookDeliveryHook,
} from '../ports.js';
import type { TokenManager } from '../token-manager.js';
import { WEBHOOK_ACKS, type WebhookEnvelope } from '../webhook-envelope.js';
import { listActiveCertificates } from './list-active-certificates.js';
import { parseSignStatus } from './poll-sign-status.js';
import { esrmRunner } from './sign-document.js';

export interface FinalizeWebhookDeps {
  store: SigningSessionStore;
  gateway: EsrmGateway;
  tokens: TokenManager;
  selectCertificate: CertificateSelector;
  encodeBase64: Base64Encoder;
  decodeBase64: (text: string) => Uint8Array;
  hook: WebhookDeliveryHook;
  logger: Logger;
  /** Credentials for the session's principal (provider called with its clientId and userName). */
  credentialsFor: (session: SigningSession) => Promise<MisaCredentials>;
}

export interface WebhookCallContext {
  correlationId: string;
  signal?: AbortSignal | undefined;
}

/** What MISA's `Signing/status` confirmed for the transaction. */
type Confirmed =
  { kind: 'signed'; signed: Uint8Array } | { kind: 'terminal'; terminal: SigningTerminal };

function cached(
  session: SigningSession,
  envelope: WebhookEnvelope,
  correlationId: string,
  decodeBase64: (text: string) => Uint8Array,
): WebhookHandleResultDto | undefined {
  if (session.cachedSuccess !== undefined) {
    return {
      ack: { ...session.cachedSuccess.ack },
      outcome: {
        kind: 'SuccessWithSignedBytes',
        transactionId: envelope.transactionId,
        format: session.format,
        signedBytes: decodeBase64(session.cachedSuccess.signed),
        correlationId,
      },
    };
  }
  if (session.terminal !== undefined) {
    return {
      ack: { ...WEBHOOK_ACKS.success },
      outcome: terminalOutcome(session, envelope, session.terminal, correlationId),
    };
  }
  return undefined;
}

function terminalOutcome(
  session: SigningSession,
  envelope: WebhookEnvelope,
  terminal: SigningTerminal,
  correlationId: string,
): WebhookOutcomeDto {
  return {
    kind: 'TerminalWithoutFinalize',
    transactionId: envelope.transactionId,
    format: session.format,
    status: terminal.status,
    ...(terminal.misaErrorCode === undefined ? {} : { misaErrorCode: terminal.misaErrorCode }),
    correlationId,
  };
}

/** Envelope shape checks for a SUCCESS delivery (kept although MISA's status decides). */
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

/** Resolves credentials, asks MISA for the status and, on SUCCESS, attaches its signature. */
async function confirmAndAttach(
  d: FinalizeWebhookDeps,
  session: SigningSession,
  c: WebhookCallContext,
): Promise<Confirmed> {
  const format = session.format;
  const credentials = await d.credentialsFor(session);
  if (credentials.clientId !== session.clientId || credentials.userName !== session.userName) {
    // A provider must not be able to finalize another principal's transaction.
    throw new ESignGeneralException({
      category: 'Validation',
      rawCode: 'CredentialsMismatch',
      detail:
        'CredentialsMismatch: the credentials resolved for the webhook finalize do not belong to the principal that began the transaction.',
      correlationId: c.correlationId,
      format,
    });
  }
  const esrm = esrmRunner(d.tokens, format, {
    credentials,
    correlationId: c.correlationId,
    signal: c.signal,
  });
  const raw = await esrm((call) => d.gateway.status(call, session.transactionId));
  const status = parseSignStatus(raw.status);
  if (status === 'FAILED' || status === 'CANCELLED') {
    return {
      kind: 'terminal',
      terminal: {
        status: status === 'FAILED' ? 'Failed' : 'Cancelled',
        ...(raw.errorCode === undefined ? {} : { misaErrorCode: raw.errorCode }),
      },
    };
  }
  if (status !== 'SUCCESS') {
    throw new ESignGeneralException({
      category: 'StatusLookupFailed',
      rawCode: 'SignNotFinal',
      detail: `MISA Signing/status reports ${status}; the webhook cannot be finalized yet.`,
      correlationId: c.correlationId,
      format,
    });
  }
  const signature = raw.firstSignature;
  if (signature === undefined || signature === '') {
    throw new ESignGeneralException({
      category: 'StatusLookupFailed',
      rawCode: 'MissingSignature',
      detail: 'MISA Signing/status reports SUCCESS without a signature.',
      correlationId: c.correlationId,
      format,
    });
  }
  const certificates = await esrm((call) => listActiveCertificates(d.gateway, call));
  const cert = await d.selectCertificate(certificates);
  const signed = await esrm((call) => d.gateway.attach(call, cert, session.hash, signature));
  return { kind: 'signed', signed };
}

/**
 * Webhook finalize (INV §3, ruling R10: the envelope is only a trigger). Under the
 * per-(clientId, tx) finalize lock:
 * 1. re-read the session (evicted → `UnknownTransactionException`);
 * 2. state precedence: a cached success answers every later delivery whatever its status; a
 *    recorded terminal answers ACK "0" + `TerminalWithoutFinalize`; neither calls MISA nor the hook;
 * 3. a SUCCESS envelope must still pass the shape checks;
 * 4. resolve the session principal's credentials (must match its clientId and userName), then ask
 *    MISA `Signing/status`: SUCCESS → certificates, select, attach MISA's own `signatures[0]`
 *    (never the envelope's), cache, hook, ACK "0"; FAILED/CANCELLED → record the terminal, hook
 *    `TerminalWithoutFinalize` with MISA's status and errorCode, ACK "0"; anything else (PENDING,
 *    UNKNOWN) → `webhook.finalize_failed`.
 * Any `ESignException` in step 4 → ACK `webhook.finalize_failed` + `FailureWithError` (category
 * "None", `misaErrorCode` = rawCode); nothing cached, hook not called, so MISA's retry can finish.
 * No 2FA here (ruling R11): a challenge is just a finalize failure — no OTP prompt, no pending
 * challenge. Hook errors on success/terminal propagate (as .NET).
 */
export async function finalizeFromWebhook(
  deps: FinalizeWebhookDeps,
  envelope: WebhookEnvelope,
  ctx: WebhookCallContext,
): Promise<WebhookHandleResultDto> {
  const { correlationId } = ctx;
  const { clientId, transactionId } = envelope;
  return deps.store.withFinalizeLock(clientId, transactionId, async () => {
    const session = await deps.store.get(clientId, transactionId);
    if (session === undefined) {
      throw new UnknownTransactionException({
        correlationId,
        transactionId,
        detail: 'Session evicted while awaiting finalize lock.',
      });
    }
    const settled = cached(session, envelope, correlationId, deps.decodeBase64);
    if (settled !== undefined) return settled;
    if (envelope.status === 'Success') assertSuccessShape(envelope, session, correlationId);

    const format = session.format;
    let confirmed: Confirmed;
    try {
      confirmed = await confirmAndAttach(deps, session, ctx);
    } catch (err) {
      if (!isESignException(err)) throw err;
      deps.logger.warn('MISA eSign webhook finalize failed', {
        transactionId,
        format,
        errorCategory: err.category,
        rawCode: err.rawCode,
        correlationId,
      });
      const outcome: WebhookOutcomeDto = {
        kind: 'FailureWithError',
        transactionId,
        format,
        categoryName: 'None',
        ...(err.rawCode === undefined ? {} : { misaErrorCode: err.rawCode }),
        correlationId,
      };
      return { ack: { ...WEBHOOK_ACKS.finalizeFailed }, outcome };
    }

    const ack = { ...WEBHOOK_ACKS.success };
    if (confirmed.kind === 'terminal') {
      await deps.store.recordTerminal(clientId, transactionId, confirmed.terminal);
      const outcome = terminalOutcome(session, envelope, confirmed.terminal, correlationId);
      await deps.hook(outcome);
      return { ack, outcome };
    }

    const { signed } = confirmed;
    await deps.store.cacheSuccess(clientId, transactionId, ack, deps.encodeBase64(signed));
    deps.logger.info('MISA eSign webhook finalized', {
      transactionId,
      format,
      signedByteCount: signed.byteLength,
      correlationId,
    });
    const outcome: WebhookOutcomeDto = {
      kind: 'SuccessWithSignedBytes',
      transactionId,
      format,
      signedBytes: signed,
      correlationId,
    };
    await deps.hook(outcome);
    return { ack, outcome };
  });
}

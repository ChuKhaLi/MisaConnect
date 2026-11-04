import type {
  MisaCredentials,
  WebhookHandleResultDto,
  WebhookOutcomeDto,
} from '../../domain/dtos.js';
import {
  ESignGeneralException,
  isESignException,
  UnknownTransactionException,
} from '../../domain/errors.js';
import type { EsrmGateway } from '../esrm-gateway.js';
import type { Base64Encoder } from '../mapping/sign-request-mapper.js';
import type {
  CertificateSelector,
  SigningSession,
  SigningSessionStore,
  WebhookDeliveryHook,
} from '../ports.js';
import type { TokenManager } from '../token-manager.js';
import { WEBHOOK_ACKS, type WebhookEnvelope } from '../webhook-envelope.js';
import { listActiveCertificates } from './list-active-certificates.js';
import { esrmRunner } from './sign-document.js';

export interface FinalizeWebhookDeps {
  store: SigningSessionStore;
  gateway: EsrmGateway;
  tokens: TokenManager;
  selectCertificate: CertificateSelector;
  encodeBase64: Base64Encoder;
  decodeBase64: (text: string) => Uint8Array;
  hook: WebhookDeliveryHook;
  /** Credentials for the session's principal (provider called with its clientId and userName). */
  credentialsFor: (session: SigningSession) => Promise<MisaCredentials>;
  /** The client's 2FA policy, as for `sign*` / `beginSign*`. */
  withTwoFactor: (
    credentials: MisaCredentials,
    operation: () => Promise<Uint8Array>,
  ) => Promise<Uint8Array>;
}

export interface WebhookCallContext {
  correlationId: string;
  signal?: AbortSignal | undefined;
}

/** Answer for a session that already finalized: its cached ACK and bytes, no hook call. */
export function cachedResult(
  session: SigningSession & { cachedSuccess: NonNullable<SigningSession['cachedSuccess']> },
  envelope: WebhookEnvelope,
  correlationId: string,
  decodeBase64: (text: string) => Uint8Array,
): WebhookHandleResultDto {
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

/**
 * Webhook finalize (INV §3): under the per-(clientId, tx) finalize lock, re-reads the session
 * (evicted → `UnknownTransactionException`), short-circuits on a cached success, else resolves the
 * session principal's credentials, lists certificates, selects one and attaches
 * `signatures[0].signature`. Success → ACK "0", cached (bytes as base64), hook invoked (a hook
 * error propagates). Any `ESignException` on the way → ACK `webhook.finalize_failed` +
 * `FailureWithError` (category "None", `misaErrorCode` = rawCode); not cached, hook not invoked,
 * so MISA's retry can finalize later.
 */
export async function finalizeFromWebhook(
  deps: FinalizeWebhookDeps,
  envelope: WebhookEnvelope,
  ctx: WebhookCallContext,
): Promise<WebhookHandleResultDto> {
  const { correlationId, signal } = ctx;
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
    if (session.cachedSuccess !== undefined) {
      return cachedResult(
        { ...session, cachedSuccess: session.cachedSuccess },
        envelope,
        correlationId,
        deps.decodeBase64,
      );
    }

    const format = session.format;
    let signed: Uint8Array;
    try {
      const credentials = await deps.credentialsFor(session);
      if (credentials.clientId !== session.clientId || credentials.userName !== session.userName) {
        // A provider must not be able to finalize another principal's transaction.
        throw new ESignGeneralException({
          category: 'Validation',
          rawCode: 'CredentialsMismatch',
          detail:
            'CredentialsMismatch: the credentials resolved for the webhook finalize do not belong to the principal that began the transaction.',
          correlationId,
          format,
        });
      }
      const signature = envelope.signatures[0]?.signature ?? '';
      signed = await deps.withTwoFactor(credentials, async () => {
        const esrm = esrmRunner(deps.tokens, format, { credentials, correlationId, signal });
        const certificates = await esrm((call) => listActiveCertificates(deps.gateway, call));
        const cert = await deps.selectCertificate(certificates);
        return esrm((call) => deps.gateway.attach(call, cert, session.hash, signature));
      });
    } catch (err) {
      if (!isESignException(err)) throw err;
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
    await deps.store.cacheSuccess(clientId, transactionId, ack, deps.encodeBase64(signed));
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

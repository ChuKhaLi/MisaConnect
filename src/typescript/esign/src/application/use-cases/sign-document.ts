import type { MisaCredentials } from '../../domain/dtos.js';
import type { EsrmCall, EsrmGateway } from '../esrm-gateway.js';
import {
  toNormalized,
  type Base64Encoder,
  type SignFormat,
  type SignRequestDtoByFormat,
} from '../mapping/sign-request-mapper.js';
import type { CertificateSelector, Clock, Delay } from '../ports.js';
import type { TokenManager } from '../token-manager.js';
import { validateSignRequest } from '../validation/sign-request-validator.js';
import { listActiveCertificates } from './list-active-certificates.js';
import { pollSignStatus } from './poll-sign-status.js';

export interface SignDocumentDeps {
  gateway: EsrmGateway;
  /** The client's one token manager (shared cache and single flight). */
  tokens: TokenManager;
  selectCertificate: CertificateSelector;
  clock: Clock;
  delay: Delay;
  polling: { intervalMs: number; totalTimeoutMs: number };
  encodeBase64: Base64Encoder;
  /** Fills an absent/empty `documentId` (tests); defaults to the mapper's random UUID. */
  newDocumentId?: (() => string) | undefined;
}

export interface SignContext {
  credentials: MisaCredentials;
  correlationId: string;
  signal?: AbortSignal | undefined;
}

export interface SignDocumentResult {
  signed: Uint8Array;
  transactionId: string;
  certificateKeyAlias: string;
  /** ISO-8601 UTC instant. */
  completedAtUtc: string;
}

/**
 * Polling sign flow (INV §2.4): validate → token → certificates → select → hash → Signing/hash →
 * poll → attach. Each ESRM call runs under `withAuth`, so a 401 re-authenticates and re-sends only
 * that call, and every later call uses the renewed token (design §9.1).
 */
export async function signDocument<F extends SignFormat>(
  deps: SignDocumentDeps,
  format: F,
  dto: SignRequestDtoByFormat[F],
  ctx: SignContext,
): Promise<SignDocumentResult> {
  const { correlationId, signal, credentials } = ctx;
  validateSignRequest(format, dto, correlationId);
  signal?.throwIfAborted();

  const withId =
    deps.newDocumentId !== undefined && !dto.documentId
      ? { ...dto, documentId: deps.newDocumentId() }
      : dto;
  const request = toNormalized(format, withId, deps.encodeBase64);

  const authCtx = { correlationId, signal };
  const esrm = <T>(fn: (call: EsrmCall, userId: string) => Promise<T>): Promise<T> =>
    deps.tokens.withAuth(credentials, authCtx, (bearer, userId) =>
      fn({ credentials, bearer, correlationId, signal, format }, userId),
    );

  const certificates = await esrm((call) => listActiveCertificates(deps.gateway, call));
  const cert = await deps.selectCertificate(certificates);
  const hash = await esrm((call) => deps.gateway.hash(call, cert, request));
  const transactionId = await esrm((call, userId) =>
    deps.gateway.signHash(call, {
      userId,
      certAlias: cert.keyAlias,
      dataToBeDisplayed: request.dataToBeDisplayed,
      documentId: hash.documentId,
      digest: hash.digest,
      documentName: request.documentName,
    }),
  );
  const snapshot = await pollSignStatus(
    {
      getStatus: (tx) => esrm((call) => deps.gateway.status(call, tx)),
      clock: deps.clock,
      delay: deps.delay,
      ...deps.polling,
    },
    transactionId,
    { correlationId, signal, format },
  );
  const signed = await esrm((call) =>
    deps.gateway.attach(call, cert, hash, snapshot.firstSignature ?? '', request),
  );

  return {
    signed,
    transactionId,
    certificateKeyAlias: cert.keyAlias,
    completedAtUtc: deps.clock.now().toISOString(),
  };
}

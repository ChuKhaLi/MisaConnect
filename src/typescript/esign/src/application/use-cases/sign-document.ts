import type { Certificate, MisaCredentials } from '../../domain/dtos.js';
import type { EsrmCall, EsrmGateway } from '../esrm-gateway.js';
import type { HashOutput } from '../hash-output.js';
import {
  toNormalized,
  type Base64Encoder,
  type NormalizedSignRequest,
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

/** Runs one ESRM call under `withAuth` (401 → re-authenticate once and re-send that call only). */
export type EsrmRunner = <T>(fn: (call: EsrmCall, userId: string) => Promise<T>) => Promise<T>;

export function esrmRunner(tokens: TokenManager, format: SignFormat, ctx: SignContext): EsrmRunner {
  const { credentials, correlationId, signal } = ctx;
  const authCtx = { correlationId, signal };
  return (fn) =>
    tokens.withAuth(credentials, authCtx, (bearer, userId) =>
      fn({ credentials, bearer, correlationId, signal, format }, userId),
    );
}

/** Dependencies of the part of the flow shared by `sign*` and `beginSign*`. */
export type StartSigningDeps = Pick<
  SignDocumentDeps,
  'gateway' | 'tokens' | 'selectCertificate' | 'encodeBase64' | 'newDocumentId'
>;

export interface StartedSigning {
  request: NormalizedSignRequest;
  cert: Certificate;
  hash: HashOutput;
  transactionId: string;
  esrm: EsrmRunner;
}

/**
 * validate → token → certificates → select → hash → `Signing/hash`. Shared by the polling flow
 * (which then polls and attaches) and `beginSign*` (which registers a webhook session).
 */
export async function startSigning<F extends SignFormat>(
  deps: StartSigningDeps,
  format: F,
  dto: SignRequestDtoByFormat[F],
  ctx: SignContext,
): Promise<StartedSigning> {
  validateSignRequest(format, dto, ctx.correlationId);
  ctx.signal?.throwIfAborted();

  const withId =
    deps.newDocumentId !== undefined && !dto.documentId
      ? { ...dto, documentId: deps.newDocumentId() }
      : dto;
  const request = toNormalized(format, withId, deps.encodeBase64);
  const esrm = esrmRunner(deps.tokens, format, ctx);

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
  return { request, cert, hash, transactionId, esrm };
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
  const { correlationId, signal } = ctx;
  const { request, cert, hash, transactionId, esrm } = await startSigning(deps, format, dto, ctx);
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

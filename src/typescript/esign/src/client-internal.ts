import type { Clock, Delay } from './application/ports.js';
import type {
  SignFormat,
  SignRequestDtoByFormat,
} from './application/mapping/sign-request-mapper.js';
import {
  signDocument,
  type SignDocumentDeps,
  type SignDocumentResult,
} from './application/use-cases/sign-document.js';
import { beginSign } from './application/use-cases/begin-sign.js';
import { finalizeFromWebhook } from './application/use-cases/finalize-webhook.js';
import { handleWebhook } from './application/use-cases/handle-webhook.js';
import { requireOtpCode, withTransparentOtp } from './application/use-cases/two-factor.js';
import type { BeginResultDto, MisaCredentials } from './domain/dtos.js';
import { AuthenticationFailedException, ESignGeneralException } from './domain/errors.js';
import type { CallOptions, IMisaESignClient } from './client.js';
import {
  abortableDelay,
  createCredentialFingerprinter,
  defaultTokenCacheKey,
  firstCertificateSelector,
  systemClock,
} from './infrastructure/defaults.js';
import {
  resolveCredentials,
  validateOptions,
  type MisaESignOptions,
} from './infrastructure/options.js';
import { createInMemorySigningSessionStore } from './infrastructure/in-memory-session-store.js';
import { noopLogger, safeLogger } from './infrastructure/logging.js';
import { createTransport, type TransportDeps } from './infrastructure/transport.js';
import { createDefaultTokenManager } from './infrastructure/wire/auth.js';
import { decodeBase64, encodeBase64 } from './infrastructure/wire/base64.js';
import { createEsrmGateway } from './infrastructure/wire/esrm.js';

/** Test seams; never reachable from the public factory. */
export interface InternalClientDeps {
  /** Overrides the `clock` option. */
  clock?: Clock;
  /** Overrides the `delay` option (polling waits and retry backoff). */
  delay?: Delay;
  /** Deterministic ids: the default correlation id and any generated `documentId`. */
  newId?: () => string;
  transport?: TransportDeps;
}

/**
 * Builds the client. Not exported from the package entry point: `createMisaESignClient` and
 * in-repo tests call it. Options are validated as for the public factory (http is allowed only
 * on loopback hosts, which is what the in-process fake server uses).
 */
export function createMisaESignClientInternal(
  options: MisaESignOptions,
  deps: InternalClientDeps = {},
): IMisaESignClient {
  const resolved = validateOptions({
    ...options,
    ...(deps.clock === undefined ? {} : { clock: deps.clock }),
    ...(deps.delay === undefined ? {} : { delay: deps.delay }),
  });
  // One transport and one token manager per client: every method shares the token cache and the
  // single-flight login/refresh.
  const transport = createTransport(resolved, deps.transport);
  const fingerprint = createCredentialFingerprinter();
  const cacheKeyOf = resolved.tokenCacheKey ?? defaultTokenCacheKey;
  /** Identity of a caller: token-cache key plus secret fingerprint (never the secrets themselves). */
  const principalOf = (c: MisaCredentials): string | undefined =>
    c.userName && c.clientId
      ? `${cacheKeyOf({ userName: c.userName, clientId: c.clientId, host: resolved.host })}\0${fingerprint(c)}`
      : undefined;
  const tokens = createDefaultTokenManager(resolved, transport, { fingerprint });
  const signDeps: SignDocumentDeps = {
    gateway: createEsrmGateway(resolved, transport),
    tokens,
    selectCertificate: resolved.certificateSelector ?? firstCertificateSelector,
    clock: resolved.clock ?? systemClock,
    delay: resolved.delay ?? abortableDelay,
    polling: resolved.polling,
    encodeBase64,
    newDocumentId: deps.newId,
  };
  const mode = resolved.webhook.mode;
  const logger = safeLogger(resolved.logger ?? noopLogger);
  // Webhook sessions: one store per client (in-memory default is process-local).
  const sessionStore =
    resolved.signingSessionStore ?? createInMemorySigningSessionStore(signDeps.clock);
  const deliveryHook = resolved.webhookDeliveryHook ?? ((): void => undefined);

  const correlationIdOf = (opts?: CallOptions): string =>
    opts?.correlationId ??
    resolved.correlationId?.() ??
    deps.newId?.() ??
    globalThis.crypto.randomUUID();

  const modeError = (rawCode: string, detail: string, correlationId: string, format: SignFormat) =>
    new ESignGeneralException({ category: 'Validation', rawCode, detail, correlationId, format });

  /** Explicit-mode state: principals (see `principalOf`) with a 2FA challenge pending. */
  const pendingChallenges = new Set<string>();

  /**
   * Runs one authenticated operation with the client's 2FA policy: with an `otpProvider` the
   * challenge is answered transparently (the whole operation is retried once); without one the
   * challenge is recorded for this caller's principal and propagates. A success clears only this
   * principal's entry. `sign*`, `beginSign*` and the webhook finalize all run through it.
   */
  async function withTwoFactor<T>(
    credentials: MisaCredentials,
    correlationId: string,
    signal: AbortSignal | undefined,
    operation: () => Promise<T>,
  ): Promise<T> {
    const principal = principalOf(credentials);
    try {
      const result = resolved.otpProvider
        ? await withTransparentOtp(
            { tokens, otpProvider: resolved.otpProvider },
            { credentials, correlationId, signal },
            operation,
          )
        : await operation();
      if (principal !== undefined) pendingChallenges.delete(principal);
      return result;
    } catch (err) {
      if (
        !resolved.otpProvider &&
        principal !== undefined &&
        err instanceof AuthenticationFailedException &&
        err.requires2FA
      ) {
        pendingChallenges.add(principal);
      }
      throw err;
    }
  }

  /** The caller's own credentials, provided a challenge is pending for exactly this principal. */
  async function pendingCredentials(
    opts: CallOptions | undefined,
    correlationId: string,
  ): Promise<{
    credentials: MisaCredentials;
    ctx: { correlationId: string; signal?: AbortSignal };
  }> {
    const signal = opts?.signal;
    signal?.throwIfAborted();
    const ctx = { correlationId, ...(signal === undefined ? {} : { signal }) };
    const credentials = await resolveCredentials(resolved, ctx, opts?.credentials);
    const principal = principalOf(credentials);
    if (principal === undefined || !pendingChallenges.has(principal)) {
      throw new ESignGeneralException({
        category: 'Validation',
        rawCode: 'NoPendingOtpChallenge',
        detail:
          'NoPendingOtpChallenge: no 2FA challenge is pending for these credentials; call this after a sign call fails with requires2FA.',
        correlationId,
      });
    }
    return { credentials, ctx };
  }

  async function sign<F extends SignFormat>(
    format: F,
    dto: SignRequestDtoByFormat[F],
    opts: CallOptions | undefined,
  ): Promise<SignDocumentResult> {
    const correlationId = correlationIdOf(opts);
    if (mode === 'webhook') {
      throw modeError(
        'WebhookModeOnly',
        `sign${format} is unavailable when webhook.mode is 'webhook'; use beginSign${format}.`,
        correlationId,
        format,
      );
    }
    const signal = opts?.signal;
    signal?.throwIfAborted();
    const credentials = await resolveCredentials(
      resolved,
      { correlationId, ...(signal === undefined ? {} : { signal }) },
      opts?.credentials,
    );
    return withTwoFactor(credentials, correlationId, signal, () =>
      signDocument(signDeps, format, dto, { credentials, correlationId, signal }),
    );
  }

  async function begin<F extends SignFormat>(
    format: F,
    dto: SignRequestDtoByFormat[F],
    opts: CallOptions | undefined,
  ): Promise<BeginResultDto> {
    const correlationId = correlationIdOf(opts);
    if (mode === 'polling') {
      throw modeError(
        'PollingModeOnly',
        `beginSign${format} is unavailable when webhook.mode is 'polling'; use sign${format}.`,
        correlationId,
        format,
      );
    }
    const signal = opts?.signal;
    signal?.throwIfAborted();
    const credentials = await resolveCredentials(
      resolved,
      { correlationId, ...(signal === undefined ? {} : { signal }) },
      opts?.credentials,
    );
    return beginSign(
      {
        ...signDeps,
        store: sessionStore,
        sessionTtlMs: resolved.webhook.sessionTtlMs,
        withTwoFactor: (operation) => withTwoFactor(credentials, correlationId, signal, operation),
      },
      format,
      dto,
      { credentials, correlationId, signal },
    );
  }

  /**
   * Finalize has no caller context: the session's principal is resolved again — a provider is
   * called with the session's `{ clientId, userName }`, static credentials are used as is, and a
   * per-call `opts.credentials` on `handleWebhook` wins. The finalize use case rejects credentials
   * that are not the session's principal.
   */
  async function handle(body: unknown, opts: CallOptions | undefined) {
    const correlationId = correlationIdOf(opts);
    const signal = opts?.signal;
    const ctx = { correlationId, ...(signal === undefined ? {} : { signal }) };
    return handleWebhook(
      {
        store: sessionStore,
        hook: deliveryHook,
        decodeBase64,
        logger,
        finalize: (envelope, c) =>
          finalizeFromWebhook(
            {
              store: sessionStore,
              gateway: signDeps.gateway,
              tokens,
              selectCertificate: signDeps.selectCertificate,
              encodeBase64,
              decodeBase64,
              hook: deliveryHook,
              credentialsFor: (session) =>
                resolveCredentials(
                  resolved,
                  { ...ctx, clientId: session.clientId, userName: session.userName },
                  opts?.credentials,
                ),
              withTwoFactor: (credentials, operation) =>
                withTwoFactor(credentials, correlationId, signal, operation),
            },
            envelope,
            c,
          ),
      },
      body,
      ctx,
    );
  }

  const base = (r: SignDocumentResult) => ({
    transactionId: r.transactionId,
    certificateKeyAlias: r.certificateKeyAlias,
    completedAtUtc: r.completedAtUtc,
  });

  return {
    signPdf: async (request, opts) => {
      const r = await sign('Pdf', request, opts);
      return { signedPdf: r.signed, ...base(r), format: 'Pdf' };
    },
    signXml: async (request, opts) => {
      const r = await sign('Xml', request, opts);
      return { signedXml: r.signed, ...base(r), format: 'Xml' };
    },
    signWord: async (request, opts) => {
      const r = await sign('Word', request, opts);
      return { signedWord: r.signed, ...base(r), format: 'Word' };
    },
    signExcel: async (request, opts) => {
      const r = await sign('Excel', request, opts);
      return { signedExcel: r.signed, ...base(r), format: 'Excel' };
    },

    signInWithOtp: async (code, otpType, remember, opts) => {
      const correlationId = correlationIdOf(opts);
      const { credentials, ctx } = await pendingCredentials(opts, correlationId);
      requireOtpCode(code, correlationId);
      await tokens.exchangeOtp(credentials, { code, otpType, remember }, ctx);
    },
    resendOtp: async (language, opts) => {
      const correlationId = correlationIdOf(opts);
      const { credentials, ctx } = await pendingCredentials(opts, correlationId);
      return tokens.resendOtp(credentials, language || resolved.otp.defaultResendLanguage, ctx);
    },

    beginSignPdf: (request, opts) => begin('Pdf', request, opts),
    beginSignXml: (request, opts) => begin('Xml', request, opts),
    beginSignWord: (request, opts) => begin('Word', request, opts),
    beginSignExcel: (request, opts) => begin('Excel', request, opts),
    handleWebhook: (envelope, opts) => handle(envelope, opts),
  };
}

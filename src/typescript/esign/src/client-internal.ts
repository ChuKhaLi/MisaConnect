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
import { withTransparentOtp } from './application/use-cases/two-factor.js';
import type { MisaCredentials } from './domain/dtos.js';
import { AuthenticationFailedException, ESignGeneralException } from './domain/errors.js';
import type { CallOptions, IMisaESignClient } from './client.js';
import {
  abortableDelay,
  firstCertificateSelector,
  systemClock,
} from './infrastructure/defaults.js';
import {
  resolveCredentials,
  validateOptions,
  type MisaESignOptions,
} from './infrastructure/options.js';
import { createTransport, type TransportDeps } from './infrastructure/transport.js';
import { createDefaultTokenManager } from './infrastructure/wire/auth.js';
import { encodeBase64 } from './infrastructure/wire/base64.js';
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
  const tokens = createDefaultTokenManager(resolved, transport);
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

  const correlationIdOf = (opts?: CallOptions): string =>
    opts?.correlationId ??
    resolved.correlationId?.() ??
    deps.newId?.() ??
    globalThis.crypto.randomUUID();

  const modeError = (rawCode: string, detail: string, correlationId: string, format: SignFormat) =>
    new ESignGeneralException({ category: 'Validation', rawCode, detail, correlationId, format });

  const notImplemented = (method: string, opts?: CallOptions): Promise<never> =>
    Promise.reject(
      new ESignGeneralException({
        category: 'MisaUnknown',
        rawCode: 'NotImplemented',
        detail: `${method} is not implemented yet.`,
        correlationId: correlationIdOf(opts),
      }),
    );

  /** Explicit-mode state: the user a 2FA challenge is pending for (set on failure, cleared on success). */
  let pendingUserName: string | undefined;

  /**
   * Runs one authenticated operation with the client's 2FA policy: with an `otpProvider` the
   * challenge is answered transparently (the whole operation is retried once); without one the
   * challenge is recorded for `signInWithOtp` / `resendOtp` and propagates. Task 11's `beginSign*`
   * calls this too.
   */
  async function withTwoFactor<T>(
    credentials: MisaCredentials,
    correlationId: string,
    signal: AbortSignal | undefined,
    operation: () => Promise<T>,
  ): Promise<T> {
    try {
      const result = resolved.otpProvider
        ? await withTransparentOtp(
            { tokens, otpProvider: resolved.otpProvider },
            { credentials, correlationId, signal },
            operation,
          )
        : await operation();
      pendingUserName = undefined;
      return result;
    } catch (err) {
      if (
        !resolved.otpProvider &&
        err instanceof AuthenticationFailedException &&
        err.requires2FA
      ) {
        pendingUserName = err.username || credentials.userName || undefined;
      }
      throw err;
    }
  }

  async function pendingCredentials(
    opts: CallOptions | undefined,
    correlationId: string,
  ): Promise<{
    credentials: MisaCredentials;
    ctx: { correlationId: string; signal?: AbortSignal };
  }> {
    const userName = pendingUserName;
    const signal = opts?.signal;
    signal?.throwIfAborted();
    if (userName === undefined) {
      throw new ESignGeneralException({
        category: 'Validation',
        rawCode: 'NoPendingOtpChallenge',
        detail:
          'NoPendingOtpChallenge: no 2FA challenge is pending; call this after a sign call fails with requires2FA.',
        correlationId,
      });
    }
    const ctx = { correlationId, ...(signal === undefined ? {} : { signal }) };
    const credentials = await resolveCredentials(resolved, ctx, opts?.credentials);
    return { credentials: { ...credentials, userName }, ctx };
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

  async function begin(format: SignFormat, opts: CallOptions | undefined): Promise<never> {
    if (mode === 'polling') {
      throw modeError(
        'PollingModeOnly',
        `beginSign${format} is unavailable when webhook.mode is 'polling'; use sign${format}.`,
        correlationIdOf(opts),
        format,
      );
    }
    return notImplemented(`beginSign${format}`, opts);
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

    // Task 10 replaces these.
    signInWithOtp: async (code, otpType, remember, opts) => {
      const correlationId = correlationIdOf(opts);
      const { credentials, ctx } = await pendingCredentials(opts, correlationId);
      if (!code || code.trim() === '') {
        throw new ESignGeneralException({
          category: 'Validation',
          rawCode: 'InvalidOtpSubmission',
          detail: 'InvalidOtpSubmission: the OTP code must not be empty.',
          correlationId,
        });
      }
      await tokens.exchangeOtp(credentials, { code, otpType, remember }, ctx);
    },
    resendOtp: async (language, opts) => {
      const correlationId = correlationIdOf(opts);
      const { credentials, ctx } = await pendingCredentials(opts, correlationId);
      return tokens.resendOtp(credentials, language || resolved.otp.defaultResendLanguage, ctx);
    },

    // Task 11 replaces these.
    beginSignPdf: (_request, opts) => begin('Pdf', opts),
    beginSignXml: (_request, opts) => begin('Xml', opts),
    beginSignWord: (_request, opts) => begin('Word', opts),
    beginSignExcel: (_request, opts) => begin('Excel', opts),
    handleWebhook: (_envelope, opts) => notImplemented('handleWebhook', opts),
  };
}

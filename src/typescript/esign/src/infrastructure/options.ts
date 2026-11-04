import type {
  CertificateSelector,
  Clock,
  Delay,
  Logger,
  MisaCredentialsProvider,
  OtpProvider,
  SigningSessionStore,
  TokenCache,
  WebhookDeliveryHook,
} from '../application/ports.js';
import type { MisaCredentials } from '../domain/dtos.js';
import type { WebhookMode } from '../domain/enums.js';
import { ESignGeneralException } from '../domain/errors.js';

export const ProductionHost = 'esignapp.misa.vn';

export interface MisaESignOptions {
  environment?: 'sandbox' | 'production';
  baseUrl: string;
  authUnderWebdev?: boolean;
  credentials: MisaCredentials | MisaCredentialsProvider;
  polling?: { intervalMs?: number; totalTimeoutMs?: number };
  transportRetry?: { maxAttempts?: number; baseDelayMs?: number; maxDelayMs?: number };
  errors?: { includeRawErrorMessage?: boolean };
  otp?: { defaultResendLanguage?: string };
  webhook?: { mode?: WebhookMode; sessionTtlMs?: number };
  tokenCache?: TokenCache;
  tokenCacheKey?: (c: { userName: string; clientId: string; host: string }) => string;
  certificateSelector?: CertificateSelector;
  otpProvider?: OtpProvider;
  signingSessionStore?: SigningSessionStore;
  webhookDeliveryHook?: WebhookDeliveryHook;
  clock?: Clock;
  delay?: Delay;
  correlationId?: () => string;
  logger?: Logger;
  fetch?: typeof fetch;
}

export interface ResolvedESignOptions {
  environment: 'sandbox' | 'production';
  /** scheme://host[:port]/ (path dropped). */
  origin: string;
  host: string;
  authUnderWebdev: boolean;
  credentials: MisaCredentials | MisaCredentialsProvider;
  polling: { intervalMs: number; totalTimeoutMs: number };
  transportRetry: { maxAttempts: number; baseDelayMs: number; maxDelayMs: number };
  errors: { includeRawErrorMessage: boolean };
  otp: { defaultResendLanguage: string };
  webhook: { mode: WebhookMode; sessionTtlMs: number };
  /** Collaborators are as given or `undefined`; defaults are filled where they are used. */
  tokenCache: TokenCache | undefined;
  tokenCacheKey: MisaESignOptions['tokenCacheKey'];
  certificateSelector: CertificateSelector | undefined;
  otpProvider: OtpProvider | undefined;
  signingSessionStore: SigningSessionStore | undefined;
  webhookDeliveryHook: WebhookDeliveryHook | undefined;
  clock: Clock | undefined;
  delay: Delay | undefined;
  correlationId: (() => string) | undefined;
  logger: Logger | undefined;
  fetch: typeof fetch | undefined;
}

const isBlank = (v: unknown): boolean => typeof v !== 'string' || v.trim() === '';
const LOOPBACK = new Set(['localhost', '127.0.0.1', '[::1]', '::1']);
const CREDENTIAL_FIELDS = ['clientId', 'clientKey', 'userName', 'password'] as const;

/**
 * Masks any userinfo in a URL-ish string for error messages: `https://user:pass@host` →
 * `https://***@host`. Works on unparseable input too, and on a password containing a raw `@`
 * (everything up to the last `@` before the path is masked).
 */
export function redactUserinfo(value: string): string {
  return value.replace(/^([a-z][a-z0-9+.-]*:\/\/)?[^/?#]*@/i, '$1***@');
}

export function validateOptions(o: MisaESignOptions): ResolvedESignOptions {
  const failures: string[] = [];
  const environment = o.environment ?? 'sandbox';
  if (environment !== 'sandbox' && environment !== 'production') {
    failures.push(
      `environment '${String(environment)}' is not a known value (sandbox|production).`,
    );
  }

  let url: URL | undefined;
  if (isBlank(o.baseUrl)) {
    failures.push('baseUrl is required.');
  } else {
    try {
      url = new URL(o.baseUrl);
    } catch {
      url = undefined;
    }
    if (url === undefined) {
      failures.push(`baseUrl '${redactUserinfo(o.baseUrl)}' must be an absolute URI.`);
    } else if (
      url.protocol !== 'https:' &&
      !(url.protocol === 'http:' && LOOPBACK.has(url.hostname.toLowerCase()))
    ) {
      failures.push(
        `baseUrl '${redactUserinfo(o.baseUrl)}' must use https (http is allowed only for localhost, 127.0.0.1 and ::1).`,
      );
    }
    if (url !== undefined && (environment === 'sandbox' || environment === 'production')) {
      const isProdHost = url.hostname.toLowerCase() === ProductionHost;
      if (environment === 'production' && !isProdHost) {
        failures.push(`Production environment requires baseUrl host '${ProductionHost}'.`);
      } else if (environment === 'sandbox' && isProdHost) {
        failures.push(`Sandbox environment must not use the production host '${ProductionHost}'.`);
      }
    }
  }

  const c: unknown = o.credentials;
  if (typeof c !== 'function') {
    const s = (c ?? {}) as Partial<MisaCredentials>;
    for (const f of CREDENTIAL_FIELDS) {
      if (isBlank(s[f])) failures.push(`credentials.${f} is required.`);
    }
  }

  const polling = {
    intervalMs: o.polling?.intervalMs ?? 2000,
    totalTimeoutMs: o.polling?.totalTimeoutMs ?? 60000,
  };
  if (!(polling.intervalMs > 0)) failures.push('polling.intervalMs must be greater than 0.');
  if (!(polling.totalTimeoutMs > polling.intervalMs)) {
    failures.push('polling.totalTimeoutMs must be greater than polling.intervalMs.');
  }

  const transportRetry = {
    maxAttempts: o.transportRetry?.maxAttempts ?? 3,
    baseDelayMs: o.transportRetry?.baseDelayMs ?? 200,
    maxDelayMs: o.transportRetry?.maxDelayMs ?? 2000,
  };
  if (!(transportRetry.maxAttempts >= 1)) {
    failures.push('transportRetry.maxAttempts must be at least 1.');
  }
  if (!(transportRetry.maxDelayMs >= transportRetry.baseDelayMs)) {
    failures.push('transportRetry.maxDelayMs must be at least transportRetry.baseDelayMs.');
  }

  const defaultResendLanguage = o.otp?.defaultResendLanguage ?? 'en-US';
  if (isBlank(defaultResendLanguage)) failures.push('otp.defaultResendLanguage must not be empty.');

  const webhook = {
    mode: o.webhook?.mode ?? 'both',
    sessionTtlMs: o.webhook?.sessionTtlMs ?? 86_400_000,
  };
  if (!(webhook.sessionTtlMs > 0)) failures.push('webhook.sessionTtlMs must be greater than 0.');

  if (failures.length > 0 || url === undefined) {
    throw new ESignGeneralException({
      category: 'Validation',
      rawCode: 'InvalidOptions',
      detail: failures.join('; '),
      correlationId: 'options',
    });
  }

  return {
    environment,
    origin: url.origin + '/',
    host: url.hostname.toLowerCase(),
    authUnderWebdev: o.authUnderWebdev ?? environment === 'sandbox',
    credentials: o.credentials,
    polling,
    transportRetry,
    errors: { includeRawErrorMessage: o.errors?.includeRawErrorMessage ?? false },
    otp: { defaultResendLanguage },
    webhook,
    tokenCache: o.tokenCache,
    tokenCacheKey: o.tokenCacheKey,
    certificateSelector: o.certificateSelector,
    otpProvider: o.otpProvider,
    signingSessionStore: o.signingSessionStore,
    webhookDeliveryHook: o.webhookDeliveryHook,
    clock: o.clock,
    delay: o.delay,
    correlationId: o.correlationId,
    logger: o.logger,
    fetch: o.fetch,
  };
}

function checkCredentials(c: MisaCredentials, correlationId: string): MisaCredentials {
  const missing = CREDENTIAL_FIELDS.filter((f) =>
    isBlank((c as Partial<MisaCredentials> | undefined)?.[f]),
  );
  if (missing.length > 0) {
    throw new ESignGeneralException({
      category: 'Validation',
      rawCode: 'InvalidCredentials',
      detail: `InvalidCredentials: missing required fields: ${missing.join(', ')}.`,
      correlationId,
    });
  }
  return c;
}

/** Per-call override, then provider (called with ctx), then static credentials. */
export async function resolveCredentials(
  resolved: ResolvedESignOptions,
  ctx: { clientId?: string; userName?: string; signal?: AbortSignal; correlationId?: string },
  override?: MisaCredentials,
): Promise<MisaCredentials> {
  const { correlationId, ...providerCtx } = ctx;
  const cid = correlationId ?? 'credentials';
  if (override !== undefined) return checkCredentials(override, cid);
  if (typeof resolved.credentials === 'function') {
    return checkCredentials(await resolved.credentials(providerCtx), cid);
  }
  return resolved.credentials;
}

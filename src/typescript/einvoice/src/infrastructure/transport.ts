import type {
  AccessToken,
  Envelope,
  HttpMethod,
  MeInvoiceTransport,
  RequestOptions,
} from '../application/ports.js';
import {
  ensureAccessToken,
  TOKEN_LIFETIME_MS,
  type EnsureAccessTokenDeps,
} from '../application/use-cases/ensure-access-token.js';
import { MeInvoiceErrorCategory, MeInvoiceException } from '../domain/errors.js';
import { readEnvelope, throwFromEnvelope } from './envelope.js';
import { serializeRequest } from './json.js';
import type { ResolvedOptions } from './options.js';

export const TOKEN_ROUTE = 'webapp/token';

/**
 * OAuth-style string codes that only the token endpoint returns, in the `error` field with an
 * empty `errorCode` array (verified against the sandbox 2026-11-03). MISA answers INVALID_USER
 * for a wrong user, password, tax code or AppID; INVALID_TAXCODE for a malformed wire tax code
 * (e.g. `taxCode` already carrying the `-<appId>` suffix). .NET maps both to MisaUnknown.
 */
const TOKEN_ERRORS: ReadonlyMap<string, { category: MeInvoiceErrorCategory; message: string }> =
  new Map([
    [
      'INVALID_USER',
      {
        category: MeInvoiceErrorCategory.Authentication,
        message: 'MISA rejected the sign-in (wrong userName, password, taxCode or appId).',
      },
    ],
    [
      'INVALID_TAXCODE',
      {
        category: MeInvoiceErrorCategory.Configuration,
        message:
          'MISA rejected the tax code format. Pass the bare tax code as taxCode and the AppID separately as appId.',
      },
    ],
  ]);

function throwIfTokenError(envelope: Envelope<unknown>): void {
  if (Array.isArray(envelope.errorCode) ? envelope.errorCode.length > 0 : envelope.errorCode)
    return;
  if (typeof envelope.error !== 'string') return;
  const known = TOKEN_ERRORS.get(envelope.error.trim().toUpperCase());
  if (known === undefined) return;
  throw new MeInvoiceException(known.category, {
    rawErrorCode: envelope.error.trim().toUpperCase(),
    message: known.message,
  });
}

/** Parity with the .NET `HttpClient.Timeout` default. */
export const DEFAULT_TIMEOUT_MS = 100_000;
const BACKOFF_BASE_MS = 1250;
const BACKOFF_JITTER_MS = 500;

export interface TransportDeps {
  /** Backoff wait; must reject with `signal.reason` when the signal aborts. */
  delay?: (ms: number, signal?: AbortSignal) => Promise<void>;
  /** Returns a number in [0, 1). */
  random?: () => number;
  /** Per-attempt timeout. */
  timeoutMs?: number;
}

/** Port `WireTaxCode.Compose`. */
export function wireTaxCode(taxCode: string, appId: string): string {
  return `${taxCode}-${appId}`;
}

/** Default backoff: a `setTimeout` wait that rejects with the signal's reason on abort. */
export function abortableDelay(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    if (signal?.aborted) {
      reject(signal.reason as Error);
      return;
    }
    const onAbort = (): void => {
      clearTimeout(timer);
      reject(signal?.reason as Error);
    };
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort);
      resolve();
    }, ms);
    signal?.addEventListener('abort', onAbort, { once: true });
  });
}

interface RawResponse {
  status: number;
  text: string;
}

/** An attempt that produced no HTTP response; eligible for the single retry. */
class AttemptFailure {
  constructor(
    readonly kind: 'Timeout' | 'ConnectionFailed',
    readonly cause: unknown,
  ) {}
}

// Names of .NET `HttpStatusCode` members, used as `rawErrorCode` (parity with `ThrottleRetryHandler`).
const STATUS_NAMES: Readonly<Record<number, string>> = {
  429: 'TooManyRequests',
  500: 'InternalServerError',
  501: 'NotImplemented',
  502: 'BadGateway',
  503: 'ServiceUnavailable',
  504: 'GatewayTimeout',
  505: 'HttpVersionNotSupported',
  506: 'VariantAlsoNegotiates',
  507: 'InsufficientStorage',
  508: 'LoopDetected',
  510: 'NotExtended',
  511: 'NetworkAuthenticationRequired',
};

const isRetryableStatus = (status: number): boolean => status === 429 || status >= 500;

function attemptSignal(
  caller: AbortSignal | undefined,
  timeoutMs: number,
): { signal: AbortSignal; timedOut: () => boolean; dispose: () => void } {
  const ctrl = new AbortController();
  let timedOut = false;
  const timer = setTimeout(() => {
    timedOut = true;
    ctrl.abort(new DOMException('The operation timed out.', 'TimeoutError'));
  }, timeoutMs);
  const onAbort = (): void => ctrl.abort(caller?.reason);
  if (caller?.aborted) onAbort();
  else caller?.addEventListener('abort', onAbort, { once: true });
  return {
    signal: ctrl.signal,
    timedOut: () => timedOut,
    dispose: () => {
      clearTimeout(timer);
      caller?.removeEventListener('abort', onAbort);
    },
  };
}

/**
 * fetch-based MISA transport. Layers, outer to inner: retry (one retry on 429 / 5xx /
 * network error / timeout) → bearer (token + `taxcode` headers, one refresh on 401;
 * bypassed for the token route) → single fetch attempt with a per-attempt timeout.
 */
export function createTransport(
  resolved: ResolvedOptions,
  deps: TransportDeps = {},
): MeInvoiceTransport {
  const doFetch = resolved.fetch;
  const delay = deps.delay ?? abortableDelay;
  const random = deps.random ?? Math.random;
  const timeoutMs = deps.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const wire = wireTaxCode(resolved.taxCode, resolved.appId);
  const tokenDeps: EnsureAccessTokenDeps = {
    cacheKey: wire,
    tokenCache: resolved.tokenCache,
    clock: resolved.clock,
    acquire: (signal) => acquireToken(signal),
    inflight: new Map<string, Promise<AccessToken>>(),
  };

  async function sendOnce(
    method: HttpMethod,
    url: string,
    headers: Record<string, string>,
    body: string | undefined,
    signal: AbortSignal | undefined,
  ): Promise<RawResponse> {
    const attempt = attemptSignal(signal, timeoutMs);
    try {
      const init: RequestInit = { method, headers, signal: attempt.signal };
      if (body !== undefined) init.body = body;
      const response = await doFetch(url, init);
      const text = await response.text();
      return { status: response.status, text };
    } catch (err) {
      if (signal?.aborted) throw signal.reason;
      if (attempt.timedOut()) throw new AttemptFailure('Timeout', err);
      // Any other fetch/body failure (undici TypeError, node-fetch FetchError, proxy agents…).
      throw new AttemptFailure('ConnectionFailed', err);
    } finally {
      attempt.dispose();
    }
  }

  function headersFor(body: string | undefined, token?: string): Record<string, string> {
    const headers: Record<string, string> = {};
    if (token !== undefined) {
      headers['Authorization'] = `Bearer ${token}`;
      headers['taxcode'] = wire;
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    return headers;
  }

  async function evictIfCurrent(token: string): Promise<void> {
    const cached = await resolved.tokenCache.get(wire);
    if (cached?.value === token) await resolved.tokenCache.delete(wire);
  }

  async function sendWithBearer(
    method: HttpMethod,
    url: string,
    body: string | undefined,
    signal: AbortSignal | undefined,
  ): Promise<RawResponse> {
    const token = await ensureAccessToken(tokenDeps, signal);
    const first = await sendOnce(method, url, headersFor(body, token), body, signal);
    if (first.status !== 401) return first;

    // Only evict the token we used, so concurrent 401s don't discard a fresh refresh.
    await evictIfCurrent(token);
    const refreshed = await ensureAccessToken(tokenDeps, signal, token);
    const second = await sendOnce(method, url, headersFor(body, refreshed), body, signal);
    if (second.status === 401) {
      throw new MeInvoiceException('Authentication', {
        rawErrorCode: 'UnAuthorize',
        message: 'MISA rejected the access token even after refresh.',
      });
    }
    return second;
  }

  async function withRetry(
    send: () => Promise<RawResponse>,
    signal: AbortSignal | undefined,
  ): Promise<RawResponse> {
    const attempt = async (): Promise<RawResponse | AttemptFailure> => {
      try {
        return await send();
      } catch (err) {
        if (err instanceof AttemptFailure) return err;
        throw err;
      }
    };
    const first = await attempt();
    if (!(first instanceof AttemptFailure) && !isRetryableStatus(first.status)) return first;

    await delay(BACKOFF_BASE_MS + Math.floor(random() * BACKOFF_JITTER_MS), signal);
    signal?.throwIfAborted();

    const second = await attempt();
    if (!(second instanceof AttemptFailure) && !isRetryableStatus(second.status)) return second;
    // .NET parity (ThrottleRetryHandler): when the first attempt got no response, the
    // first failure decides the error even if the retry returned 429/5xx.
    throw retryExhausted(first instanceof AttemptFailure ? first : second);
  }

  async function execute<T>(
    method: HttpMethod,
    path: string,
    query: Record<string, string>,
    body: unknown,
    signal: AbortSignal | undefined,
  ): Promise<Envelope<T>> {
    signal?.throwIfAborted();
    const url = buildUrl(resolved.baseUrl, path, query);
    const text = body === undefined ? undefined : serializeRequest(body);
    const bypassBearer = new URL(url).pathname.toLowerCase().endsWith(`/${TOKEN_ROUTE}`);
    const response = await withRetry(
      () =>
        bypassBearer
          ? sendOnce(method, url, headersFor(text), text, signal)
          : sendWithBearer(method, url, text, signal),
      signal,
    );
    return readEnvelope<T>(response.status, response.text);
  }

  async function acquireToken(signal?: AbortSignal): Promise<AccessToken> {
    const envelope = await execute<unknown>(
      'POST',
      TOKEN_ROUTE,
      {},
      { taxcode: wire, username: resolved.userName, password: resolved.password },
      signal,
    );
    if (!envelope.success || envelope.data === undefined) {
      throwIfTokenError(envelope);
      throwFromEnvelope(envelope);
    }
    const inner = envelope.data;
    if (typeof inner !== 'object' || inner === null || Array.isArray(inner)) {
      throw new MeInvoiceException('MisaUnknown', {
        rawErrorCode: 'EmptyResponse',
        message: 'Token response had no inner payload.',
      });
    }
    const value = (inner as Record<string, unknown>)['access_token'];
    if (typeof value !== 'string' || value === '') {
      throw new MeInvoiceException('Authentication', {
        rawErrorCode: 'EmptyToken',
        message: 'MISA returned a token response with no access_token.',
      });
    }
    return { value, expiresAt: new Date(resolved.clock.now().getTime() + TOKEN_LIFETIME_MS) };
  }

  return {
    request<T>(
      method: HttpMethod,
      path: string,
      query: Record<string, string>,
      body?: unknown,
      opts?: RequestOptions,
    ): Promise<Envelope<T>> {
      return execute<T>(method, path, query, body, opts?.signal);
    },
  };
}

function retryExhausted(outcome: RawResponse | AttemptFailure): MeInvoiceException {
  if (outcome instanceof AttemptFailure) {
    if (outcome.kind === 'Timeout') {
      return new MeInvoiceException('TransportFailed', {
        rawErrorCode: 'Timeout',
        message: 'MISA call timed out after one retry.',
        cause: outcome.cause,
      });
    }
    const cause = outcome.cause;
    return new MeInvoiceException('TransportFailed', {
      rawErrorCode: 'ConnectionFailed',
      message: cause instanceof Error ? cause.message : 'MISA connection failed after one retry.',
      cause,
    });
  }
  const status = outcome.status;
  return new MeInvoiceException(status === 429 ? 'MisaThrottled' : 'MisaUnavailable', {
    rawErrorCode: STATUS_NAMES[status] ?? String(status),
    message: `MISA call failed with status ${status} after one retry.`,
  });
}

function buildUrl(baseUrl: string, path: string, query: Record<string, string>): string {
  const qs = Object.entries(query)
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)
    .join('&');
  return `${baseUrl}${path.replace(/^\/+/, '')}${qs === '' ? '' : `?${qs}`}`;
}

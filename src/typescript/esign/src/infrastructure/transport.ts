import type { MisaCredentials } from '../domain/dtos.js';
import {
  ESignGeneralException,
  ESignTransportException,
  isESignException,
} from '../domain/errors.js';
import { abortableDelay } from './defaults.js';
import { noopLogger, safeLogger } from './logging.js';
import { createNodeHttpsFetch } from './node-https-fetch.js';
import type { ResolvedESignOptions } from './options.js';
import { endpointLabel, resolveUrl, type RouteKey } from './routes.js';

/** Per-attempt timeout (parity with the .NET `HttpClient.Timeout` default). */
export const DEFAULT_TIMEOUT_MS = 100_000;

export interface TransportRequest {
  route: RouteKey;
  method: 'GET' | 'POST';
  suffix?: string;
  body?: unknown;
  credentials: MisaCredentials;
  bearer?: string;
  correlationId: string;
  signal?: AbortSignal;
  /**
   * A 2xx body must be JSON: unparseable bodies throw `UnparseableResponse`, and (on ESRM routes
   * only) a non-JSON content type throws `UnexpectedContentType`. Auth routes parse the body as
   * JSON whatever the content type, as .NET does.
   */
  expectJson: boolean;
}

/** Auth routes: .NET reads the body as a string and deserializes it whatever the content type. */
const AUTH_ROUTES: ReadonlySet<RouteKey> = new Set<RouteKey>([
  'login',
  'twoFactor',
  'refresh',
  'resendOtp',
]);

export interface TransportResponse {
  status: number;
  json: unknown;
  text: string;
  contentType: string;
}

export interface Transport {
  send(req: TransportRequest): Promise<TransportResponse>;
}

export interface TransportDeps {
  /** Returns a number in [0, 1). */
  random?: () => number;
  /** Per-attempt timeout override (tests). */
  timeoutMs?: number;
}

/** Network error codes that prove no request bytes reached MISA (safe to retry a sign call). */
const SIGN_SAFE_CODES = new Set(['ECONNREFUSED', 'ENOTFOUND', 'EAI_AGAIN']);

function errorCode(err: unknown): string | undefined {
  let cur: unknown = err;
  for (let i = 0; i < 4 && cur !== null && typeof cur === 'object'; i++) {
    const code = (cur as { code?: unknown }).code;
    if (typeof code === 'string') return code;
    cur = (cur as { cause?: unknown }).cause;
  }
  return undefined;
}

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

type Attempt =
  | { kind: 'response'; status: number; text: string; contentType: string; retryAfter?: string }
  | { kind: 'timeout'; cause: unknown }
  | { kind: 'network'; cause: unknown; code?: string };

function retryAfterMs(value: string | undefined, nowMs: number): number | undefined {
  if (value === undefined || value.trim() === '') return undefined;
  const v = value.trim();
  if (/^\d+$/.test(v)) return Number(v) * 1000;
  const at = Date.parse(v);
  return Number.isNaN(at) ? undefined : Math.max(0, at - nowMs);
}

export function createTransport(
  resolved: ResolvedESignOptions,
  deps: TransportDeps = {},
): Transport {
  const doFetch = resolved.fetch ?? createNodeHttpsFetch();
  const delay = resolved.delay ?? abortableDelay;
  const random = deps.random ?? Math.random;
  const timeoutMs = deps.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const logger = safeLogger(resolved.logger ?? noopLogger);
  const { maxAttempts, baseDelayMs, maxDelayMs } = resolved.transportRetry;

  async function attemptOnce(
    req: TransportRequest,
    url: string,
    headers: Record<string, string>,
    body: string | undefined,
  ): Promise<Attempt> {
    const attempt = attemptSignal(req.signal, timeoutMs);
    try {
      const init: RequestInit = { method: req.method, headers, signal: attempt.signal };
      if (body !== undefined) init.body = body;
      const response = await doFetch(url, init);
      const text = await response.text();
      const out: Attempt = {
        kind: 'response',
        status: response.status,
        text,
        contentType: response.headers.get('content-type') ?? '',
      };
      const ra = response.headers.get('retry-after');
      if (ra !== null) out.retryAfter = ra;
      return out;
    } catch (err) {
      if (req.signal?.aborted) throw req.signal.reason;
      if (attempt.timedOut()) return { kind: 'timeout', cause: err };
      const code = errorCode(err);
      return code === undefined
        ? { kind: 'network', cause: err }
        : { kind: 'network', cause: err, code };
    } finally {
      attempt.dispose();
    }
  }

  function isRetryable(route: RouteKey, a: Attempt): boolean {
    if (route === 'signHash') {
      if (a.kind === 'response') return a.status === 429;
      if (a.kind === 'network') return a.code !== undefined && SIGN_SAFE_CODES.has(a.code);
      return false;
    }
    if (a.kind === 'response') return a.status === 429 || a.status >= 500;
    return true;
  }

  function waitMs(a: Attempt, n: number): number {
    const jitter = random() * Math.min(maxDelayMs, baseDelayMs * 2 ** (n - 1));
    if (a.kind === 'response' && a.status === 429) {
      const now = resolved.clock?.now().getTime() ?? Date.now();
      const ra = retryAfterMs(a.retryAfter, now);
      if (ra !== undefined) return Math.min(ra, maxDelayMs);
    }
    return jitter;
  }

  function toResponse(a: Extract<Attempt, { kind: 'response' }>, req: TransportRequest) {
    const ct = a.contentType;
    // Auth routes are content-type agnostic: try JSON whatever the header says (.NET parity).
    const isJson = AUTH_ROUTES.has(req.route) || /json/i.test(ct);
    const label = endpointLabel(req.route);
    let parsed: unknown;
    if (isJson && a.text.trim() === '') {
      // An empty JSON body is not a content-type error: callers see `json: undefined` (INV §5.4).
      parsed = undefined;
    } else if (isJson) {
      try {
        parsed = JSON.parse(a.text);
      } catch {
        if (req.expectJson === true && a.status >= 200 && a.status < 300) {
          throw new ESignGeneralException({
            category: 'MisaUnknown',
            rawCode: 'UnparseableResponse',
            detail: `MISA ${label} returned a JSON response that could not be parsed (body length ${a.text.length}).`,
            correlationId: req.correlationId,
          });
        }
      }
    } else if (req.expectJson === true && a.status >= 200 && a.status < 300) {
      throw new ESignGeneralException({
        category: 'MisaUnknown',
        rawCode: 'UnexpectedContentType',
        // Never echo the body: it may carry tokens, certificates, document bytes or PII.
        detail: `MISA ${label} returned content type '${ct}' instead of JSON (body length ${a.text.length}).`,
        correlationId: req.correlationId,
      });
    }
    return { status: a.status, json: parsed, text: a.text, contentType: ct };
  }

  async function run(req: TransportRequest): Promise<TransportResponse> {
    req.signal?.throwIfAborted();
    const url = resolveUrl(resolved, req.route, req.suffix);
    const body = req.body === undefined ? undefined : JSON.stringify(req.body);
    const headers: Record<string, string> = {
      'x-clientId': req.credentials.clientId,
      'x-clientKey': req.credentials.clientKey,
      'X-Correlation-Id': req.correlationId,
    };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (req.bearer !== undefined) headers['AuthorizationRM'] = `Bearer ${req.bearer}`;

    for (let n = 1; ; n++) {
      const a = await attemptOnce(req, url, headers, body);
      if (a.kind === 'response' && !(a.status === 429 || a.status >= 500))
        return toResponse(a, req);
      if (!isRetryable(req.route, a) || n === maxAttempts) {
        throw failure(a, n, req);
      }
      await delay(waitMs(a, n), req.signal);
      req.signal?.throwIfAborted();
    }
  }

  function failure(a: Attempt, attempts: number, req: TransportRequest): ESignTransportException {
    const label = endpointLabel(req.route);
    if (a.kind === 'response') {
      return new ESignTransportException({
        lastStatusCode: a.status,
        attemptCount: attempts,
        detail: `MISA ${label} failed with HTTP ${a.status} after ${attempts} attempt(s).`,
        correlationId: req.correlationId,
      });
    }
    return new ESignTransportException({
      attemptCount: attempts,
      detail:
        a.kind === 'timeout'
          ? `MISA ${label} timed out after ${attempts} attempt(s).`
          : `MISA ${label} connection failed after ${attempts} attempt(s)${
              a.code === undefined ? '' : ` (${a.code})`
            }.`,
      correlationId: req.correlationId,
      cause: a.cause,
    });
  }

  return {
    async send(req) {
      const fields = {
        endpoint: endpointLabel(req.route),
        method: req.method,
        correlationId: req.correlationId,
      };
      logger.info('MISA eSign call started', fields);
      const started = Date.now();
      try {
        const res = await run(req);
        logger.info('MISA eSign call completed', {
          ...fields,
          durationMs: Date.now() - started,
          outcome: 'Success',
          status: res.status,
        });
        return res;
      } catch (err) {
        const durationMs = Date.now() - started;
        if (req.signal?.aborted && !isESignException(err)) {
          logger.info('MISA eSign call completed', { ...fields, durationMs, outcome: 'Cancelled' });
          throw err;
        }
        if (isESignException(err)) {
          logger.warn('MISA eSign call failed', {
            ...fields,
            errorCategory: err.category,
            ...(err.rawCode === undefined ? {} : { rawCode: err.rawCode }),
          });
        } else {
          logger.error('MISA eSign call failed', fields);
        }
        logger.info('MISA eSign call completed', { ...fields, durationMs, outcome: 'Error' });
        throw err;
      }
    },
  };
}

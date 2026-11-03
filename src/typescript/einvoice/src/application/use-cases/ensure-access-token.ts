import type { AccessToken, Clock, TokenCache } from '../ports.js';

const HOUR_MS = 60 * 60 * 1000;
/** Reuse a cached token only while it is valid for more than this (port `EnsureAccessToken.cs`). */
export const SAFETY_SKEW_MS = HOUR_MS;
/** MISA tokens live 14 days; stored expiry is 14 days − 1 h. */
export const TOKEN_LIFETIME_MS = 14 * 24 * HOUR_MS;

export interface EnsureAccessTokenDeps {
  cacheKey: string;
  tokenCache: TokenCache;
  clock: Clock;
  acquire: (signal?: AbortSignal) => Promise<AccessToken>;
  /** Single-flight state, owned by one transport instance. */
  inflight: Map<string, Promise<AccessToken>>;
}

/**
 * Returns a cached access token or acquires a new one. Concurrent misses for the same
 * key share one in-flight acquisition. The shared acquisition is not bound to any one
 * caller's signal; each caller's `signal` only abandons its own wait.
 */
export async function ensureAccessToken(
  deps: EnsureAccessTokenDeps,
  signal?: AbortSignal,
): Promise<string> {
  signal?.throwIfAborted();
  const key = deps.cacheKey;
  const threshold = deps.clock.now().getTime() + SAFETY_SKEW_MS;
  const cached = await raceAbort(deps.tokenCache.get(key), signal);
  if (cached !== undefined && expiryTime(cached.expiresAt) > threshold) return cached.value;

  let pending = deps.inflight.get(key);
  if (pending === undefined) {
    const started = acquireAndStore(deps);
    pending = started;
    deps.inflight.set(key, started);
    const clear = (): void => {
      if (deps.inflight.get(key) === started) deps.inflight.delete(key);
    };
    started.then(clear, clear);
  }
  return (await raceAbort(pending, signal)).value;
}

/**
 * Expiry in epoch ms. Custom caches that deserialise JSON may hand back an ISO string or a
 * number instead of a `Date`; those are converted, and anything unparseable counts as expired.
 */
function expiryTime(expiresAt: unknown): number {
  const date =
    expiresAt instanceof Date
      ? expiresAt
      : typeof expiresAt === 'string' || typeof expiresAt === 'number'
        ? new Date(expiresAt)
        : undefined;
  const ms = date?.getTime();
  return ms === undefined || Number.isNaN(ms) ? Number.NEGATIVE_INFINITY : ms;
}

async function acquireAndStore(deps: EnsureAccessTokenDeps): Promise<AccessToken> {
  const fresh = await deps.acquire();
  const token: AccessToken = {
    value: fresh.value,
    expiresAt: new Date(deps.clock.now().getTime() + TOKEN_LIFETIME_MS - SAFETY_SKEW_MS),
  };
  await deps.tokenCache.set(deps.cacheKey, token);
  return token;
}

function raceAbort<T>(promise: Promise<T>, signal: AbortSignal | undefined): Promise<T> {
  if (signal === undefined) return promise;
  if (signal.aborted) return Promise.reject(signal.reason as Error);
  return new Promise<T>((resolve, reject) => {
    const onAbort = (): void => reject(signal.reason as Error);
    signal.addEventListener('abort', onAbort, { once: true });
    promise.then(
      (value) => {
        signal.removeEventListener('abort', onAbort);
        resolve(value);
      },
      (err: unknown) => {
        signal.removeEventListener('abort', onAbort);
        reject(err as Error);
      },
    );
  });
}

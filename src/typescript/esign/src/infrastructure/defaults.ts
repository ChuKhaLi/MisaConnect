import { createHash, randomBytes } from 'node:crypto';
import type { MisaCredentials } from '../domain/dtos.js';
import type {
  AccessToken,
  CertificateSelector,
  Clock,
  Delay,
  TokenCache,
} from '../application/ports.js';

/** Default token cache: a process-local map (one per client instance). */
export function createInMemoryTokenCache(): TokenCache {
  const store = new Map<string, AccessToken>();
  return {
    get: (key) => store.get(key),
    set: (key, token) => {
      store.set(key, token);
    },
    delete: (key) => {
      store.delete(key);
    },
  };
}

export const systemClock: Clock = { now: () => new Date() };

/** Default backoff: a `setTimeout` wait that rejects with the signal's reason on abort. */
export const abortableDelay: Delay = (ms, signal) =>
  new Promise<void>((resolve, reject) => {
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

/** Default token-cache key `"{userName}|{clientId}|{host}"` (mirrors .NET `DefaultTokenCacheKeySelector`). */
export function defaultTokenCacheKey(c: {
  userName: string;
  clientId: string;
  host: string;
}): string {
  return `${c.userName}|${c.clientId}|${c.host}`;
}

/**
 * Returns a function computing `hex(SHA-256(salt ‖ password ‖ '\0' ‖ clientKey))` with a random
 * salt generated once per call of this factory (one per token manager). Never log its output.
 */
export function createCredentialFingerprinter(): (
  c: Pick<MisaCredentials, 'password' | 'clientKey'>,
) => string {
  const salt = randomBytes(16);
  return (c) =>
    createHash('sha256')
      .update(salt)
      .update(c.password)
      .update('\0')
      .update(c.clientKey)
      .digest('hex');
}

/** Default certificate selector: the first active certificate. */
export const firstCertificateSelector: CertificateSelector = (certs) => {
  const first = certs[0];
  if (first === undefined) throw new RangeError('No certificate to select from.');
  return first;
};

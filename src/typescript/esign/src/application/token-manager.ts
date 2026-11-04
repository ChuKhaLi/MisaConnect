import type { MisaCredentials, OtpResendResultDto } from '../domain/dtos.js';
import type { OtpDeliveryChannel } from '../domain/enums.js';
import { AuthenticationFailedException, ESignGeneralException } from '../domain/errors.js';
import type { AccessToken, Clock, TokenCache } from './ports.js';

/** Proactive refresh window: a cached token is reused only while it is valid for longer (design §9.8). */
export const REFRESH_SKEW_MS = 30_000;

export interface AuthCallContext {
  correlationId: string;
  signal?: AbortSignal | undefined;
}

export interface OtpSubmission {
  code: string;
  otpType: OtpDeliveryChannel;
  remember: boolean;
}

/** What a refresh returns; omitted fields are taken from the previous token. */
export interface RefreshedSession {
  value: string;
  refreshToken?: string | undefined;
  expiresAt: Date;
  userId?: string | undefined;
  username?: string | undefined;
}

/** MISA auth endpoints (login, refresh, two-factor, resend). Errors are thrown already mapped. */
export interface AuthGateway {
  login(creds: MisaCredentials, ctx: AuthCallContext): Promise<AccessToken>;
  refresh(
    creds: MisaCredentials,
    refreshToken: string,
    ctx: AuthCallContext,
  ): Promise<RefreshedSession>;
  twoFactor(
    creds: MisaCredentials,
    submission: OtpSubmission,
    ctx: AuthCallContext,
  ): Promise<AccessToken>;
  resendOtp(
    creds: MisaCredentials,
    language: string,
    ctx: AuthCallContext,
  ): Promise<OtpResendResultDto>;
}

export interface TokenManagerDeps {
  gateway: AuthGateway;
  tokenCache: TokenCache;
  clock: Clock;
  /** Composes the cache key; called only with non-empty `userName` and `clientId`. */
  cacheKey: (creds: MisaCredentials) => string;
  /**
   * Binds cached tokens to the credential secrets (R5): a cached token is used only when its
   * `credentialFingerprint` equals this value for the caller's credentials. Never log it.
   */
  fingerprint: (creds: MisaCredentials) => string;
}

export interface TokenManager {
  getToken(creds: MisaCredentials, ctx: AuthCallContext): Promise<AccessToken>;
  /**
   * Runs `call` with the current bearer. When it fails with HTTP 401 (an
   * `AuthenticationFailedException` that is not a 2FA challenge), re-authenticates once (refresh,
   * else login) and re-runs `call` with the new token; a second 401 fails with rawCode "401".
   */
  withAuth<T>(
    creds: MisaCredentials,
    ctx: AuthCallContext,
    call: (bearer: string, userId: string) => Promise<T>,
  ): Promise<T>;
  exchangeOtp(
    creds: MisaCredentials,
    submission: OtpSubmission,
    ctx: AuthCallContext,
  ): Promise<AccessToken>;
  resendOtp(
    creds: MisaCredentials,
    language: string,
    ctx: AuthCallContext,
  ): Promise<OtpResendResultDto>;
}

/**
 * Expiry in epoch ms. Custom caches that deserialise JSON may hand back an ISO string or a number
 * instead of a `Date`; those are converted, and anything unparseable counts as expired.
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

/** A 401 from an ESRM call (mapESignError's only plain `AuthenticationFailedException`; ruling R3). */
function isStaleToken(err: unknown): boolean {
  return err instanceof AuthenticationFailedException && !err.requires2FA;
}

/**
 * Token use cases for one client: cache-or-acquire with refresh-then-login, the 401 re-auth path
 * (design §9.1/§9.2), OTP exchange and resend. Concurrent work for one cache key is shared
 * (single flight); the shared work is not bound to any one caller's signal, each caller's signal
 * only abandons its own wait.
 */
export function createTokenManager(deps: TokenManagerDeps): TokenManager {
  const { gateway, tokenCache, clock } = deps;
  const flights = new Map<string, Promise<AccessToken>>();

  function singleFlight(
    slot: string,
    signal: AbortSignal | undefined,
    work: () => Promise<AccessToken>,
  ): Promise<AccessToken> {
    signal?.throwIfAborted();
    let p = flights.get(slot);
    if (p === undefined) {
      const started = work();
      p = started;
      flights.set(slot, started);
      const clear = (): void => {
        if (flights.get(slot) === started) flights.delete(slot);
      };
      started.then(clear, clear);
    }
    return raceAbort(p, signal);
  }

  function keyFor(creds: MisaCredentials, correlationId: string): string {
    if (!creds.userName || !creds.clientId) {
      throw new ESignGeneralException({
        category: 'Validation',
        rawCode: 'InvalidCredentials',
        detail:
          'InvalidCredentials: cannot compose the token-cache key; userName and clientId must both be non-empty.',
        correlationId,
      });
    }
    return deps.cacheKey(creds);
  }

  /** Slot of a shared flight: kind, cache key and credential fingerprint (R5: never shared across secrets). */
  const slotOf = (kind: string, key: string, fp: string): string => `${kind}\0${key}\0${fp}`;

  const isFresh = (t: AccessToken): boolean =>
    expiryTime(t.expiresAt) - REFRESH_SKEW_MS > clock.now().getTime();

  /** The cached entry when it was obtained with the same credential secrets (R5), else undefined. */
  async function readOwned(key: string, fp: string): Promise<AccessToken | undefined> {
    const cached = (await tokenCache.get(key)) ?? undefined;
    return cached !== undefined && cached.credentialFingerprint === fp ? cached : undefined;
  }

  async function store(key: string, fp: string, token: AccessToken): Promise<AccessToken> {
    const bound: AccessToken = { ...token, credentialFingerprint: fp };
    await tokenCache.set(key, bound);
    return bound;
  }

  /** Evicts the entry only while it still belongs to these credentials (never another user's). */
  async function evictOwned(key: string, fp: string): Promise<void> {
    if ((await readOwned(key, fp)) !== undefined) await tokenCache.delete(key);
  }

  /**
   * Refreshes `previous` (merging omitted fields); on any failure evicts and logs in once.
   * `previous` must belong to these credentials; with none, logs in directly.
   */
  async function renew(
    key: string,
    fp: string,
    creds: MisaCredentials,
    previous: AccessToken | undefined,
    ctx: AuthCallContext,
  ): Promise<AccessToken> {
    const rt = previous?.refreshToken;
    if (previous !== undefined && rt) {
      let refreshed: AccessToken | undefined;
      try {
        const s = await gateway.refresh(creds, rt, ctx);
        refreshed = {
          value: s.value,
          refreshToken: s.refreshToken || rt,
          expiresAt: s.expiresAt,
          userId: s.userId || previous.userId,
          username: s.username || previous.username,
        };
      } catch {
        refreshed = undefined;
      }
      if (refreshed !== undefined) return store(key, fp, refreshed);
      await evictOwned(key, fp);
    }
    return store(key, fp, await gateway.login(creds, ctx));
  }

  async function getToken(creds: MisaCredentials, ctx: AuthCallContext): Promise<AccessToken> {
    const key = keyFor(creds, ctx.correlationId);
    const fp = deps.fingerprint(creds);
    const shared: AuthCallContext = { correlationId: ctx.correlationId };
    return singleFlight(slotOf('acquire', key, fp), ctx.signal, async () => {
      const owned = await readOwned(key, fp);
      if (owned !== undefined && isFresh(owned)) return owned;
      return renew(key, fp, creds, owned, shared);
    });
  }

  async function reauthenticate(
    creds: MisaCredentials,
    stale: AccessToken,
    ctx: AuthCallContext,
  ): Promise<AccessToken> {
    const key = keyFor(creds, ctx.correlationId);
    const fp = deps.fingerprint(creds);
    const shared: AuthCallContext = { correlationId: ctx.correlationId };
    return singleFlight(slotOf('reauth', key, fp), ctx.signal, async () => {
      const owned = await readOwned(key, fp);
      // Another caller with the same credentials already replaced the rejected token: use theirs.
      if (owned !== undefined && isFresh(owned) && owned.value !== stale.value) return owned;
      return renew(key, fp, creds, owned ?? stale, shared);
    });
  }

  return {
    getToken,

    async withAuth(creds, ctx, call) {
      const token = await getToken(creds, ctx);
      try {
        return await call(token.value, token.userId);
      } catch (err) {
        if (!isStaleToken(err)) throw err;
      }
      const fresh = await reauthenticate(creds, token, ctx);
      try {
        return await call(fresh.value, fresh.userId);
      } catch (err) {
        if (!isStaleToken(err)) throw err;
        throw new AuthenticationFailedException({
          rawCode: '401',
          detail: 'MISA rejected the renewed access token with HTTP 401.',
          correlationId: ctx.correlationId,
          cause: err,
        });
      }
    },

    exchangeOtp(creds, submission, ctx) {
      const key = keyFor(creds, ctx.correlationId);
      const fp = deps.fingerprint(creds);
      const shared: AuthCallContext = { correlationId: ctx.correlationId };
      return singleFlight(slotOf('otp', key, fp), ctx.signal, async () =>
        store(key, fp, await gateway.twoFactor(creds, submission, shared)),
      );
    },

    resendOtp(creds, language, ctx) {
      return gateway.resendOtp(creds, language, ctx);
    },
  };
}

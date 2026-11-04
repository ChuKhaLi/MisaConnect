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

  const isFresh = (t: AccessToken | undefined): t is AccessToken =>
    t !== undefined &&
    t !== null &&
    expiryTime(t.expiresAt) - REFRESH_SKEW_MS > clock.now().getTime();

  async function loginAndStore(
    key: string,
    creds: MisaCredentials,
    ctx: AuthCallContext,
  ): Promise<AccessToken> {
    const token = await gateway.login(creds, ctx);
    await tokenCache.set(key, token);
    return token;
  }

  /** Refreshes `previous` (merging omitted fields); on any failure evicts and logs in once. */
  async function renew(
    key: string,
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
      if (refreshed !== undefined) {
        await tokenCache.set(key, refreshed);
        return refreshed;
      }
    }
    if (previous !== undefined) await tokenCache.delete(key);
    return loginAndStore(key, creds, ctx);
  }

  async function getToken(creds: MisaCredentials, ctx: AuthCallContext): Promise<AccessToken> {
    const key = keyFor(creds, ctx.correlationId);
    const shared: AuthCallContext = { correlationId: ctx.correlationId };
    return singleFlight(`acquire:${key}`, ctx.signal, async () => {
      const cached = (await tokenCache.get(key)) ?? undefined;
      if (isFresh(cached)) return cached;
      return renew(key, creds, cached, shared);
    });
  }

  async function reauthenticate(
    creds: MisaCredentials,
    stale: AccessToken,
    ctx: AuthCallContext,
  ): Promise<AccessToken> {
    const key = keyFor(creds, ctx.correlationId);
    const shared: AuthCallContext = { correlationId: ctx.correlationId };
    return singleFlight(`reauth:${key}`, ctx.signal, async () => {
      const cached = (await tokenCache.get(key)) ?? undefined;
      // Another caller already replaced the rejected token: use theirs.
      if (isFresh(cached) && cached.value !== stale.value) return cached;
      return renew(key, creds, cached ?? stale, shared);
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
      const shared: AuthCallContext = { correlationId: ctx.correlationId };
      return singleFlight(`otp:${key}`, ctx.signal, async () => {
        const token = await gateway.twoFactor(creds, submission, shared);
        await tokenCache.set(key, token);
        return token;
      });
    },

    resendOtp(creds, language, ctx) {
      return gateway.resendOtp(creds, language, ctx);
    },
  };
}

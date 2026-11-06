import { describe, expect, it } from 'vitest';
import type { AccessToken } from '../../src/application/ports.js';
import type { MisaCredentials } from '../../src/domain/dtos.js';
import {
  AuthenticationFailedException,
  ESignGeneralException,
  ESignTransportException,
  InvalidOtpException,
} from '../../src/domain/errors.js';
import { mapESignError } from '../../src/application/errors/esign-error-mapper.js';
import {
  createCredentialFingerprinter,
  createInMemoryTokenCache,
  defaultTokenCacheKey,
  firstCertificateSelector,
} from '../../src/infrastructure/defaults.js';
import type { ResolvedESignOptions } from '../../src/infrastructure/options.js';
import { createTransport } from '../../src/infrastructure/transport.js';
import { createDefaultTokenManager } from '../../src/infrastructure/wire/auth.js';

const creds: MisaCredentials = {
  clientId: 'cid',
  clientKey: 'ck',
  userName: 'alice',
  password: 'pw',
};
const ctx = { correlationId: 'corr-1' };
const KEY = 'alice|cid|h.vn';
/** Deterministic test fingerprint; production uses a salted SHA-256 (see `defaults`). */
const fp = (c: MisaCredentials): string => `${c.password}#${c.clientKey}`;
const FP = 'pw#ck';
const T0 = Date.parse('2026-11-04T00:00:00Z');

type Handler = (body: Record<string, unknown>) => Response | Promise<Response>;
const json = (status: number, body: unknown): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
const loginOk = (token: string, extra: Record<string, unknown> = {}): Response =>
  json(200, {
    status: { error: false },
    data: {
      accessToken: 'raw-' + token,
      remoteSigningAccessToken: token,
      refreshToken: 'rt-' + token,
      expiresIn: 3600,
      user: { id: 'user-1', username: 'alice' },
      ...extra,
    },
  });

function setup(handlers: Partial<Record<string, Handler>>) {
  let now = T0;
  const calls: { path: string; body: Record<string, unknown> }[] = [];
  const fetchFn = (async (url: string, init: RequestInit) => {
    const path = new URL(url).pathname;
    const name = path.split('/').pop() ?? '';
    const body = (init.body ? JSON.parse(init.body as string) : {}) as Record<string, unknown>;
    calls.push({ path: name, body });
    await Promise.resolve();
    const h = handlers[name];
    if (!h) throw new Error('unexpected route ' + path);
    return h(body);
  }) as unknown as typeof fetch;
  const cache = createInMemoryTokenCache();
  const resolved = {
    origin: 'https://h.vn/',
    host: 'h.vn',
    authUnderWebdev: false,
    transportRetry: { maxAttempts: 1, baseDelayMs: 1, maxDelayMs: 1 },
    errors: { includeRawErrorMessage: false },
    otp: { defaultResendLanguage: 'en-US' },
    fetch: fetchFn,
    delay: () => Promise.resolve(),
    clock: { now: () => new Date(now) },
    tokenCache: cache,
  } as unknown as ResolvedESignOptions;
  const transport = createTransport(resolved);
  const tm = createDefaultTokenManager(resolved, transport, { fingerprint: fp });
  const count = (name: string) => calls.filter((c) => c.path === name).length;
  return {
    tm,
    cache,
    calls,
    count,
    advance: (ms: number) => {
      now += ms;
    },
  };
}

const token = (over: Partial<AccessToken> = {}): AccessToken => ({
  value: 'old',
  refreshToken: 'rt-old',
  expiresAt: new Date(T0 + 3_600_000),
  userId: 'user-1',
  username: 'alice',
  credentialFingerprint: FP,
  ...over,
});

/** Simulates an ESRM call: 401 when the bearer is not the expected one. */
const esrm401 = () =>
  mapESignError({
    route: 'certificates',
    status: 401,
    envelope: undefined,
    format: 'Unknown',
    correlationId: 'corr-1',
    includeRaw: false,
  });

describe('token manager', () => {
  it('single flight login', async () => {
    const { tm, count, calls } = setup({ 'login-api': () => loginOk('T1') });
    const results = await Promise.all(Array.from({ length: 10 }, () => tm.getToken(creds, ctx)));
    expect(count('login-api')).toBe(1);
    expect(calls[0]?.body).toEqual({ userName: 'alice', password: 'pw' });
    for (const r of results) {
      expect(r.value).toBe('T1');
      expect(r.userId).toBe('user-1');
      expect(r.refreshToken).toBe('rt-T1');
      expect(r.expiresAt.getTime()).toBe(T0 + 3_600_000);
    }
  });

  it('serves a fresh cached token without a call', async () => {
    const { tm, cache, calls } = setup({});
    await cache.set(KEY, token());
    expect((await tm.getToken(creds, ctx)).value).toBe('old');
    expect(calls).toHaveLength(0);
  });

  it('rejects empty user/client before composing the cache key', async () => {
    const { tm } = setup({});
    const err = await tm.getToken({ ...creds, userName: '' }, ctx).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ESignGeneralException);
    expect((err as ESignGeneralException).category).toBe('Validation');
    expect((err as ESignGeneralException).rawCode).toBe('InvalidCredentials');
  });

  it('refreshes a token expiring within the 30 s skew', async () => {
    const { tm, cache, count, calls } = setup({
      refreshtoken: () =>
        json(200, {
          status: { error: false },
          data: { remoteSigningAccessToken: 'T2', refreshToken: 'rt-T2', expiresIn: 3600 },
        }),
    });
    await cache.set(KEY, token({ expiresAt: new Date(T0 + 20_000) }));
    const t = await tm.getToken(creds, ctx);
    expect(count('refreshtoken')).toBe(1);
    expect(calls[0]?.body).toEqual({ refreshToken: 'rt-old' });
    expect(t.value).toBe('T2');
    expect(t.refreshToken).toBe('rt-T2');
    expect(t.userId).toBe('user-1');
    expect((await cache.get(KEY))?.value).toBe('T2');
  });

  it('keeps the previous refresh token when the refresh omits it (flat shape)', async () => {
    const { tm, cache } = setup({
      refreshtoken: () => json(200, { remoteSigningAccessToken: 'T2', expiresIn: 60 }),
    });
    await cache.set(KEY, token({ expiresAt: new Date(T0 - 1) }));
    const t = await tm.getToken(creds, ctx);
    expect(t).toEqual({
      value: 'T2',
      refreshToken: 'rt-old',
      expiresAt: new Date(T0 + 60_000),
      userId: 'user-1',
      username: 'alice',
      credentialFingerprint: FP,
    });
  });

  describe('expiresIn lifetime', () => {
    it.each([
      ['missing', undefined, 300_000],
      ['non-numeric', 'soon', 300_000],
      ['non-integer number', 10.5, 300_000],
      ['short (10 s) floored to 60 s', 10, 60_000],
      ['59 s floored to 60 s', 59, 60_000],
      ['60 s as-is', 60, 60_000],
      ['3600 s as-is', 3600, 3_600_000],
    ])('login with %s expiresIn', async (_label, expiresIn, lifetimeMs) => {
      const { tm } = setup({ 'login-api': () => loginOk('T1', { expiresIn }) });
      const t = await tm.getToken(creds, ctx);
      expect(t.expiresAt).toEqual(new Date(T0 + lifetimeMs));
    });

    it.each([
      ['0', 0, 0],
      ['negative', -5, -5_000],
    ])(
      'honours an explicit %s expiresIn (not defaulted, not floored)',
      async (_label, expiresIn, lifetimeMs) => {
        const { tm } = setup({ 'login-api': () => loginOk('T1', { expiresIn }) });
        const t = await tm.getToken(creds, ctx);
        expect(t.expiresAt).toEqual(new Date(T0 + lifetimeMs));
      },
    );
  });

  it('falls back to login when refresh returns an already-expired token (expiresIn 0)', async () => {
    const { tm, cache, count } = setup({
      refreshtoken: () => json(200, { remoteSigningAccessToken: 'T2', expiresIn: 0 }),
      'login-api': () => loginOk('T3'),
    });
    await cache.set(KEY, token({ expiresAt: new Date(T0 - 1) }));
    const t = await tm.getToken(creds, ctx);
    expect(count('refreshtoken')).toBe(1);
    expect(count('login-api')).toBe(1);
    expect(t.value).toBe('T3');
    expect((await cache.get(KEY))?.value).toBe('T3');
  });

  it('falls back to login when refresh returns 500', async () => {
    const { tm, cache, count } = setup({
      refreshtoken: () => json(500, {}),
      'login-api': () => loginOk('T3'),
    });
    await cache.set(KEY, token({ expiresAt: new Date(T0 - 1) }));
    const t = await tm.getToken(creds, ctx);
    expect(count('refreshtoken')).toBe(1);
    expect(count('login-api')).toBe(1);
    expect(t.value).toBe('T3');
    expect((await cache.get(KEY))?.value).toBe('T3');
  });

  it('withAuth: 401 -> refresh -> re-run with new bearer; later getToken reuses it', async () => {
    const { tm, cache, count } = setup({
      refreshtoken: () =>
        json(200, {
          data: { remoteSigningAccessToken: 'NEW', refreshToken: 'rt-new', expiresIn: 3600 },
        }),
    });
    await cache.set(KEY, token());
    const seen: string[] = [];
    const out = await tm.withAuth(creds, ctx, (bearer, userId) => {
      seen.push(`${bearer}/${userId}`);
      if (bearer !== 'NEW') return Promise.reject(esrm401());
      return Promise.resolve('done');
    });
    expect(out).toBe('done');
    expect(seen).toEqual(['old/user-1', 'NEW/user-1']);
    expect(count('refreshtoken')).toBe(1);
    expect((await tm.getToken(creds, ctx)).value).toBe('NEW');
    expect(count('refreshtoken')).toBe(1);
    expect((await cache.get(KEY))?.value).toBe('NEW');
  });

  it('withAuth: a 401 carrying a MISA errorCode still triggers re-auth (ruling R3)', async () => {
    const { tm, cache } = setup({
      refreshtoken: () => json(200, { remoteSigningAccessToken: 'NEW', expiresIn: 3600 }),
    });
    await cache.set(KEY, token());
    const out = await tm.withAuth(creds, ctx, (bearer) =>
      bearer === 'NEW'
        ? Promise.resolve(1)
        : Promise.reject(
            mapESignError({
              route: 'hash',
              status: 401,
              envelope: { errorCode: 'TokenExpired' },
              format: 'Pdf',
              correlationId: 'corr-1',
              includeRaw: false,
            }),
          ),
    );
    expect(out).toBe(1);
  });

  it('withAuth: refresh fails on 401 -> one login -> re-run', async () => {
    const { tm, cache, count } = setup({
      refreshtoken: () => json(400, { errorCode: 'InvalidRefreshToken' }),
      'login-api': () => loginOk('LOGGED'),
    });
    await cache.set(KEY, token());
    const seen: string[] = [];
    const out = await tm.withAuth(creds, ctx, (bearer) => {
      seen.push(bearer);
      return bearer === 'LOGGED' ? Promise.resolve('ok') : Promise.reject(esrm401());
    });
    expect(out).toBe('ok');
    expect(seen).toEqual(['old', 'LOGGED']);
    expect(count('refreshtoken')).toBe(1);
    expect(count('login-api')).toBe(1);
    expect((await cache.get(KEY))?.value).toBe('LOGGED');
  });

  it('withAuth: login failure during re-auth surfaces as is', async () => {
    const { tm, cache } = setup({
      refreshtoken: () => json(400, {}),
      'login-api': () => json(400, { errorCode: 'WrongPassword' }),
    });
    await cache.set(KEY, token());
    const err = await tm
      .withAuth(creds, ctx, () => Promise.reject(esrm401()))
      .catch((e: unknown) => e);
    expect(err).toBeInstanceOf(AuthenticationFailedException);
    expect((err as AuthenticationFailedException).rawCode).toBe('WrongPassword');
  });

  it('withAuth: second 401 -> AuthenticationFailedException rawCode "401"', async () => {
    const { tm, cache } = setup({
      refreshtoken: () => json(200, { remoteSigningAccessToken: 'NEW', expiresIn: 3600 }),
    });
    await cache.set(KEY, token());
    let runs = 0;
    const err = await tm
      .withAuth(creds, ctx, () => {
        runs++;
        return Promise.reject(
          mapESignError({
            route: 'certificates',
            status: 401,
            envelope: { errorCode: 'X' },
            format: 'Unknown',
            correlationId: 'corr-1',
            includeRaw: false,
          }),
        );
      })
      .catch((e: unknown) => e);
    expect(runs).toBe(2);
    expect(err).toBeInstanceOf(AuthenticationFailedException);
    expect((err as AuthenticationFailedException).rawCode).toBe('401');
    expect((err as AuthenticationFailedException).requires2FA).toBe(false);
  });

  it('withAuth: concurrent 401s share one refresh', async () => {
    const { tm, cache, count } = setup({
      refreshtoken: () => json(200, { remoteSigningAccessToken: 'NEW', expiresIn: 3600 }),
    });
    await cache.set(KEY, token());
    const call = (b: string) => (b === 'NEW' ? Promise.resolve(b) : Promise.reject(esrm401()));
    const out = await Promise.all(Array.from({ length: 5 }, () => tm.withAuth(creds, ctx, call)));
    expect(out).toEqual(Array(5).fill('NEW'));
    expect(count('refreshtoken')).toBe(1);
  });

  it('withAuth: non-auth errors pass through without re-auth', async () => {
    const { tm, cache, calls } = setup({});
    await cache.set(KEY, token());
    const boom = new Error('boom');
    await expect(tm.withAuth(creds, ctx, () => Promise.reject(boom))).rejects.toBe(boom);
    expect(calls).toHaveLength(0);
  });

  it('login 2FA in a nested 4xx body is detected', async () => {
    const { tm } = setup({
      'login-api': () => json(400, { status: { error: true, errorCode: 122 } }),
    });
    const err = await tm.getToken(creds, ctx).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(AuthenticationFailedException);
    expect((err as AuthenticationFailedException).requires2FA).toBe(true);
    expect((err as AuthenticationFailedException).username).toBe('alice');
  });

  it('login 122 inside a 5xx body surfaces as Transport (R4)', async () => {
    const { tm } = setup({
      'login-api': () => json(503, { errorCode: '122' }),
    });
    // The transport throws on 5xx after its retries; with one attempt it throws Transport.
    const err = await tm.getToken(creds, ctx).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ESignTransportException);
  });

  it('login 2FA on a 2xx error envelope is detected', async () => {
    const { tm } = setup({
      'login-api': () => json(200, { status: { error: true, errorCode: '122' } }),
    });
    const err = await tm.getToken(creds, ctx).catch((e: unknown) => e);
    expect((err as AuthenticationFailedException).requires2FA).toBe(true);
  });

  it('login 2xx without data -> MisaUnknown EmptyResponse', async () => {
    const { tm } = setup({ 'login-api': () => json(200, { status: { error: false } }) });
    const err = await tm.getToken(creds, ctx).catch((e: unknown) => e);
    expect((err as ESignGeneralException).category).toBe('MisaUnknown');
    expect((err as ESignGeneralException).rawCode).toBe('EmptyResponse');
  });

  it('exchangeOtp posts the two-factor body and caches the token', async () => {
    const { tm, cache, calls } = setup({ 'two-factor-auth': () => loginOk('OTP') });
    const [a, b] = await Promise.all([
      tm.exchangeOtp(creds, { code: '123456', otpType: 'Authenticator', remember: true }, ctx),
      tm.exchangeOtp(creds, { code: '123456', otpType: 'Authenticator', remember: true }, ctx),
    ]);
    expect(calls).toHaveLength(1);
    expect(calls[0]?.body).toEqual({
      userName: 'alice',
      code: '123456',
      otpType: 1,
      remember: true,
    });
    expect(a.value).toBe('OTP');
    expect(b.value).toBe('OTP');
    expect((await cache.get(KEY))?.value).toBe('OTP');
  });

  it('exchangeOtp 1001 -> InvalidOtpException', async () => {
    const { tm } = setup({
      'two-factor-auth': () => json(400, { status: { error: true, errorCode: '1001' } }),
    });
    const err = await tm
      .exchangeOtp(creds, { code: '1', otpType: 'SmsOrEmail', remember: false }, ctx)
      .catch((e: unknown) => e);
    expect(err).toBeInstanceOf(InvalidOtpException);
  });

  it('resend posts userName/language and succeeds on 2xx', async () => {
    const { tm, calls } = setup({
      'resend-otp-auth': () => json(200, { status: { error: false }, data: {} }),
    });
    const r = await tm.resendOtp(creds, 'vi-VN', ctx);
    expect(calls[0]?.body).toEqual({ userName: 'alice', language: 'vi-VN' });
    expect(r).toEqual({ success: true, correlationId: 'corr-1' });
  });

  it('resend 429 -> transport', async () => {
    const { tm } = setup({ 'resend-otp-auth': () => json(429, {}) });
    await expect(tm.resendOtp(creds, 'en-US', ctx)).rejects.toBeInstanceOf(ESignTransportException);
  });

  it('resend 400 without body -> EmptyErrorCode', async () => {
    const { tm } = setup({ 'resend-otp-auth': () => new Response('', { status: 400 }) });
    const r = await tm.resendOtp(creds, 'en-US', ctx);
    expect(r).toEqual({ success: false, rawCode: 'EmptyErrorCode', correlationId: 'corr-1' });
  });

  it('resend 400 with an envelope copies its fields', async () => {
    const { tm } = setup({
      'resend-otp-auth': () =>
        json(400, { status: { error: true, errorCode: 'E1', userMsg: 'u', devMsg: 'd' } }),
    });
    const r = await tm.resendOtp(creds, 'en-US', ctx);
    expect(r).toEqual({
      success: false,
      rawCode: 'E1',
      userMsg: 'u',
      devMsg: 'd',
      correlationId: 'corr-1',
    });
  });

  it('a failed shared login is not cached: the next getToken logs in again', async () => {
    let n = 0;
    const { tm, count } = setup({
      'login-api': () => (n++ === 0 ? json(400, { errorCode: 'Boom' }) : loginOk('T2')),
    });
    const r = await Promise.allSettled([tm.getToken(creds, ctx), tm.getToken(creds, ctx)]);
    expect(r.map((x) => x.status)).toEqual(['rejected', 'rejected']);
    expect(count('login-api')).toBe(1);
    expect((await tm.getToken(creds, ctx)).value).toBe('T2');
    expect(count('login-api')).toBe(2);
  });

  it("one caller's abort leaves other waiters on the same flight unaffected", async () => {
    let release!: () => void;
    const gate = new Promise<void>((r) => (release = r));
    const { tm, count } = setup({ 'login-api': () => gate.then(() => loginOk('T1')) });
    const ac = new AbortController();
    const aborted = tm.getToken(creds, { ...ctx, signal: ac.signal });
    const other = tm.getToken(creds, ctx);
    const reason = new Error('caller gave up');
    ac.abort(reason);
    await expect(aborted).rejects.toBe(reason);
    release();
    expect((await other).value).toBe('T1');
    expect(count('login-api')).toBe(1);
  });

  describe('credential binding (R5)', () => {
    const wrong: MisaCredentials = { ...creds, password: 'WRONG' };

    it('a wrong password never gets the cached token; the login failure surfaces', async () => {
      const { tm, count, calls } = setup({
        'login-api': (b) =>
          b.password === 'pw' ? loginOk('A-TOKEN') : json(400, { errorCode: 'WrongPassword' }),
      });
      expect((await tm.getToken(creds, ctx)).value).toBe('A-TOKEN');
      const err = await tm.getToken(wrong, ctx).catch((e: unknown) => e);
      expect(err).toBeInstanceOf(AuthenticationFailedException);
      expect((err as AuthenticationFailedException).rawCode).toBe('WrongPassword');
      expect(count('login-api')).toBe(2);
      expect(calls[1]?.body).toEqual({ userName: 'alice', password: 'WRONG' });
      // A's entry survives and still serves A without a new login.
      expect((await tm.getToken(creds, ctx)).value).toBe('A-TOKEN');
      expect(count('login-api')).toBe(2);
    });

    it('withAuth with a wrong password does not run the call with the cached token', async () => {
      const { tm, cache } = setup({ 'login-api': () => json(400, { errorCode: 'WrongPassword' }) });
      await cache.set(KEY, token());
      let ran = false;
      await expect(
        tm.withAuth(wrong, ctx, () => {
          ran = true;
          return Promise.resolve(1);
        }),
      ).rejects.toBeInstanceOf(AuthenticationFailedException);
      expect(ran).toBe(false);
      expect((await cache.get(KEY))?.value).toBe('old');
    });

    it('a changed clientKey forces a fresh login', async () => {
      const { tm, cache, count } = setup({ 'login-api': () => loginOk('K2') });
      await cache.set(KEY, token());
      const t = await tm.getToken({ ...creds, clientKey: 'ck2' }, ctx);
      expect(t.value).toBe('K2');
      expect(count('login-api')).toBe(1);
    });

    it('a cached token without a fingerprint is a miss', async () => {
      const { tm, cache, count } = setup({ 'login-api': () => loginOk('NEW') });
      const legacy = token();
      delete legacy.credentialFingerprint;
      await cache.set(KEY, legacy);
      expect((await tm.getToken(creds, ctx)).value).toBe('NEW');
      expect(count('login-api')).toBe(1);
    });

    it('an expired entry of another credential is not refreshed with its refresh token', async () => {
      const { tm, cache, count } = setup({
        refreshtoken: () => json(200, { remoteSigningAccessToken: 'STOLEN', expiresIn: 3600 }),
        'login-api': () => json(400, { errorCode: 'WrongPassword' }),
      });
      await cache.set(KEY, token({ expiresAt: new Date(T0 - 1) }));
      await expect(tm.getToken(wrong, ctx)).rejects.toBeInstanceOf(AuthenticationFailedException);
      expect(count('refreshtoken')).toBe(0);
      expect((await cache.get(KEY))?.value).toBe('old');
    });

    it('concurrent right/wrong-password callers do not share a flight', async () => {
      let release!: () => void;
      const gate = new Promise<void>((r) => (release = r));
      const { tm, count } = setup({
        'login-api': (b) =>
          gate.then(() =>
            b.password === 'pw' ? loginOk('A-TOKEN') : json(400, { errorCode: 'WrongPassword' }),
          ),
      });
      const a = tm.getToken(creds, ctx);
      const w = tm.getToken(wrong, ctx).catch((e: unknown) => e);
      await Promise.resolve();
      release();
      expect((await a).value).toBe('A-TOKEN');
      expect(await w).toBeInstanceOf(AuthenticationFailedException);
      expect(count('login-api')).toBe(2);
    });

    it('a wrong-password OTP exchange does not join the right one', async () => {
      const { tm, count } = setup({ 'two-factor-auth': () => loginOk('OTP') });
      const s = { code: '1', otpType: 'SmsOrEmail', remember: false } as const;
      await Promise.all([tm.exchangeOtp(creds, s, ctx), tm.exchangeOtp(wrong, s, ctx)]);
      expect(count('two-factor-auth')).toBe(2);
    });
  });

  describe('empty remoteSigningAccessToken is never cached', () => {
    const isEmptyResponse = (e: unknown) =>
      e instanceof ESignGeneralException &&
      e.category === 'MisaUnknown' &&
      e.rawCode === 'EmptyResponse';

    it('login', async () => {
      const { tm, cache } = setup({ 'login-api': () => loginOk('') });
      const err = await tm.getToken(creds, ctx).catch((e: unknown) => e);
      expect(isEmptyResponse(err)).toBe(true);
      expect(await cache.get(KEY)).toBeUndefined();
    });

    it('two-factor', async () => {
      const { tm, cache } = setup({ 'two-factor-auth': () => loginOk('') });
      const err = await tm
        .exchangeOtp(creds, { code: '1', otpType: 'SmsOrEmail', remember: false }, ctx)
        .catch((e: unknown) => e);
      expect(isEmptyResponse(err)).toBe(true);
      expect(await cache.get(KEY)).toBeUndefined();
    });

    it.each([
      ['envelope', { data: { remoteSigningAccessToken: '', expiresIn: 3600 } }],
      ['flat', { accessToken: 'raw', remoteSigningAccessToken: '', expiresIn: 3600 }],
      ['flat without remote token', { accessToken: 'raw', expiresIn: 3600 }],
    ])('refresh (%s) -> falls back to login', async (_name, body) => {
      const { tm, cache, count } = setup({
        refreshtoken: () => json(200, body),
        'login-api': () => loginOk('L'),
      });
      await cache.set(KEY, token({ expiresAt: new Date(T0 - 1) }));
      expect((await tm.getToken(creds, ctx)).value).toBe('L');
      expect(count('refreshtoken')).toBe(1);
      expect(count('login-api')).toBe(1);
    });
  });
});

describe('defaults', () => {
  it('credential fingerprint: salted SHA-256 hex over password and clientKey', () => {
    const f = createCredentialFingerprinter();
    const a = f(creds);
    expect(a).toMatch(/^[0-9a-f]{64}$/);
    expect(f({ ...creds })).toBe(a);
    expect(f({ ...creds, password: 'x' })).not.toBe(a);
    expect(f({ ...creds, clientKey: 'x' })).not.toBe(a);
    // The '\0' separator keeps the password/clientKey boundary unambiguous.
    expect(f({ ...creds, password: 'ab', clientKey: 'c' })).not.toBe(
      f({ ...creds, password: 'a', clientKey: 'bc' }),
    );
    // Per-instance salt.
    expect(createCredentialFingerprinter()(creds)).not.toBe(a);
  });

  it('default token cache key', () => {
    expect(defaultTokenCacheKey({ userName: 'u', clientId: 'c', host: 'h' })).toBe('u|c|h');
  });

  it('first certificate selector returns the first', () => {
    const certs = [{ keyAlias: 'a' }, { keyAlias: 'b' }] as unknown as Parameters<
      typeof firstCertificateSelector
    >[0];
    expect(firstCertificateSelector(certs)).toBe(certs[0]);
  });

  it('in-memory token cache get/set/delete', () => {
    const c = createInMemoryTokenCache();
    const t = token();
    void c.set('k', t);
    expect(c.get('k')).toBe(t);
    void c.delete('k');
    expect(c.get('k')).toBeUndefined();
  });
});

import { describe, expect, it, vi } from 'vitest';
import { ESignGeneralException } from '../../src/domain/errors.js';
import { resolveCredentials, validateOptions } from '../../src/infrastructure/options.js';
import type { MisaESignOptions } from '../../src/infrastructure/options.js';

const creds = { clientId: 'c', clientKey: 'k', userName: 'u', password: 'p' };
const base = (o: Partial<MisaESignOptions> = {}): MisaESignOptions => ({
  baseUrl: 'https://sandbox.example.vn/webdev/x',
  credentials: creds,
  ...o,
});
const failure = (o: MisaESignOptions): ESignGeneralException => {
  try {
    validateOptions(o);
  } catch (e) {
    return e as ESignGeneralException;
  }
  throw new Error('expected failure');
};

describe('validateOptions', () => {
  it('fills defaults', () => {
    const r = validateOptions(base());
    expect(r.environment).toBe('sandbox');
    expect(r.polling).toEqual({ intervalMs: 2000, totalTimeoutMs: 60000 });
    expect(r.transportRetry).toEqual({ maxAttempts: 3, baseDelayMs: 200, maxDelayMs: 2000 });
    expect(r.otp.defaultResendLanguage).toBe('en-US');
    expect(r.webhook).toEqual({ mode: 'both', sessionTtlMs: 86_400_000 });
    expect(r.errors.includeRawErrorMessage).toBe(false);
    expect(r.authUnderWebdev).toBe(true);
    expect(r.tokenCache).toBeUndefined();
  });

  it('drops the path from the origin', () => {
    const r = validateOptions(base());
    expect(r.origin).toBe('https://sandbox.example.vn/');
    expect(r.host).toBe('sandbox.example.vn');
  });

  it('authUnderWebdev is false for production unless set', () => {
    const p = { environment: 'production' as const, baseUrl: 'https://esignapp.misa.vn/' };
    expect(validateOptions(base(p)).authUnderWebdev).toBe(false);
    expect(validateOptions(base({ ...p, authUnderWebdev: true })).authUnderWebdev).toBe(true);
  });

  it('reports failures as one Validation exception', () => {
    const e = failure(base({ baseUrl: '' }));
    expect(e).toBeInstanceOf(ESignGeneralException);
    expect(e.category).toBe('Validation');
    expect(e.rawCode).toBe('InvalidOptions');
    expect(e.correlationId).toBe('options');
  });

  it.each([
    ['relative baseUrl', base({ baseUrl: '/x' }), 'absolute'],
    ['http on non-loopback', base({ baseUrl: 'http://example.vn/' }), 'https'],
    ['production wrong host', base({ environment: 'production' }), 'esignapp.misa.vn'],
    ['sandbox prod host', base({ baseUrl: 'https://esignapp.misa.vn/' }), 'Sandbox'],
    ['interval', base({ polling: { intervalMs: 0 } }), 'intervalMs'],
    ['timeout', base({ polling: { intervalMs: 5000, totalTimeoutMs: 5000 } }), 'totalTimeoutMs'],
    ['attempts', base({ transportRetry: { maxAttempts: 0 } }), 'maxAttempts'],
    ['delays', base({ transportRetry: { baseDelayMs: 500, maxDelayMs: 100 } }), 'maxDelayMs'],
    ['language', base({ otp: { defaultResendLanguage: ' ' } }), 'defaultResendLanguage'],
    ['ttl', base({ webhook: { sessionTtlMs: 0 } }), 'sessionTtlMs'],
    ['blank static cred', base({ credentials: { ...creds, clientKey: ' ' } }), 'clientKey'],
  ])('rejects %s', (_n, o, text) => {
    expect(failure(o).detail).toContain(text);
  });

  it.each(['http://localhost:5000/', 'http://127.0.0.1/', 'http://[::1]:8080/'])(
    'allows http on loopback %s',
    (baseUrl) => {
      expect(validateOptions(base({ baseUrl })).origin).toBe(new URL(baseUrl).origin + '/');
    },
  );

  it('reports two failures together', () => {
    const e = failure(base({ polling: { intervalMs: 0 }, webhook: { sessionTtlMs: -1 } }));
    expect(e.detail).toContain('intervalMs');
    expect(e.detail).toContain('sessionTtlMs');
    expect(e.detail).toContain('; ');
  });

  it('accepts a provider function as is', () => {
    const provider = vi.fn();
    expect(validateOptions(base({ credentials: provider })).credentials).toBe(provider);
  });
});

describe('resolveCredentials', () => {
  it('prefers override, then provider with ctx, then static', async () => {
    const other = { ...creds, userName: 'o' };
    const provider = vi.fn().mockResolvedValue({ ...creds, userName: 'prov' });
    const withProvider = validateOptions(base({ credentials: provider }));
    expect(await resolveCredentials(withProvider, {}, other)).toBe(other);
    expect(provider).not.toHaveBeenCalled();
    const ctx = { clientId: 'c', userName: 'u' };
    expect((await resolveCredentials(withProvider, ctx)).userName).toBe('prov');
    expect(provider).toHaveBeenCalledWith(ctx);
    expect(await resolveCredentials(validateOptions(base()), {})).toBe(creds);
  });

  it('rejects provider output with blank fields', async () => {
    const r = validateOptions(base({ credentials: () => ({ ...creds, password: '' }) }));
    await expect(resolveCredentials(r, {})).rejects.toMatchObject({
      category: 'Validation',
      rawCode: 'InvalidCredentials',
    });
  });
});

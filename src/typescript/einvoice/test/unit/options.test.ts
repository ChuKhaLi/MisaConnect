import { describe, expect, it } from 'vitest';
import { MeInvoiceException } from '../../src/domain/errors.js';
import { defaultInvoiceValidator } from '../../src/application/invoice-validator.js';
import { resolveTemplate } from '../../src/application/template-resolver.js';
import {
  createInMemoryTokenCache,
  systemClock,
  uuidRefIdGenerator,
} from '../../src/infrastructure/defaults.js';
import { noopLogger } from '../../src/infrastructure/logging.js';
import { validateOptions, type MisaEInvoiceOptions } from '../../src/infrastructure/options.js';

const valid: MisaEInvoiceOptions = {
  baseUrl: 'https://testapi.meinvoice.vn/api/integration',
  taxCode: '0100000000',
  userName: 'u',
  password: 'p',
  appId: 'a',
};

function catchErr(fn: () => unknown): MeInvoiceException {
  try {
    fn();
  } catch (e) {
    return e as MeInvoiceException;
  }
  throw new Error('did not throw');
}

describe('validateOptions', () => {
  it('lists every problem in one exception', () => {
    const e = catchErr(() =>
      validateOptions({ ...valid, password: '', baseUrl: 'http://testapi.meinvoice.vn/x' }),
    );
    expect(e).toBeInstanceOf(MeInvoiceException);
    expect(e.category).toBe('Configuration');
    expect(e.rawErrorCode).toBe('InvalidOptions');
    const paths = (e.failures ?? []).map((f) => f.fieldPath);
    expect(paths).toContain('password');
    expect(paths).toContain('baseUrl');
  });

  it('reports every missing required field', () => {
    const e = catchErr(() => validateOptions({} as MisaEInvoiceOptions));
    expect((e.failures ?? []).map((f) => f.fieldPath).sort()).toEqual(
      ['appId', 'baseUrl', 'password', 'taxCode', 'userName'].sort(),
    );
  });

  it('rejects production env with sandbox host', () => {
    const e = catchErr(() => validateOptions({ ...valid, environment: 'production' }));
    expect(e.failures?.[0]?.fieldPath).toBe('baseUrl');
    expect(e.failures?.[0]?.message).toContain('api.meinvoice.vn');
  });

  it('rejects unknown environment', () => {
    const e = catchErr(() => validateOptions({ ...valid, environment: 'x' as 'sandbox' }));
    expect((e.failures ?? []).some((f) => f.fieldPath === 'environment')).toBe(true);
  });

  it('resolves production with trailing slash preserved', () => {
    const r = validateOptions({
      ...valid,
      environment: 'production',
      baseUrl: 'https://api.meinvoice.vn/api/integration/',
    });
    expect(r.environment).toBe('production');
    expect(r.baseUrl).toBe('https://api.meinvoice.vn/api/integration/');
  });

  it('defaults to sandbox and adds exactly one trailing slash', () => {
    const r = validateOptions({
      ...valid,
      baseUrl: 'https://testapi.meinvoice.vn/api/integration//',
    });
    expect(r.environment).toBe('sandbox');
    expect(r.baseUrl).toBe('https://testapi.meinvoice.vn/api/integration/');
    expect(r.delete.includeRawErrorMessage).toBe(false);
  });

  it('resolves default collaborators', () => {
    const r = validateOptions(valid);
    expect(r.validator).toBe(defaultInvoiceValidator);
    expect(r.templateResolver).toBe(resolveTemplate);
    expect(r.logger).toBe(noopLogger);
    expect(typeof r.fetch).toBe('function');
    expect(r.clock).toBe(systemClock);
    expect(r.refIdGenerator).toBe(uuidRefIdGenerator);
    expect(r.tokenCache).toBeDefined();
    expect(r.correlationId()).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('keeps supplied collaborators', () => {
    const correlationId = () => 'c';
    const r = validateOptions({
      ...valid,
      correlationId,
      delete: { includeRawErrorMessage: true },
    });
    expect(r.correlationId).toBe(correlationId);
    expect(r.delete.includeRawErrorMessage).toBe(true);
  });

  it('allowAnyBaseUrl skips https and host checks only', () => {
    const r = validateOptions(
      { ...valid, baseUrl: 'http://127.0.0.1:1234/' },
      { allowAnyBaseUrl: true },
    );
    expect(r.baseUrl).toBe('http://127.0.0.1:1234/');
    expect(() =>
      validateOptions(
        { ...valid, taxCode: '', baseUrl: 'http://127.0.0.1:1/' },
        { allowAnyBaseUrl: true },
      ),
    ).toThrow(MeInvoiceException);
  });
});

describe('defaults', () => {
  it('in-memory token cache round-trips', async () => {
    const c = createInMemoryTokenCache();
    const t = { value: 'v', expiresAt: new Date(1) };
    expect(await c.get('k')).toBeUndefined();
    await c.set('k', t);
    expect(await c.get('k')).toBe(t);
    await c.delete('k');
    expect(await c.get('k')).toBeUndefined();
  });

  it('systemClock returns a Date', () => {
    expect(systemClock.now()).toBeInstanceOf(Date);
  });
});

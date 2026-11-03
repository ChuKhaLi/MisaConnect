import { createRequire } from 'node:module';
import { describe, it, expect } from 'vitest';

describe('package exports', () => {
  it('loads via ESM import', async () => {
    const mod = await import('@misaconnect/einvoice');
    expect(mod.MeInvoiceErrorCategory.Validation).toBe('Validation');
    expect(new mod.MeInvoiceException('Validation')).toBeInstanceOf(Error);
    expect(typeof mod.createMisaEInvoiceClient).toBe('function');
  });

  it('loads via CommonJS require', () => {
    const mod = createRequire(import.meta.url)('@misaconnect/einvoice') as {
      MeInvoiceErrorCategory: { Validation: string };
    };
    expect(mod.MeInvoiceErrorCategory.Validation).toBe('Validation');
  });
});

describe('public collaborator exports', () => {
  it('exposes the default validator and template resolver for wrapping', async () => {
    const mod = await import('@misaconnect/einvoice');
    expect(typeof mod.defaultInvoiceValidator.validate).toBe('function');
    expect(typeof mod.defaultInvoiceValidator.validateBatch).toBe('function');
    expect(mod.resolveTemplate([], undefined)).toEqual({ kind: 'NoActiveTemplate' });
  });
});

describe('isMeInvoiceException (dual-package hazard)', () => {
  it('recognises an exception thrown by the other module format', async () => {
    const esm = await import('@misaconnect/einvoice');
    const cjs = createRequire(import.meta.url)('@misaconnect/einvoice') as typeof esm;
    const fromCjs = new cjs.MeInvoiceException('Validation', { rawErrorCode: 'X' });
    expect(fromCjs instanceof esm.MeInvoiceException).toBe(false);
    expect(esm.isMeInvoiceException(fromCjs)).toBe(true);
    expect(esm.isMeInvoiceException(new esm.MeInvoiceException('MisaUnknown'))).toBe(true);
    expect(esm.isMeInvoiceException(new Error('x'))).toBe(false);
    expect(esm.isMeInvoiceException({ name: 'MeInvoiceException' })).toBe(false);
    expect(esm.isMeInvoiceException(undefined)).toBe(false);
  });
});

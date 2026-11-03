import { describe, it, expect } from 'vitest';
import { MeInvoiceErrorCategory, MeInvoiceException } from '../../src/domain/errors.js';
import { parseVatRate } from '../../src/domain/vat-rate.js';
import {
  DeleteDraftStatus,
  InvoiceStatus,
  LookupBatchStatus,
  LookupStatus,
  SaveOutcomeDto,
} from '../../src/domain/statuses.js';

describe('MeInvoiceErrorCategory', () => {
  it('has 16 frozen keys equal to their values', () => {
    const keys = Object.keys(MeInvoiceErrorCategory);
    expect(keys).toHaveLength(16);
    for (const k of keys) {
      expect(MeInvoiceErrorCategory[k as keyof typeof MeInvoiceErrorCategory]).toBe(k);
    }
    expect(Object.isFrozen(MeInvoiceErrorCategory)).toBe(true);
    expect(keys[0]).toBe('Configuration');
    expect(keys[15]).toBe('TransportFailed');
  });
});

describe('statuses', () => {
  it('have the expected sizes and are frozen', () => {
    expect(Object.keys(DeleteDraftStatus)).toHaveLength(9);
    expect(Object.keys(LookupStatus)).toHaveLength(9);
    expect(Object.keys(LookupBatchStatus)).toEqual(['Completed', 'Validation']);
    expect(Object.keys(SaveOutcomeDto)).toEqual(['Success', 'Error']);
    expect(Object.keys(InvoiceStatus)).toHaveLength(6);
    for (const e of [
      DeleteDraftStatus,
      LookupStatus,
      LookupBatchStatus,
      SaveOutcomeDto,
      InvoiceStatus,
    ]) {
      expect(Object.isFrozen(e)).toBe(true);
    }
  });
});

describe('MeInvoiceException', () => {
  it('defaults message to rawErrorCode, then category', () => {
    const e = new MeInvoiceException('Validation', { rawErrorCode: 'X' });
    expect(e.message).toBe('X');
    expect(e).toBeInstanceOf(Error);
    expect(e.name).toBe('MeInvoiceException');
    expect(new MeInvoiceException('Signing').message).toBe('Signing');
    expect(new MeInvoiceException('Signing', { rawErrorCode: 'X', message: 'm' }).message).toBe(
      'm',
    );
  });

  it('carries optional details and cause', () => {
    const cause = new Error('inner');
    const e = new MeInvoiceException('Validation', {
      field: 'f',
      component: 'c',
      refId: 'r',
      failures: [{ fieldPath: 'a', message: 'b' }],
      cause,
    });
    expect(e.category).toBe('Validation');
    expect(e.field).toBe('f');
    expect(e.component).toBe('c');
    expect(e.refId).toBe('r');
    expect(e.failures).toEqual([{ fieldPath: 'a', message: 'b' }]);
    expect(e.cause).toBe(cause);
  });
});

describe('parseVatRate', () => {
  it('parses named and percent spellings', () => {
    expect(parseVatRate('KCT')).toEqual({ kind: 'KCT' });
    expect(parseVatRate(' kkknt ')).toEqual({ kind: 'KKKNT' });
    expect(parseVatRate('0%')).toEqual({ kind: '0' });
    expect(parseVatRate('5%')).toEqual({ kind: '5' });
    expect(parseVatRate('8%')).toEqual({ kind: '8' });
    expect(parseVatRate('10%')).toEqual({ kind: '10' });
  });

  it('parses KHAC with a rate', () => {
    expect(parseVatRate('KHAC:5.5%')).toEqual({ kind: 'KHAC', rate: 5.5 });
    expect(parseVatRate('khac:7')).toEqual({ kind: 'KHAC', rate: 7 });
  });

  it('returns undefined for unknown, bare numbers, empty and non-positive KHAC', () => {
    expect(parseVatRate('abc')).toBeUndefined();
    expect(parseVatRate('')).toBeUndefined();
    expect(parseVatRate('10')).toBeUndefined();
    expect(parseVatRate('KHAC:0%')).toBeUndefined();
    expect(parseVatRate('KHAC:x%')).toBeUndefined();
    expect(parseVatRate('constructor')).toBeUndefined();
  });
});

import { describe, it, expect } from 'vitest';
import { formatDateOnly, money, serializeRequest } from '../../src/infrastructure/json.js';
import { MeInvoiceException } from '../../src/domain/errors.js';

describe('serializeRequest', () => {
  it('writes money strings byte-for-byte as number literals', () => {
    expect(serializeRequest({ a: money('12345678901234567890.123456') })).toBe(
      '{"a":12345678901234567890.123456}',
    );
  });

  it('writes numeric money', () => {
    expect(serializeRequest({ a: money(1.5) })).toBe('{"a":1.5}');
  });

  it('drops null and undefined object members', () => {
    expect(serializeRequest({ a: null, b: undefined, c: 1 })).toBe('{"c":1}');
  });

  it('keeps array order and drops nothing', () => {
    expect(serializeRequest([3, 'x', { a: 1 }, null])).toBe('[3,"x",{"a":1},null]');
  });

  it('serializes nested structures and escapes strings', () => {
    expect(
      serializeRequest({ s: 'a"b\n☕', n: { m: money('-0.50') }, l: [money('2')], t: true }),
    ).toBe('{"s":"a\\"b\\n☕","n":{"m":-0.50},"l":[2],"t":true}');
  });

  it('rejects malformed money strings with InvalidMoney', () => {
    for (const bad of ['1e5', '', '1.', '.5', ' 1', '1,5', '+1']) {
      try {
        money(bad);
        expect.unreachable(`should have thrown for "${bad}"`);
      } catch (e) {
        expect(e).toBeInstanceOf(MeInvoiceException);
        expect((e as MeInvoiceException).category).toBe('Validation');
        expect((e as MeInvoiceException).rawErrorCode).toBe('InvalidMoney');
      }
    }
  });

  it('rejects non-finite money numbers', () => {
    expect(() => money(NaN)).toThrow(MeInvoiceException);
    expect(() => money(Infinity)).toThrow(MeInvoiceException);
  });

  it('rejects non-finite plain numbers', () => {
    expect(() => serializeRequest({ a: NaN })).toThrow(MeInvoiceException);
  });
});

describe('formatDateOnly', () => {
  it('formats a Date from local calendar fields', () => {
    expect(formatDateOnly(new Date(2026, 10, 3))).toBe('2026-11-03');
  });

  it('passes yyyy-MM-dd strings through', () => {
    expect(formatDateOnly('2026-11-03')).toBe('2026-11-03');
  });

  it('rejects other string shapes', () => {
    expect(() => formatDateOnly('03/11/2026')).toThrow(MeInvoiceException);
  });

  it.each(['2026-13-45', '2026-00-10', '2026-02-29', '2026-04-31', '2026-01-00'])(
    'rejects the impossible date %s with Validation InvalidDate and the field',
    (text) => {
      let err: unknown;
      try {
        formatDateOnly(text, 'invDate');
      } catch (e) {
        err = e;
      }
      expect(err).toBeInstanceOf(MeInvoiceException);
      expect(err).toMatchObject({
        category: 'Validation',
        rawErrorCode: 'InvalidDate',
        field: 'invDate',
      });
    },
  );

  it('accepts a leap day in a leap year', () => {
    expect(formatDateOnly('2028-02-29')).toBe('2028-02-29');
  });
});

describe('fix round 1', () => {
  it('carries the field name into InvalidMoney errors', () => {
    try {
      money('1e5', 'totals.amountOC');
      expect.unreachable();
    } catch (e) {
      expect((e as MeInvoiceException).rawErrorCode).toBe('InvalidMoney');
      expect((e as MeInvoiceException).field).toBe('totals.amountOC');
    }
    try {
      money(NaN, 'x');
      expect.unreachable();
    } catch (e) {
      expect((e as MeInvoiceException).field).toBe('x');
    }
  });

  it('reports the property path for non-finite plain numbers', () => {
    try {
      serializeRequest({ a: [{ b: NaN }] });
      expect.unreachable();
    } catch (e) {
      expect((e as MeInvoiceException).field).toBe('a[0].b');
    }
  });

  it('rejects invalid Date values with InvalidDate', () => {
    for (const fn of [
      () => formatDateOnly(new Date('x')),
      () => serializeRequest({ d: new Date('x') }),
    ]) {
      try {
        fn();
        expect.unreachable();
      } catch (e) {
        expect(e).toBeInstanceOf(MeInvoiceException);
        expect((e as MeInvoiceException).category).toBe('Validation');
        expect((e as MeInvoiceException).rawErrorCode).toBe('InvalidDate');
      }
    }
  });

  it('does not trust forged money-shaped objects (JSON injection)', () => {
    const out = serializeRequest({ a: { __money: '1,"X":2' } });
    expect(out).toBe('{"a":{"__money":"1,\\"X\\":2"}}');
    expect(serializeRequest({ a: money('1.5') })).toBe('{"a":1.5}');
  });
});

describe('money() numbers are emitted as plain decimal text', () => {
  const emit = (n: number): string => serializeRequest({ a: money(n) });
  it('expands large exponent form exactly', () => {
    expect(emit(1e21)).toBe('{"a":1000000000000000000000}');
    expect(emit(1.5e21)).toBe('{"a":1500000000000000000000}');
    expect(emit(-1.2345e25)).toBe('{"a":-12345000000000000000000000}');
  });
  it('expands small exponent form exactly', () => {
    expect(emit(1e-7)).toBe('{"a":0.0000001}');
    expect(emit(-1.25e-8)).toBe('{"a":-0.0000000125}');
  });
  it('keeps ordinary numbers unchanged', () => {
    expect(emit(0.1)).toBe('{"a":0.1}');
    expect(emit(123456.789)).toBe('{"a":123456.789}');
    expect(emit(-0)).toBe('{"a":0}');
  });
});

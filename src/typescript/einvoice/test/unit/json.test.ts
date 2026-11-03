import { describe, it, expect } from 'vitest';
import { HUGE_INPUT_BUDGET_MS, largestBigIntInput } from './bigint-guard.js';
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

  it('writes money inside an array', () => {
    expect(serializeRequest({ l: [money('1.50'), money(2)] })).toBe('{"l":[1.50,2]}');
  });

  it('rejects bigint money with InvalidMoney', () => {
    try {
      money(10n as unknown as number);
      expect.unreachable();
    } catch (e) {
      expect(e).toBeInstanceOf(MeInvoiceException);
      expect((e as MeInvoiceException).rawErrorCode).toBe('InvalidMoney');
    }
  });

  it('serialises a Date member as its ISO string', () => {
    expect(serializeRequest({ d: new Date(Date.UTC(2026, 0, 5, 1, 2, 3)) })).toBe(
      '{"d":"2026-01-05T01:02:03.000Z"}',
    );
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

  it('zero-pads month and day of a Date', () => {
    expect(formatDateOnly(new Date(2026, 0, 5))).toBe('2026-01-05');
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

describe('money range (.NET decimal: |x| <= decimal.MaxValue, <= 28 fractional digits)', () => {
  const code = (fn: () => unknown): { code?: string; field?: string } => {
    try {
      fn();
    } catch (e) {
      return {
        ...((e as MeInvoiceException).rawErrorCode !== undefined
          ? { code: (e as MeInvoiceException).rawErrorCode }
          : {}),
        ...((e as MeInvoiceException).field !== undefined
          ? { field: (e as MeInvoiceException).field }
          : {}),
      };
    }
    return {};
  };
  const emitMoney = (v: number | string): string => serializeRequest({ a: money(v) });

  it('rejects Number.MAX_VALUE and numbers above decimal.MaxValue with InvalidMoney and the field', () => {
    expect(code(() => money(Number.MAX_VALUE, 'f'))).toEqual({ code: 'InvalidMoney', field: 'f' });
    // 7.922816251426434e28 is exactly 79228162514264340000000000000 > decimal.MaxValue.
    expect(code(() => money(7.922816251426434e28, 'f'))).toEqual({
      code: 'InvalidMoney',
      field: 'f',
    });
    expect(code(() => money(-7.922816251426434e28, 'f')).code).toBe('InvalidMoney');
    expect(emitMoney(7.922816251426433e28)).toBe('{"a":79228162514264330000000000000}');
  });

  it('rounds number residues to 28 fractional digits, as .NET decimal parsing of the literal does', () => {
    // .NET 10: JsonSerializer.Deserialize<decimal>("5.551115123125783e-17") == 0.0000000000000000555111512313
    expect(emitMoney(0.1 + 0.2 - 0.3)).toBe('{"a":0.0000000000000000555111512313}');
    expect(emitMoney(-(0.1 + 0.2 - 0.3))).toBe('{"a":-0.0000000000000000555111512313}');
    expect(emitMoney(5e-324)).toBe('{"a":0}');
    expect(emitMoney(-5e-324)).toBe('{"a":0}');
    expect(emitMoney(1e-29)).toBe('{"a":0}');
    expect(emitMoney(1e-28)).toBe('{"a":0.0000000000000000000000000001}');
  });

  it('breaks exact ties to even (bankers rounding), like .NET decimal parsing', () => {
    // .NET 10: 2.5e-28 -> 2e-28, 1.5e-28 -> 2e-28, 3.5e-28 -> 4e-28, 5e-29 -> 0.
    expect(emitMoney(2.5e-28)).toBe('{"a":0.0000000000000000000000000002}');
    expect(emitMoney(1.5e-28)).toBe('{"a":0.0000000000000000000000000002}');
    expect(emitMoney(3.5e-28)).toBe('{"a":0.0000000000000000000000000004}');
    expect(emitMoney(-3.5e-28)).toBe('{"a":-0.0000000000000000000000000004}');
    expect(emitMoney(5e-29)).toBe('{"a":0}');
    expect(emitMoney(5.000001e-29)).toBe('{"a":0.0000000000000000000000000001}');
  });

  it('keeps every digit of the shortest round-trip text (no 15-significant-digit rounding)', () => {
    expect(emitMoney(0.30000000000000004)).toBe('{"a":0.30000000000000004}');
    expect(emitMoney(123456789.12345679)).toBe('{"a":123456789.12345679}');
  });

  it('strings: rejects > 28 fractional digits and |x| > decimal.MaxValue; accepts the limits', () => {
    expect(code(() => money('0.' + '1'.repeat(29), 'f'))).toEqual({
      code: 'InvalidMoney',
      field: 'f',
    });
    expect(code(() => money('1' + '0'.repeat(29), 'f')).code).toBe('InvalidMoney');
    expect(code(() => money('9'.repeat(29), 'f')).code).toBe('InvalidMoney');
    expect(code(() => money('79228162514264337593543950336', 'f')).code).toBe('InvalidMoney');
    expect(code(() => money('-79228162514264337593543950336', 'f')).code).toBe('InvalidMoney');
    expect(code(() => money('79228162514264337593543950335.1', 'f')).code).toBe('InvalidMoney');
    expect(emitMoney('79228162514264337593543950335')).toBe('{"a":79228162514264337593543950335}');
    expect(emitMoney('-79228162514264337593543950335.0')).toBe(
      '{"a":-79228162514264337593543950335.0}',
    );
    expect(() => money('0.' + '1'.repeat(28))).not.toThrow();
    expect(() => money('000' + '79228162514264337593543950335')).not.toThrow();
  });

  it('bounds huge strings before any BigInt work', () => {
    const start = Date.now();
    const largest = largestBigIntInput(() => {
      expect(code(() => money('1'.repeat(1_000_000))).code).toBe('InvalidMoney');
      expect(code(() => money('0.' + '1'.repeat(1_000_000))).code).toBe('InvalidMoney');
    });
    expect(largest).toBeLessThanOrEqual(1_000);
    expect(Date.now() - start).toBeLessThan(HUGE_INPUT_BUDGET_MS);
  });

  it('still accepts 1e21 and 1e-7', () => {
    expect(serializeRequest({ a: money(1e21) })).toBe('{"a":1000000000000000000000}');
    expect(serializeRequest({ a: money(1e-7) })).toBe('{"a":0.0000001}');
  });
});

describe('serializeRequest rejects bigint', () => {
  it('throws Validation / InvalidNumber with the field path', () => {
    try {
      serializeRequest({ a: { b: 1n } });
      expect.unreachable();
    } catch (e) {
      expect(e).toBeInstanceOf(MeInvoiceException);
      expect((e as MeInvoiceException).category).toBe('Validation');
      expect((e as MeInvoiceException).rawErrorCode).toBe('InvalidNumber');
      expect((e as MeInvoiceException).field).toBe('a.b');
    }
  });
});

import { MeInvoiceException } from '../domain/errors.js';
import type { Money } from '../domain/dtos.js';
import { isRealDate } from './wire/datetime.js';

const MONEY_PATTERN = /^-?\d+(\.\d+)?$/;
const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Branded wrapper; its text is emitted verbatim as a JSON number literal. */
export interface MoneyLiteral {
  readonly __money: string;
}

/** Objects actually returned by `money()`; makes the brand unforgeable by caller data. */
const issuedMoney = new WeakSet<object>();

function invalidMoney(message: string, field?: string): MeInvoiceException {
  return new MeInvoiceException('Validation', {
    rawErrorCode: 'InvalidMoney',
    message,
    ...(field !== undefined ? { field } : {}),
  });
}

function invalidDate(message: string, field?: string): MeInvoiceException {
  return new MeInvoiceException('Validation', {
    rawErrorCode: 'InvalidDate',
    message,
    ...(field !== undefined ? { field } : {}),
  });
}

/**
 * Finite number → plain decimal text (no exponent), digit-exact to its shortest round-trip
 * string: `1e21` → `1000000000000000000000`, `1e-7` → `0.0000001`.
 */
function plainDecimal(value: number): string {
  const s = String(value === 0 ? 0 : value);
  const m = /^(-?)(\d+)(?:\.(\d+))?e([+-]\d+)$/.exec(s);
  if (!m) return s;
  const sign = m[1] ?? '';
  const digits = (m[2] ?? '') + (m[3] ?? '');
  const point = (m[2] ?? '').length + Number(m[4]);
  if (point <= 0) return `${sign}0.${'0'.repeat(-point)}${digits}`;
  if (point >= digits.length) return sign + digits + '0'.repeat(point - digits.length);
  return `${sign}${digits.slice(0, point)}.${digits.slice(point)}`;
}

/** Validates a money value and wraps it so `serializeRequest` writes it verbatim. */
export function money(value: Money, field?: string): MoneyLiteral {
  let text: string;
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) {
      throw invalidMoney('Money amount must be a finite number.', field);
    }
    text = plainDecimal(value);
  } else if (typeof value === 'string' && MONEY_PATTERN.test(value)) {
    text = value;
  } else {
    throw invalidMoney('Money amount must be a plain decimal number.', field);
  }
  const literal: MoneyLiteral = Object.freeze({ __money: text });
  issuedMoney.add(literal);
  return literal;
}

function write(v: unknown, path: string): string | undefined {
  if (v === null) return 'null';
  switch (typeof v) {
    case 'undefined':
    case 'function':
    case 'symbol':
      return undefined;
    case 'string':
      return JSON.stringify(v);
    case 'boolean':
      return v ? 'true' : 'false';
    case 'number':
      if (!Number.isFinite(v)) {
        throw new MeInvoiceException('Validation', {
          rawErrorCode: 'InvalidNumber',
          message: 'Non-finite numbers cannot be serialized.',
          field: path,
        });
      }
      return JSON.stringify(v);
    case 'bigint':
      return v.toString();
  }
  if (issuedMoney.has(v as object)) {
    const text = (v as MoneyLiteral).__money;
    if (!MONEY_PATTERN.test(text)) {
      throw invalidMoney('Money amount must be a plain decimal number.', path);
    }
    return text;
  }
  if (v instanceof Date) {
    if (Number.isNaN(v.getTime())) throw invalidDate('Invalid Date value.', path);
    return JSON.stringify(v.toISOString());
  }
  if (Array.isArray(v)) {
    return `[${v.map((x, i) => write(x, `${path}[${i}]`) ?? 'null').join(',')}]`;
  }
  const parts: string[] = [];
  for (const [k, val] of Object.entries(v as Record<string, unknown>)) {
    if (val === null) continue;
    const text = write(val, path === '' ? k : `${path}.${k}`);
    if (text !== undefined) parts.push(`${JSON.stringify(k)}:${text}`);
  }
  return `{${parts.join(',')}}`;
}

/** Like `JSON.stringify`, but omits null/undefined members and writes `money()` values verbatim. */
export function serializeRequest(value: unknown): string {
  return write(value, '') ?? 'null';
}

/** Formats a date as `yyyy-MM-dd` (local calendar fields for `Date`); rejects impossible dates. */
export function formatDateOnly(v: string | Date, field?: string): string {
  if (typeof v === 'string') {
    const m = DATE_ONLY_PATTERN.exec(v);
    if (!m) {
      throw invalidDate('Date must be formatted yyyy-MM-dd.', field);
    }
    if (!isRealDate(Number(m[1]), Number(m[2]), Number(m[3]))) {
      throw invalidDate(`Date '${v}' is not a real calendar date.`, field);
    }
    return v;
  }
  if (!(v instanceof Date)) throw invalidDate('Date is required.', field);
  if (Number.isNaN(v.getTime())) throw invalidDate('Invalid Date value.', field);
  const y = String(v.getFullYear()).padStart(4, '0');
  const m = String(v.getMonth() + 1).padStart(2, '0');
  const d = String(v.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

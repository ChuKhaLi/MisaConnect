import { MeInvoiceException } from '../domain/errors.js';
import type { Money } from '../domain/dtos.js';

const MONEY_PATTERN = /^-?\d+(\.\d+)?$/;
const DATE_ONLY_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

/** Branded wrapper; its text is emitted verbatim as a JSON number literal. */
export interface MoneyLiteral {
  readonly __money: string;
}

function invalidMoney(message: string): MeInvoiceException {
  return new MeInvoiceException('Validation', { rawErrorCode: 'InvalidMoney', message });
}

/** Validates a money value and wraps it so `serializeRequest` writes it verbatim. */
export function money(value: Money): MoneyLiteral {
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) throw invalidMoney('Money amount must be a finite number.');
    return { __money: JSON.stringify(value) };
  }
  if (typeof value !== 'string' || !MONEY_PATTERN.test(value)) {
    throw invalidMoney('Money amount must be a plain decimal number.');
  }
  return { __money: value };
}

function isMoneyLiteral(v: unknown): v is MoneyLiteral {
  return (
    typeof v === 'object' &&
    v !== null &&
    typeof (v as { __money?: unknown }).__money === 'string' &&
    Object.keys(v).length === 1
  );
}

function write(v: unknown): string | undefined {
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
        });
      }
      return JSON.stringify(v);
    case 'bigint':
      return v.toString();
  }
  if (isMoneyLiteral(v)) return v.__money;
  if (v instanceof Date) return JSON.stringify(v.toISOString());
  if (Array.isArray(v)) {
    return `[${v.map((x) => write(x) ?? 'null').join(',')}]`;
  }
  const parts: string[] = [];
  for (const [k, val] of Object.entries(v as Record<string, unknown>)) {
    if (val === null) continue;
    const text = write(val);
    if (text !== undefined) parts.push(`${JSON.stringify(k)}:${text}`);
  }
  return `{${parts.join(',')}}`;
}

/** Like `JSON.stringify`, but omits null/undefined members and writes `money()` values verbatim. */
export function serializeRequest(value: unknown): string {
  return write(value) ?? 'null';
}

/** Formats a date as `yyyy-MM-dd` (local calendar fields for `Date`). */
export function formatDateOnly(v: string | Date): string {
  if (typeof v === 'string') {
    if (!DATE_ONLY_PATTERN.test(v)) {
      throw new MeInvoiceException('Validation', {
        rawErrorCode: 'InvalidDate',
        message: 'Date must be formatted yyyy-MM-dd.',
      });
    }
    return v;
  }
  const y = String(v.getFullYear()).padStart(4, '0');
  const m = String(v.getMonth() + 1).padStart(2, '0');
  const d = String(v.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

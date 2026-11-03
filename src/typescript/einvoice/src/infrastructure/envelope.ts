import { mapErrorCode } from '../application/error-mapper.js';
import type { Envelope } from '../application/ports.js';
import { MeInvoiceException } from '../domain/errors.js';

export type { Envelope } from '../application/ports.js';

/** One entry of the insert endpoint's `error` array (port `InsertErrorEntryDto`). */
export interface InsertErrorEntry {
  refID?: string;
  errorCode?: string;
  errorMessage?: string;
}

const isRecord = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

function tryParse(text: string): { ok: true; value: unknown } | { ok: false } {
  try {
    return { ok: true, value: JSON.parse(text) as unknown };
  } catch {
    return { ok: false };
  }
}

/**
 * Parses a MISA response body into an envelope. Never surfaces a raw `SyntaxError`:
 * empty body → `MisaUnknown 'EmptyResponse'`; non-JSON or non-object body →
 * `MisaUnavailable` when `status >= 500`, else `MisaUnknown 'InvalidResponse'`.
 */
export function readEnvelope<T>(status: number, text: string): Envelope<T> {
  if (text.trim() === '') {
    throw new MeInvoiceException('MisaUnknown', {
      rawErrorCode: 'EmptyResponse',
      message: 'MISA returned an empty response body.',
    });
  }
  const parsed = tryParse(text);
  if (parsed.ok && parsed.value === null) {
    throw new MeInvoiceException('MisaUnknown', {
      rawErrorCode: 'EmptyResponse',
      message: 'MISA returned an empty response body.',
    });
  }
  if (!parsed.ok || !isRecord(parsed.value)) {
    throw new MeInvoiceException(status >= 500 ? 'MisaUnavailable' : 'MisaUnknown', {
      rawErrorCode: 'InvalidResponse',
      message: `MISA returned a response body that is not a JSON envelope (HTTP ${status}).`,
    });
  }
  const body = parsed.value;
  return {
    success: body['success'] === true,
    data: decodeData(body['data']) as T | undefined,
    error: body['error'] ?? undefined,
    errorCode: body['errorCode'] ?? undefined,
    ...(typeof body['ErrorMessage'] === 'string' ? { errorMessage: body['ErrorMessage'] } : {}),
  };
}

/**
 * `data` is stringified JSON (token, templates, insert), a raw JSON array (lookups,
 * `MeInvoiceClient.DeserializeSnapshots`), or a plain string (base64 PDF).
 */
function decodeData(raw: unknown): unknown {
  if (raw === null || raw === undefined || raw === '') return undefined;
  if (typeof raw !== 'string') return raw;
  const first = raw.trimStart()[0];
  if (first === '{' || first === '[') {
    const parsed = tryParse(raw);
    if (parsed.ok) return parsed.value;
  }
  return raw;
}

/** Port `ExtractRawErrorCode`: `errorCode` is a string, a string array (first wins), or absent. */
export function extractErrorCode(envelope: Envelope<unknown>): string | undefined {
  const code = envelope.errorCode;
  if (typeof code === 'string') return code;
  if (Array.isArray(code) && code.length > 0) {
    const first: unknown = code[0];
    return typeof first === 'string' ? first : JSON.stringify(first);
  }
  return undefined;
}

/** Port `ExtractErrorMessage`: string as-is, anything else as raw JSON text. */
export function extractErrorMessage(envelope: Envelope<unknown>): string | undefined {
  const e = envelope.error;
  if (e === undefined || e === null) return undefined;
  return typeof e === 'string' ? e : JSON.stringify(e);
}

/** Port `ParseErrorEntries`: stringified array, raw array, or a plain message string. */
export function parseInsertErrors(error: unknown): InsertErrorEntry[] {
  if (typeof error === 'string') {
    if (error === '') return [];
    const parsed = tryParse(error);
    if (parsed.ok && Array.isArray(parsed.value)) return parsed.value.map(toEntry);
    return [{ errorMessage: error }];
  }
  if (Array.isArray(error)) return error.map(toEntry);
  return [];
}

function toEntry(raw: unknown): InsertErrorEntry {
  const entry: InsertErrorEntry = {};
  if (!isRecord(raw)) return entry;
  if (typeof raw['RefID'] === 'string') entry.refID = raw['RefID'];
  if (typeof raw['ErrorCode'] === 'string') entry.errorCode = raw['ErrorCode'];
  if (typeof raw['ErrorMessage'] === 'string') entry.errorMessage = raw['ErrorMessage'];
  return entry;
}

/** Port `ThrowFromEnvelope`: maps the envelope's error code (or the insert-array code). */
export function throwFromEnvelope(envelope: Envelope<unknown>, refId?: string): never {
  let raw = extractErrorCode(envelope);
  const msg = extractErrorMessage(envelope);
  if ((raw === undefined || raw === '') && msg !== undefined && msg !== '') {
    const first = parseInsertErrors(envelope.error)[0];
    if (first?.errorCode !== undefined && first.errorCode !== '') raw = first.errorCode;
  }
  const code = mapErrorCode(raw, msg);
  const message = code.detail ?? msg;
  throw new MeInvoiceException(code.category, {
    rawErrorCode: code.rawCode,
    ...(message !== undefined ? { message } : {}),
    ...(code.field !== undefined ? { field: code.field } : {}),
    ...(code.component !== undefined ? { component: code.component } : {}),
    ...(refId !== undefined ? { refId } : {}),
  });
}

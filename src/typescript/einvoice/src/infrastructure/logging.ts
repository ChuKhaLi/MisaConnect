import type { Logger } from '../application/ports.js';

const SENSITIVE = /authorization|bearer|taxcode|password|token|appid|app_id|app-id/i;
const MASK = '***';

function scrubValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(scrubValue);
  if (
    value !== null &&
    typeof value === 'object' &&
    Object.getPrototypeOf(value) === Object.prototype
  ) {
    return scrub(value as Record<string, unknown>);
  }
  return value;
}

/** Returns a copy of `fields` with sensitive values replaced by `***` (recursive). */
export function scrub(fields: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(fields)) {
    out[key] = SENSITIVE.test(key) ? MASK : scrubValue(value);
  }
  return out;
}

const SENSITIVE_JSON_FIELD =
  /"(password|appid|app_id|app-id|token|taxcode|authorization|bearer)"\s*:\s*"[^"]*"/gi;

/**
 * Port `MeInvoiceLogScrubber.Redact(string)`: masks the value of sensitive JSON string
 * fields (`"password":"..."`, `"token":"..."`, ...) embedded anywhere in free text.
 */
export function redactText(text: string): string {
  return text.replace(SENSITIVE_JSON_FIELD, (_match, name: string) => `"${name}":"${MASK}"`);
}

export const noopLogger: Logger = {
  debug: () => undefined,
  info: () => undefined,
  warn: () => undefined,
  error: () => undefined,
};

/**
 * Wraps a caller-supplied logger so a throwing method is swallowed: logging must never turn a
 * completed MISA call (e.g. a saved draft) into a failure, which would invite duplicate retries.
 */
export function safeLogger(logger: Logger): Logger {
  const guard =
    (level: keyof Logger) =>
    (message: string, fields?: Record<string, unknown>): void => {
      try {
        logger[level](message, fields);
      } catch {
        // Logger failures are deliberately ignored.
      }
    };
  return { debug: guard('debug'), info: guard('info'), warn: guard('warn'), error: guard('error') };
}

type PinoMethod = (obj: Record<string, unknown>, msg: string) => void;

export interface PinoLike {
  debug: PinoMethod;
  info: PinoMethod;
  warn: PinoMethod;
  error: PinoMethod;
}

export function fromPino(p: PinoLike): Logger {
  return {
    debug: (m, f) => p.debug(f ?? {}, m),
    info: (m, f) => p.info(f ?? {}, m),
    warn: (m, f) => p.warn(f ?? {}, m),
    error: (m, f) => p.error(f ?? {}, m),
  };
}

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

export const noopLogger: Logger = {
  debug: () => undefined,
  info: () => undefined,
  warn: () => undefined,
  error: () => undefined,
};

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

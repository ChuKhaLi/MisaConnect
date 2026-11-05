import type { Logger } from '../application/ports.js';

const MASK = '***';

export const noopLogger: Logger = {
  debug: () => undefined,
  info: () => undefined,
  warn: () => undefined,
  error: () => undefined,
};

/**
 * Wraps a caller-supplied logger so a throwing method is swallowed: logging must never turn a
 * completed MISA call into a failure.
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

// Ports of the regexes in .NET `ESignLogScrubber` (authoritative).
const BEARER = /Bearer\s+[A-Za-z0-9\-_=.+/]{8,}/gi;
const AUTH_HEADER = /(AuthorizationRM|Authorization)\s*[:=]\s*[A-Za-z0-9\-_=.+/ ]{8,}/gi;
const SENSITIVE_JSON_FIELD =
  /"(accessToken|remoteSigningAccessToken|refreshToken|authorizationRM|authorization|password|code|certificate|certiticateChain|certificateChain|fileToSign|documentBytes|documentHash|sh|digest|signature|signatureId|mainDom|document|fontData|signatureImage|logoImage|email|emailName|phoneNumber|firstName|lastName|extraData|secret)"\s*:\s*"[^"]*"/gi;
const EXTRA_DATA_OBJECT = /"extraData"\s*:\s*\{[^}]*\}/gi;
const DEVICE_JSON_FIELD = /"(device[A-Za-z0-9_]*)"\s*:\s*"[^"]*"/gi;
const BASE64_CHUNK = /[A-Za-z0-9+/]{32,}={0,2}/g;

/** Masks tokens, headers, PII and document material in free text (INV §6). */
export function scrubText(text: string): string {
  if (text === undefined || text === null || text === '') return text;
  let r = text.replace(EXTRA_DATA_OBJECT, `"extraData":"${MASK}"`);
  r = r.replace(AUTH_HEADER, (_m, name: string) => `${name}: ${MASK}`);
  r = r.replace(BEARER, `Bearer ${MASK}`);
  r = r.replace(SENSITIVE_JSON_FIELD, (_m, name: string) => `"${name}":"${MASK}"`);
  r = r.replace(DEVICE_JSON_FIELD, (_m, name: string) => `"${name}":"${MASK}"`);
  return r.replace(BASE64_CHUNK, MASK);
}

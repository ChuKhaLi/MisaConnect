/**
 * .NET `decimal` limits and exact-decimal helpers, shared by the validator (FR-025) and the
 * money serializer so both agree on what a money value is.
 */

/** Maximum fractional digits of a .NET `decimal`. */
export const MAX_SCALE = 28;
/** `decimal.MaxValue` (2^96 − 1): 79,228,162,514,264,337,593,543,950,335. */
export const MAX_DECIMAL = 79228162514264337593543950335n;
const MAX_INT_DIGITS = MAX_DECIMAL.toString().length;

/** `int × 10^-scale`, exactly. */
export interface ExactDecimal {
  int: bigint;
  scale: number;
}

const NUMBER_TEXT_RE = /^(-?)(\d+)(?:\.(\d+))?(?:e([+-]?\d+))?$/;
const PLAIN_DECIMAL_RE = /^(-?)(\d+)(?:\.(\d+))?$/;

/**
 * Exact decimal of a finite number's shortest round-trip text, rounded half-to-even to
 * {@link MAX_SCALE} fractional digits — what .NET's decimal parser (e.g. System.Text.Json
 * reading that JSON literal) produces: `5.551115123125783e-17` → `0.0000000000000000555111512313`,
 * `2.5e-28` → `0.0000000000000000000000000002`, `5e-324` → `0`. Undefined when non-finite or
 * |x| > `decimal.MaxValue`.
 */
export function decimalFromNumber(value: number): ExactDecimal | undefined {
  if (!Number.isFinite(value)) return undefined;
  const m = NUMBER_TEXT_RE.exec(String(value));
  if (!m) return undefined;
  const frac = m[3] ?? '';
  // Finite doubles have |exponent| <= 324, so the BigInt work below is bounded.
  let digits = BigInt((m[2] ?? '') + frac);
  let scale = frac.length - Number(m[4] ?? '0');
  if (scale < 0) {
    digits *= 10n ** BigInt(-scale);
    scale = 0;
  } else if (scale > MAX_SCALE) {
    digits = roundHalfEven(digits, scale - MAX_SCALE);
    scale = MAX_SCALE;
  }
  return inRange(m[1] === '-' ? -digits : digits, scale);
}

/**
 * Exact decimal of a plain decimal string (`^-?\d+(\.\d+)?$`, no rounding). Undefined when it is
 * not one, has more than {@link MAX_SCALE} fractional digits, or |x| > `decimal.MaxValue`.
 */
export function decimalFromPlainString(text: string): ExactDecimal | undefined {
  const m = PLAIN_DECIMAL_RE.exec(text);
  if (!m) return undefined;
  const whole = (m[2] ?? '').replace(/^0+/, '');
  const frac = m[3] ?? '';
  // Bound sizes before any BigInt work.
  if (frac.length > MAX_SCALE || whole.length > MAX_INT_DIGITS) return undefined;
  const digits = BigInt(whole + frac);
  return inRange(m[1] === '-' ? -digits : digits, frac.length);
}

/** Plain text without trailing fractional zeros or a negative zero. */
export function formatDecimal(d: ExactDecimal): string {
  const neg = d.int < 0n;
  const abs = (neg ? -d.int : d.int).toString().padStart(d.scale + 1, '0');
  const whole = abs.slice(0, abs.length - d.scale);
  const frac = abs.slice(abs.length - d.scale).replace(/0+$/, '');
  return (neg ? '-' : '') + whole + (frac ? '.' + frac : '');
}

function roundHalfEven(digits: bigint, drop: number): bigint {
  const divisor = 10n ** BigInt(drop);
  const q = digits / divisor;
  const twiceRemainder = (digits % divisor) * 2n;
  return twiceRemainder > divisor || (twiceRemainder === divisor && q % 2n === 1n) ? q + 1n : q;
}

function inRange(int: bigint, scale: number): ExactDecimal | undefined {
  const abs = int < 0n ? -int : int;
  return abs > MAX_DECIMAL * 10n ** BigInt(scale) ? undefined : { int, scale };
}

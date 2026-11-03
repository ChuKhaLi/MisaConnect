import { vi } from 'vitest';

/** Generous wall-clock budget: catches runaway work (regex backtracking, huge expansions). */
export const HUGE_INPUT_BUDGET_MS = 5_000;

/**
 * Runs `fn` with `BigInt` wrapped and returns the largest input it was given: the length of a
 * string argument or the magnitude of a number (an exponent fed to `10n ** BigInt(x)`).
 * Deterministic proof that huge inputs are bounded before any BigInt work, unlike a timing
 * assert (V8 parses a 1M-digit BigInt in ~0.3 s, too close to any safe time budget).
 */
export function largestBigIntInput(fn: () => void): number {
  const original = globalThis.BigInt;
  let largest = 0;
  const wrapped = (value: string | number | bigint | boolean): bigint => {
    const size =
      typeof value === 'string' ? value.length : typeof value === 'number' ? Math.abs(value) : 0;
    largest = Math.max(largest, size);
    return original(value);
  };
  vi.stubGlobal('BigInt', Object.assign(wrapped, original));
  try {
    fn();
  } finally {
    vi.stubGlobal('BigInt', original);
  }
  return largest;
}

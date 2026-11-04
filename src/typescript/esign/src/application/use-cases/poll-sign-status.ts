import type { DocumentFormat, SignStatus } from '../../domain/enums.js';
import { SignTerminalStateException, SignTimeoutException } from '../../domain/errors.js';
import type { RawSignStatus } from '../esrm-gateway.js';
import type { Clock, Delay } from '../ports.js';

export interface PollDeps {
  /** One `Signing/status` call (authenticated by the caller). */
  getStatus(transactionId: string, signal?: AbortSignal): Promise<RawSignStatus>;
  clock: Clock;
  delay: Delay;
  intervalMs: number;
  totalTimeoutMs: number;
}

export interface PollContext {
  correlationId: string;
  signal?: AbortSignal | undefined;
  format: DocumentFormat;
}

export interface SignStatusSnapshot extends RawSignStatus {
  status: SignStatus;
}

/** MISA status, case-insensitive; `CANCELED` is an alias of `CANCELLED`; anything else is UNKNOWN. */
export function parseSignStatus(raw: string | undefined): SignStatus {
  switch (raw?.toUpperCase()) {
    case 'PENDING':
      return 'PENDING';
    case 'SUCCESS':
      return 'SUCCESS';
    case 'FAILED':
      return 'FAILED';
    case 'CANCELLED':
    case 'CANCELED':
      return 'CANCELLED';
    default:
      return 'UNKNOWN';
  }
}

function terminalDetail(s: SignStatusSnapshot): string {
  if (s.errorDescription) return `Signing/status terminal state ${s.status}: ${s.errorDescription}`;
  if (s.errorCode) return `Signing/status terminal state ${s.status} (errorCode=${s.errorCode}).`;
  return `Signing/status terminal state ${s.status}.`;
}

/**
 * Polls `Signing/status` until SUCCESS (INV §2.4 step 6). FAILED / CANCELLED / UNKNOWN throw
 * `SignTerminalStateException`; after each PENDING the deadline is checked, then the loop waits
 * `min(interval, remaining)`. An abort rejects with the signal's reason and sends no further call.
 */
export async function pollSignStatus(
  deps: PollDeps,
  transactionId: string,
  ctx: PollContext,
): Promise<SignStatusSnapshot> {
  const { clock, intervalMs, totalTimeoutMs } = deps;
  const start = clock.now().getTime();
  const deadline = start + totalTimeoutMs;

  for (;;) {
    ctx.signal?.throwIfAborted();
    const raw = await deps.getStatus(transactionId, ctx.signal);
    const snapshot: SignStatusSnapshot = { ...raw, status: parseSignStatus(raw.status) };

    if (snapshot.status === 'SUCCESS') return snapshot;
    if (snapshot.status !== 'PENDING') {
      throw new SignTerminalStateException({
        terminalStatus: snapshot.status,
        transactionId,
        rawCode:
          snapshot.status === 'UNKNOWN'
            ? (snapshot.errorCode ?? 'UnknownStatus')
            : snapshot.errorCode,
        detail: terminalDetail(snapshot),
        correlationId: ctx.correlationId,
        format: ctx.format,
      });
    }

    const now = clock.now().getTime();
    if (now >= deadline) {
      throw new SignTimeoutException({
        transactionId,
        elapsedMs: now - start,
        detail: `Polling exceeded total timeout of ${totalTimeoutMs} ms on transactionId=${transactionId}.`,
        correlationId: ctx.correlationId,
        format: ctx.format,
      });
    }
    await deps.delay(Math.min(intervalMs, deadline - now), ctx.signal);
  }
}

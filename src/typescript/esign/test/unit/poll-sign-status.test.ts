import { describe, expect, it } from 'vitest';
import type { RawSignStatus } from '../../src/application/esrm-gateway.js';
import type { Delay } from '../../src/application/ports.js';
import {
  parseSignStatus,
  pollSignStatus,
  type PollDeps,
} from '../../src/application/use-cases/poll-sign-status.js';
import { SignTerminalStateException, SignTimeoutException } from '../../src/domain/errors.js';

const T0 = Date.parse('2026-11-04T00:00:00Z');
const TX = 'tx-1';
const ctx = { correlationId: 'corr-1', format: 'Pdf' as const };

function harness(
  answers: (Omit<RawSignStatus, 'transactionId'> | (() => Omit<RawSignStatus, 'transactionId'>))[],
  over: Partial<PollDeps> = {},
) {
  let now = T0;
  const delays: number[] = [];
  let calls = 0;
  const deps: PollDeps = {
    getStatus: (tx) => {
      const a = answers[Math.min(calls, answers.length - 1)];
      calls++;
      const raw = typeof a === 'function' ? a() : a;
      return Promise.resolve({ transactionId: tx, ...raw });
    },
    clock: { now: () => new Date(now) },
    delay: (ms) => {
      delays.push(ms);
      now += ms;
      return Promise.resolve();
    },
    intervalMs: 2000,
    totalTimeoutMs: 60000,
    ...over,
  };
  return { deps, delays, calls: () => calls };
}

async function rejection(p: Promise<unknown>): Promise<unknown> {
  try {
    await p;
  } catch (e) {
    return e;
  }
  throw new Error('expected rejection');
}

describe('parseSignStatus', () => {
  it.each([
    ['PENDING', 'PENDING'],
    ['success', 'SUCCESS'],
    ['Failed', 'FAILED'],
    ['CANCELLED', 'CANCELLED'],
    ['canceled', 'CANCELLED'],
    ['weird', 'UNKNOWN'],
    [undefined, 'UNKNOWN'],
  ] as const)('%s → %s', (raw, expected) => {
    expect(parseSignStatus(raw)).toBe(expected);
  });
});

describe('pollSignStatus', () => {
  it('PENDING twice then SUCCESS → three status calls and the first signature', async () => {
    const h = harness([
      { status: 'PENDING' },
      { status: 'PENDING' },
      { status: 'SUCCESS', firstSignature: 'SIG' },
    ]);
    const snap = await pollSignStatus(h.deps, TX, ctx);
    expect(h.calls()).toBe(3);
    expect(h.delays).toEqual([2000, 2000]);
    expect(snap.status).toBe('SUCCESS');
    expect(snap.firstSignature).toBe('SIG');
  });

  it('canceled → SignTerminalCancelled', async () => {
    const h = harness([{ status: 'canceled' }]);
    const err = await rejection(pollSignStatus(h.deps, TX, ctx));
    expect(err).toBeInstanceOf(SignTerminalStateException);
    const e = err as SignTerminalStateException;
    expect(e.category).toBe('SignTerminalCancelled');
    expect(e.terminalStatus).toBe('CANCELLED');
    expect(e.transactionId).toBe(TX);
    expect(e.correlationId).toBe('corr-1');
    expect(e.format).toBe('Pdf');
  });

  it('FAILED → SignTerminalFailed with the errorCode and description', async () => {
    const h = harness([{ status: 'FAILED', errorCode: 'ResultBad', errorDescription: 'boom' }]);
    const e = (await rejection(pollSignStatus(h.deps, TX, ctx))) as SignTerminalStateException;
    expect(e.category).toBe('SignTerminalFailed');
    expect(e.rawCode).toBe('ResultBad');
    expect(e.detail).toBe('Signing/status terminal state FAILED: boom');
  });

  it('weird status → SignTerminalUnknown with rawCode from errorCode', async () => {
    const h = harness([{ status: 'weird', errorCode: 'E42' }]);
    const e = (await rejection(pollSignStatus(h.deps, TX, ctx))) as SignTerminalStateException;
    expect(e.category).toBe('SignTerminalUnknown');
    expect(e.terminalStatus).toBe('UNKNOWN');
    expect(e.rawCode).toBe('E42');
    expect(e.detail).toBe('Signing/status terminal state UNKNOWN (errorCode=E42).');
  });

  it('weird status without errorCode → rawCode "UnknownStatus"', async () => {
    const h = harness([{ status: 'weird' }]);
    const e = (await rejection(pollSignStatus(h.deps, TX, ctx))) as SignTerminalStateException;
    expect(e.category).toBe('SignTerminalUnknown');
    expect(e.rawCode).toBe('UnknownStatus');
    expect(e.detail).toBe('Signing/status terminal state UNKNOWN.');
  });

  it('times out with the fake clock: delays clamp to the remaining time', async () => {
    const h = harness([{ status: 'PENDING' }], { intervalMs: 2000, totalTimeoutMs: 5000 });
    const err = await rejection(pollSignStatus(h.deps, TX, ctx));
    expect(err).toBeInstanceOf(SignTimeoutException);
    const e = err as SignTimeoutException;
    expect(e.category).toBe('SignTimeout');
    expect(e.rawCode).toBe('SignTimeout');
    expect(e.elapsedMs).toBe(5000);
    expect(e.transactionId).toBe(TX);
    expect(h.delays).toEqual([2000, 2000, 1000]);
    expect(h.calls()).toBe(4);
  });

  it('abort during polling stops without another status call', async () => {
    const ctrl = new AbortController();
    const reason = new Error('caller gave up');
    // A delay that only ends when the signal aborts (like the default abortable delay).
    const delay: Delay = (_ms, signal) =>
      new Promise<void>((_resolve, reject) => {
        signal?.addEventListener('abort', () => reject(signal.reason as Error), { once: true });
      });
    const h = harness([{ status: 'PENDING' }], { delay });
    const p = pollSignStatus(h.deps, TX, { ...ctx, signal: ctrl.signal });
    await new Promise((r) => setTimeout(r, 5));
    expect(h.calls()).toBe(1);
    ctrl.abort(reason);
    await expect(p).rejects.toBe(reason);
    await new Promise((r) => setTimeout(r, 5));
    expect(h.calls()).toBe(1);
  });

  it('a delay that ignores the signal still sends no status call after abort', async () => {
    const ctrl = new AbortController();
    const reason = new Error('stop');
    const delay: Delay = () => {
      ctrl.abort(reason);
      return Promise.resolve();
    };
    const h = harness([{ status: 'PENDING' }], { delay });
    await expect(pollSignStatus(h.deps, TX, { ...ctx, signal: ctrl.signal })).rejects.toBe(reason);
    expect(h.calls()).toBe(1);
  });

  it('an already-aborted signal sends no status call', async () => {
    const ctrl = new AbortController();
    ctrl.abort(new Error('early'));
    const h = harness([{ status: 'SUCCESS' }]);
    await expect(pollSignStatus(h.deps, TX, { ...ctx, signal: ctrl.signal })).rejects.toThrow(
      'early',
    );
    expect(h.calls()).toBe(0);
  });
});

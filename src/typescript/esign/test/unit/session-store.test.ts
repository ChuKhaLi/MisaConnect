import { describe, expect, it } from 'vitest';
import type { SigningSession } from '../../src/application/ports.js';
import { UnknownTransactionException } from '../../src/domain/errors.js';
import {
  createInMemorySigningSessionStore,
  MAX_OBSERVED_MESSAGE_IDS,
} from '../../src/infrastructure/in-memory-session-store.js';

const T0 = new Date('2026-11-04T08:00:00.000Z');

function session(over: Partial<SigningSession> = {}): SigningSession {
  return {
    clientId: 'cid',
    userName: 'alice',
    transactionId: 'tx-1',
    format: 'Pdf',
    hash: { format: 'Pdf', documentId: 'doc-1', digest: 'DIGEST' },
    recordedDocumentIds: ['doc-1'],
    createdAt: T0.toISOString(),
    ttlMs: 60_000,
    observedMessageIds: [],
    ...over,
  };
}

function fakeClock(start: Date = T0): { now(): Date; advance(ms: number): void } {
  let t = start.getTime();
  return { now: () => new Date(t), advance: (ms) => (t += ms) };
}

describe('in-memory signing session store', () => {
  it('registers and gets by (clientId, transactionId)', async () => {
    const store = createInMemorySigningSessionStore(fakeClock());
    await store.register(session());
    expect(await store.get('cid', 'tx-1')).toMatchObject({
      transactionId: 'tx-1',
      userName: 'alice',
    });
    expect(await store.get('other', 'tx-1')).toBeUndefined();
    expect(await store.get('cid', 'tx-2')).toBeUndefined();
  });

  it('expires a session lazily once createdAt + ttlMs is reached', async () => {
    const clock = fakeClock();
    const store = createInMemorySigningSessionStore(clock);
    await store.register(session());
    clock.advance(59_999);
    expect(await store.get('cid', 'tx-1')).toBeDefined();
    clock.advance(1);
    expect(await store.get('cid', 'tx-1')).toBeUndefined();
    // Evicted: the lock now fails as for an unknown transaction.
    await expect(
      store.withFinalizeLock('cid', 'tx-1', () => Promise.resolve(1)),
    ).rejects.toBeInstanceOf(UnknownTransactionException);
  });

  it('records message ids once and caches a success', async () => {
    const store = createInMemorySigningSessionStore(fakeClock());
    await store.register(session());
    await store.recordMessageId('cid', 'tx-1', 'm1');
    await store.recordMessageId('cid', 'tx-1', 'm1');
    await store.recordMessageId('cid', 'tx-1', 'm2');
    const ack = { errorCode: '0', devMsg: 'Webhook accepted.', userMsg: 'Webhook accepted.' };
    await store.cacheSuccess('cid', 'tx-1', ack, 'U0lHTkVE');
    const s = await store.get('cid', 'tx-1');
    expect(s?.observedMessageIds).toEqual(['m1', 'm2']);
    expect(s?.cachedSuccess).toEqual({ ack, signed: 'U0lHTkVE' });
    // Writes to an unknown session are no-ops.
    await store.recordMessageId('cid', 'nope', 'm1');
    await store.cacheSuccess('cid', 'nope', ack, 'x');
    expect(await store.get('cid', 'nope')).toBeUndefined();
  });

  it('returns copies: mutating a returned session does not change the store', async () => {
    const store = createInMemorySigningSessionStore(fakeClock());
    await store.register(session());
    const s = await store.get('cid', 'tx-1');
    s?.observedMessageIds.push('mutated');
    expect((await store.get('cid', 'tx-1'))?.observedMessageIds).toEqual([]);
  });

  it('withFinalizeLock on a missing session throws UnknownTransactionException', async () => {
    const store = createInMemorySigningSessionStore(fakeClock());
    await expect(
      store.withFinalizeLock('cid', 'tx-1', () => Promise.resolve(1)),
    ).rejects.toBeInstanceOf(UnknownTransactionException);
  });

  it('serializes two finalizers on the same key and releases the lock after a failure', async () => {
    const store = createInMemorySigningSessionStore(fakeClock());
    await store.register(session());
    const events: string[] = [];
    let releaseFirst!: () => void;
    const firstGate = new Promise<void>((r) => (releaseFirst = r));
    const first = store.withFinalizeLock('cid', 'tx-1', async () => {
      events.push('first:start');
      await firstGate;
      events.push('first:end');
      throw new Error('boom');
    });
    const second = store.withFinalizeLock('cid', 'tx-1', () => {
      events.push('second:start');
      return Promise.resolve('ok');
    });
    await new Promise((r) => setTimeout(r, 5));
    expect(events).toEqual(['first:start']);
    releaseFirst();
    await expect(first).rejects.toThrow('boom');
    await expect(second).resolves.toBe('ok');
    expect(events).toEqual(['first:start', 'first:end', 'second:start']);
  });

  it('does not serialize different transactions', async () => {
    const store = createInMemorySigningSessionStore(fakeClock());
    await store.register(session());
    await store.register(session({ transactionId: 'tx-2' }));
    let release!: () => void;
    const gate = new Promise<void>((r) => (release = r));
    const a = store.withFinalizeLock('cid', 'tx-1', () => gate);
    await expect(store.withFinalizeLock('cid', 'tx-2', () => Promise.resolve(2))).resolves.toBe(2);
    release();
    await a;
  });

  it('keeps only the most recent 100 message ids', async () => {
    expect(MAX_OBSERVED_MESSAGE_IDS).toBe(100);
    const store = createInMemorySigningSessionStore(fakeClock());
    await store.register(session());
    for (let i = 0; i < 150; i++) await store.recordMessageId('cid', 'tx-1', `m${i}`);
    const ids = (await store.get('cid', 'tx-1'))?.observedMessageIds ?? [];
    expect(ids).toHaveLength(100);
    expect(ids[0]).toBe('m50');
    expect(ids[99]).toBe('m149');
  });

  it('records a terminal state', async () => {
    const store = createInMemorySigningSessionStore(fakeClock());
    await store.register(session());
    await store.recordTerminal('cid', 'tx-1', { status: 'Failed', misaErrorCode: 'E1' });
    expect((await store.get('cid', 'tx-1'))?.terminal).toEqual({
      status: 'Failed',
      misaErrorCode: 'E1',
    });
  });

  it('ignores writes to an expired session', async () => {
    const clock = fakeClock();
    const store = createInMemorySigningSessionStore(clock);
    await store.register(session());
    clock.advance(60_000);
    const ack = { errorCode: '0', devMsg: 'd', userMsg: 'u' };
    await store.recordMessageId('cid', 'tx-1', 'm1');
    await store.cacheSuccess('cid', 'tx-1', ack, 'x');
    await store.recordTerminal('cid', 'tx-1', { status: 'Failed' });
    clock.advance(-60_000); // were the entry still there, it would be live again
    expect(await store.get('cid', 'tx-1')).toBeUndefined();
  });

  it('register sweeps expired sessions', async () => {
    const clock = fakeClock();
    const store = createInMemorySigningSessionStore(clock);
    await store.register(session({ transactionId: 'old' }));
    clock.advance(60_000);
    await store.register(session({ transactionId: 'new', createdAt: clock.now().toISOString() }));
    clock.advance(-60_000); // 'old' would be live at this time if it had not been swept
    expect(await store.get('cid', 'old')).toBeUndefined();
  });
});

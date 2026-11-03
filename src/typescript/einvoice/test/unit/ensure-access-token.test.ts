import { describe, expect, it } from 'vitest';
import type { AccessToken, Clock } from '../../src/application/ports.js';
import {
  ensureAccessToken,
  type EnsureAccessTokenDeps,
} from '../../src/application/use-cases/ensure-access-token.js';
import { createInMemoryTokenCache } from '../../src/infrastructure/defaults.js';

const NOW = new Date('2026-11-03T00:00:00Z');
const MIN = 60_000;
const clock: Clock = { now: () => NOW };

function deferred<T>(): {
  promise: Promise<T>;
  resolve: (v: T) => void;
  reject: (e: unknown) => void;
} {
  let resolve!: (v: T) => void;
  let reject!: (e: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

function makeDeps(acquire: EnsureAccessTokenDeps['acquire']): EnsureAccessTokenDeps {
  return {
    cacheKey: '0100000000-app',
    tokenCache: createInMemoryTokenCache(),
    clock,
    acquire,
    inflight: new Map<string, Promise<AccessToken>>(),
  };
}

describe('ensureAccessToken', () => {
  it('acquires on a cold cache and stores with lifetime 14 days minus 1 hour', async () => {
    let calls = 0;
    const deps = makeDeps(() => {
      calls++;
      return Promise.resolve({ value: 'fresh', expiresAt: new Date(0) });
    });
    expect(await ensureAccessToken(deps)).toBe('fresh');
    expect(calls).toBe(1);
    const stored = await deps.tokenCache.get('0100000000-app');
    expect(stored?.value).toBe('fresh');
    expect(stored?.expiresAt.getTime()).toBe(NOW.getTime() + 14 * 24 * 60 * MIN - 60 * MIN);
  });

  it('re-acquires when the cached token expires within the hour (now + 59 min)', async () => {
    let calls = 0;
    const deps = makeDeps(() => {
      calls++;
      return Promise.resolve({ value: 'fresh', expiresAt: new Date(0) });
    });
    await deps.tokenCache.set(deps.cacheKey, {
      value: 'old',
      expiresAt: new Date(NOW.getTime() + 59 * MIN),
    });
    expect(await ensureAccessToken(deps)).toBe('fresh');
    expect(calls).toBe(1);
  });

  it('reuses a cached token valid beyond the hour (now + 61 min)', async () => {
    let calls = 0;
    const deps = makeDeps(() => {
      calls++;
      return Promise.resolve({ value: 'fresh', expiresAt: new Date(0) });
    });
    await deps.tokenCache.set(deps.cacheKey, {
      value: 'old',
      expiresAt: new Date(NOW.getTime() + 61 * MIN),
    });
    expect(await ensureAccessToken(deps)).toBe('old');
    expect(calls).toBe(0);
  });

  it('single flight: concurrent misses share one acquisition', async () => {
    let calls = 0;
    const d = deferred<AccessToken>();
    const deps = makeDeps(() => {
      calls++;
      return d.promise;
    });
    const all = Promise.all(Array.from({ length: 10 }, () => ensureAccessToken(deps)));
    await new Promise((r) => setTimeout(r, 0));
    d.resolve({ value: 'shared', expiresAt: new Date(0) });
    expect(await all).toEqual(Array.from({ length: 10 }, () => 'shared'));
    expect(calls).toBe(1);
    expect(deps.inflight.size).toBe(0);
  });

  it('a failed acquisition is shared, then cleared so the next call retries', async () => {
    let calls = 0;
    const deps = makeDeps(() => {
      calls++;
      return calls === 1
        ? Promise.reject(new Error('boom'))
        : Promise.resolve({ value: 'second', expiresAt: new Date(0) });
    });
    const results = await Promise.allSettled([ensureAccessToken(deps), ensureAccessToken(deps)]);
    expect(results.map((r) => r.status)).toEqual(['rejected', 'rejected']);
    expect(calls).toBe(1);
    expect(await ensureAccessToken(deps)).toBe('second');
    expect(calls).toBe(2);
  });

  it('a waiter that aborts rejects with its AbortError without cancelling the shared acquisition', async () => {
    const d = deferred<AccessToken>();
    const deps = makeDeps(() => d.promise);
    const ctrl = new AbortController();
    const aborted = ensureAccessToken(deps, ctrl.signal);
    const other = ensureAccessToken(deps);
    await new Promise((r) => setTimeout(r, 0));
    ctrl.abort();
    await expect(aborted).rejects.toMatchObject({ name: 'AbortError' });
    d.resolve({ value: 'shared', expiresAt: new Date(0) });
    expect(await other).toBe('shared');
  });

  it('keeps single-flight state per inflight map (per transport instance)', async () => {
    let calls = 0;
    const acquire = (): Promise<AccessToken> => {
      calls++;
      return new Promise((r) => setTimeout(() => r({ value: 't', expiresAt: new Date(0) }), 0));
    };
    const a = makeDeps(acquire);
    const b = makeDeps(acquire);
    await Promise.all([ensureAccessToken(a), ensureAccessToken(b)]);
    expect(calls).toBe(2);
  });
});

import { describe, expect, it } from 'vitest';
import type { AccessToken, Clock, TokenCache } from '../../src/application/ports.js';
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

describe('ensureAccessToken with a deserialising token cache', () => {
  const coldAcquire =
    (counter: { calls: number }): EnsureAccessTokenDeps['acquire'] =>
    () => {
      counter.calls++;
      return Promise.resolve({ value: 'fresh', expiresAt: new Date(0) });
    };

  it('accepts an ISO-string expiresAt (e.g. from JSON) and reuses a still-valid token', async () => {
    const counter = { calls: 0 };
    const deps = makeDeps(coldAcquire(counter));
    const iso = new Date(NOW.getTime() + 5 * 24 * 60 * MIN).toISOString();
    await deps.tokenCache.set(deps.cacheKey, {
      value: 'cached',
      expiresAt: iso as unknown as Date,
    });
    expect(await ensureAccessToken(deps)).toBe('cached');
    expect(counter.calls).toBe(0);
  });

  it('accepts an epoch-millis number expiresAt', async () => {
    const counter = { calls: 0 };
    const deps = makeDeps(coldAcquire(counter));
    await deps.tokenCache.set(deps.cacheKey, {
      value: 'cached',
      expiresAt: (NOW.getTime() + 5 * 24 * 60 * MIN) as unknown as Date,
    });
    expect(await ensureAccessToken(deps)).toBe('cached');
    expect(counter.calls).toBe(0);
  });

  it('treats an unparseable expiresAt as expired and re-acquires', async () => {
    const counter = { calls: 0 };
    const deps = makeDeps(coldAcquire(counter));
    await deps.tokenCache.set(deps.cacheKey, {
      value: 'cached',
      expiresAt: 'not-a-date' as unknown as Date,
    });
    expect(await ensureAccessToken(deps)).toBe('fresh');
    expect(counter.calls).toBe(1);
  });
});

describe('ensureAccessToken with a slow distributed cache', () => {
  it('10 concurrent cold calls share one acquisition when get() resolves after a macrotask', async () => {
    const store = new Map<string, AccessToken>();
    // Distributed-cache shape: the value is read when the request is sent, the answer arrives later.
    const slowCache: TokenCache = {
      get: (key) => {
        const snapshot = store.get(key);
        return new Promise((resolve) => setTimeout(() => resolve(snapshot), 0));
      },
      set: (key, token) => {
        store.set(key, token);
        return Promise.resolve();
      },
      delete: (key) => {
        store.delete(key);
        return Promise.resolve();
      },
    };
    let calls = 0;
    const deps: EnsureAccessTokenDeps = {
      ...makeDeps(() => {
        calls++;
        return Promise.resolve({ value: `fresh-${calls}`, expiresAt: new Date(0) });
      }),
      tokenCache: slowCache,
    };
    const tokens = await Promise.all(Array.from({ length: 10 }, () => ensureAccessToken(deps)));
    expect(calls).toBe(1);
    expect(new Set(tokens)).toEqual(new Set(['fresh-1']));
  });

  it('never hands back the 401-rejected token from a resolution that read before eviction', async () => {
    const store = new Map<string, AccessToken>();
    const slowCache: TokenCache = {
      get: (key) => {
        const snapshot = store.get(key);
        return new Promise((resolve) => setTimeout(() => resolve(snapshot), 0));
      },
      set: (key, token) => {
        store.set(key, token);
        return Promise.resolve();
      },
      delete: (key) => {
        store.delete(key);
        return Promise.resolve();
      },
    };
    const deps: EnsureAccessTokenDeps = {
      ...makeDeps(() => Promise.resolve({ value: 'fresh', expiresAt: new Date(0) })),
      tokenCache: slowCache,
    };
    store.set(deps.cacheKey, { value: 'old', expiresAt: new Date(NOW.getTime() + 24 * 60 * MIN) });
    const earlier = ensureAccessToken(deps); // reads 'old' before the eviction below
    await slowCache.delete(deps.cacheKey);
    const refreshed = ensureAccessToken(deps, undefined, 'old');
    expect(await earlier).toBe('old');
    expect(await refreshed).toBe('fresh');
  });
});

describe('ensureAccessToken when the cache read fails', () => {
  const failingThenWorking = (
    boom: Error,
  ): { cache: TokenCache; reads: () => number; gate: ReturnType<typeof deferred<undefined>> } => {
    const gate = deferred<undefined>();
    let reads = 0;
    const cache: TokenCache = {
      get: () => {
        reads++;
        return reads === 1
          ? gate.promise.then(() => Promise.reject(boom))
          : Promise.resolve(undefined);
      },
      set: () => Promise.resolve(),
      delete: () => Promise.resolve(),
    };
    return { cache, reads: () => reads, gate };
  };

  it('every joined waiter gets the raw cache error (unwrapped), the entry is cleared, the next call retries', async () => {
    const boom = new Error('cache down');
    const { cache, reads, gate } = failingThenWorking(boom);
    const deps: EnsureAccessTokenDeps = {
      ...makeDeps(() => Promise.resolve({ value: 'fresh', expiresAt: new Date(0) })),
      tokenCache: cache,
    };
    const waiters = [ensureAccessToken(deps), ensureAccessToken(deps), ensureAccessToken(deps)];
    await new Promise((r) => setTimeout(r, 0));
    expect(deps.inflight.size).toBe(1);
    gate.resolve(undefined);
    const results = await Promise.allSettled(waiters);
    for (const r of results) {
      expect(r.status).toBe('rejected');
      expect((r as PromiseRejectedResult).reason).toBe(boom);
    }
    expect(reads()).toBe(1);
    expect(deps.inflight.size).toBe(0);
    expect(await ensureAccessToken(deps)).toBe('fresh');
    expect(reads()).toBe(2);
  });

  it('one waiter aborting during a slow shared cache read rejects alone; the others get the token', async () => {
    const gate = deferred<AccessToken | undefined>();
    let reads = 0;
    const cache: TokenCache = {
      get: () => {
        reads++;
        return gate.promise;
      },
      set: () => Promise.resolve(),
      delete: () => Promise.resolve(),
    };
    const deps: EnsureAccessTokenDeps = {
      ...makeDeps(() => Promise.resolve({ value: 'fresh', expiresAt: new Date(0) })),
      tokenCache: cache,
    };
    const ctrl = new AbortController();
    const aborting = ensureAccessToken(deps, ctrl.signal);
    const others = [ensureAccessToken(deps), ensureAccessToken(deps)];
    await new Promise((r) => setTimeout(r, 0));
    ctrl.abort();
    await expect(aborting).rejects.toMatchObject({ name: 'AbortError' });
    gate.resolve({ value: 'cached', expiresAt: new Date(NOW.getTime() + 24 * 60 * MIN) });
    expect(await Promise.all(others)).toEqual(['cached', 'cached']);
    expect(reads).toBe(1);
  });
});

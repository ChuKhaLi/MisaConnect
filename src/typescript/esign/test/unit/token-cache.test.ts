import { describe, expect, it } from 'vitest';
import type { AccessToken } from '../../src/application/ports.js';
import { createInMemoryTokenCache } from '../../src/infrastructure/defaults.js';

const token = (value: string): AccessToken => ({
  value,
  refreshToken: 'rt-' + value,
  expiresAt: new Date('2026-11-07T10:00:00Z'),
  userId: 'u',
  username: 'alice',
});

describe('in-memory token cache deleteIfMatches', () => {
  it('removes the entry when its token value matches', async () => {
    const cache = createInMemoryTokenCache();
    await cache.set('k', token('T1'));
    await cache.deleteIfMatches?.('k', 'T1');
    expect(await cache.get('k')).toBeUndefined();
  });

  it('keeps an entry that holds a different token', async () => {
    const cache = createInMemoryTokenCache();
    await cache.set('k', token('T9'));
    await cache.deleteIfMatches?.('k', 'T1');
    expect((await cache.get('k'))?.value).toBe('T9');
  });

  it('is a no-op for a missing key', async () => {
    const cache = createInMemoryTokenCache();
    await cache.deleteIfMatches?.('k', 'T1');
    expect(await cache.get('k')).toBeUndefined();
  });

  it('implements the optional method', () => {
    expect(typeof createInMemoryTokenCache().deleteIfMatches).toBe('function');
  });
});

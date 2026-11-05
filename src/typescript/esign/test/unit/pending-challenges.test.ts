import { describe, expect, it } from 'vitest';
import { createPendingChallengeSet, MAX_PENDING_CHALLENGES } from '../../src/client-internal.js';

describe('pending OTP challenge set', () => {
  it('is capped at 1000 principals by default, evicting the oldest', () => {
    expect(MAX_PENDING_CHALLENGES).toBe(1000);
    const set = createPendingChallengeSet();
    for (let i = 0; i < 1500; i++) set.add(`p${i}`);
    expect(set.size).toBe(1000);
    expect(set.has('p0')).toBe(false);
    expect(set.has('p499')).toBe(false);
    expect(set.has('p500')).toBe(true);
    expect(set.has('p1499')).toBe(true);
  });

  it('re-adding a principal makes it the newest, so it survives eviction', () => {
    const set = createPendingChallengeSet(3);
    set.add('a');
    set.add('b');
    set.add('c');
    set.add('a'); // a is now newest; b is oldest
    set.add('d');
    expect(set.has('a')).toBe(true);
    expect(set.has('b')).toBe(false);
    expect(set.has('c')).toBe(true);
    expect(set.has('d')).toBe(true);
    expect(set.size).toBe(3);
  });

  it('delete removes only that principal', () => {
    const set = createPendingChallengeSet(3);
    set.add('a');
    set.add('b');
    set.delete('a');
    expect(set.has('a')).toBe(false);
    expect(set.has('b')).toBe(true);
    expect(set.size).toBe(1);
  });
});

import type { AccessToken, Clock, RefIdGenerator, TokenCache } from '../application/ports.js';

export function createInMemoryTokenCache(): TokenCache {
  const store = new Map<string, AccessToken>();
  return {
    get: (key) => Promise.resolve(store.get(key)),
    set: (key, token) => {
      store.set(key, token);
      return Promise.resolve();
    },
    delete: (key) => {
      store.delete(key);
      return Promise.resolve();
    },
  };
}

export const uuidRefIdGenerator: RefIdGenerator = () => crypto.randomUUID();

export const systemClock: Clock = { now: () => new Date() };

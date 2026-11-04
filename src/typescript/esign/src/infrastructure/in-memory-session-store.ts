import type { Clock, SigningSession, SigningSessionStore } from '../application/ports.js';
import { UnknownTransactionException } from '../domain/errors.js';
import { systemClock } from './defaults.js';

interface Entry {
  session: SigningSession;
  /** Tail of the finalize-lock chain: resolves when the last queued holder releases. */
  tail: Promise<void>;
}

const keyOf = (clientId: string, transactionId: string): string => `${clientId}\0${transactionId}`;

/**
 * Default `SigningSessionStore`: a process-local map keyed by (clientId, transactionId). TTL is
 * enforced lazily on `get` (an expired entry is removed then). The finalize lock is a per-entry
 * promise chain. Sessions are copied in and out, so callers never share mutable state with it.
 */
export function createInMemorySigningSessionStore(clock: Clock = systemClock): SigningSessionStore {
  const entries = new Map<string, Entry>();

  function live(clientId: string, transactionId: string): Entry | undefined {
    const key = keyOf(clientId, transactionId);
    const entry = entries.get(key);
    if (entry === undefined) return undefined;
    const expiresAt = Date.parse(entry.session.createdAt) + entry.session.ttlMs;
    if (!(clock.now().getTime() < expiresAt)) {
      entries.delete(key);
      return undefined;
    }
    return entry;
  }

  return {
    register(s) {
      entries.set(keyOf(s.clientId, s.transactionId), {
        session: structuredClone(s),
        tail: Promise.resolve(),
      });
      return Promise.resolve();
    },

    get(clientId, transactionId) {
      const entry = live(clientId, transactionId);
      return Promise.resolve(entry === undefined ? undefined : structuredClone(entry.session));
    },

    recordMessageId(clientId, transactionId, messageId) {
      const entry = entries.get(keyOf(clientId, transactionId));
      if (entry !== undefined && !entry.session.observedMessageIds.includes(messageId)) {
        entry.session.observedMessageIds.push(messageId);
      }
      return Promise.resolve();
    },

    cacheSuccess(clientId, transactionId, ack, signedBase64) {
      const entry = entries.get(keyOf(clientId, transactionId));
      if (entry !== undefined) {
        entry.session.cachedSuccess = { ack: { ...ack }, signed: signedBase64 };
      }
      return Promise.resolve();
    },

    async withFinalizeLock(clientId, transactionId, fn) {
      const entry = live(clientId, transactionId);
      if (entry === undefined) {
        throw new UnknownTransactionException({
          correlationId: 'session-store',
          transactionId,
          detail:
            'No session is registered for this transaction; cannot acquire the finalize lock.',
        });
      }
      const previous = entry.tail;
      let release!: () => void;
      entry.tail = new Promise<void>((resolve) => (release = resolve));
      await previous;
      try {
        return await fn();
      } finally {
        release();
      }
    },
  };
}

using System.Collections.Concurrent;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Infrastructure.Sessions;

internal sealed class InMemorySigningSessionStore : ISigningSessionStore, IFinalizeLockOwner
{
    private readonly ConcurrentDictionary<(string ClientId, string TransactionId), SessionEntry> _store = new();
    private readonly ISystemClock _clock;

    public InMemorySigningSessionStore(ISystemClock clock)
    {
        _clock = clock;
    }

    public ValueTask RegisterAsync(SigningSession session, CancellationToken ct)
    {
        _store[(session.ClientId, session.TransactionId)] = new SessionEntry(session);
        return ValueTask.CompletedTask;
    }

    public ValueTask<SigningSession?> TryGetByTransactionIdAsync(string clientId, string transactionId, CancellationToken ct)
    {
        var key = (clientId, transactionId);
        if (!_store.TryGetValue(key, out var entry))
        {
            return ValueTask.FromResult<SigningSession?>(null);
        }
        if (_clock.UtcNow >= entry.Record.CreatedAtUtc + entry.Record.Ttl)
        {
            _store.TryRemove(key, out _);
            return ValueTask.FromResult<SigningSession?>(null);
        }
        return ValueTask.FromResult<SigningSession?>(entry.Record);
    }

    public ValueTask RecordObservedMessageIdAsync(string clientId, string transactionId, string messageId, CancellationToken ct)
    {
        if (!_store.TryGetValue((clientId, transactionId), out var entry))
        {
            return ValueTask.CompletedTask;
        }
        lock (entry.WriteLock)
        {
            var current = entry.Record;
            var observed = new HashSet<string>(current.ObservedMessageIds, StringComparer.Ordinal) { messageId };
            entry.Record = current with { ObservedMessageIds = observed };
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask CacheSuccessAsync(string clientId, string transactionId, string triggeringMessageId, WebhookAck ack, byte[] signedBytes, CancellationToken ct)
    {
        if (!_store.TryGetValue((clientId, transactionId), out var entry))
        {
            return ValueTask.CompletedTask;
        }
        lock (entry.WriteLock)
        {
            var cached = new SigningSessionCachedSuccess(triggeringMessageId, ack, signedBytes);
            entry.Record = entry.Record with { CachedSuccess = cached };
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask<IAsyncDisposable> AcquireFinalizeLockAsync(string clientId, string transactionId, CancellationToken ct)
    {
        if (!_store.TryGetValue((clientId, transactionId), out var entry))
        {
            throw new InvalidOperationException(
                $"Cannot acquire finalize lock — no session registered for clientId='{clientId}', transactionId='{transactionId}'.");
        }
        await entry.FinalizeLock.WaitAsync(ct).ConfigureAwait(false);
        return new SemaphoreRelease(entry.FinalizeLock);
    }

    private sealed class SessionEntry
    {
        public SigningSession Record;
        public readonly SemaphoreSlim FinalizeLock = new(1, 1);
        public readonly object WriteLock = new();

        public SessionEntry(SigningSession record)
        {
            Record = record;
        }
    }

    private sealed class SemaphoreRelease : IAsyncDisposable
    {
        private readonly SemaphoreSlim _sem;
        private int _released;

        public SemaphoreRelease(SemaphoreSlim sem) { _sem = sem; }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _sem.Release();
            }
            return ValueTask.CompletedTask;
        }
    }
}

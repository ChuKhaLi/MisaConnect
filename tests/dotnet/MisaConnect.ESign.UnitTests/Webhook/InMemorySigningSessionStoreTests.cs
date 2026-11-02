using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Webhook;
using MisaConnect.ESign.Infrastructure.Sessions;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Webhook;

public class InMemorySigningSessionStoreTests
{
    private static SigningSession Sample(string clientId = "c", string txId = "tx", DocumentFormat format = DocumentFormat.Pdf)
    {
        var hash = new PdfHashOutput(
            DocumentId: "doc-1",
            DocumentBytes: "AAA",
            DocumentHash: "BBB",
            Sh: "CC",
            SignatureName: "n",
            Digest: "DD");
        return new SigningSession(
            ClientId: clientId,
            TransactionId: txId,
            Format: format,
            HashPayload: new PerFormatHashPayload.Pdf(hash),
            RecordedDocumentIds: new[] { "doc-1" },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Ttl: TimeSpan.FromMinutes(5),
            ObservedMessageIds: new HashSet<string>(),
            CachedSuccess: null);
    }

    [Fact]
    public async Task Register_then_TryGet_returns_the_session()
    {
        var store = new InMemorySigningSessionStore(new FakeClock(DateTimeOffset.UtcNow));
        var session = Sample();

        await store.RegisterAsync(session, CancellationToken.None);
        var retrieved = await store.TryGetByTransactionIdAsync(session.ClientId, session.TransactionId, CancellationToken.None);

        Assert.NotNull(retrieved);
        Assert.Equal(session.TransactionId, retrieved!.TransactionId);
    }

    [Fact]
    public async Task TryGet_after_Ttl_elapsed_returns_null_and_evicts()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        var session = Sample();

        await store.RegisterAsync(session, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(10));

        var retrieved = await store.TryGetByTransactionIdAsync(session.ClientId, session.TransactionId, CancellationToken.None);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RecordObservedMessageId_appends_to_set()
    {
        var store = new InMemorySigningSessionStore(new FakeClock(DateTimeOffset.UtcNow));
        var session = Sample();
        await store.RegisterAsync(session, CancellationToken.None);

        await store.RecordObservedMessageIdAsync(session.ClientId, session.TransactionId, "m1", CancellationToken.None);
        await store.RecordObservedMessageIdAsync(session.ClientId, session.TransactionId, "m2", CancellationToken.None);

        var refreshed = await store.TryGetByTransactionIdAsync(session.ClientId, session.TransactionId, CancellationToken.None);
        Assert.NotNull(refreshed);
        Assert.Contains("m1", refreshed!.ObservedMessageIds);
        Assert.Contains("m2", refreshed.ObservedMessageIds);
    }

    [Fact]
    public async Task CacheSuccess_stores_signed_bytes_and_ack()
    {
        var store = new InMemorySigningSessionStore(new FakeClock(DateTimeOffset.UtcNow));
        var session = Sample();
        await store.RegisterAsync(session, CancellationToken.None);

        var ack = WebhookAck.Success("0");
        var bytes = new byte[] { 1, 2, 3 };
        await store.CacheSuccessAsync(session.ClientId, session.TransactionId, "m1", ack, bytes, CancellationToken.None);

        var refreshed = await store.TryGetByTransactionIdAsync(session.ClientId, session.TransactionId, CancellationToken.None);
        Assert.NotNull(refreshed);
        Assert.NotNull(refreshed!.CachedSuccess);
        Assert.Equal(ack, refreshed.CachedSuccess!.Ack);
        Assert.Equal(bytes, refreshed.CachedSuccess.SignedBytes);
    }

    [Fact]
    public async Task FinalizeLock_serializes_concurrent_callers()
    {
        var store = new InMemorySigningSessionStore(new FakeClock(DateTimeOffset.UtcNow));
        var session = Sample();
        await store.RegisterAsync(session, CancellationToken.None);

        await using var first = await store.AcquireFinalizeLockAsync(session.ClientId, session.TransactionId, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var second = store.AcquireFinalizeLockAsync(session.ClientId, session.TransactionId, cts.Token).AsTask();
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => second);
    }
}

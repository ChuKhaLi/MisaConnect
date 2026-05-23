using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// Bookkeeping store for in-flight webhook-mode signing sessions.
///
/// Adapters MUST guarantee that for any <c>(clientId, transactionId)</c> pair,
/// at most one in-flight <see cref="CacheSuccessAsync"/> call is permitted at a
/// time — the <c>HandleWebhook</c> orchestrator relies on this to enforce
/// single-flight finalize (FR-078). The in-memory default uses a per-key
/// <c>SemaphoreSlim</c>; distributed adapters use their native locking
/// primitives (Redlock, <c>SELECT ... FOR UPDATE</c>, etc.).
///
/// Failure ACKs MUST NOT be cached (FR-081); there is no
/// <c>CacheFailureAsync</c> method.
/// </summary>
public interface ISigningSessionStore
{
    ValueTask RegisterAsync(SigningSession session, CancellationToken ct);

    ValueTask<SigningSession?> TryGetByTransactionIdAsync(string clientId, string transactionId, CancellationToken ct);

    ValueTask RecordObservedMessageIdAsync(string clientId, string transactionId, string messageId, CancellationToken ct);

    ValueTask CacheSuccessAsync(string clientId, string transactionId, string triggeringMessageId, WebhookAck ack, byte[] signedBytes, CancellationToken ct);
}

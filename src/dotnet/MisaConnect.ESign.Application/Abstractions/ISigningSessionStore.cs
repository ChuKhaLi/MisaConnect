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

    /// <summary>
    /// Records the terminal (FAILED/CANCELLED) state MISA confirmed for the transaction
    /// as <see cref="SigningSession.CachedTerminal"/>. The first write wins; later calls
    /// for the same <c>(clientId, transactionId)</c> leave the recorded state unchanged.
    /// </summary>
    /// <remarks>
    /// The default implementation records nothing, so a store written for an earlier
    /// version keeps working: terminal outcomes are then confirmed with MISA and
    /// delivered on every webhook repeat.
    /// </remarks>
    /// <param name="clientId">The session's MISA <c>clientId</c>.</param>
    /// <param name="transactionId">The MISA transaction id.</param>
    /// <param name="triggeringMessageId">The webhook <c>messageId</c> that recorded the state.</param>
    /// <param name="ack">The ACK returned for that delivery.</param>
    /// <param name="status">The terminal status MISA reported.</param>
    /// <param name="misaErrorCode">MISA's error code for the terminal state, when present.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask CacheTerminalAsync(string clientId, string transactionId, string triggeringMessageId, WebhookAck ack, WebhookStatus status, string? misaErrorCode, CancellationToken ct)
        => ValueTask.CompletedTask;
}

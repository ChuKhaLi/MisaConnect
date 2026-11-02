namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// Single-flight finalize lock for a <c>(clientId, transactionId)</c> pair.
/// In-memory adapter and any distributed adapter implement this in addition to
/// <see cref="ISigningSessionStore"/>. The returned disposable releases the
/// lock when disposed (success or failure path).
/// </summary>
public interface IFinalizeLockOwner
{
    ValueTask<IAsyncDisposable> AcquireFinalizeLockAsync(string clientId, string transactionId, CancellationToken ct);
}

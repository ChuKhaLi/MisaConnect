using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Infrastructure.Credentials;

/// <summary>
/// Resolves the credentials for the current call: an internal flow-scoped override
/// (set via <see cref="Use"/>) wins, otherwise the registered
/// <see cref="IMisaCredentialsAccessor"/> is consulted. Lets the SDK run a
/// sub-flow (e.g. webhook finalize) as a specific principal without touching the
/// consumer's accessor.
/// </summary>
internal sealed class EffectiveCredentials
{
    private static readonly AsyncLocal<MisaCredentials?> Override = new();

    private readonly IMisaCredentialsAccessor _inner;

    public EffectiveCredentials(IMisaCredentialsAccessor inner) => _inner = inner;

    /// <summary>Returns the override when set; otherwise the inner accessor's value.</summary>
    public MisaCredentials Get() => Override.Value ?? _inner.Get();

    /// <summary>Sets the flow-scoped override; dispose restores the previous value.</summary>
    public IDisposable Use(MisaCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var previous = Override.Value;
        Override.Value = credentials;
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly MisaCredentials? _previous;
        public Restore(MisaCredentials? previous) => _previous = previous;
        public void Dispose() => Override.Value = _previous;
    }
}

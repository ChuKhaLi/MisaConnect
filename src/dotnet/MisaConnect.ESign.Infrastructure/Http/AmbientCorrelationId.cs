using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Infrastructure.Http;

/// <summary>
/// Carries the flow's correlation ID across the <c>IHttpClientFactory</c> handler scope.
/// Message handlers are resolved in their own DI scope, so auth calls they make (proactive
/// refresh, 401 refresh, fallback login) would otherwise read that scope's correlation ID.
/// The handlers push the <c>X-Correlation-Id</c> of the request they handle; every
/// Infrastructure reader of the correlation ID prefers it while it is set.
/// </summary>
internal static class AmbientCorrelationId
{
    private static readonly AsyncLocal<string?> s_current = new();

    public static string? Current => s_current.Value;

    /// <summary>Sets <paramref name="correlationId"/> until the returned scope is disposed;
    /// null or empty is a no-op scope.</summary>
    public static IDisposable Push(string? correlationId)
    {
        if (string.IsNullOrEmpty(correlationId))
        {
            return NoopScope.Instance;
        }
        var previous = s_current.Value;
        s_current.Value = correlationId;
        return new RestoreScope(previous);
    }

    /// <summary>Pushes the request's own <c>X-Correlation-Id</c> header, if any.</summary>
    public static IDisposable PushFrom(HttpRequestMessage request) =>
        Push(request.Headers.TryGetValues(ESign.MisaESignWireClient.CorrelationIdHeader, out var values)
            ? values.FirstOrDefault()
            : null);

    public static string Resolve(ICorrelationIdAccessor accessor) => Current ?? accessor.Current;

    /// <summary>Wraps <paramref name="accessor"/> so <see cref="ICorrelationIdAccessor.Current"/>
    /// prefers the ambient value.</summary>
    public static ICorrelationIdAccessor Wrap(ICorrelationIdAccessor accessor) =>
        accessor is AmbientAwareAccessor ? accessor : new AmbientAwareAccessor(accessor);

    private sealed class AmbientAwareAccessor : ICorrelationIdAccessor
    {
        private readonly ICorrelationIdAccessor _inner;
        public AmbientAwareAccessor(ICorrelationIdAccessor inner) { _inner = inner; }
        public string Current => Resolve(_inner);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly string? _previous;
        public RestoreScope(string? previous) { _previous = previous; }
        public void Dispose() => s_current.Value = _previous;
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}

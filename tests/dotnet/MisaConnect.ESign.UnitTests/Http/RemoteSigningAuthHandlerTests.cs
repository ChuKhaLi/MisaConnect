using System.Net;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.Http;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Http;

public class RemoteSigningAuthHandlerTests
{
    private const string Key = "k";
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private sealed class StaticKeySelector : ITokenCacheKeySelector
    {
        public string Compose() => Key;
    }

    private sealed class ScriptedInner : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, Task<HttpStatusCode>> _script;
        public List<string?> Bearers { get; } = new();
        public int Calls { get; private set; }

        public ScriptedInner(Func<HttpRequestMessage, int, Task<HttpStatusCode>> script) { _script = script; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Bearers.Add(request.Headers.TryGetValues("AuthorizationRM", out var v) ? v.Single() : null);
            return new HttpResponseMessage(await _script(request, Calls));
        }
    }

    private sealed class Harness
    {
        public InMemoryTokenCache Cache { get; } = new();
        public StubWireClient Wire { get; } = new();
        public FakeClock Clock { get; } = new(T0);
        public ScriptedInner Inner { get; }
        public HttpMessageInvoker Invoker { get; }

        public Harness(Func<Harness, HttpRequestMessage, int, Task<HttpStatusCode>> script, string correlation = "handler-cid")
        {
            Inner = new ScriptedInner((r, n) => script(this, r, n));
            var refresh = new RefreshAccessToken(Wire);
            var singleFlight = new SingleFlightRefresh();
            var keySelector = new StaticKeySelector();
            var handler = new RemoteSigningAuthHandler(
                ensureFactory: () => new EnsureAccessToken(
                    Wire, Cache, keySelector, Clock, () => ("alice", "pw"), refresh,
                    (k, f, ct) => singleFlight.RefreshAsync(k, f, ct)),
                refreshFactory: () => refresh,
                cache: Cache,
                keySelector: keySelector,
                singleFlight: singleFlight,
                options: Options.Create(new MisaESignOptions()),
                correlation: new StubCorrelationIdAccessor(correlation),
                clock: Clock)
            {
                InnerHandler = Inner,
            };
            Invoker = new HttpMessageInvoker(handler);
        }

        public Task<HttpResponseMessage> SendAsync(string? correlationId = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://esrm.example/api/v1/certificates");
            if (correlationId is not null)
            {
                req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
            }
            return Invoker.SendAsync(req, CancellationToken.None);
        }

        public Task SeedAsync(string value) => Cache.SetAsync(Key, Token(value), CancellationToken.None);

        public async Task<string?> CachedAsync() => (await Cache.TryGetAsync(Key, CancellationToken.None))?.Value;
    }

    private static AccessToken Token(string value) =>
        new(value, "raw-" + value, "rt-" + value, T0.AddMinutes(60), "u1", "alice");

    private static AuthSession Session(string value) =>
        new("raw-" + value, value, "rt-" + value, T0.AddMinutes(60), "u1", "alice");

    [Fact]
    public async Task Failed_refresh_reuses_fresh_token_stored_by_another_flow()
    {
        var h = new Harness((_, _, n) => Task.FromResult(n == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK));
        await h.SeedAsync("T1");
        h.Wire.OnRefresh = async (_, _) =>
        {
            await h.SeedAsync("T2"); // another flow stores a fresh token meanwhile
            throw new InvalidOperationException("refresh failed");
        };
        h.Wire.OnLogin = (_, _, _) => Task.FromResult(Session("T3"));

        using var response = await h.SendAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, h.Wire.LoginCalls);
        Assert.Equal("Bearer T2", h.Inner.Bearers[1]);
        Assert.Equal("T2", await h.CachedAsync());
    }

    [Fact]
    public async Task Failed_refresh_evicts_the_sent_token_then_logs_in()
    {
        var h = new Harness((_, _, n) => Task.FromResult(n == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK));
        await h.SeedAsync("T1");
        h.Wire.OnRefresh = (_, _) => throw new InvalidOperationException("refresh failed");
        h.Wire.OnLogin = (_, _, _) => Task.FromResult(Session("T3"));

        using var response = await h.SendAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, h.Wire.LoginCalls);
        Assert.Equal("Bearer T3", h.Inner.Bearers[1]);
        Assert.Equal("T3", await h.CachedAsync());
    }

    [Fact]
    public async Task Retry_401_does_not_evict_a_different_token()
    {
        var h = new Harness(async (self, _, n) =>
        {
            if (n == 2)
            {
                await self.SeedAsync("T9"); // another flow stores its token before our retry is rejected
            }
            return HttpStatusCode.Unauthorized;
        });
        await h.SeedAsync("T1");
        h.Wire.OnRefresh = (_, _) => Task.FromResult(Session("T2"));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => h.SendAsync());

        Assert.Equal("T9", await h.CachedAsync());
    }

    [Fact]
    public async Task Auth_calls_inside_handler_see_the_request_correlation_id()
    {
        var h = new Harness((_, _, _) => Task.FromResult(HttpStatusCode.Unauthorized));
        string? seenAtLogin = null;
        h.Wire.OnLogin = (_, _, _) =>
        {
            seenAtLogin = AmbientCorrelationId.Current;
            return Task.FromResult(Session("T1"));
        };
        h.Wire.OnRefresh = (_, _) => Task.FromResult(Session("T2"));

        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(() => h.SendAsync("flow-cid"));

        Assert.Equal("flow-cid", seenAtLogin);
        Assert.Equal("flow-cid", ex.CorrelationId);
        Assert.Null(AmbientCorrelationId.Current);
    }

    [Fact]
    public async Task Retry_401_evicts_the_rejected_token()
    {
        var h = new Harness((_, _, _) => Task.FromResult(HttpStatusCode.Unauthorized));
        await h.SeedAsync("T1");
        h.Wire.OnRefresh = (_, _) => Task.FromResult(Session("T2"));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => h.SendAsync());

        Assert.Null(await h.CachedAsync());
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

// Slice 008 US1/US3 (FR-003/FR-007): with CredentialsMode=Dynamic and a consumer
// accessor that reads a per-call ambient, two signers get distinct logins, distinct
// token-cache keys, and distinct x-clientId/x-clientKey headers — no collision.
public class DynamicCredentialsFakeServerTests
{
    private static readonly AsyncLocal<MisaCredentials?> Ambient = new();

    private sealed class AmbientCredentialsAccessor : IMisaCredentialsAccessor
    {
        public MisaCredentials Get() =>
            Ambient.Value ?? throw new InvalidOperationException("No MISA credentials set for the current call.");
    }

    private static IDisposable Use(MisaCredentials creds)
    {
        var prev = Ambient.Value;
        Ambient.Value = creds;
        return new Pop(prev);
    }

    private sealed class Pop : IDisposable
    {
        private readonly MisaCredentials? _prev;
        public Pop(MisaCredentials? prev) => _prev = prev;
        public void Dispose() => Ambient.Value = _prev;
    }

    private static ServiceProvider BuildDynamic(string baseUrl)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        // Register the consumer accessor BEFORE AddMisaConnectESign so the SDK's
        // TryAddSingleton default yields to it (register-before contract).
        services.AddSingleton<IMisaCredentialsAccessor, AmbientCredentialsAccessor>();
        services.AddMisaConnectESign(o =>
        {
            o.Environment = ESignEnvironment.Sandbox;
            o.BaseUrl = baseUrl;
            o.CredentialsMode = CredentialsMode.Dynamic;
            o.Polling.Interval = TimeSpan.FromMilliseconds(10);
            o.Polling.TotalTimeout = TimeSpan.FromSeconds(5);
            o.TransportRetry.MaxAttempts = 1;
            // ClientId/ClientKey/UserName/Password intentionally left empty.
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Two_signers_get_distinct_logins_keys_and_client_headers()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = BuildDynamic(server.BaseUrl);

        var alice = new MisaCredentials("cid-A", "ckey-A", "alice", "pw-A");
        var bob = new MisaCredentials("cid-B", "ckey-B", "bob", "pw-B");

        using (Use(alice))
        using (var scope = sp.CreateAsyncScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
            await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        }

        using (Use(bob))
        using (var scope = sp.CreateAsyncScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
            await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        }

        // Distinct cache keys ⇒ no token reuse ⇒ two separate logins.
        Assert.Equal(2, server.Calls.Login);

        var logins = server.LoginRequests;
        Assert.Equal(2, logins.Count);

        // Two different login bodies (distinct usernames).
        Assert.Contains(logins, r => r.Body.Contains("alice", StringComparison.Ordinal));
        Assert.Contains(logins, r => r.Body.Contains("bob", StringComparison.Ordinal));

        // Two different x-clientId / x-clientKey header sets on the wire.
        Assert.Contains(logins, r => r.ClientId == "cid-A" && r.ClientKey == "ckey-A");
        Assert.Contains(logins, r => r.ClientId == "cid-B" && r.ClientKey == "ckey-B");
    }

    [Fact]
    public void Token_cache_key_is_isolated_per_signer()
    {
        // No server needed: exercise the registered key selector directly under
        // two ambient scopes and assert the composed keys differ.
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<IMisaCredentialsAccessor, AmbientCredentialsAccessor>();
        services.AddMisaConnectESign(o =>
        {
            o.Environment = ESignEnvironment.Sandbox;
            o.BaseUrl = "https://sandbox.example.com/";
            o.CredentialsMode = CredentialsMode.Dynamic;
        });
        using var sp = services.BuildServiceProvider();
        var selector = sp.GetRequiredService<ITokenCacheKeySelector>();

        string keyA, keyB;
        using (Use(new MisaCredentials("cid-A", "ck", "alice", "pw"))) keyA = selector.Compose();
        using (Use(new MisaCredentials("cid-B", "ck", "bob", "pw"))) keyB = selector.Compose();

        Assert.Equal("alice|cid-A|sandbox.example.com", keyA);
        Assert.Equal("bob|cid-B|sandbox.example.com", keyB);
        Assert.NotEqual(keyA, keyB);
    }
}

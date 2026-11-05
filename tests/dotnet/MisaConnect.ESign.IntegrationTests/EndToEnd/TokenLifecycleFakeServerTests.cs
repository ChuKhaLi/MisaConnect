using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class TokenLifecycleFakeServerTests
{
    private const string LoginBodyWithoutRefreshToken =
        "{\"status\":{\"code\":200,\"error\":false},\"data\":{\"accessToken\":\"raw-token\",\"remoteSigningAccessToken\":\"rs-token\",\"expiresIn\":3600,\"tokenType\":\"Bearer\",\"user\":{\"id\":\"user-id\",\"username\":\"alice\"}}}";

    private static async Task SignAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        Assert.NotNull(result.SignedPdf);
    }

    [Fact]
    public async Task Refreshed_token_is_used_for_the_rest_of_the_flow()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.RevokeInitialBearerAfterCertificates = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        await SignAsync(sp);

        Assert.Equal(1, server.Calls.Refresh);
        var bearers = server.EsrmBearers;
        var firstHash = bearers.ToList().FindIndex(b => b.Route == "hash");
        Assert.True(firstHash >= 0);
        Assert.NotEmpty(bearers.Skip(firstHash + 1));
        Assert.All(bearers.Skip(firstHash + 1), b => Assert.Equal("rs-token-2", b.Bearer));
    }

    [Fact]
    public async Task Failed_refresh_falls_back_to_one_login()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.RevokeInitialBearerAfterCertificates = true;
        server.Configure.Intercept = r => r.Route == "refresh" ? new FakeResponse(500, "{}") : null;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        await SignAsync(sp);

        Assert.Equal(2, server.Calls.Login);
        Assert.Equal(1, server.Calls.Refresh);
        Assert.Equal("rs-token-relogin", server.EsrmBearers[^1].Bearer);
    }

    [Fact]
    public async Task Unparseable_refresh_body_falls_back_to_login()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.RevokeInitialBearerAfterCertificates = true;
        server.Configure.Intercept = r => r.Route == "refresh" ? new FakeResponse(200, "not json") : null;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        await SignAsync(sp);

        Assert.Equal(2, server.Calls.Login);
    }

    [Fact]
    public async Task Already_expired_refresh_result_falls_back_to_login()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.RevokeInitialBearerAfterCertificates = true;
        server.Configure.Intercept = r => r.Route == "refresh"
            ? new FakeResponse(200, "{\"accessToken\":\"a\",\"remoteSigningAccessToken\":\"rs-token-2\",\"refreshToken\":\"r\",\"expiresIn\":0}")
            : null;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        await SignAsync(sp);

        Assert.Equal(2, server.Calls.Login);
        Assert.Equal("rs-token-relogin", server.EsrmBearers[^1].Bearer);
    }

    [Fact]
    public async Task Missing_refresh_token_falls_back_to_login()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.RevokeInitialBearerAfterCertificates = true;
        server.Configure.Intercept = r => r.Route == "login" && server.Calls.Login == 1
            ? new FakeResponse(200, LoginBodyWithoutRefreshToken)
            : null;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        await SignAsync(sp);

        Assert.Equal(0, server.Calls.Refresh);
        Assert.Equal(2, server.Calls.Login);
    }

    [Fact]
    public async Task Second_401_after_fallback_fails_with_Authentication()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.Intercept = r => r.Route switch
        {
            "hash" => new FakeResponse(401, "{}"),
            "refresh" => new FakeResponse(500, "{}"),
            _ => null,
        };
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using (var scope = sp.CreateAsyncScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
            await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
                client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));
        }

        Assert.Equal(2, server.Calls.Login);

        // The failed token must not stay cached.
        var cache = sp.GetRequiredService<ITokenCache>();
        var keySelector = sp.GetRequiredService<ITokenCacheKeySelector>();
        Assert.Null(await cache.TryGetAsync(keySelector.Compose(), CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_401s_cause_one_refresh()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        // Warm up so the initial token is cached and a certificates call has completed;
        // then arm the revocation so both concurrent signs start with the revoked token
        // and hit a 401 together.
        await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        server.Configure.RevokeInitialBearerAfterCertificates = true;

        var results = await Task.WhenAll(
            client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None),
            client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));

        Assert.All(results, r => Assert.NotNull(r.SignedPdf));
        Assert.Equal(1, server.Calls.Refresh);
    }
}

using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// End to end: the raw <c>expiresIn</c> on the login response decides the lifetime of the cached
/// token (lenient read, then the lifetime rule: missing → 300 s, 1–59 → 60 s, explicit as-is).
/// </summary>
public class ExpiresInLifetimeFakeServerTests
{
    private static string LoginBody(string rawExpiresIn) =>
        "{\"status\":{\"code\":200,\"error\":false},\"data\":{\"accessToken\":\"raw-token\",\"remoteSigningAccessToken\":\"rs-token\",\"refreshToken\":\"refresh-token\",\"expiresIn\":"
        + rawExpiresIn
        + ",\"tokenType\":\"Bearer\",\"user\":{\"id\":\"user-id\",\"username\":\"alice\"}}}";

    public static TheoryData<string, int> Cases => new()
    {
        { "3600", 3600 },
        { "\"3600\"", 3600 },
        { "\"abc\"", 300 },
        { "3600.5", 300 },
        { "true", 300 },
        { "{}", 300 },
        { "null", 300 },
        { "\"10\"", 60 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Login_expiresIn_sets_the_cached_token_lifetime(string rawExpiresIn, int expectedSeconds)
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.Intercept = r => r.Route == "login" ? new FakeResponse(200, LoginBody(rawExpiresIn)) : null;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        var before = DateTimeOffset.UtcNow;
        using (var scope = sp.CreateAsyncScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
            var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
            Assert.NotNull(result.SignedPdf); // the odd value never fails the login
        }
        var after = DateTimeOffset.UtcNow;

        var key = sp.GetRequiredService<ITokenCacheKeySelector>().Compose();
        var cached = await sp.GetRequiredService<ITokenCache>().TryGetAsync(key, CancellationToken.None);

        Assert.NotNull(cached);
        Assert.Equal(1, server.Calls.Login);
        Assert.InRange(cached!.ExpiresAtUtc, before.AddSeconds(expectedSeconds), after.AddSeconds(expectedSeconds));
    }
}

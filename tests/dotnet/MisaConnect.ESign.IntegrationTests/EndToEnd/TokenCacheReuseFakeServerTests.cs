using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class TokenCacheReuseFakeServerTests
{
    [Fact]
    public async Task Two_back_to_back_calls_perform_exactly_one_login()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        // Two separate scopes (matches per-request usage)
        using (var scope = sp.CreateAsyncScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
            await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        }
        using (var scope = sp.CreateAsyncScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
            await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        }

        Assert.Equal(1, server.Calls.Login);
        Assert.Equal(0, server.Calls.Refresh);
    }
}

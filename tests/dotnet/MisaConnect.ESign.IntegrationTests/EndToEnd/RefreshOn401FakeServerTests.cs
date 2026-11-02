using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class RefreshOn401FakeServerTests
{
    [Fact]
    public async Task Single_401_triggers_refresh_then_retry_once()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.CertsForce401Once = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.Equal(1, server.Calls.Login);
        Assert.Equal(1, server.Calls.Refresh);
        Assert.Equal(2, server.Calls.Certificates); // first 401, then retry succeeds
        Assert.NotNull(result.SignedPdf);
    }
}

using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// US1 (Defect A + C + B-guard): ESRM calls resolve at the host root regardless
/// of any path in the configured base URL, and carry the remote-signing token.
/// </summary>
public class EsrmRootRoutingFakeServerTests
{
    [Theory]
    [InlineData(false)] // base = bare host
    [InlineData(true)]  // base includes a /webdev/ path segment
    public async Task Cert_list_and_signing_succeed_regardless_of_base_path(bool baseHasWebdevPath)
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        var baseUrl = baseHasWebdevPath ? server.BaseUrl + "webdev/" : server.BaseUrl;
        await using var sp = TestServiceProvider.Build(baseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        // Fake serves ESRM ONLY at the host root, so success proves root routing.
        Assert.NotNull(result.SignedPdf);
        Assert.True(server.Calls.Certificates >= 1);
    }

    [Fact]
    public async Task Esrm_bearer_is_the_remote_signing_access_token()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl + "webdev/");

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        // Login returns remoteSigningAccessToken = "rs-token"; that is what the
        // ESRM AuthorizationRM bearer must carry (not the raw accessToken).
        Assert.Equal("rs-token", server.LastCertificatesBearer);
    }

    [Fact]
    public async Task Refresh_on_401_still_works_with_webdev_base()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.CertsForce401Once = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl + "webdev/");

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.Equal(1, server.Calls.Refresh);
        Assert.Equal(2, server.Calls.Certificates); // first 401, then retry succeeds at root
        Assert.NotNull(result.SignedPdf);
    }
}

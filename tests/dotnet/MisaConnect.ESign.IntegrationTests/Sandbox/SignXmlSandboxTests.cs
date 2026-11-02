using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.IntegrationTests.EndToEnd;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.Sandbox;

public class SignXmlSandboxTests
{
    [SandboxFact]
    public async Task Sandbox_signs_xml_fixture_and_returns_signed_bytes_with_xades_signature()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMisaConnectESign(o =>
        {
            o.Environment = ESignEnvironment.Sandbox;
            o.BaseUrl = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_BASE_URL")!;
            o.ClientId = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_CLIENT_ID")!;
            o.ClientKey = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY")!;
            o.UserName = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_USERNAME")!;
            o.Password = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_PASSWORD")!;
            o.Polling.Interval = TimeSpan.FromSeconds(2);
            o.Polling.TotalTimeout = TimeSpan.FromSeconds(60);
        });
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var result = await client.SignXmlAsync(TestPerFormatFixtures.SampleXmlRequest(), CancellationToken.None);
        Assert.Equal(DocumentFormat.Xml, result.Format);
        Assert.NotNull(result.SignedXml);
        Assert.True(result.SignedXml.Length > 0);
        // The returned bytes should contain XAdES signature markers.
        var text = System.Text.Encoding.UTF8.GetString(result.SignedXml);
        Assert.Contains("Signature", text, StringComparison.OrdinalIgnoreCase);
    }
}

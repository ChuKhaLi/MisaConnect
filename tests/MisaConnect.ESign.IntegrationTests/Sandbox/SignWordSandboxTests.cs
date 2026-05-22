using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.IntegrationTests.EndToEnd;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.Sandbox;

public class SignWordSandboxTests
{
    [SandboxFact]
    public async Task Sandbox_signs_word_fixture_and_returns_signed_ooxml_bytes()
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

        var result = await client.SignWordAsync(TestPerFormatFixtures.SampleWordRequest(), CancellationToken.None);
        Assert.Equal(DocumentFormat.Word, result.Format);
        Assert.NotNull(result.SignedWord);
        Assert.True(result.SignedWord.Length > 0);
        // OOXML files begin with the ZIP header `PK\x03\x04`.
        Assert.Equal(0x50, result.SignedWord[0]);
        Assert.Equal(0x4B, result.SignedWord[1]);
    }
}

using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.IntegrationTests.EndToEnd;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.Sandbox;

public class SignExcelSandboxTests
{
    [SandboxFact]
    public async Task Sandbox_signs_excel_fixture_and_returns_signed_ooxml_bytes()
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

        var result = await client.SignExcelAsync(TestPerFormatFixtures.SampleExcelRequest(), CancellationToken.None);
        Assert.Equal(DocumentFormat.Excel, result.Format);
        Assert.NotNull(result.SignedExcel);
        Assert.True(result.SignedExcel.Length > 0);
        Assert.Equal(0x50, result.SignedExcel[0]);
        Assert.Equal(0x4B, result.SignedExcel[1]);
    }
}

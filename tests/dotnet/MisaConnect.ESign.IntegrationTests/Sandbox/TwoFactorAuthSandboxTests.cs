using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.IntegrationTests.EndToEnd;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.Sandbox;

public class TwoFactorAuthSandboxTests
{
    [SandboxFact(SandboxRequirement.TwoFactorAuth)]
    public async Task Sandbox_account_2fa_enrolled_completes_otp_exchange_and_resumes_signing()
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

        var otp = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER")!;

        try
        {
            await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA)
        {
            await client.SignInWithOtpAsync(otp, OtpDeliveryChannel.SmsOrEmail, false, CancellationToken.None);
            var second = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
            Assert.NotNull(second.SignedPdf);
            return;
        }
    }
}

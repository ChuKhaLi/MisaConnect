using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class OtpProviderTransparentFlowFakeServerTests
{
    private sealed class FakeProvider : IOtpProvider
    {
        public int Calls;
        public Task<OtpSubmission> ProvideAsync(OtpChallenge challenge, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new OtpSubmission("OTP-AUTO", OtpDeliveryChannel.SmsOrEmail, false));
        }
        public Task<OtpResendResult> RequestResendAsync(OtpChallenge challenge, string? language, CancellationToken ct) =>
            Task.FromResult(new OtpResendResult(true, null, null, null, challenge.CorrelationId));
    }

    [Fact]
    public async Task Provider_supplied_otp_drives_sign_pdf_to_completion_without_consumer_seeing_exception()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;

        var provider = new FakeProvider();
        await using var sp = TestServiceProviderWithProvider.Build(server.BaseUrl, provider);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        Assert.NotNull(result.SignedPdf);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, server.Calls.TwoFactorAuth);
    }
}

internal static class TestServiceProviderWithProvider
{
    public static ServiceProvider Build(string baseUrl, IOtpProvider provider)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.None));
        services.AddSingleton(provider);
        services.AddMisaConnectESign(o =>
        {
            o.Environment = MisaConnect.ESign.Infrastructure.Configuration.ESignEnvironment.Sandbox;
            o.BaseUrl = baseUrl;
            o.ClientId = "client-id";
            o.ClientKey = "client-key";
            o.UserName = "alice";
            o.Password = "password";
            o.Polling.Interval = TimeSpan.FromMilliseconds(10);
            o.Polling.TotalTimeout = TimeSpan.FromSeconds(5);
            o.TransportRetry.MaxAttempts = 1;
        });
        return services.BuildServiceProvider();
    }
}

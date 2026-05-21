using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class TwoFactorAuthHappyPathFakeServerTests
{
    [Fact]
    public async Task Catch_then_supply_otp_then_resume_signing()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;

        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var first = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));
        Assert.True(first.Requires2FA);
        Assert.Equal("alice", first.Username);

        await client.SignInWithOtpAsync("123456", OtpDeliveryChannel.SmsOrEmail, true, CancellationToken.None);

        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        Assert.NotNull(result.SignedPdf);

        Assert.Equal(1, server.Calls.TwoFactorAuth);

        var loginCallsBeforeSecond = server.Calls.Login;
        var twoFactorBeforeSecond = server.Calls.TwoFactorAuth;
        var third = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        Assert.NotNull(third.SignedPdf);
        Assert.Equal(loginCallsBeforeSecond, server.Calls.Login);
        Assert.Equal(twoFactorBeforeSecond, server.Calls.TwoFactorAuth);

        Assert.NotEmpty(server.TwoFactorBodies);
        var body = server.TwoFactorBodies[0];
        Assert.False(body.HasAuthorizationRm);
        Assert.Contains("\"userName\":\"alice\"", body.Body);
        Assert.Contains("\"code\":\"123456\"", body.Body);
        Assert.Contains("\"otpType\":0", body.Body);
        Assert.Contains("\"remember\":true", body.Body);
    }
}

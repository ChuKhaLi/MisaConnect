using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// US3: login/two-factor location follows the environment default (Sandbox ⇒
/// /webdev/) and the AuthUnderWebdev override, while ESRM stays at the host root.
/// </summary>
public class EsrmAuthTopologyFakeServerTests
{
    private const string LoginUnderWebdev = "/webdev/api/auth/api/v1/auth/login-api";
    private const string LoginAtRoot = "/api/auth/api/v1/auth/login-api";
    private const string TwoFactorUnderWebdev = "/webdev/api/auth/api/v1/auth/two-factor-auth";
    private const string TwoFactorAtRoot = "/api/auth/api/v1/auth/two-factor-auth";

    [Fact]
    public async Task Sandbox_default_serves_login_under_webdev()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl); // Environment = Sandbox

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.Equal(LoginUnderWebdev, server.LastLoginPath);
    }

    [Fact]
    public async Task Override_false_serves_login_at_host_root()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl, o => o.AuthUnderWebdev = false);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.Equal(LoginAtRoot, server.LastLoginPath);
    }

    [Fact]
    public async Task Override_true_serves_login_under_webdev()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl, o => o.AuthUnderWebdev = true);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.Equal(LoginUnderWebdev, server.LastLoginPath);
    }

    [Fact]
    public async Task Sandbox_default_serves_two_factor_under_webdev()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl); // Environment = Sandbox

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));
        await client.SignInWithOtpAsync("123456", OtpDeliveryChannel.SmsOrEmail, true, CancellationToken.None);

        Assert.Equal(TwoFactorUnderWebdev, server.LastTwoFactorPath);
    }

    [Fact]
    public async Task Override_false_serves_two_factor_at_host_root()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl, o => o.AuthUnderWebdev = false);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));
        await client.SignInWithOtpAsync("123456", OtpDeliveryChannel.SmsOrEmail, true, CancellationToken.None);

        Assert.Equal(TwoFactorAtRoot, server.LastTwoFactorPath);
    }
}

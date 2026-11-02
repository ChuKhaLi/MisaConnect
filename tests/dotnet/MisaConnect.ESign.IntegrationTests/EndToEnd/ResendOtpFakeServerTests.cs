using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class ResendOtpFakeServerTests
{
    private static async Task<IMisaESignClient> EstablishPendingChallenge(FakeMisaESignServer server, ServiceProvider sp)
    {
        var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));
        _ = server;
        return client;
    }

    [Fact]
    public async Task Default_language_is_en_us_when_caller_passes_null()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;
        server.Configure.ResendOtpMode = ResendOtpResponseMode.Success200;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        var client = await EstablishPendingChallenge(server, sp);

        var result = await client.ResendOtpAsync(language: null, ct: CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(1, server.Calls.ResendOtp);
        Assert.Contains("\"language\":\"en-US\"", server.ResendBodies[0].Body);
        Assert.Contains("\"userName\":\"alice\"", server.ResendBodies[0].Body);
        Assert.False(server.ResendBodies[0].HasAuthorizationRm);
    }

    [Fact]
    public async Task Caller_override_language_threads_verbatim()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;
        server.Configure.ResendOtpMode = ResendOtpResponseMode.Success200;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        var client = await EstablishPendingChallenge(server, sp);

        await client.ResendOtpAsync(language: "vi-VN", ct: CancellationToken.None);
        Assert.Contains("\"language\":\"vi-VN\"", server.ResendBodies[0].Body);
    }

    [Fact]
    public async Task Typed_failure_200_surfaces_typed_dto_without_throwing()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;
        server.Configure.ResendOtpMode = ResendOtpResponseMode.TypedFailure200;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        var client = await EstablishPendingChallenge(server, sp);

        var result = await client.ResendOtpAsync(language: null, ct: CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("RateLimited", result.RawCode);
    }

    [Fact]
    public async Task Typed_failure_4xx_surfaces_typed_dto_without_throwing()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;
        server.Configure.ResendOtpMode = ResendOtpResponseMode.TypedFailure4xx;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        var client = await EstablishPendingChallenge(server, sp);

        var result = await client.ResendOtpAsync(language: null, ct: CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("BadRequest", result.RawCode);
    }

    [Fact]
    public async Task Transport_500_throws_transport_exception()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;
        server.Configure.ResendOtpMode = ResendOtpResponseMode.Transport500;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        var client = await EstablishPendingChallenge(server, sp);

        await Assert.ThrowsAsync<ESignTransportException>(() =>
            client.ResendOtpAsync(language: null, ct: CancellationToken.None));
    }
}

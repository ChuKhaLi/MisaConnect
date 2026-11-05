using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// FR-007 / FR-008: MISA's <c>error</c> flag may be a boolean, a string or null, and the
/// two-factor-required code <c>122</c> may arrive in the nested or the flat error shape
/// on a 200 or a 400.
/// </summary>
public class LoginErrorShapesFakeServerTests
{
    private const string LoginBodyNullError =
        "{\"status\":{\"code\":200,\"error\":null},\"data\":{\"accessToken\":\"raw-token\",\"remoteSigningAccessToken\":\"rs-token\",\"refreshToken\":\"refresh-token\",\"expiresIn\":3600,\"tokenType\":\"Bearer\",\"user\":{\"id\":\"user-id\",\"username\":\"alice\"}}}";

    [Theory]
    [InlineData(200, "{\"error\":\"true\",\"errorCode\":\"122\"}")]
    [InlineData(200, "{\"status\":{\"code\":200,\"error\":\"true\",\"errorCode\":\"122\"}}")]
    [InlineData(400, "{\"status\":{\"code\":400,\"error\":true,\"errorCode\":\"122\"}}")]
    [InlineData(400, "{\"error\":true,\"errorCode\":\"122\"}")]
    public async Task Code_122_is_detected(int status, string body)
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.Intercept = r => r.Route == "login" ? new FakeResponse(status, body) : null;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));

        Assert.True(ex.Requires2FA);
    }

    [Fact]
    public async Task Null_error_in_login_status_keeps_the_response()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.Intercept = r => r.Route == "login" ? new FakeResponse(200, LoginBodyNullError) : null;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.NotNull(result.SignedPdf);
    }

    // Uses Signing/hash: the hash route rewrites unknown codes (DocumentInvalid -> InvalidDocument),
    // whereas the signing route keeps the raw code verbatim.
    [Fact]
    public async Task String_error_keeps_the_error_code()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.Intercept = r => r.Route == "signing-hash"
            ? new FakeResponse(400, "{\"error\":\"true\",\"errorCode\":\"DocumentInvalid\"}")
            : null;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var ex = await Assert.ThrowsAsync<SignRejectedException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));

        Assert.Equal("DocumentInvalid", ex.RawCode);
    }
}

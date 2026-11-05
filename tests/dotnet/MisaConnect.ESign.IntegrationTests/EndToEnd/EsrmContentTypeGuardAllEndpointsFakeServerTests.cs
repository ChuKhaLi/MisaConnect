using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// FR-006: the content-type guard covers every ESRM JSON response (hash, Signing/hash,
/// Signing/status, attachment), not only the certificate list. Auth calls stay lenient.
/// </summary>
public class EsrmContentTypeGuardAllEndpointsFakeServerTests
{
    private const string LoginBody =
        "{\"status\":{\"code\":200,\"error\":false},\"data\":{\"accessToken\":\"raw-token\",\"remoteSigningAccessToken\":\"rs-token\",\"refreshToken\":\"refresh-token\",\"expiresIn\":3600,\"tokenType\":\"Bearer\",\"user\":{\"id\":\"user-id\",\"username\":\"alice\"}}}";

    private const string StatusSuccessBody =
        "{\"status\":\"SUCCESS\",\"transactionId\":\"tx-fake-1\",\"signatures\":[{\"documentId\":\"doc-1\",\"signature\":\"SIGNATURE-BYTES\"}]}";

    public static TheoryData<string> EsrmRoutes => new() { "hash", "signing-hash", "signing-status", "attachment" };

    private static async Task<ESignGeneralException> SignExpectingFailureAsync(FakeMisaESignServer server)
    {
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        return await Assert.ThrowsAsync<ESignGeneralException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(EsrmRoutes))]
    public async Task Html_success_is_UnexpectedContentType(string route)
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.Intercept = r => r.Route == route
            ? new FakeResponse(200, "<html><body>spa</body></html>", "text/html")
            : null;

        var ex = await SignExpectingFailureAsync(server);

        Assert.Equal("UnexpectedContentType", ex.RawCode);
        Assert.Contains("text/html", ex.Message);
        Assert.DoesNotContain("<html", ex.Message);
    }

    [Theory]
    [MemberData(nameof(EsrmRoutes))]
    public async Task Unparseable_json_is_UnparseableResponse(string route)
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.Intercept = r => r.Route == route
            ? new FakeResponse(200, "{not json", "application/json")
            : null;

        var ex = await SignExpectingFailureAsync(server);

        Assert.Equal("UnparseableResponse", ex.RawCode);
        Assert.Contains("bytes", ex.Message);
        Assert.DoesNotContain("not json", ex.Message);
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/problem+json")]
    public async Task Json_with_charset_and_plus_json_is_accepted(string contentType)
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.Intercept = r => r.Route == "signing-status"
            ? new FakeResponse(200, StatusSuccessBody, contentType)
            : null;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.NotNull(result.SignedPdf);
    }

    [Fact]
    public async Task Auth_calls_stay_lenient()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.Intercept = r => r.Route == "login"
            ? new FakeResponse(200, LoginBody, "text/plain")
            : null;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.NotNull(result.SignedPdf);
    }
}

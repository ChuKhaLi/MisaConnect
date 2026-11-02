using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.SampleApi;

/// <summary>
/// FR-100 / SC-036: With <c>Misa:ESign:Webhook:AllowedIps</c> configured,
/// POSTs from outside the CIDR range return 403 and POSTs from inside reach
/// the handler. With empty <c>AllowedIps</c> any IP reaches the handler.
/// </summary>
public class WebhookCidrAllowlistTests
{
    [Fact]
    public async Task IP_outside_allowlist_returns_403()
    {
        await using var factory = new ESignWebFactory()
            .WithConfig(o =>
            {
                o["Misa:ESign:Webhook:Path"] = "/esign/webhook";
                o["Misa:ESign:Webhook:AllowedIps:0"] = "10.0.0.0/8";
            })
            .WithForcedRemoteIp(IPAddress.Parse("192.168.42.1"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/esign/webhook", EnvelopeBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IP_inside_allowlist_reaches_handler()
    {
        await using var factory = new ESignWebFactory()
            .WithConfig(o =>
            {
                o["Misa:ESign:Webhook:Path"] = "/esign/webhook";
                o["Misa:ESign:Webhook:AllowedIps:0"] = "10.0.0.0/8";
            })
            .WithForcedRemoteIp(IPAddress.Parse("10.0.5.7"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/esign/webhook", EnvelopeBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task No_allowlist_reaches_handler_from_any_ip()
    {
        await using var factory = new ESignWebFactory()
            .WithConfig(o => { o["Misa:ESign:Webhook:Path"] = "/esign/webhook"; })
            .WithForcedRemoteIp(IPAddress.Parse("8.8.8.8"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/esign/webhook", EnvelopeBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static object EnvelopeBody() => new
    {
        messageId = "m-1",
        clientId = "client-id",
        extraData = (object?)null,
        status = "SUCCESS",
        errorCode = (string?)null,
        transactionId = "tx-doesnt-exist",
        signatures = new[] { new { documentId = "doc-1", signature = "SIG" } },
    };
}

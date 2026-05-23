using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.SampleApi;

/// <summary>
/// FR-099 / SC-035: With <c>Misa:ESign:Webhook:Secret</c> configured the
/// sample API mounts only the secret-bearing route. POST to the bare path
/// returns 404 (no route); POST to a wrong secret returns 404 (constant-time
/// compare failed); POST to the correct secret reaches the handler.
/// </summary>
public class WebhookSecretRoutingTests
{
    private const string ConfiguredSecret = "0123456789abcdef0123456789abcdef"; // 32 chars

    [Fact]
    public async Task Bare_path_returns_404_when_secret_is_configured()
    {
        await using var factory = new ESignWebFactory().WithConfig(o =>
        {
            o["Misa:ESign:Webhook:Path"] = "/esign/webhook";
            o["Misa:ESign:Webhook:Secret"] = ConfiguredSecret;
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/esign/webhook", EnvelopeBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_secret_returns_404()
    {
        await using var factory = new ESignWebFactory().WithConfig(o =>
        {
            o["Misa:ESign:Webhook:Path"] = "/esign/webhook";
            o["Misa:ESign:Webhook:Secret"] = ConfiguredSecret;
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/esign/webhook/WRONGSECRET", EnvelopeBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Correct_secret_reaches_handler()
    {
        await using var factory = new ESignWebFactory().WithConfig(o =>
        {
            o["Misa:ESign:Webhook:Path"] = "/esign/webhook";
            o["Misa:ESign:Webhook:Secret"] = ConfiguredSecret;
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/esign/webhook/{ConfiguredSecret}", EnvelopeBody());

        // No session registered → handler returns 200 with a webhook.unknown_transaction ACK
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

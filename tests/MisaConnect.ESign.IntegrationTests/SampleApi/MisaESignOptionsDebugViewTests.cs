using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.Samples.Api.Diagnostics;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.SampleApi;

/// <summary>
/// T103 — diagnostic options dump must redact the configured webhook secret
/// (FR-099 / Constitution Principle VIII). Verifies the literal secret value
/// never appears in the snapshot returned by <see cref="MisaESignOptionsDebugView.Redacted"/>.
/// </summary>
public class MisaESignOptionsDebugViewTests
{
    [Fact]
    public void Redacted_does_not_contain_the_configured_secret_value()
    {
        const string secret = "supersecret-0123456789abcdef0123";
        var options = new MisaESignOptions
        {
            ClientId = "cid",
            ClientKey = "ckey",
            UserName = "alice",
            Password = "p@ss",
            Webhook = new MisaESignWebhookOptions
            {
                Mode = WebhookMode.Both,
                Secret = secret,
                AllowedIps = new[] { "10.0.0.0/8" },
            },
        };

        var snapshot = MisaESignOptionsDebugView.Redacted(options);

        Assert.DoesNotContain(secret, string.Join(" | ", snapshot.Values));
        Assert.Equal("<redacted>", snapshot["Misa:ESign:Webhook:Secret"]);
        Assert.Equal("<redacted>", snapshot["Misa:ESign:Password"]);
        Assert.Equal("<redacted>", snapshot["Misa:ESign:ClientKey"]);
        Assert.Equal("10.0.0.0/8", snapshot["Misa:ESign:Webhook:AllowedIps"]);
    }
}

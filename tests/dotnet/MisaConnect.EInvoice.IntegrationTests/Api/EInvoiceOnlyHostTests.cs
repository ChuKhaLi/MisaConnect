using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Api;

/// <summary>
/// The sample host must start with only <c>Misa:EInvoice</c> configured: eSign is
/// optional and is wired up only when a <c>Misa:ESign</c> section exists.
/// </summary>
public class EInvoiceOnlyHostTests
{
    [Fact]
    public async Task Host_starts_without_ESign_section_and_does_not_map_ESign_webhook()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        using var webhook = await client.PostAsync("/esign/webhook", content: null);
        Assert.Equal(HttpStatusCode.NotFound, webhook.StatusCode);
    }
}

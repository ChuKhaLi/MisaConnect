using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

[Collection(SandboxCollection.Name)]
public class ListTemplatesTests
{
    [SandboxFact]
    public async Task Live_list_returns_at_least_one_active_template()
    {
        var client = SandboxClientFactory.Create();
        var templates = await client.ListTemplatesAsync();
        Assert.NotEmpty(templates);
        Assert.All(templates, t => Assert.True(t.IsActive));
        Assert.All(templates, t => Assert.NotEmpty(t.IPTemplateID));
        Assert.All(templates, t => Assert.NotEmpty(t.InvSeries));
    }
}

using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

/// <summary>
/// Live <c>/webapp/templates/ticket</c>. Proves the request shape (query params + credential
/// body) is accepted by MISA. The sandbox account may have no ticket templates: an empty list
/// passes; every returned row must carry a TicketTemplateID.
/// </summary>
public class ListTicketTemplatesTests
{
    [SandboxFact]
    public async Task Live_ticket_template_list_succeeds()
    {
        var client = SandboxClientFactory.Create();
        var rows = await client.ListTicketTemplatesAsync(ct: TestContext.Current.CancellationToken);
        Assert.All(rows, t => Assert.False(string.IsNullOrEmpty(t.TicketTemplateID)));
    }
}

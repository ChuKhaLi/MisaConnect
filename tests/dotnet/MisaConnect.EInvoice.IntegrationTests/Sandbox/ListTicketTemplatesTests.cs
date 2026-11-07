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
        // Logged so a run shows whether the field mapping met real rows or only an empty list.
        TestContext.Current.SendDiagnosticMessage($"ticket templates returned: {rows.Count}; with ServiceName: {rows.Count(t => t.ServiceName is not null)}");
        Assert.All(rows, t => Assert.False(string.IsNullOrEmpty(t.TicketTemplateID)));
    }
}

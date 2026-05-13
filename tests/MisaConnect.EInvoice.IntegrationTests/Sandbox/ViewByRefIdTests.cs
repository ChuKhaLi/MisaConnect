using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

[Collection(SandboxCollection.Name)]
public class ViewByRefIdTests
{
    [SandboxFact(Timeout = 60000)]
    public async Task Round_trip_save_then_view_returns_PDF()
    {
        var client = SandboxClientFactory.Create();
        var dto = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var saved = await client.SaveDraftAsync(new[] { dto });
        Assert.NotEmpty(saved);

        var refId = saved[0].RefId;
        var pdf = await client.GetDraftPdfAsync(refId);

        Assert.NotEmpty(pdf.Content);
        Assert.Equal((byte)'%', pdf.Content[0]);
    }

    [SandboxFact(Timeout = 30000)]
    public async Task Unknown_RefId_surfaces_ResourceNotFound()
    {
        var client = SandboxClientFactory.Create();
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(
            () => client.GetDraftPdfAsync($"non-existent-{Guid.NewGuid()}"));
        Assert.Equal(MeInvoiceErrorCategory.ResourceNotFound, ex.Category);
    }
}

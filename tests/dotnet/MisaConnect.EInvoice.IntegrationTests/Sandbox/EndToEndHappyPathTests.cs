using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

[Collection(SandboxCollection.Name)]
public class EndToEndHappyPathTests
{
    [SandboxFact(Timeout = 120000)]
    public async Task Token_then_Templates_then_Preview_then_Save_then_View()
    {
        var client = SandboxClientFactory.Create();

        // Step 1: list templates (also implicitly acquires token).
        var templates = await client.ListTemplatesAsync();
        Assert.NotEmpty(templates);

        // Step 2: preview.
        var dto = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var preview = await client.PreviewAsync(dto);
        Assert.NotEmpty(preview.Content);

        // Step 3: save.
        var saveResults = await client.SaveDraftAsync(new[] { dto });
        Assert.Single(saveResults);

        // Step 4: view-by-RefID.
        var pdf = await client.GetDraftPdfAsync(saveResults[0].RefId);
        Assert.NotEmpty(pdf.Content);
        Assert.Equal((byte)'%', pdf.Content[0]);
    }
}

using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

[Collection(SandboxCollection.Name)]
public class PreviewTests
{
    [SandboxFact(Timeout = 30000)]
    public async Task Live_preview_returns_PDF_bytes_starting_with_magic()
    {
        var client = SandboxClientFactory.Create();
        var dto = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var pdf = await client.PreviewAsync(dto);

        Assert.NotEmpty(pdf.Content);
        Assert.Equal((byte)'%', pdf.Content[0]);
        Assert.Equal((byte)'P', pdf.Content[1]);
        Assert.Equal((byte)'D', pdf.Content[2]);
        Assert.Equal((byte)'F', pdf.Content[3]);
        Assert.Equal((byte)'-', pdf.Content[4]);
    }
}

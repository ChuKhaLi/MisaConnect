using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

[Collection(SandboxCollection.Name)]
public class SaveDraftTests
{
    [SandboxFact(Timeout = 30000)]
    public async Task Live_save_draft_single_invoice_succeeds()
    {
        var client = SandboxClientFactory.Create();
        var dto = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var results = await client.SaveDraftAsync(new[] { dto });

        Assert.Single(results);
        Assert.Equal(SaveOutcomeDto.Success, results[0].Outcome);
        Assert.Equal(dto.RefId, results[0].RefId);
    }

    [SandboxFact(Timeout = 30000)]
    public async Task Live_save_draft_batch_of_two_succeeds()
    {
        var client = SandboxClientFactory.Create();
        var a = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var b = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var results = await client.SaveDraftAsync(new[] { a, b });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(SaveOutcomeDto.Success, r.Outcome));
    }

    [SandboxFact(Timeout = 30000)]
    public async Task Service_generated_RefId_appears_in_response()
    {
        var client = SandboxClientFactory.Create();
        var dto = SampleInvoiceFactory.CreateDto(refId: null);
        var results = await client.SaveDraftAsync(new[] { dto });

        Assert.NotEmpty(results[0].RefId);
        Assert.NotEqual(string.Empty, results[0].RefId);
    }
}

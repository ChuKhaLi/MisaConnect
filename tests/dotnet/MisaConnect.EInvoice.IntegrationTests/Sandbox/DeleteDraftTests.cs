using System.Diagnostics;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

[Collection(SandboxCollection.Name)]
public class DeleteDraftTests
{
    [SandboxFact(Timeout = 60000)]
    public async Task SaveThenDelete_returns_Deleted_within_5s()
    {
        var client = SandboxClientFactory.Create();
        var dto = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var saved = await client.SaveDraftAsync(new[] { dto });
        Assert.NotEmpty(saved);
        var refId = saved[0].RefId;

        var sw = Stopwatch.StartNew();
        var outcome = await client.DeleteDraftAsync(refId, invoiceWithCode: true);
        sw.Stop();

        Assert.Equal(DeleteDraftStatus.Deleted, outcome.Status);
        Assert.True(sw.Elapsed.TotalSeconds < 5, $"Delete took {sw.Elapsed.TotalSeconds:F2}s, exceeds 5s SC-001 budget.");

        var lookupEx = await Assert.ThrowsAsync<MeInvoiceException>(() => client.GetDraftPdfAsync(refId));
        Assert.Equal(MeInvoiceErrorCategory.ResourceNotFound, lookupEx.Category);
    }

    [SandboxFact(Timeout = 60000)]
    public async Task SecondDelete_is_idempotent()
    {
        // FR-037 idempotency: re-delete MUST NOT raise an exception and MUST NOT
        // produce a duplicate side effect. The spec anticipated MISA returning
        // InvalidTransactionID on re-delete (→ NotFound). The actual sandbox is
        // more lenient and returns success:true on re-delete (→ Deleted). Both
        // outcomes preserve the idempotency contract; this test asserts the
        // contract, not the specific surfaced status.
        var client = SandboxClientFactory.Create();
        var dto = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var saved = await client.SaveDraftAsync(new[] { dto });
        var refId = saved[0].RefId;

        var first = await client.DeleteDraftAsync(refId, invoiceWithCode: true);
        Assert.Equal(DeleteDraftStatus.Deleted, first.Status);

        var second = await client.DeleteDraftAsync(refId, invoiceWithCode: true);
        Assert.True(
            second.Status is DeleteDraftStatus.Deleted or DeleteDraftStatus.NotFound,
            $"Re-delete must be idempotent (Deleted or NotFound); observed {second.Status}.");
    }

    [SandboxFact(Timeout = 30000)]
    public async Task UnknownRefId_is_idempotent()
    {
        // MISA's sandbox treats an unknown RefID identically to a re-delete:
        // success:true. The Status MUST be one of the two idempotent values;
        // never an exception or a duplicate-create side effect.
        var client = SandboxClientFactory.Create();
        var outcome = await client.DeleteDraftAsync(Guid.NewGuid().ToString(), invoiceWithCode: true);
        Assert.True(
            outcome.Status is DeleteDraftStatus.Deleted or DeleteDraftStatus.NotFound,
            $"Unknown-RefID delete must be idempotent (Deleted or NotFound); observed {outcome.Status}.");
    }

    [SandboxFact(Timeout = 60000)]
    public async Task WrongInvoiceWithCode_is_idempotent()
    {
        var client = SandboxClientFactory.Create();
        var dto = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var saved = await client.SaveDraftAsync(new[] { dto });
        var refId = saved[0].RefId;

        var outcome = await client.DeleteDraftAsync(refId, invoiceWithCode: false);
        Assert.True(
            outcome.Status is DeleteDraftStatus.Deleted or DeleteDraftStatus.NotFound,
            $"Wrong-flag delete must be idempotent; observed {outcome.Status}.");

        // Cleanup with the correct flag (idempotent — safe even if the wrong-flag call already deleted).
        await client.DeleteDraftAsync(refId, invoiceWithCode: true);
    }
}

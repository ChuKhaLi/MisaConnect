using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

/// <summary>
/// Spec contract tests T27–T29 from <c>contracts/lookup-by-refid.md</c> —
/// FR-055 sandbox end-to-end coverage for the batch-lookup-by-RefID path.
/// Seeds drafts via slice 1's <c>SaveDraftAsync</c>, exercises the new
/// lookup operation, and tears down each draft via slice 2 / 004's
/// <see cref="SandboxDraftCleanup"/>.
/// </summary>
[Collection(SandboxCollection.Name)]
public class LookupTests
{
    // Slice 5 R-LU-23 (empirical finding 2026-05-13): MISA's /webapp/getlist
    // does NOT return Draft-state invoices in the sandbox tenant (verified by
    // polling for 33+ s with both invoiceWithCode values). The same draft is
    // visible to /webapp/viewrefid immediately, so the data is persisted —
    // but the lookup endpoints surface only Signed / Issued / later states.
    // The two affected tests below skip with a documented reason until either
    // (a) a future slice repurposes them against slice 6's issued-invoice
    // fixture pool, or (b) MISA's behaviour changes.
    private const string LookupSkipReason =
        "R-LU-23: MISA /webapp/getlist does not return Draft-state invoices in " +
        "the sandbox tenant. See specs/005-misa-invoice-lookup/research.md R-LU-23. " +
        "Lookup-of-issued-invoice coverage lives in slice 6's AmendmentTests against " +
        "the issued-invoice fixture pool.";

    [SandboxFact(Timeout = 60000, Skip = LookupSkipReason)]
    public async Task Sandbox_seed_three_drafts_then_batch_lookup_returns_Found_Draft()
    {
        var client = SandboxClientFactory.Create();
        var a = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var b = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var c = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());

        var saved = await client.SaveDraftAsync(new[] { a, b, c });
        Assert.Equal(3, saved.Count);
        var refIds = saved.Select(s => s.RefId).ToArray();

        await using var cleanA = SandboxDraftCleanup.Track(client, refIds[0], invoiceWithCode: true);
        await using var cleanB = SandboxDraftCleanup.Track(client, refIds[1], invoiceWithCode: true);
        await using var cleanC = SandboxDraftCleanup.Track(client, refIds[2], invoiceWithCode: true);

        var outcome = await client.LookupByRefIdAsync(refIds, invoiceWithCode: true);

        Assert.Equal("Completed", outcome.Status);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(3, outcome.Outcomes!.Count);

        var byRef = outcome.Outcomes.ToDictionary(o => o.RefId, StringComparer.Ordinal);
        Assert.All(refIds, r =>
        {
            Assert.True(byRef.ContainsKey(r), $"Expected outcome for {r}.");
            Assert.Equal("Found", byRef[r].Status);
            Assert.NotNull(byRef[r].Snapshot);
            Assert.Equal("Draft", byRef[r].Snapshot!.Status);
        });
    }

    [SandboxFact(Timeout = 60000, Skip = LookupSkipReason)]
    public async Task Sandbox_mixed_known_and_unknown_RefIDs_returns_Found_and_NotFound()
    {
        var client = SandboxClientFactory.Create();
        var a = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var b = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());

        var saved = await client.SaveDraftAsync(new[] { a, b });
        Assert.Equal(2, saved.Count);
        var seedA = saved[0].RefId;
        var seedB = saved[1].RefId;
        var unknown = Guid.NewGuid().ToString();

        await using var cleanA = SandboxDraftCleanup.Track(client, seedA, invoiceWithCode: true);
        await using var cleanB = SandboxDraftCleanup.Track(client, seedB, invoiceWithCode: true);

        var outcome = await client.LookupByRefIdAsync(
            new[] { seedA, unknown, seedB },
            invoiceWithCode: true);

        Assert.Equal("Completed", outcome.Status);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(3, outcome.Outcomes!.Count);
        // Order preservation per R-LU-10.
        Assert.Equal(seedA, outcome.Outcomes![0].RefId);
        Assert.Equal("Found", outcome.Outcomes![0].Status);
        Assert.Equal(unknown, outcome.Outcomes![1].RefId);
        Assert.Equal("NotFound", outcome.Outcomes![1].Status);
        Assert.Null(outcome.Outcomes![1].Snapshot);
        Assert.Equal(seedB, outcome.Outcomes![2].RefId);
        Assert.Equal("Found", outcome.Outcomes![2].Status);
    }

    [SandboxFact(Timeout = 30000)]
    public async Task Sandbox_all_unknown_RefIDs_returns_all_NotFound()
    {
        var client = SandboxClientFactory.Create();
        var refIds = new string[5];
        for (var i = 0; i < refIds.Length; i++)
        {
            refIds[i] = Guid.NewGuid().ToString();
        }

        var outcome = await client.LookupByRefIdAsync(refIds, invoiceWithCode: true);

        Assert.Equal("Completed", outcome.Status);
        Assert.NotNull(outcome.Outcomes);
        Assert.Equal(5, outcome.Outcomes!.Count);
        Assert.All(outcome.Outcomes, o =>
        {
            Assert.Equal("NotFound", o.Status);
            Assert.Null(o.Snapshot);
        });
    }
}

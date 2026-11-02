using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

/// <summary>
/// Slice 5 US2 sandbox tests T30–T32 from
/// <c>specs/005-misa-invoice-lookup/contracts/lookup-paginated.md</c>. All
/// <see cref="SandboxFactAttribute"/> per FR-055 — they skip cleanly when the
/// MISA sandbox is unreachable.
///
/// Filed as a separate test class to avoid colliding with the US1 sandbox
/// lookup tests authored by another agent.
/// </summary>
[Collection(SandboxCollection.Name)]
public class LookupPagedSandboxTests
{
    // Slice 5 R-LU-23 (empirical finding 2026-05-13): MISA's /webapp/paging
    // does NOT return Draft-state invoices in the sandbox tenant. See
    // LookupTests.LookupSkipReason / specs/005-misa-invoice-lookup/research.md
    // R-LU-23 for the full diagnostic. The "seeded-draft visible in page"
    // assertion in the two tests below cannot succeed against the live
    // sandbox; they skip until reframed against an issued-invoice fixture.
    private const string PagingSkipReason =
        "R-LU-23: MISA /webapp/paging does not return Draft-state invoices in " +
        "the sandbox tenant. See specs/005-misa-invoice-lookup/research.md R-LU-23.";

    [SandboxFact(Timeout = 60000, Skip = PagingSkipReason)]
    public async Task Sandbox_standard_paging_returns_seeded_draft()
    {
        var client = SandboxClientFactory.Create();
        var seedRefId = Guid.NewGuid().ToString();
        var dto = SampleInvoiceFactory.CreateDto(refId: seedRefId);
        var saved = await client.SaveDraftAsync(new[] { dto });
        Assert.NotEmpty(saved);
        var refId = saved[0].RefId;

        await using var cleanup = SandboxDraftCleanup.Track(client, refId, invoiceWithCode: true);

        // Bracket the seed's InvDate (2026-05-11 per SampleInvoiceFactory) with
        // a generous +/- 7 day window so the seed is guaranteed to fall inside
        // even if MISA's storage uses a slightly different InvDate granularity.
        var seedDate = dto.InvDate;
        var request = new PagedLookupRequestDto(
            Start: 0,
            Length: 100,
            Sort: "InvDate",
            FromDate: seedDate.AddDays(-7),
            ToDate: seedDate.AddDays(7),
            PublishStatus: null);

        var result = await client.LookupStandardAsync(request, invoiceWithCode: true);

        Assert.Contains(result.Items, item =>
            string.Equals(item.RefId, refId, StringComparison.Ordinal) &&
            string.Equals(item.Status, "Draft", StringComparison.Ordinal));
    }

    [SandboxFact(Timeout = 90000, Skip = PagingSkipReason)]
    public async Task Sandbox_standard_paging_with_publishStatus_filter()
    {
        var client = SandboxClientFactory.Create();
        var seed1 = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var seed2 = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        var saved = await client.SaveDraftAsync(new[] { seed1, seed2 });
        Assert.Equal(2, saved.Count);
        var refId1 = saved[0].RefId;
        var refId2 = saved[1].RefId;

        await using var cleanup1 = SandboxDraftCleanup.Track(client, refId1, invoiceWithCode: true);
        await using var cleanup2 = SandboxDraftCleanup.Track(client, refId2, invoiceWithCode: true);

        var request = new PagedLookupRequestDto(
            Start: 0,
            Length: 100,
            Sort: "InvDate",
            FromDate: seed1.InvDate.AddDays(-7),
            ToDate: seed1.InvDate.AddDays(7),
            PublishStatus: 0); // Drafts only

        var result = await client.LookupStandardAsync(request, invoiceWithCode: true);

        Assert.Contains(result.Items, i => i.RefId == refId1);
        Assert.Contains(result.Items, i => i.RefId == refId2);
        Assert.All(result.Items, item => Assert.Equal(0, item.RawPublishStatus));
    }

    [SandboxFact(Timeout = 30000)]
    public async Task Sandbox_standard_paging_traversal_end_of_results()
    {
        var client = SandboxClientFactory.Create();

        // Date range guaranteed to predate any sandbox data.
        var request = new PagedLookupRequestDto(
            Start: 0,
            Length: 100,
            Sort: "InvDate",
            FromDate: new DateOnly(1900, 1, 1),
            ToDate: new DateOnly(1900, 1, 2),
            PublishStatus: null);

        var result = await client.LookupStandardAsync(request, invoiceWithCode: true);

        Assert.Equal(0, result.ReturnedCount);
        Assert.Empty(result.Items);
    }
}

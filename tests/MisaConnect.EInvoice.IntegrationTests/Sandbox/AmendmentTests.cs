using System.Diagnostics;
using MisaConnect.EInvoice.Client.Dtos;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

/// <summary>
/// Slice 6 sandbox tests (T-RP-15, T-AJ-16). Gated by
/// <see cref="SandboxAmendmentFactAttribute"/>, which skips cleanly when
/// either the sandbox is unreachable OR the fixture pool is empty for the
/// requested kind. The committed pool file starts empty — operators top up
/// via the procedure documented in <c>Sandbox/README.md</c>.
/// </summary>
public class AmendmentTests
{
    [SandboxAmendmentFact("replacement")]
    public async Task Sandbox_replacement_against_fixture_pool_returns_fresh_RefId_and_lookup_shows_Replaced()
    {
        var loader = new FixturePoolLoader(FixturePoolLoader.DefaultPoolDirectory(), DateTimeOffset.UtcNow);
        var fixture = loader.Checkout("replacement",
            $"{typeof(AmendmentTests).FullName}.{nameof(Sandbox_replacement_against_fixture_pool_returns_fresh_RefId_and_lookup_shows_Replaced)}");

        var client = SandboxClientFactory.Create();
        var freshRefId = $"fx-replace-{Guid.NewGuid():N}";
        var request = new ReplacementRequestDto(
            Invoice: AmendmentSandboxInvoice.AbsoluteCorrected(freshRefId),
            OriginalRef: new OriginalInvoiceReferenceDto(
                OrgRefID: fixture.RefId,
                OrgInvNo: fixture.InvNo,
                OrgInvTemplateNo: fixture.InvTemplateNo,
                OrgInvSeries: fixture.InvSeries,
                OrgInvDate: DateOnly.ParseExact(fixture.InvDate, "yyyy-MM-dd")),
            ChangeReason: "Slice 6 sandbox replacement test",
            InvoiceWithCode: true);

        var sw = Stopwatch.StartNew();
        var amendment = await client.IssueReplacementAsync(request);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"SC-009: replacement round-trip took {sw.Elapsed.TotalSeconds:F1}s (>5s budget).");
        Assert.Equal(SaveOutcomeDto.Success, amendment.Outcome);
        Assert.Equal(freshRefId, amendment.RefId);

        sw.Restart();
        var lookup = await client.LookupByRefIdAsync(new[] { freshRefId }, invoiceWithCode: true);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"SC-012: lookup verification took {sw.Elapsed.TotalSeconds:F1}s (>5s budget).");
        Assert.Equal("Completed", lookup.Status);
        Assert.NotNull(lookup.Outcomes);
        var first = Assert.Single(lookup.Outcomes!);
        Assert.NotNull(first.Snapshot);
        Assert.Equal("Replaced", first.Snapshot!.Status);
    }

    [SandboxAmendmentFact("adjustment")]
    public async Task Sandbox_adjustment_against_fixture_pool_returns_fresh_RefId_and_lookup_shows_Adjusted()
    {
        var loader = new FixturePoolLoader(FixturePoolLoader.DefaultPoolDirectory(), DateTimeOffset.UtcNow);
        var fixture = loader.Checkout("adjustment",
            $"{typeof(AmendmentTests).FullName}.{nameof(Sandbox_adjustment_against_fixture_pool_returns_fresh_RefId_and_lookup_shows_Adjusted)}");

        var client = SandboxClientFactory.Create();
        var freshRefId = $"fx-adjust-{Guid.NewGuid():N}";
        var request = new AdjustmentRequestDto(
            Invoice: AmendmentSandboxInvoice.IncreaseDelta(freshRefId, amount: 100_000m),
            OriginalRef: new OriginalInvoiceReferenceDto(
                OrgRefID: fixture.RefId,
                OrgInvNo: fixture.InvNo,
                OrgInvTemplateNo: fixture.InvTemplateNo,
                OrgInvSeries: fixture.InvSeries,
                OrgInvDate: DateOnly.ParseExact(fixture.InvDate, "yyyy-MM-dd")),
            ChangeReason: "Slice 6 sandbox adjustment test",
            InvoiceWithCode: true);

        var sw = Stopwatch.StartNew();
        var amendment = await client.IssueAdjustmentAsync(request);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"SC-010: adjustment round-trip took {sw.Elapsed.TotalSeconds:F1}s (>5s budget).");
        Assert.Equal(SaveOutcomeDto.Success, amendment.Outcome);
        Assert.Equal(freshRefId, amendment.RefId);

        sw.Restart();
        var lookup = await client.LookupByRefIdAsync(new[] { freshRefId }, invoiceWithCode: true);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"SC-012: lookup verification took {sw.Elapsed.TotalSeconds:F1}s (>5s budget).");
        Assert.Equal("Completed", lookup.Status);
        var first = Assert.Single(lookup.Outcomes!);
        Assert.NotNull(first.Snapshot);
        Assert.Equal("Adjusted", first.Snapshot!.Status);
    }
}

internal static class AmendmentSandboxInvoice
{
    public static InvoiceDto AbsoluteCorrected(string refId) => new(
        RefId: refId,
        Template: null,
        InvDate: DateOnly.FromDateTime(DateTime.UtcNow),
        CreatedDate: DateTimeOffset.UtcNow,
        ModifiedDate: DateTimeOffset.UtcNow,
        Currency: "VND",
        ExchangeRate: 1m,
        PaymentMethod: "TM",
        BuyerType: 1,
        Buyer: new BuyerInfoDto("Cong ty TNHH ABC", "0123456789"),
        Lines: new[]
        {
            new InvoiceLineDto(
                InventoryItemType: 0,
                SortOrder: 1,
                Description: "Replacement reissue",
                UnitName: "Lan",
                Quantity: 1m,
                UnitPrice: 1_000_000m,
                AmountOC: 1_000_000m,
                Amount: 1_000_000m,
                AmountWithoutVATOC: 1_000_000m,
                AmountWithoutVAT: 1_000_000m,
                VatRateName: "10%",
                VATAmountOC: 100_000m,
                VATAmount: 100_000m,
                SortOrderView: 1),
        },
        Totals: new InvoiceTotalsDto(
            1_000_000m, 1_000_000m,
            0m, 0m,
            1_000_000m, 1_000_000m,
            100_000m, 100_000m,
            1_100_000m, 1_100_000m,
            "Mot trieu mot tram nghin"));

    public static InvoiceDto IncreaseDelta(string refId, decimal amount) => new(
        RefId: refId,
        Template: null,
        InvDate: DateOnly.FromDateTime(DateTime.UtcNow),
        CreatedDate: DateTimeOffset.UtcNow,
        ModifiedDate: DateTimeOffset.UtcNow,
        Currency: "VND",
        ExchangeRate: 1m,
        PaymentMethod: "TM",
        BuyerType: 1,
        Buyer: new BuyerInfoDto("Cong ty TNHH ABC", "0123456789"),
        Lines: new[]
        {
            new InvoiceLineDto(
                InventoryItemType: 0,
                SortOrder: 1,
                Description: "Adjustment delta",
                UnitName: "Lan",
                Quantity: 1m,
                UnitPrice: amount,
                AmountOC: amount,
                Amount: amount,
                AmountWithoutVATOC: amount,
                AmountWithoutVAT: amount,
                VatRateName: null,
                VATAmountOC: 0m,
                VATAmount: 0m,
                SortOrderView: 1),
        },
        Totals: new InvoiceTotalsDto(
            amount, amount,
            0m, 0m,
            amount, amount,
            0m, 0m,
            amount, amount,
            "Delta"));
}

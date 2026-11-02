using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;
using Xunit.Abstractions;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

/// <summary>
/// Slice 5 sandbox tests T33 + T34 from
/// <c>contracts/lookup-paginated.md</c>. Both tests are conditional on a
/// POS-calculating (<c>InvSeries[4] == 'M'</c>) template existing in the
/// sandbox. The contract says: "If absent, skip with a clear reason". The
/// existing <see cref="SandboxFactAttribute"/> only handles
/// host-unreachability skipping; here we soft-skip at runtime when the
/// prerequisite template is missing, logging the reason to
/// <see cref="ITestOutputHelper"/>. FR-055.
/// </summary>
[Collection(SandboxCollection.Name)]
public class LookupCalculatingSandboxTests
{
    // Slice 5 R-LU-23: MISA /webapp/paging/calculating does not return
    // Draft-state invoices either. The two tests in this file both seed a
    // draft and then assert it appears in the calculating page — that
    // assertion cannot hold against live MISA. See LookupTests for the
    // detailed skip reason.
    private const string CalculatingSkipReason =
        "R-LU-23: MISA /webapp/paging/calculating does not return Draft-state " +
        "invoices in the sandbox tenant. See specs/005-misa-invoice-lookup/research.md R-LU-23.";

    private readonly ITestOutputHelper _output;

    public LookupCalculatingSandboxTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SandboxFact(Timeout = 120000, Skip = CalculatingSkipReason)]
    public async Task Sandbox_calculating_paging_returns_seeded_pos_invoice()
    {
        var client = SandboxClientFactory.Create();
        var templates = await client.ListTemplatesAsync(withCode: true);
        var posTemplate = templates.FirstOrDefault(IsPosCalculating);
        if (posTemplate is null)
        {
            // T33 prerequisite missing — soft-skip with a clear reason. The
            // sandbox tenant has no POS-calculating template registered, so
            // the calculating endpoint cannot be reconciled against a real
            // draft. Same skip semantic as SandboxFact for unreachability.
            _output.WriteLine("Skipping: no POS-calculating (M-series) template in sandbox.");
            return;
        }

        var dto = MakePosDto(posTemplate);
        var saveResults = await client.SaveDraftAsync(new[] { dto }, withCode: true);
        Assert.Single(saveResults);
        var refId = saveResults[0].RefId;
        await using var cleanup = SandboxDraftCleanup.Track(client, refId, invoiceWithCode: true);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var request = new PagedLookupRequestDto(
            Start: 0,
            Length: 100,
            Sort: "InvDate",
            FromDate: today.AddDays(-7),
            ToDate: today.AddDays(7),
            PublishStatus: null);

        var page = await client.LookupCalculatingAsync(request, invoiceWithCode: true);
        Assert.Contains(page.Items, s => s.RefId == refId);
    }

    [SandboxFact(Timeout = 120000, Skip = CalculatingSkipReason)]
    public async Task Sandbox_standard_and_calculating_are_disjoint()
    {
        var client = SandboxClientFactory.Create();
        var templates = await client.ListTemplatesAsync(withCode: true);
        var posTemplate = templates.FirstOrDefault(IsPosCalculating);
        var standardTemplate = templates.FirstOrDefault(IsStandard);
        if (posTemplate is null || standardTemplate is null)
        {
            // T34 needs both classes present so the disjointness can be
            // observed in a single date range. If either is missing, the
            // test cannot meaningfully run — soft-skip per contract.
            _output.WriteLine($"Skipping: sandbox missing required templates (POS={posTemplate is not null}, Standard={standardTemplate is not null}).");
            return;
        }

        var standardDto = MakeStandardDto(standardTemplate);
        var posDto = MakePosDto(posTemplate);

        var savedStandard = await client.SaveDraftAsync(new[] { standardDto }, withCode: true);
        Assert.Single(savedStandard);
        var standardRefId = savedStandard[0].RefId;
        await using var cleanupStandard = SandboxDraftCleanup.Track(client, standardRefId, invoiceWithCode: true);

        var savedPos = await client.SaveDraftAsync(new[] { posDto }, withCode: true);
        Assert.Single(savedPos);
        var posRefId = savedPos[0].RefId;
        await using var cleanupPos = SandboxDraftCleanup.Track(client, posRefId, invoiceWithCode: true);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var request = new PagedLookupRequestDto(
            Start: 0,
            Length: 100,
            Sort: "InvDate",
            FromDate: today.AddDays(-7),
            ToDate: today.AddDays(7),
            PublishStatus: null);

        var standardPage = await client.LookupStandardAsync(request, invoiceWithCode: true);
        var calculatingPage = await client.LookupCalculatingAsync(request, invoiceWithCode: true);

        // Standard seed appears only on /standard.
        Assert.Contains(standardPage.Items, s => s.RefId == standardRefId);
        Assert.DoesNotContain(calculatingPage.Items, s => s.RefId == standardRefId);

        // POS seed appears only on /calculating.
        Assert.Contains(calculatingPage.Items, s => s.RefId == posRefId);
        Assert.DoesNotContain(standardPage.Items, s => s.RefId == posRefId);
    }

    private static bool IsPosCalculating(TemplateDto t) =>
        t.InvSeries.Length >= 5 && t.InvSeries[4] == 'M';

    private static bool IsStandard(TemplateDto t) =>
        t.InvSeries.Length >= 5 && t.InvSeries[4] == 'T';

    private static InvoiceDto MakeStandardDto(TemplateDto template)
    {
        var baseDto = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        return baseDto with { Template = new TemplateRefDto(template.IPTemplateID, template.InvSeries) };
    }

    private static InvoiceDto MakePosDto(TemplateDto template)
    {
        var baseDto = SampleInvoiceFactory.CreateDto(refId: Guid.NewGuid().ToString());
        return baseDto with { Template = new TemplateRefDto(template.IPTemplateID, template.InvSeries) };
    }
}

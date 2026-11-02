using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.TestSupport.Fixtures;

public static class TaggedFixtures
{
    public const string TagPrefix = "EINVOICE-TEST";

    public static Invoice WithSandboxTag(this Invoice invoice, string? runId = null, string? caseId = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        runId ??= DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        caseId ??= "case";

        var fields = invoice.CustomFields is null
            ? new Dictionary<int, string>()
            : new Dictionary<int, string>(invoice.CustomFields);
        fields[1] = $"{TagPrefix}:{runId}:{caseId}";

        return invoice with { CustomFields = fields };
    }
}

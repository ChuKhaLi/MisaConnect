using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.Templates;

public sealed class TemplateResolver : ITemplateResolver
{
    private readonly ListActiveTemplates _list;

    public TemplateResolver(ListActiveTemplates list) => _list = list;

    public async Task<TemplateResolution> ResolveAsync(TemplateRef? callerSupplied, bool invoiceWithCode, CancellationToken ct)
    {
        var active = await _list.ExecuteAsync(invoiceWithCode, ct).ConfigureAwait(false);

        if (callerSupplied is null)
        {
            return active.Count switch
            {
                0 => new TemplateResolution.NoActiveTemplate(),
                1 => new TemplateResolution.Resolved(active[0]),
                _ => new TemplateResolution.AmbiguousTemplate(active)
            };
        }

        foreach (var t in active)
        {
            var idMatches = string.IsNullOrEmpty(callerSupplied.IPTemplateID)
                || string.Equals(t.IPTemplateID, callerSupplied.IPTemplateID, StringComparison.OrdinalIgnoreCase);
            var seriesMatches = string.IsNullOrEmpty(callerSupplied.InvSeries)
                || string.Equals(t.InvSeries, callerSupplied.InvSeries, StringComparison.OrdinalIgnoreCase);
            if (idMatches && seriesMatches)
            {
                return new TemplateResolution.Resolved(t);
            }
        }

        var key = !string.IsNullOrEmpty(callerSupplied.IPTemplateID)
            ? callerSupplied.IPTemplateID
            : callerSupplied.InvSeries;
        return new TemplateResolution.UnknownTemplate(key);
    }
}

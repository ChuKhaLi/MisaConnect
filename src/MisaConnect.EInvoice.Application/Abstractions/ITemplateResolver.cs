using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Templates;

namespace MisaConnect.EInvoice.Application.Abstractions;

public abstract record TemplateResolution
{
    private TemplateResolution() { }

    public sealed record Resolved(Template Template) : TemplateResolution;
    public sealed record AmbiguousTemplate(IReadOnlyList<Template> ActiveTemplates) : TemplateResolution;
    public sealed record NoActiveTemplate : TemplateResolution;
    public sealed record UnknownTemplate(string CallerSuppliedRef) : TemplateResolution;
}

public interface ITemplateResolver
{
    Task<TemplateResolution> ResolveAsync(TemplateRef? callerSupplied, bool invoiceWithCode, CancellationToken ct);
}

using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.UseCases;

public sealed class PreviewInvoice
{
    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;
    private readonly IInvoiceValidator _validator;
    private readonly ITemplateResolver _resolver;
    private readonly IRefIdGenerator _refIdGenerator;

    public PreviewInvoice(
        IMeInvoiceClient client,
        EnsureAccessToken ensureToken,
        IInvoiceValidator validator,
        ITemplateResolver resolver,
        IRefIdGenerator refIdGenerator)
    {
        _client = client;
        _ensureToken = ensureToken;
        _validator = validator;
        _resolver = resolver;
        _refIdGenerator = refIdGenerator;
    }

    public async Task<PreviewResult> ExecuteAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct)
    {
        var resolution = await _resolver.ResolveAsync(invoice.Template, invoiceWithCode, ct).ConfigureAwait(false);
        Invoice working = invoice;
        switch (resolution)
        {
            case TemplateResolution.Resolved r:
                working = invoice with { Template = new TemplateRef(r.Template.IPTemplateID, r.Template.InvSeries) };
                break;
            case TemplateResolution.AmbiguousTemplate:
                throw new MeInvoiceException(MeInvoiceErrorCategory.Configuration, "AmbiguousTemplate", "Multiple active templates exist; caller must specify.");
            case TemplateResolution.NoActiveTemplate:
                throw new MeInvoiceException(MeInvoiceErrorCategory.TemplateState, "NoActiveTemplate", "No active templates configured.");
            case TemplateResolution.UnknownTemplate u:
                throw new MeInvoiceException(MeInvoiceErrorCategory.TemplateState, "UnknownTemplate", $"Template '{u.CallerSuppliedRef}' is not active.");
        }

        if (string.IsNullOrEmpty(working.RefId.Value))
        {
            working = working with { RefId = _refIdGenerator.NewRefId() };
        }

        var failures = _validator.Validate(working);
        if (failures.Count > 0)
        {
            throw new MeInvoiceException(MeInvoiceErrorCategory.Validation, failures: failures, refId: working.RefId.Value);
        }

        await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);
        var pdf = await _client.PreviewAsync(working, invoiceWithCode, ct).ConfigureAwait(false);
        return new PreviewResult(pdf);
    }
}

using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Templates;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Slice 6 (FR-056) — orchestrator for issuing a replacement invoice.
/// Validates locally (FR-058/059/060/061) before any MISA call, resolves
/// the template via slice 1's <see cref="ITemplateResolver"/>, assigns a
/// fresh <c>RefID</c> if absent, runs the slice 1 invoice validator, then
/// posts to MISA with <c>EInvoiceStatus="3"</c>.
/// </summary>
public sealed class IssueReplacementInvoice
{
    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;
    private readonly IInvoiceValidator _validator;
    private readonly ITemplateResolver _resolver;
    private readonly IRefIdGenerator _refIdGenerator;

    public IssueReplacementInvoice(
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

    public async Task<SaveResult> ExecuteAsync(ReplacementRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestFailures = request.Validate();
        if (requestFailures is { Count: > 0 })
        {
            throw new MeInvoiceException(
                MeInvoiceErrorCategory.Validation,
                rawErrorCode: "ValidationFailed",
                failures: requestFailures);
        }

        var invoice = request.Invoice;
        if (string.IsNullOrEmpty(invoice.RefId.Value))
        {
            invoice = invoice with { RefId = _refIdGenerator.NewRefId() };
        }

        var resolution = await _resolver.ResolveAsync(invoice.Template, request.InvoiceWithCode, ct).ConfigureAwait(false);
        switch (resolution)
        {
            case TemplateResolution.Resolved r:
                invoice = invoice with { Template = new TemplateRef(r.Template.IPTemplateID, r.Template.InvSeries) };
                break;
            case TemplateResolution.AmbiguousTemplate:
                throw new MeInvoiceException(MeInvoiceErrorCategory.Configuration, "AmbiguousTemplate", "Multiple active templates.");
            case TemplateResolution.NoActiveTemplate:
                throw new MeInvoiceException(MeInvoiceErrorCategory.TemplateState, "NoActiveTemplate");
            case TemplateResolution.UnknownTemplate u:
                throw new MeInvoiceException(MeInvoiceErrorCategory.TemplateState, "UnknownTemplate", u.CallerSuppliedRef);
        }

        var validatorFailures = _validator.Validate(invoice);
        if (validatorFailures.Count > 0)
        {
            throw new MeInvoiceException(
                MeInvoiceErrorCategory.Validation,
                rawErrorCode: "ValidationFailed",
                failures: validatorFailures);
        }

        await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);

        return await _client.IssueReplacementAsync(
            invoice,
            request.OriginalRef,
            request.ChangeReason,
            request.InvoiceWithCode,
            ct).ConfigureAwait(false);
    }
}

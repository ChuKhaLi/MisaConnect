using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Templates;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Slice 6 (FR-057) — orchestrator for issuing an adjustment invoice.
/// Same shape as <see cref="IssueReplacementInvoice"/> except the call
/// delegates to <see cref="IMeInvoiceClient.IssueAdjustmentAsync"/>; the
/// wire payload carries <c>EInvoiceStatus="4"</c> and the invoice's line
/// items / totals represent the delta to apply (positive / negative /
/// zero-net per spec Clarifications Session 2026-05-13).
/// </summary>
public sealed class IssueAdjustmentInvoice
{
    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;
    private readonly IInvoiceValidator _validator;
    private readonly ITemplateResolver _resolver;
    private readonly IRefIdGenerator _refIdGenerator;

    public IssueAdjustmentInvoice(
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

    public async Task<SaveResult> ExecuteAsync(AdjustmentRequest request, CancellationToken ct)
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

        return await _client.IssueAdjustmentAsync(
            invoice,
            request.OriginalRef,
            request.ChangeReason,
            request.InvoiceWithCode,
            ct).ConfigureAwait(false);
    }
}

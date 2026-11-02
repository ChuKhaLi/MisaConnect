using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.UseCases;

public sealed class SaveDraftInvoices
{
    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;
    private readonly IInvoiceValidator _validator;
    private readonly ITemplateResolver _resolver;
    private readonly IRefIdGenerator _refIdGenerator;

    public SaveDraftInvoices(
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

    public async Task<IReadOnlyList<SaveResult>> ExecuteAsync(
        IReadOnlyList<Invoice> invoices,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoices);

        if (invoices.Count is < 1 or > BatchSubmission.MaxInvoices)
        {
            var failure = new ValidationFailure(
                "$batch.Count",
                $"Batch size {invoices.Count} exceeds the MISA limit of {BatchSubmission.MaxInvoices} invoices per request.",
                "FR-030");
            throw new MeInvoiceException(
                MeInvoiceErrorCategory.Validation,
                rawErrorCode: "InvoiceQuantityTooLarge",
                failures: new[] { failure });
        }

        var results = new SaveResult?[invoices.Count];
        var toSubmit = new List<(int Index, Invoice Invoice)>();

        for (var i = 0; i < invoices.Count; i++)
        {
            var invoice = invoices[i];
            if (string.IsNullOrEmpty(invoice.RefId.Value))
            {
                invoice = invoice with { RefId = _refIdGenerator.NewRefId() };
            }

            var resolution = await _resolver.ResolveAsync(invoice.Template, invoiceWithCode, ct).ConfigureAwait(false);
            switch (resolution)
            {
                case TemplateResolution.Resolved r:
                    invoice = invoice with { Template = new TemplateRef(r.Template.IPTemplateID, r.Template.InvSeries) };
                    break;
                case TemplateResolution.AmbiguousTemplate:
                    results[i] = new SaveResult(invoice.RefId, SaveOutcome.Error,
                        new MeInvoiceErrorCode(MeInvoiceErrorCategory.Configuration, "AmbiguousTemplate", "Multiple active templates."));
                    continue;
                case TemplateResolution.NoActiveTemplate:
                    results[i] = new SaveResult(invoice.RefId, SaveOutcome.Error,
                        new MeInvoiceErrorCode(MeInvoiceErrorCategory.TemplateState, "NoActiveTemplate"));
                    continue;
                case TemplateResolution.UnknownTemplate u:
                    results[i] = new SaveResult(invoice.RefId, SaveOutcome.Error,
                        new MeInvoiceErrorCode(MeInvoiceErrorCategory.TemplateState, "UnknownTemplate", u.CallerSuppliedRef));
                    continue;
            }

            var failures = _validator.Validate(invoice);
            if (failures.Count > 0)
            {
                results[i] = new SaveResult(
                    invoice.RefId,
                    SaveOutcome.Error,
                    new MeInvoiceErrorCode(MeInvoiceErrorCategory.Validation, "ValidationFailed"),
                    failures);
                continue;
            }

            toSubmit.Add((i, invoice));
        }

        if (toSubmit.Count == 0)
        {
            return FillRemaining(results);
        }

        await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);
        var submission = BatchSubmission.From(toSubmit.Select(x => x.Invoice).ToList());
        var misaResults = await _client.SaveDraftAsync(submission, invoiceWithCode, ct).ConfigureAwait(false);

        var byRef = new Dictionary<string, SaveResult>(StringComparer.Ordinal);
        foreach (var r in misaResults)
        {
            byRef[r.RefId.Value] = r;
        }

        foreach (var (originalIndex, submittedInvoice) in toSubmit)
        {
            if (byRef.TryGetValue(submittedInvoice.RefId.Value, out var matched))
            {
                results[originalIndex] = matched;
            }
            else
            {
                results[originalIndex] = new SaveResult(
                    submittedInvoice.RefId,
                    SaveOutcome.Error,
                    new MeInvoiceErrorCode(MeInvoiceErrorCategory.MisaUnknown, "NoEchoedResult"));
            }
        }

        return FillRemaining(results);
    }

    private static IReadOnlyList<SaveResult> FillRemaining(SaveResult?[] results)
    {
        var output = new List<SaveResult>(results.Length);
        foreach (var r in results)
        {
            output.Add(r ?? throw new InvalidOperationException("SaveDraftInvoices result slot left unfilled."));
        }
        return output;
    }
}

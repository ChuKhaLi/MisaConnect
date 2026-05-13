using System.Diagnostics;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MisaConnect.EInvoice.Infrastructure.Logging;

/// <summary>
/// Decorator over <see cref="IMeInvoiceClient"/> emitting the FR-014 log shape
/// (endpoint, outcome, errorCategory, durationMs, refId, customerTaxCode,
/// templateId). Never logs buyer info, line content, amounts, tokens, or raw
/// MISA error messages.
/// </summary>
public sealed class MeInvoiceCallLogger : IMeInvoiceClient
{
    private readonly IMeInvoiceClient _inner;
    private readonly ILogger<MeInvoiceCallLogger> _logger;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly IOptions<MisaEInvoiceOptions> _options;

    public MeInvoiceCallLogger(
        IMeInvoiceClient inner,
        ILogger<MeInvoiceCallLogger> logger,
        ICorrelationIdAccessor correlation,
        IOptions<MisaEInvoiceOptions> options)
    {
        _inner = inner;
        _logger = logger;
        _correlation = correlation;
        _options = options;
    }

    public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
        Wrap("AcquireToken", null, null, () => _inner.AcquireTokenAsync(ct));

    public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) =>
        Wrap("ListTemplates", null, null, () => _inner.ListTemplatesAsync(invoiceWithCode, ct));

    public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) =>
        Wrap("Preview", invoice.RefId.Value, invoice.Template?.IPTemplateID, () => _inner.PreviewAsync(invoice, invoiceWithCode, ct));

    public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct)
    {
        var first = batch.Invoices[0].RefId.Value;
        var label = batch.Invoices.Count == 1 ? first : $"{first}+{batch.Invoices.Count - 1}-more";
        var template = batch.Invoices[0].Template?.IPTemplateID;
        return Wrap("SaveDraft", label, template, () => _inner.SaveDraftAsync(batch, invoiceWithCode, ct));
    }

    public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
        Wrap("GetDraftPdfByRefId", refId.Value, null, () => _inner.GetDraftPdfByRefIdAsync(refId, invoiceWithCode, ct));

    public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
        Wrap("DeleteDraft", refId.Value, null, () => _inner.DeleteDraftAsync(refId, invoiceWithCode, ct));

    // Slice 5 — read-only lookup operations. Per R-LU-14 the log entry is
    // one-per-chunk-call (not per RefID); the refIdSet summary is composed at
    // this decorator layer.

    public Task<LookupByRefIdChunkResult> LookupByRefIdAsync(
        IReadOnlyList<RefId> refIds,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        var label = SummariseRefIds(refIds);
        return Wrap("LookupByRefId", label, null, () => _inner.LookupByRefIdAsync(refIds, invoiceWithCode, ct));
    }

    public Task<PagedResult> LookupStandardAsync(
        PagedLookupRequest request,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        var label = $"start={request.Start},length={request.Length}";
        return Wrap("LookupStandard", label, null, () => _inner.LookupStandardAsync(request, invoiceWithCode, ct));
    }

    public Task<PagedResult> LookupCalculatingAsync(
        PagedLookupRequest request,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        var label = $"start={request.Start},length={request.Length}";
        return Wrap("LookupCalculating", label, null, () => _inner.LookupCalculatingAsync(request, invoiceWithCode, ct));
    }

    // Slice 6 — amendment operations. Per FR-066 the structured log entry
    // adds eInvoiceStatus + orgRefId always; changeReason is flag-gated on
    // Misa:Delete:IncludeRawErrorMessage and passed through the scrubber.

    public Task<SaveResult> IssueReplacementAsync(
        Invoice invoice,
        OriginalInvoiceReference originalRef,
        string changeReason,
        bool invoiceWithCode,
        CancellationToken ct) =>
        WrapAmendment(
            "IssueReplacement",
            "3",
            invoice,
            originalRef,
            changeReason,
            () => _inner.IssueReplacementAsync(invoice, originalRef, changeReason, invoiceWithCode, ct));

    public Task<SaveResult> IssueAdjustmentAsync(
        Invoice invoice,
        OriginalInvoiceReference originalRef,
        string changeReason,
        bool invoiceWithCode,
        CancellationToken ct) =>
        WrapAmendment(
            "IssueAdjustment",
            "4",
            invoice,
            originalRef,
            changeReason,
            () => _inner.IssueAdjustmentAsync(invoice, originalRef, changeReason, invoiceWithCode, ct));

    private async Task<SaveResult> WrapAmendment(
        string endpoint,
        string eInvoiceStatus,
        Invoice invoice,
        OriginalInvoiceReference originalRef,
        string changeReason,
        Func<Task<SaveResult>> action)
    {
        var taxCode = _options.Value.TaxCode;
        var cid = _correlation.CorrelationId;
        var refIdLabel = invoice?.RefId.Value;
        var templateId = invoice?.Template?.IPTemplateID;
        var orgRefId = originalRef?.OrgRefID;
        var includeChangeReason = _options.Value.Delete.IncludeRawErrorMessage;
        var redactedChangeReason = includeChangeReason
            ? MeInvoiceLogScrubber.Redact(changeReason ?? string.Empty)
            : null;

        if (includeChangeReason)
        {
            _logger.LogInformation(
                "MeInvoice call started: endpoint={Endpoint}, correlationId={CorrelationId}, customerTaxCode={TaxCode}, templateId={TemplateId}, refId={RefId}, eInvoiceStatus={EInvoiceStatus}, orgRefId={OrgRefId}, changeReason={ChangeReason}",
                endpoint, cid, taxCode, templateId, refIdLabel, eInvoiceStatus, orgRefId, redactedChangeReason);
        }
        else
        {
            _logger.LogInformation(
                "MeInvoice call started: endpoint={Endpoint}, correlationId={CorrelationId}, customerTaxCode={TaxCode}, templateId={TemplateId}, refId={RefId}, eInvoiceStatus={EInvoiceStatus}, orgRefId={OrgRefId}",
                endpoint, cid, taxCode, templateId, refIdLabel, eInvoiceStatus, orgRefId);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            sw.Stop();
            if (includeChangeReason)
            {
                _logger.LogInformation(
                    "MeInvoice call completed: endpoint={Endpoint}, outcome={Outcome}, errorCategory={ErrorCategory}, durationMs={DurationMs}, refId={RefId}, customerTaxCode={TaxCode}, templateId={TemplateId}, eInvoiceStatus={EInvoiceStatus}, orgRefId={OrgRefId}, changeReason={ChangeReason}",
                    endpoint, "Success", null, sw.ElapsedMilliseconds, refIdLabel, taxCode, templateId, eInvoiceStatus, orgRefId, redactedChangeReason);
            }
            else
            {
                _logger.LogInformation(
                    "MeInvoice call completed: endpoint={Endpoint}, outcome={Outcome}, errorCategory={ErrorCategory}, durationMs={DurationMs}, refId={RefId}, customerTaxCode={TaxCode}, templateId={TemplateId}, eInvoiceStatus={EInvoiceStatus}, orgRefId={OrgRefId}",
                    endpoint, "Success", null, sw.ElapsedMilliseconds, refIdLabel, taxCode, templateId, eInvoiceStatus, orgRefId);
            }
            return result;
        }
        catch (MeInvoiceException ex)
        {
            sw.Stop();
            if (includeChangeReason)
            {
                var scrubbed = MeInvoiceLogScrubber.Redact(ex.Message ?? string.Empty);
                _logger.LogWarning(
                    "MeInvoice call completed: endpoint={Endpoint}, outcome={Outcome}, errorCategory={ErrorCategory}, durationMs={DurationMs}, refId={RefId}, customerTaxCode={TaxCode}, templateId={TemplateId}, eInvoiceStatus={EInvoiceStatus}, orgRefId={OrgRefId}, changeReason={ChangeReason}, errorMessage={ErrorMessage}",
                    endpoint, "Error", ex.Category, sw.ElapsedMilliseconds, refIdLabel, taxCode, templateId, eInvoiceStatus, orgRefId, redactedChangeReason, scrubbed);
            }
            else
            {
                _logger.LogWarning(
                    "MeInvoice call completed: endpoint={Endpoint}, outcome={Outcome}, errorCategory={ErrorCategory}, durationMs={DurationMs}, refId={RefId}, customerTaxCode={TaxCode}, templateId={TemplateId}, eInvoiceStatus={EInvoiceStatus}, orgRefId={OrgRefId}",
                    endpoint, "Error", ex.Category, sw.ElapsedMilliseconds, refIdLabel, taxCode, templateId, eInvoiceStatus, orgRefId);
            }
            throw;
        }
        catch (Exception)
        {
            sw.Stop();
            _logger.LogError(
                "MeInvoice call completed: endpoint={Endpoint}, outcome={Outcome}, errorCategory={ErrorCategory}, durationMs={DurationMs}, refId={RefId}, customerTaxCode={TaxCode}, templateId={TemplateId}, eInvoiceStatus={EInvoiceStatus}, orgRefId={OrgRefId}",
                endpoint, "Error", MeInvoiceErrorCategory.MisaUnknown, sw.ElapsedMilliseconds, refIdLabel, taxCode, templateId, eInvoiceStatus, orgRefId);
            throw;
        }
    }

    private static string SummariseRefIds(IReadOnlyList<RefId> refIds)
    {
        if (refIds is null || refIds.Count == 0) return string.Empty;
        if (refIds.Count <= 5)
        {
            return string.Join(',', refIds.Select(r => r.Value));
        }
        return $"{refIds[0].Value}+{refIds.Count - 1}-more";
    }

    private async Task<T> Wrap<T>(string endpoint, string? refIdLabel, string? templateId, Func<Task<T>> action)
    {
        var taxCode = _options.Value.TaxCode;
        var cid = _correlation.CorrelationId;
        _logger.LogInformation(
            "MeInvoice call started: endpoint={Endpoint}, correlationId={CorrelationId}, customerTaxCode={TaxCode}, templateId={TemplateId}, refId={RefId}",
            endpoint, cid, taxCode, templateId, refIdLabel);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            sw.Stop();
            _logger.LogInformation(
                "MeInvoice call completed: endpoint={Endpoint}, outcome={Outcome}, errorCategory={ErrorCategory}, durationMs={DurationMs}, refId={RefId}, customerTaxCode={TaxCode}, templateId={TemplateId}",
                endpoint, "Success", null, sw.ElapsedMilliseconds, refIdLabel, taxCode, templateId);
            return result;
        }
        catch (MeInvoiceException ex)
        {
            sw.Stop();
            // FR-038: when Misa:Delete:IncludeRawErrorMessage == true, include the
            // raw MISA ErrorMessage after MeInvoiceLogScrubber.Redact. When false
            // (default), the errorMessage key is OMITTED ENTIRELY from the log
            // entry — log providers that serialise structured KVPs see no
            // "errorMessage" key.
            if (_options.Value.Delete.IncludeRawErrorMessage)
            {
                var scrubbed = MeInvoiceLogScrubber.Redact(ex.Message ?? string.Empty);
                _logger.LogWarning(
                    "MeInvoice call completed: endpoint={Endpoint}, outcome={Outcome}, errorCategory={ErrorCategory}, durationMs={DurationMs}, refId={RefId}, customerTaxCode={TaxCode}, templateId={TemplateId}, errorMessage={ErrorMessage}",
                    endpoint, "Error", ex.Category, sw.ElapsedMilliseconds, refIdLabel, taxCode, templateId, scrubbed);
            }
            else
            {
                _logger.LogWarning(
                    "MeInvoice call completed: endpoint={Endpoint}, outcome={Outcome}, errorCategory={ErrorCategory}, durationMs={DurationMs}, refId={RefId}, customerTaxCode={TaxCode}, templateId={TemplateId}",
                    endpoint, "Error", ex.Category, sw.ElapsedMilliseconds, refIdLabel, taxCode, templateId);
            }
            throw;
        }
        catch (Exception)
        {
            sw.Stop();
            _logger.LogError(
                "MeInvoice call completed: endpoint={Endpoint}, outcome={Outcome}, errorCategory={ErrorCategory}, durationMs={DurationMs}, refId={RefId}, customerTaxCode={TaxCode}, templateId={TemplateId}",
                endpoint, "Error", MeInvoiceErrorCategory.MisaUnknown, sw.ElapsedMilliseconds, refIdLabel, taxCode, templateId);
            throw;
        }
    }
}

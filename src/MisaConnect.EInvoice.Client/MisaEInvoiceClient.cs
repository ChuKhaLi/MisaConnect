using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Client.Mapping;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Client;

/// <summary>
/// Default <see cref="IMisaEInvoiceClient"/> implementation. Delegates to shared
/// Application use cases so the library and HTTP API surfaces converge on one core.
/// </summary>
public sealed class MisaEInvoiceClient : IMisaEInvoiceClient
{
    private readonly ListActiveTemplates _listTemplates;
    private readonly PreviewInvoice _preview;
    private readonly SaveDraftInvoices _saveDraft;
    private readonly GetDraftPdfByRefId _getPdf;
    private readonly DeleteDraftInvoice _deleteDraft;
    private readonly LookupByRefIds _lookupByRefIds;
    private readonly LookupStandard _lookupStandard;
    private readonly LookupCalculating _lookupCalculating;
    private readonly IssueReplacementInvoice _issueReplacement;
    private readonly IssueAdjustmentInvoice _issueAdjustment;

    public MisaEInvoiceClient(
        ListActiveTemplates listTemplates,
        PreviewInvoice preview,
        SaveDraftInvoices saveDraft,
        GetDraftPdfByRefId getPdf,
        DeleteDraftInvoice deleteDraft,
        LookupByRefIds lookupByRefIds,
        LookupStandard lookupStandard,
        LookupCalculating lookupCalculating,
        IssueReplacementInvoice issueReplacement,
        IssueAdjustmentInvoice issueAdjustment)
    {
        _listTemplates = listTemplates;
        _preview = preview;
        _saveDraft = saveDraft;
        _getPdf = getPdf;
        _deleteDraft = deleteDraft;
        _lookupByRefIds = lookupByRefIds;
        _lookupStandard = lookupStandard;
        _lookupCalculating = lookupCalculating;
        _issueReplacement = issueReplacement;
        _issueAdjustment = issueAdjustment;
    }

    public async Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(bool withCode = true, CancellationToken ct = default)
    {
        var templates = await _listTemplates.ExecuteAsync(withCode, ct).ConfigureAwait(false);
        var list = new List<TemplateDto>(templates.Count);
        foreach (var t in templates) list.Add(DtoMapper.ToDto(t));
        return list;
    }

    public async Task<PdfDocumentDto> PreviewAsync(InvoiceDto invoice, bool withCode = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var domain = DtoMapper.ToDomain(invoice);
        var result = await _preview.ExecuteAsync(domain, withCode, ct).ConfigureAwait(false);
        return DtoMapper.ToDto(result.Pdf);
    }

    public async Task<IReadOnlyList<SaveResultDto>> SaveDraftAsync(IReadOnlyList<InvoiceDto> invoices, bool withCode = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        var domain = new List<Domain.Invoices.Invoice>(invoices.Count);
        foreach (var dto in invoices) domain.Add(DtoMapper.ToDomain(dto));
        var results = await _saveDraft.ExecuteAsync(domain, withCode, ct).ConfigureAwait(false);
        var list = new List<SaveResultDto>(results.Count);
        foreach (var r in results) list.Add(DtoMapper.ToDto(r));
        return list;
    }

    public async Task<PdfDocumentDto> GetDraftPdfAsync(string refId, bool withCode = true, CancellationToken ct = default)
    {
        var pdf = await _getPdf.ExecuteAsync(refId, withCode, ct).ConfigureAwait(false);
        return DtoMapper.ToDto(pdf);
    }

    public Task<DeleteDraftOutcome> DeleteDraftAsync(string refId, bool invoiceWithCode, CancellationToken ct = default)
    {
        RefId parsed;
        try
        {
            parsed = RefId.From(refId);
        }
        catch (ArgumentException ex)
        {
            throw new MeInvoiceException(
                MeInvoiceErrorCategory.Validation,
                "RefIdInvalid",
                ex.Message);
        }

        return _deleteDraft.ExecuteAsync(new DeleteDraftRequest(parsed, invoiceWithCode), ct);
    }

    public async Task<LookupBatchOutcomeDto> LookupByRefIdAsync(
        IReadOnlyList<string> refIds,
        bool invoiceWithCode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(refIds);
        if (refIds.Count == 0)
        {
            throw new MeInvoiceException(
                MeInvoiceErrorCategory.Validation,
                "RefIdsEmpty",
                "RefIds list cannot be empty.");
        }

        var parsed = new List<RefId>(refIds.Count);
        foreach (var s in refIds)
        {
            try
            {
                parsed.Add(RefId.From(s));
            }
            catch (ArgumentException ex)
            {
                throw new MeInvoiceException(
                    MeInvoiceErrorCategory.Validation,
                    "RefIdInvalid",
                    ex.Message);
            }
        }

        var outcome = await _lookupByRefIds.ExecuteAsync(
            new LookupByRefIdRequest(parsed, invoiceWithCode), ct).ConfigureAwait(false);
        return LookupDtoMapper.ToDto(outcome);
    }

    public async Task<PagedLookupResultDto> LookupStandardAsync(
        PagedLookupRequestDto request,
        bool invoiceWithCode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var domain = LookupDtoMapper.ToDomain(request);
        var result = await _lookupStandard.ExecuteAsync(domain, invoiceWithCode, ct).ConfigureAwait(false);
        return LookupDtoMapper.ToDto(result);
    }

    public async Task<PagedLookupResultDto> LookupCalculatingAsync(
        PagedLookupRequestDto request,
        bool invoiceWithCode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var domain = LookupDtoMapper.ToDomain(request);
        var result = await _lookupCalculating.ExecuteAsync(domain, invoiceWithCode, ct).ConfigureAwait(false);
        return LookupDtoMapper.ToDto(result);
    }

    public async Task<AmendmentResultDto> IssueReplacementAsync(
        ReplacementRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var domain = AmendmentDtoMapper.ToDomain(request);
        var result = await _issueReplacement.ExecuteAsync(domain, ct).ConfigureAwait(false);
        return AmendmentDtoMapper.ToDto(result, request.OriginalRef.OrgRefID);
    }

    public async Task<AmendmentResultDto> IssueAdjustmentAsync(
        AdjustmentRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var domain = AmendmentDtoMapper.ToDomain(request);
        var result = await _issueAdjustment.ExecuteAsync(domain, ct).ConfigureAwait(false);
        return AmendmentDtoMapper.ToDto(result, request.OriginalRef.OrgRefID);
    }
}

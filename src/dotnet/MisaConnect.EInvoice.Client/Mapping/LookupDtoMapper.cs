using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Client.Dtos;

namespace MisaConnect.EInvoice.Client.Mapping;

internal static class LookupDtoMapper
{
    public static PagedLookupRequest ToDomain(PagedLookupRequestDto dto)
    {
        var sort = string.IsNullOrWhiteSpace(dto.Sort) ? "InvDate" : dto.Sort;
        return new PagedLookupRequest(
            Start: dto.Start,
            Length: dto.Length,
            Sort: sort,
            FromDate: dto.FromDate,
            ToDate: dto.ToDate,
            PublishStatus: dto.PublishStatus);
    }

    public static InvoiceSnapshotDto ToDto(InvoiceSnapshot snap) => new(
        RefId: snap.RefId.Value ?? string.Empty,
        InvoiceTemplateID: snap.InvoiceTemplateID,
        InvSeries: snap.InvSeries,
        InvDate: snap.InvDate,
        InvNo: snap.InvNo,
        AccountObjectTaxCode: snap.AccountObjectTaxCode,
        AccountObjectName: snap.AccountObjectName,
        TotalSaleAmount: snap.TotalSaleAmount,
        TotalVATAmount: snap.TotalVATAmount,
        TotalAmount: snap.TotalAmount,
        TotalSaleAmountOC: snap.TotalSaleAmountOC,
        TotalVATAmountOC: snap.TotalVATAmountOC,
        TotalAmountOC: snap.TotalAmountOC,
        RawEInvoiceStatus: snap.RawEInvoiceStatus,
        RawPublishStatus: snap.RawPublishStatus,
        Status: snap.Status.ToString(),
        OrgRefID: snap.OrgRefID?.Value,
        CreatedDate: snap.CreatedDate,
        ModifiedDate: snap.ModifiedDate);

    public static LookupOutcomeDto ToDto(LookupOutcome outcome) => new(
        RefId: outcome.RefId.Value ?? string.Empty,
        Status: outcome.Status.ToString(),
        Snapshot: outcome.Snapshot is null ? null : ToDto(outcome.Snapshot),
        ErrorCode: outcome.RawErrorCode,
        Message: outcome.Message);

    public static LookupBatchOutcomeDto ToDto(LookupBatchOutcome outcome)
    {
        if (outcome.Status == LookupBatchStatus.Validation)
        {
            return new LookupBatchOutcomeDto(
                Status: "Validation",
                Outcomes: null,
                Reason: outcome.Reason);
        }

        var list = new List<LookupOutcomeDto>(outcome.Outcomes!.Count);
        foreach (var o in outcome.Outcomes) list.Add(ToDto(o));
        return new LookupBatchOutcomeDto(
            Status: "Completed",
            Outcomes: list,
            Reason: null);
    }

    public static PagedLookupResultDto ToDto(PagedResult result)
    {
        var items = new List<InvoiceSnapshotDto>(result.Items.Count);
        foreach (var snap in result.Items) items.Add(ToDto(snap));
        return new PagedLookupResultDto(items, result.Start, result.Length, result.ReturnedCount);
    }
}

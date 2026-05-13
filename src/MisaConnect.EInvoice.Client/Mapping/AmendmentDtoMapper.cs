using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Client.Mapping;

/// <summary>
/// Slice 6 — converts between the public amendment DTOs (Client layer) and
/// the Application-layer request value objects (<see cref="ReplacementRequest"/>,
/// <see cref="AdjustmentRequest"/>, <see cref="OriginalInvoiceReference"/>),
/// plus a result-side projection from the domain <see cref="SaveResult"/>
/// to <see cref="AmendmentResultDto"/> echoing the caller's <c>OrgRefID</c>.
/// </summary>
public static class AmendmentDtoMapper
{
    public static OriginalInvoiceReference ToDomain(OriginalInvoiceReferenceDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new OriginalInvoiceReference(
            OrgRefID: dto.OrgRefID,
            OrgInvNo: dto.OrgInvNo,
            OrgInvTemplateNo: dto.OrgInvTemplateNo,
            OrgInvSeries: dto.OrgInvSeries,
            OrgInvDate: dto.OrgInvDate);
    }

    public static ReplacementRequest ToDomain(ReplacementRequestDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new ReplacementRequest(
            Invoice: DtoMapper.ToDomain(dto.Invoice),
            OriginalRef: ToDomain(dto.OriginalRef),
            ChangeReason: dto.ChangeReason,
            InvoiceWithCode: dto.InvoiceWithCode);
    }

    public static AdjustmentRequest ToDomain(AdjustmentRequestDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new AdjustmentRequest(
            Invoice: DtoMapper.ToDomain(dto.Invoice),
            OriginalRef: ToDomain(dto.OriginalRef),
            ChangeReason: dto.ChangeReason,
            InvoiceWithCode: dto.InvoiceWithCode);
    }

    public static AmendmentResultDto ToDto(SaveResult result, string orgRefId)
    {
        ArgumentNullException.ThrowIfNull(result);
        IReadOnlyList<ValidationFailureDto>? failures = null;
        if (result.LocalFailures is { Count: > 0 })
        {
            var list = new List<ValidationFailureDto>(result.LocalFailures.Count);
            foreach (var f in result.LocalFailures)
            {
                list.Add(new ValidationFailureDto(f.FieldPath, f.Message, f.RuleId));
            }
            failures = list;
        }
        return new AmendmentResultDto(
            RefId: result.RefId.Value,
            Outcome: result.Outcome == SaveOutcome.Success ? SaveOutcomeDto.Success : SaveOutcomeDto.Error,
            ErrorCategory: result.Error?.Category.ToString(),
            RawErrorCode: result.Error?.RawCode,
            ErrorMessage: result.Error?.Detail,
            Failures: failures,
            OrgRefId: orgRefId);
    }
}

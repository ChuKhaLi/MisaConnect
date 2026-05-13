namespace MisaConnect.EInvoice.Application.Results;

public enum LookupBatchStatus
{
    Completed,
    Validation,
}

/// <summary>
/// Operation-level wrapper for batch lookup (R-LU-07). <see cref="Outcomes"/>
/// is non-null iff <see cref="Status"/> is
/// <see cref="LookupBatchStatus.Completed"/>; an operation-level
/// <see cref="LookupBatchStatus.Validation"/> means the request never reached
/// the chunking phase, so there is no per-RefID list.
/// </summary>
public sealed record LookupBatchOutcome(
    LookupBatchStatus Status,
    IReadOnlyList<LookupOutcome>? Outcomes,
    string? Reason);

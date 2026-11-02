namespace MisaConnect.EInvoice.Application.Results;

/// <summary>
/// Operation-level result for paged lookup (R-LU-04). <see cref="ReturnedCount"/>
/// is denormalised (always equal to <c>Items.Count</c>) so non-.NET callers
/// can detect end-of-results via <c>ReturnedCount &lt; Length</c> without
/// indexing into <see cref="Items"/>.
/// </summary>
public sealed record PagedResult(
    IReadOnlyList<InvoiceSnapshot> Items,
    int Start,
    int Length,
    int ReturnedCount);

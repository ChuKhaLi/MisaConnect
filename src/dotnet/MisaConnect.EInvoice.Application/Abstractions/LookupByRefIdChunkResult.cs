using MisaConnect.EInvoice.Application.Results;

namespace MisaConnect.EInvoice.Application.Abstractions;

/// <summary>
/// Infrastructure-facing result of a single chunk call to MISA's
/// <c>/webapp/getlist</c>. The use case
/// (<see cref="MisaConnect.EInvoice.Application.UseCases.LookupByRefIds"/>) maps
/// <see cref="ErrorCode"/> to the per-RefID <see cref="LookupStatus"/>
/// category and spreads chunk-level failures to each RefID in the chunk
/// per R-LU-10.
/// </summary>
public sealed record LookupByRefIdChunkResult(
    IReadOnlyList<InvoiceSnapshot> Snapshots,
    string? ErrorCode,
    string? ErrorMessage);

using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Application-level request shape for batch lookup. The surface adapter
/// (HTTP API / Library) parses caller-supplied strings into
/// <see cref="RefId"/> before constructing this record; the use case treats
/// the list as pre-validated except for emptiness (FR-047).
/// </summary>
public sealed record LookupByRefIdRequest(
    IReadOnlyList<RefId> RefIds,
    bool InvoiceWithCode)
{
    /// <summary>
    /// Returns <c>null</c> when the request shape is valid, or a human-readable
    /// reason string when the operation-level <c>Validation</c> outcome must
    /// be surfaced (per FR-047 / R-LU-07).
    /// </summary>
    public string? Validate()
    {
        if (RefIds is null) return "RefIds list cannot be null.";
        if (RefIds.Count == 0) return "RefIds list cannot be empty.";
        return null;
    }
}

using MisaConnect.EInvoice.Domain.Errors;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Application-level request shape shared by both paged operations
/// (<see cref="LookupStandard"/> and <see cref="LookupCalculating"/>). No
/// template-class discriminator — the caller picks the operation matching
/// the template class they're querying (R-LU-02).
/// </summary>
public sealed record PagedLookupRequest(
    int Start,
    int Length,
    string Sort,
    DateOnly FromDate,
    DateOnly ToDate,
    int? PublishStatus)
{
    private static readonly int[] AllowedPublishStatuses = new[] { 0, 4, 6, 7 };

    /// <summary>
    /// Returns <c>null</c> when the request is valid, or a populated
    /// <see cref="ValidationFailure"/> for the offending field (FR-047 /
    /// R-LU-07). Only the first violation is surfaced — the surface
    /// adapter wraps the failure in a single-element list so the API
    /// response shape matches the contract.
    /// </summary>
    public ValidationFailure? Validate()
    {
        if (Start < 0)
        {
            return new ValidationFailure("Start", "Start must be >= 0.", "FR-047");
        }

        if (Length < 1 || Length > 100)
        {
            return new ValidationFailure("Length", "Length must be in [1, 100].", "FR-047");
        }

        if (FromDate > ToDate)
        {
            return new ValidationFailure("FromDate", "FromDate must be <= ToDate.", "FR-047");
        }

        if (PublishStatus is { } ps && Array.IndexOf(AllowedPublishStatuses, ps) < 0)
        {
            return new ValidationFailure(
                "PublishStatus",
                "PublishStatus must be one of {0, 4, 6, 7} when set.",
                "FR-047");
        }

        return null;
    }
}

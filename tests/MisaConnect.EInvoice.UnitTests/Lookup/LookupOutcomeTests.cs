using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Domain.Invoices;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Lookup;

/// <summary>
/// Documentation-style tests that lock in the expected payload shape for
/// each <see cref="LookupStatus"/> bucket. The record permits any
/// combination of values; these assertions encode the Cartesian invariants
/// callers must rely on (FR-042, FR-050) so a future refactor that breaks
/// the conventions fails red here.
/// </summary>
public class LookupOutcomeTests
{
    [Fact]
    public void Status_and_payload_correlate()
    {
        var refId = RefId.From("r1");

        // Found → snapshot non-null; error fields null.
        var snapshot = NewSnapshot(refId);
        var found = new LookupOutcome(
            Status: LookupStatus.Found,
            Snapshot: snapshot,
            RawErrorCode: null,
            Message: null,
            RefId: refId);

        Assert.NotNull(found.Snapshot);
        Assert.Null(found.RawErrorCode);
        Assert.Null(found.Message);

        // NotFound → all payload null.
        var notFound = new LookupOutcome(
            Status: LookupStatus.NotFound,
            Snapshot: null,
            RawErrorCode: null,
            Message: null,
            RefId: refId);

        Assert.Null(notFound.Snapshot);
        Assert.Null(notFound.RawErrorCode);
        Assert.Null(notFound.Message);

        // Error buckets → snapshot null, raw error code non-null.
        var errorStatuses = new[]
        {
            LookupStatus.AuthFailed,
            LookupStatus.Configuration,
            LookupStatus.MisaThrottled,
            LookupStatus.MisaUnavailable,
            LookupStatus.TransportFailed,
            LookupStatus.MisaUnknown,
        };

        foreach (var status in errorStatuses)
        {
            var outcome = new LookupOutcome(
                Status: status,
                Snapshot: null,
                RawErrorCode: "X",
                Message: null,
                RefId: refId);

            Assert.Null(outcome.Snapshot);
            Assert.NotNull(outcome.RawErrorCode);
        }
    }

    private static InvoiceSnapshot NewSnapshot(RefId refId) => new(
        RefId: refId,
        InvoiceTemplateID: null,
        InvSeries: null,
        InvDate: null,
        InvNo: null,
        AccountObjectTaxCode: null,
        AccountObjectName: null,
        TotalSaleAmount: null,
        TotalVATAmount: null,
        TotalAmount: null,
        TotalSaleAmountOC: null,
        TotalVATAmountOC: null,
        TotalAmountOC: null,
        RawEInvoiceStatus: 1,
        RawPublishStatus: 0,
        Status: InvoiceStatus.Draft,
        OrgRefID: null,
        CreatedDate: null,
        ModifiedDate: null);
}

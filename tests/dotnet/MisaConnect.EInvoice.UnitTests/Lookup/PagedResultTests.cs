using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Domain.Invoices;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Lookup;

/// <summary>
/// Documents the denormalised <see cref="PagedResult.ReturnedCount"/>
/// invariant (R-LU-04) — callers MUST be able to detect end-of-results
/// via <c>ReturnedCount &lt; Length</c> without indexing into
/// <see cref="PagedResult.Items"/>. Production code that constructs
/// <see cref="PagedResult"/> is responsible for keeping the two in sync.
/// </summary>
public class PagedResultTests
{
    [Fact]
    public void ReturnedCount_equals_items_count()
    {
        var items = new List<InvoiceSnapshot>();
        for (var i = 0; i < 17; i++)
        {
            items.Add(NewSnapshot(RefId.From($"r{i}")));
        }

        var result = new PagedResult(
            Items: items,
            Start: 0,
            Length: 100,
            ReturnedCount: items.Count);

        Assert.Equal(result.Items.Count, result.ReturnedCount);
        Assert.Equal(17, result.ReturnedCount);
        Assert.True(result.ReturnedCount < result.Length);
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

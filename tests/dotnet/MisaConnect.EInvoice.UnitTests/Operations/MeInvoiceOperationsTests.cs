using MisaConnect.EInvoice.Application.Operations;
using MisaConnect.EInvoice.Application.UseCases;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Operations;

public class MeInvoiceOperationsTests
{
    [Fact]
    public void All_has_exactly_ten_user_facing_operations()
    {
        // Slice 1 had four user-facing operations; slice 2 (delete-draft-invoice)
        // appended the fifth. Slice 5 appends three lookup operations
        // (lookup-by-refid, lookup-paged-standard, lookup-paged-calculating).
        // Slice 6 appends two amendment operations (issue-replacement-invoice,
        // issue-adjustment-invoice).
        Assert.Equal(10, MeInvoiceOperations.All.Count);
    }

    [Fact]
    public void All_includes_expected_names()
    {
        var names = MeInvoiceOperations.All.Select(o => o.Name).ToHashSet();
        Assert.Contains("list-templates", names);
        Assert.Contains("preview-invoice", names);
        Assert.Contains("save-draft-invoices", names);
        Assert.Contains("get-draft-pdf-by-refid", names);
        Assert.Contains("delete-draft-invoice", names);
        Assert.Contains("lookup-by-refid", names);
        Assert.Contains("lookup-paged-standard", names);
        Assert.Contains("lookup-paged-calculating", names);
        Assert.Contains("issue-replacement-invoice", names);
        Assert.Contains("issue-adjustment-invoice", names);
    }

    [Fact]
    public void All_does_not_include_EnsureAccessToken()
    {
        Assert.DoesNotContain(MeInvoiceOperations.All, op => op.UseCaseType == typeof(EnsureAccessToken));
        Assert.DoesNotContain(MeInvoiceOperations.All, op =>
            op.Name is "ensure-access-token" or "acquire-token" or "refresh-token");
    }
}

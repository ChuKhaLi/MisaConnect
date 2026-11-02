using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Domain.Errors;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.ErrorMapping;

public class MeInvoiceErrorMapperTests
{
    [Theory]
    [InlineData("InvalidAppID", MeInvoiceErrorCategory.Configuration)]
    [InlineData("InActiveAppID", MeInvoiceErrorCategory.Configuration)]
    [InlineData("UnAuthorize", MeInvoiceErrorCategory.Authentication)]
    [InlineData("TokenExpiredCode", MeInvoiceErrorCategory.Authentication)]
    [InlineData("DuplicateInvoiceRefID", MeInvoiceErrorCategory.DuplicateOrUniqueness)]
    [InlineData("DeclarationNotExist", MeInvoiceErrorCategory.DeclarationState)]
    [InlineData("InvalidXMLContainEmoji", MeInvoiceErrorCategory.Validation)]
    [InlineData("InvoiceQuantityTooLarge", MeInvoiceErrorCategory.Validation)]
    [InlineData("XMLTooLong", MeInvoiceErrorCategory.Validation)]
    [InlineData("InvoiceTemplateNotExist", MeInvoiceErrorCategory.TemplateState)]
    [InlineData("InvalidTransactionID", MeInvoiceErrorCategory.ResourceNotFound)]
    [InlineData("SignatureEmpty", MeInvoiceErrorCategory.Signing)]
    [InlineData("Exception", MeInvoiceErrorCategory.MisaUnavailable)]
    public void Maps_known_code_to_category(string raw, MeInvoiceErrorCategory expected)
    {
        var mapped = MeInvoiceErrorMapper.Map(raw, null);
        Assert.Equal(expected, mapped.Category);
        Assert.Equal(raw, mapped.RawCode);
    }

    [Fact]
    public void Unknown_code_maps_to_MisaUnknown_with_raw_attached()
    {
        var mapped = MeInvoiceErrorMapper.Map("WildcardNewCode", "msg");
        Assert.Equal(MeInvoiceErrorCategory.MisaUnknown, mapped.Category);
        Assert.Equal("WildcardNewCode", mapped.RawCode);
    }

    [Fact]
    public void Null_and_empty_raw_maps_to_MisaUnknown()
    {
        Assert.Equal(MeInvoiceErrorCategory.MisaUnknown, MeInvoiceErrorMapper.Map("", "m").Category);
        Assert.Equal(MeInvoiceErrorCategory.MisaUnknown, MeInvoiceErrorMapper.Map(null, "m").Category);
    }

    [Fact]
    public void Templated_InvoiceDetail_prefix_captures_field()
    {
        var mapped = MeInvoiceErrorMapper.Map("InvoiceDetail_Description", "msg");
        Assert.Equal(MeInvoiceErrorCategory.Validation, mapped.Category);
        Assert.Equal("Description", mapped.Field);
    }

    [Fact]
    public void Templated_RequireError_prefix_captures_field()
    {
        var mapped = MeInvoiceErrorMapper.Map("RequireError_AccountObjectName", null);
        Assert.Equal(MeInvoiceErrorCategory.Validation, mapped.Category);
        Assert.Equal("AccountObjectName", mapped.Field);
    }

    [Fact]
    public void Templated_StockInTaxCode_prefix_captures_field_and_component()
    {
        var mapped = MeInvoiceErrorMapper.Map("StockInTaxCode_NotInfo_0_TaxCode", null);
        Assert.Equal(MeInvoiceErrorCategory.OtherWorkflow, mapped.Category);
        Assert.Equal("0", mapped.Field);
        Assert.Equal("TaxCode", mapped.Component);
    }

    [Fact]
    public void Every_entry_in_ExactMap_is_reachable_via_Map()
    {
        foreach (var kvp in MeInvoiceErrorMapper.ExactMap)
        {
            var mapped = MeInvoiceErrorMapper.Map(kvp.Key, null);
            Assert.Equal(kvp.Value, mapped.Category);
        }
    }
}

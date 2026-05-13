using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Domain.Errors;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.ErrorMapping;

/// <summary>
/// Slice 6 (FR-064) — the four amendment-specific MISA codes
/// (<c>InvoiceCannotReplace</c>, <c>InvoiceCannotAdjust</c>,
/// <c>InvoiceCannotReplaceByStatusNew</c>, <c>HasAdjustmentInvoice</c>) all
/// surface as <see cref="MeInvoiceErrorCategory.Replacement"/> per slice 1's
/// forward-thinking registration, in BOTH replacement and adjustment call
/// contexts (T-RP-09 / T-RP-10 / T-AJ-11 / T-AJ-12).
/// </summary>
public class MeInvoiceErrorMapperAmendmentTests
{
    [Theory]
    [InlineData("InvoiceCannotReplace")]
    [InlineData("InvoiceCannotAdjust")]
    [InlineData("InvoiceCannotReplaceByStatusNew")]
    [InlineData("HasAdjustmentInvoice")]
    public void Amendment_codes_map_to_Replacement_category(string rawCode)
    {
        var mapped = MeInvoiceErrorMapper.Map(rawCode, null);
        Assert.Equal(MeInvoiceErrorCategory.Replacement, mapped.Category);
        Assert.Equal(rawCode, mapped.RawCode);
    }

    [Fact]
    public void InvoiceCannotReplace_maps_to_Replacement_category()
    {
        var mapped = MeInvoiceErrorMapper.Map("InvoiceCannotReplace", "Invoice cannot be replaced.");
        Assert.Equal(MeInvoiceErrorCategory.Replacement, mapped.Category);
    }

    [Fact]
    public void HasAdjustmentInvoice_on_replacement_maps_to_Replacement()
    {
        var mapped = MeInvoiceErrorMapper.Map("HasAdjustmentInvoice", "The original already has an adjustment.");
        Assert.Equal(MeInvoiceErrorCategory.Replacement, mapped.Category);
    }

    [Fact]
    public void InvoiceCannotAdjust_maps_to_Replacement_category()
    {
        var mapped = MeInvoiceErrorMapper.Map("InvoiceCannotAdjust", "Invoice cannot be adjusted.");
        Assert.Equal(MeInvoiceErrorCategory.Replacement, mapped.Category);
    }

    [Fact]
    public void HasAdjustmentInvoice_on_adjustment_maps_to_Replacement()
    {
        var mapped = MeInvoiceErrorMapper.Map("HasAdjustmentInvoice", null);
        Assert.Equal(MeInvoiceErrorCategory.Replacement, mapped.Category);
    }
}

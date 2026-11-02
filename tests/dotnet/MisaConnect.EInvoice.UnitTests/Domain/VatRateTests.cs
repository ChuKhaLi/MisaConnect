using MisaConnect.EInvoice.Domain.Invoices;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Domain;

public class VatRateTests
{
    [Fact]
    public void FromName_KCT()
    {
        Assert.Equal("KCT", VatRate.FromName("KCT").Name);
        Assert.Equal(-1, VatRate.FromName("KCT").Numeric);
    }

    [Fact]
    public void FromName_10pct()
    {
        Assert.Equal("10%", VatRate.FromName("10%").Name);
        Assert.Equal(10, VatRate.FromName("10%").Numeric);
    }

    [Fact]
    public void FromName_KHAC()
    {
        var v = VatRate.FromName("KHAC:3.5%");
        Assert.Equal("KHAC:3.5%", v.Name);
        Assert.Null(v.Numeric);
        Assert.Equal(3.5m, v.OtherRate);
    }

    [Fact]
    public void Other_3_5_renders_KHAC()
    {
        Assert.Equal("KHAC:3.5%", VatRate.Other(3.5m).Name);
    }

    [Fact]
    public void FromNumeric_minus1_is_KCT()
    {
        Assert.Equal(VatRate.Kct, VatRate.FromNumeric(-1));
    }
}

using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Mapping;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Mapping;

public class VatRateMapperTests
{
    [Theory]
    [InlineData(-1, "KCT")]
    [InlineData(-3, "KKKNT")]
    [InlineData(0, "0%")]
    [InlineData(5, "5%")]
    [InlineData(8, "8%")]
    [InlineData(10, "10%")]
    public void Round_trip_integer_to_name(int numeric, string name)
    {
        var vat = VatRate.FromNumeric(numeric);
        var wire = VatRateMapper.ToWire(vat);
        Assert.Equal(name, wire.Name);
        Assert.Equal(numeric, wire.Numeric);
    }

    [Fact]
    public void Other_KHAC_returns_null_numeric()
    {
        var vat = VatRate.Other(3.5m);
        var wire = VatRateMapper.ToWire(vat);
        Assert.Null(wire.Numeric);
        Assert.Equal("KHAC:3.5%", wire.Name);
    }
}

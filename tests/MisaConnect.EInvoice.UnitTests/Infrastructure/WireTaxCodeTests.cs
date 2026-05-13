using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Auth;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Infrastructure;

public class WireTaxCodeTests
{
    [Fact]
    public void Compose_concatenates_with_hyphen()
    {
        var opts = new MisaEInvoiceOptions { TaxCode = "0000000000", AppId = "999" };
        Assert.Equal("0000000000-999", WireTaxCode.Compose(opts));
    }

    [Fact]
    public void TryParse_splits_on_rightmost_hyphen()
    {
        Assert.True(WireTaxCode.TryParse("0000000000-999", out var tax, out var app));
        Assert.Equal("0000000000", tax);
        Assert.Equal("999", app);
    }

    [Fact]
    public void TryParse_with_hyphen_in_taxcode_uses_rightmost()
    {
        Assert.True(WireTaxCode.TryParse("0000000001-A-999", out var tax, out var app));
        Assert.Equal("0000000001-A", tax);
        Assert.Equal("999", app);
    }

    [Fact]
    public void TryParse_returns_false_when_no_hyphen()
    {
        Assert.False(WireTaxCode.TryParse("no-hyphen-at-end-", out _, out _));
    }
}

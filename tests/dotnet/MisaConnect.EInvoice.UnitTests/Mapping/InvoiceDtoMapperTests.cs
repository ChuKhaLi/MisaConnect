using MisaConnect.EInvoice.Infrastructure.MeInvoice.Mapping;
using MisaConnect.EInvoice.TestSupport.Fixtures;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Mapping;

public class InvoiceDtoMapperTests
{
    [Fact]
    public void ToWire_preserves_RefId_and_line_count()
    {
        var invoice = SampleInvoices.TypicalVatInvoice();
        var dto = InvoiceDtoMapper.ToWire(invoice);
        Assert.Equal(invoice.RefId.Value, dto.RefID);
        Assert.Equal(invoice.Lines.Count, dto.InvoiceDetails.Count);
        Assert.Equal("1", dto.EInvoiceStatus);
    }
}

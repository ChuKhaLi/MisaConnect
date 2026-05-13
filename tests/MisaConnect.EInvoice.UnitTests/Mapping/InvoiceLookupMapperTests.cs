using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Mapping;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Wire;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Mapping;

/// <summary>
/// Unit tests for <see cref="InvoiceLookupMapper.FromWire"/> — the
/// wire-to-snapshot translation layer for slice 5 lookup endpoints
/// (R-LU-20). Exercises string-to-int coercion, date parsing,
/// <c>+07:00</c> assumption for naive datetimes, and the OrgRefID
/// optionality contract.
/// </summary>
public class InvoiceLookupMapperTests
{
    [Fact]
    public void Maps_wire_dto_to_snapshot()
    {
        var dto = new InvoiceDataLookupDto(
            RefID: "r1",
            InvoiceTemplateID: null,
            InvSeries: "1C26TAA",
            InvDate: "2026-05-12",
            InvNo: null,
            AccountObjectTaxCode: null,
            AccountObjectName: null,
            TotalSaleAmount: null,
            TotalVATAmount: null,
            TotalAmount: 1100000m,
            TotalSaleAmountOC: null,
            TotalVATAmountOC: null,
            TotalAmountOC: null,
            EInvoiceStatus: "1",
            PublishStatus: "0",
            OrgRefID: null,
            CreatedDate: "2026-05-12T10:30:00",
            ModifiedDate: "2026-05-12T10:30:00");

        var snapshot = InvoiceLookupMapper.FromWire(dto);

        Assert.Equal("r1", snapshot.RefId.Value);
        Assert.Equal("1C26TAA", snapshot.InvSeries);
        Assert.Equal(new DateTime(2026, 5, 12), snapshot.InvDate);
        Assert.Equal(1, snapshot.RawEInvoiceStatus);
        Assert.Equal(0, snapshot.RawPublishStatus);
        Assert.Equal(InvoiceStatus.Draft, snapshot.Status);
        Assert.Null(snapshot.OrgRefID);
        Assert.Equal(1100000m, snapshot.TotalAmount);
        Assert.True(snapshot.CreatedDate.HasValue);
        Assert.Equal(TimeSpan.FromHours(7), snapshot.CreatedDate!.Value.Offset);
    }

    [Fact]
    public void OrgRefID_null_when_EInvoiceStatus_1()
    {
        var dto = new InvoiceDataLookupDto(
            RefID: "r1",
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
            EInvoiceStatus: "1",
            PublishStatus: "0",
            OrgRefID: null,
            CreatedDate: null,
            ModifiedDate: null);

        var snapshot = InvoiceLookupMapper.FromWire(dto);

        Assert.Null(snapshot.OrgRefID);
        Assert.Equal(InvoiceStatus.Draft, snapshot.Status);
    }

    [Fact]
    public void OrgRefID_populated_when_EInvoiceStatus_3()
    {
        var dto = new InvoiceDataLookupDto(
            RefID: "r1",
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
            EInvoiceStatus: "3",
            PublishStatus: "0",
            OrgRefID: "abc-123",
            CreatedDate: null,
            ModifiedDate: null);

        var snapshot = InvoiceLookupMapper.FromWire(dto);

        Assert.NotNull(snapshot.OrgRefID);
        Assert.Equal("abc-123", snapshot.OrgRefID!.Value.Value);
        Assert.Equal(InvoiceStatus.Replaced, snapshot.Status);
    }

    [Fact]
    public void Unparseable_InvDate_yields_null()
    {
        var dto = new InvoiceDataLookupDto(
            RefID: "r1",
            InvoiceTemplateID: null,
            InvSeries: null,
            InvDate: "not-a-date",
            InvNo: null,
            AccountObjectTaxCode: null,
            AccountObjectName: null,
            TotalSaleAmount: null,
            TotalVATAmount: null,
            TotalAmount: null,
            TotalSaleAmountOC: null,
            TotalVATAmountOC: null,
            TotalAmountOC: null,
            EInvoiceStatus: "1",
            PublishStatus: "0",
            OrgRefID: null,
            CreatedDate: null,
            ModifiedDate: null);

        var snapshot = InvoiceLookupMapper.FromWire(dto);

        Assert.Null(snapshot.InvDate);
    }
}

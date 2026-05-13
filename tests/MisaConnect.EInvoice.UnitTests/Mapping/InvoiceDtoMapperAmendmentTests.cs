using System.Text.Json;
using MisaConnect.EInvoice.Infrastructure.MeInvoice;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Mapping;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Wire;
using MisaConnect.EInvoice.TestSupport.Fixtures;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Mapping;

public class InvoiceDtoMapperAmendmentTests
{
    [Fact]
    public void SaveDraft_invocation_still_emits_EInvoiceStatus_1_after_slice6_generalisation()
    {
        var invoice = SampleInvoices.TypicalVatInvoice();
        var dto = InvoiceDtoMapper.ToWire(invoice);

        Assert.Equal("1", dto.EInvoiceStatus);
        Assert.Null(dto.OrgRefID);
        Assert.Null(dto.OrgInvNo);
        Assert.Null(dto.OrgInvTemplateNo);
        Assert.Null(dto.OrgInvSeries);
        Assert.Null(dto.OrgInvDate);
        Assert.Null(dto.ChangeReason);
    }

    [Fact]
    public void Replacement_context_emits_EInvoiceStatus_3_and_Org_block()
    {
        var invoice = SampleInvoices.TypicalVatInvoice();
        var origRef = new OriginalInvoiceReferenceWire(
            OrgRefID: "orig-ref-001",
            OrgInvNo: "00000123",
            OrgInvTemplateNo: "1",
            OrgInvSeries: "C26TAA",
            OrgInvDate: "2026-05-10");

        var dto = InvoiceDtoMapper.ToWire(
            invoice,
            eInvoiceStatus: "3",
            originalRef: origRef,
            changeReason: "Sửa thông tin người mua");

        Assert.Equal("3", dto.EInvoiceStatus);
        Assert.Equal("orig-ref-001", dto.OrgRefID);
        Assert.Equal("00000123", dto.OrgInvNo);
        Assert.Equal("1", dto.OrgInvTemplateNo);
        Assert.Equal("C26TAA", dto.OrgInvSeries);
        Assert.Equal("2026-05-10", dto.OrgInvDate);
        Assert.Equal("Sửa thông tin người mua", dto.ChangeReason);
    }

    [Fact]
    public void Adjustment_context_emits_EInvoiceStatus_4_and_Org_block()
    {
        var invoice = SampleInvoices.TypicalVatInvoice();
        var origRef = new OriginalInvoiceReferenceWire(
            OrgRefID: "orig-ref-002",
            OrgInvNo: "00000124",
            OrgInvTemplateNo: "1",
            OrgInvSeries: "C26TAA",
            OrgInvDate: "2026-05-11");

        var dto = InvoiceDtoMapper.ToWire(
            invoice,
            eInvoiceStatus: "4",
            originalRef: origRef,
            changeReason: "Bổ sung khoản phụ thu");

        Assert.Equal("4", dto.EInvoiceStatus);
        Assert.Equal("orig-ref-002", dto.OrgRefID);
        Assert.Equal("00000124", dto.OrgInvNo);
        Assert.Equal("1", dto.OrgInvTemplateNo);
        Assert.Equal("C26TAA", dto.OrgInvSeries);
        Assert.Equal("2026-05-11", dto.OrgInvDate);
        Assert.Equal("Bổ sung khoản phụ thu", dto.ChangeReason);
    }

    [Fact]
    public void SaveDraft_wire_payload_omits_amendment_keys()
    {
        var invoice = SampleInvoices.TypicalVatInvoice();
        var dto = InvoiceDtoMapper.ToWire(invoice);

        var json = JsonSerializer.Serialize(dto, MeInvoiceJsonOptions.Wire);

        Assert.DoesNotContain("OrgRefID", json);
        Assert.DoesNotContain("OrgInvNo", json);
        Assert.DoesNotContain("OrgInvTemplateNo", json);
        Assert.DoesNotContain("OrgInvSeries", json);
        Assert.DoesNotContain("OrgInvDate", json);
        Assert.DoesNotContain("ChangeReason", json);
        Assert.Contains("\"EInvoiceStatus\":\"1\"", json);
    }

    [Fact]
    public void Replacement_wire_payload_includes_amendment_keys()
    {
        var invoice = SampleInvoices.TypicalVatInvoice();
        var origRef = new OriginalInvoiceReferenceWire(
            OrgRefID: "orig-1",
            OrgInvNo: "00000125",
            OrgInvTemplateNo: "1",
            OrgInvSeries: "C26TAA",
            OrgInvDate: "2026-05-10");

        var dto = InvoiceDtoMapper.ToWire(
            invoice,
            eInvoiceStatus: "3",
            originalRef: origRef,
            changeReason: "ascii-only change reason");

        var json = JsonSerializer.Serialize(dto, MeInvoiceJsonOptions.Wire);

        Assert.Contains("\"EInvoiceStatus\":\"3\"", json);
        Assert.Contains("\"OrgRefID\":\"orig-1\"", json);
        Assert.Contains("\"OrgInvNo\":\"00000125\"", json);
        Assert.Contains("\"OrgInvTemplateNo\":\"1\"", json);
        Assert.Contains("\"OrgInvSeries\":\"C26TAA\"", json);
        Assert.Contains("\"OrgInvDate\":\"2026-05-10\"", json);
        Assert.Contains("\"ChangeReason\":\"ascii-only change reason\"", json);
    }
}

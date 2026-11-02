using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.TestSupport.Builders;

namespace MisaConnect.EInvoice.TestSupport.Fixtures;

public static class SampleInvoices
{
    /// <summary>
    /// Canonical Vietnamese VAT invoice. Two goods lines (8% + 10%) plus a
    /// trade-discount line; balanced §9 reconciliation; VND; integer math.
    /// </summary>
    public static Invoice TypicalVatInvoice()
    {
        var line1 = new InvoiceLine(
            InventoryItemType: InventoryItemType.Goods,
            SortOrder: 1,
            Description: "Dịch vụ tư vấn",
            UnitName: "Lần",
            Quantity: 1m,
            UnitPrice: 1_000_000m,
            AmountOC: 1_000_000m,
            Amount: 1_000_000m,
            AmountWithoutVATOC: 1_000_000m,
            AmountWithoutVAT: 1_000_000m,
            VatRate: VatRate.Eight,
            VATAmountOC: 80_000m,
            VATAmount: 80_000m,
            SortOrderView: 1);

        var line2 = new InvoiceLine(
            InventoryItemType: InventoryItemType.Goods,
            SortOrder: 2,
            Description: "Hàng hóa A",
            UnitName: "Cái",
            Quantity: 2m,
            UnitPrice: 500_000m,
            AmountOC: 1_000_000m,
            Amount: 1_000_000m,
            AmountWithoutVATOC: 1_000_000m,
            AmountWithoutVAT: 1_000_000m,
            VatRate: VatRate.Ten,
            VATAmountOC: 100_000m,
            VATAmount: 100_000m,
            SortOrderView: 2);

        var totals = new InvoiceTotals(
            TotalSaleAmountOC: 2_000_000m,
            TotalSaleAmount: 2_000_000m,
            TotalDiscountAmountOC: 0m,
            TotalDiscountAmount: 0m,
            TotalAmountWithoutVATOC: 2_000_000m,
            TotalAmountWithoutVAT: 2_000_000m,
            TotalVATAmountOC: 180_000m,
            TotalVATAmount: 180_000m,
            TotalAmountOC: 2_180_000m,
            TotalAmount: 2_180_000m,
            TotalAmountInWords: "Hai triệu một trăm tám mươi nghìn đồng chẵn");

        return new InvoiceBuilder()
            .WithRefId(RefId.NewGuid())
            .AddLine(line1)
            .AddLine(line2)
            .WithTotals(totals)
            .Build();
    }

    /// <summary>Individual-buyer variant — BuyerType=Individual, no tax code, 0% VAT line.</summary>
    public static Invoice IndividualBuyer()
    {
        var line = new InvoiceLine(
            InventoryItemType: InventoryItemType.Goods,
            SortOrder: 1,
            Description: "Hàng hóa cá nhân",
            UnitName: "Cái",
            Quantity: 1m,
            UnitPrice: 100_000m,
            AmountOC: 100_000m,
            Amount: 100_000m,
            AmountWithoutVATOC: 100_000m,
            AmountWithoutVAT: 100_000m,
            VatRate: VatRate.Zero,
            VATAmountOC: 0m,
            VATAmount: 0m,
            SortOrderView: 1);

        var totals = new InvoiceTotals(
            100_000m, 100_000m,
            0m, 0m,
            100_000m, 100_000m,
            0m, 0m,
            100_000m, 100_000m,
            "Một trăm nghìn đồng chẵn");

        return new InvoiceBuilder()
            .WithRefId(RefId.NewGuid())
            .WithBuyerType(BuyerType.Individual)
            .WithBuyer(new BuyerInfo("Khách hàng cá nhân"))
            .AddLine(line)
            .WithTotals(totals)
            .Build();
    }

    /// <summary>Multi-currency variant — USD, 25 000 ExchangeRate, 10% VAT.</summary>
    public static Invoice MultiCurrencyVatInvoice()
    {
        var line = new InvoiceLine(
            InventoryItemType: InventoryItemType.Goods,
            SortOrder: 1,
            Description: "Export service",
            UnitName: "Lần",
            Quantity: 1m,
            UnitPrice: 100m,
            AmountOC: 100m,
            Amount: 2_500_000m,
            AmountWithoutVATOC: 100m,
            AmountWithoutVAT: 2_500_000m,
            VatRate: VatRate.Ten,
            VATAmountOC: 10m,
            VATAmount: 250_000m,
            SortOrderView: 1);

        var totals = new InvoiceTotals(
            100m, 2_500_000m,
            0m, 0m,
            100m, 2_500_000m,
            10m, 250_000m,
            110m, 2_750_000m,
            "One hundred ten US dollars only");

        return new InvoiceBuilder()
            .WithRefId(RefId.NewGuid())
            .WithCurrency("USD")
            .WithExchangeRate(25_000m)
            .AddLine(line)
            .WithTotals(totals)
            .Build();
    }
}

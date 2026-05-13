using System.Globalization;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Wire;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Mapping;

internal static class InvoiceDtoMapper
{
    public static InvoiceDataDto ToWire(
        Invoice invoice,
        string eInvoiceStatus = "1",
        OriginalInvoiceReferenceWire? originalRef = null,
        string? changeReason = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var lines = new List<InvoiceDetailDto>(invoice.Lines.Count);
        foreach (var line in invoice.Lines)
        {
            var vatPair = line.VatRate is { } vr ? VatRateMapper.ToWire(vr) : (Name: (string?)null, Numeric: (int?)null);
            lines.Add(new InvoiceDetailDto(
                InventoryItemType: (int)line.InventoryItemType,
                SortOrder: line.SortOrder,
                SortOrderView: line.SortOrderView,
                Description: line.Description,
                UnitName: line.UnitName,
                Quantity: line.Quantity,
                UnitPrice: line.UnitPrice,
                AmountOC: line.AmountOC,
                Amount: line.Amount,
                AmountWithoutVATOC: line.AmountWithoutVATOC,
                AmountWithoutVAT: line.AmountWithoutVAT,
                VATAmountOC: line.VATAmountOC,
                VATAmount: line.VATAmount,
                VATRateName: vatPair.Name,
                VATRate: vatPair.Numeric,
                DiscountRate: line.DiscountRate,
                DiscountAmountOC: line.DiscountAmountOC,
                DiscountAmount: line.DiscountAmount));
        }

        string? customField1 = null;
        if (invoice.CustomFields is not null && invoice.CustomFields.TryGetValue(1, out var cf1))
        {
            customField1 = cf1;
        }

        return new InvoiceDataDto(
            RefID: invoice.RefId.Value,
            InvoiceTemplateID: invoice.Template?.IPTemplateID,
            InvSeries: invoice.Template?.InvSeries,
            InvDate: invoice.InvDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CreatedDate: invoice.CreatedDate.ToString("O", CultureInfo.InvariantCulture),
            ModifiedDate: invoice.ModifiedDate.ToString("O", CultureInfo.InvariantCulture),
            CurrencyCode: invoice.Currency,
            ExchangeRate: invoice.ExchangeRate,
            PaymentMethod: invoice.PaymentMethod,
            AccountObjectName: invoice.Buyer.Name,
            AccountObjectTaxCode: invoice.Buyer.TaxCode,
            AccountObjectAddress: invoice.Buyer.Address,
            ContactName: invoice.Buyer.ContactName,
            ReceiverEmail: invoice.Buyer.Email,
            ReceiverName: invoice.Buyer.ReceiverName,
            ReceiverMobile: invoice.Buyer.Mobile,
            AccountObjectBankAccount: invoice.Buyer.BankAccount,
            AccountObjectBankName: invoice.Buyer.BankName,
            TotalSaleAmountOC: invoice.Totals.TotalSaleAmountOC,
            TotalSaleAmount: invoice.Totals.TotalSaleAmount,
            TotalDiscountAmountOC: invoice.Totals.TotalDiscountAmountOC,
            TotalDiscountAmount: invoice.Totals.TotalDiscountAmount,
            TotalAmountWithoutVATOC: invoice.Totals.TotalAmountWithoutVATOC,
            TotalAmountWithoutVAT: invoice.Totals.TotalAmountWithoutVAT,
            TotalVATAmountOC: invoice.Totals.TotalVATAmountOC,
            TotalVATAmount: invoice.Totals.TotalVATAmount,
            TotalAmountOC: invoice.Totals.TotalAmountOC,
            TotalAmount: invoice.Totals.TotalAmount,
            TotalAmountInWords: invoice.Totals.TotalAmountInWords,
            EInvoiceStatus: eInvoiceStatus,
            CustomField1: customField1,
            CustomField2: null,
            CustomField3: null,
            OrgRefID: originalRef?.OrgRefID,
            OrgInvNo: originalRef?.OrgInvNo,
            OrgInvTemplateNo: originalRef?.OrgInvTemplateNo,
            OrgInvSeries: originalRef?.OrgInvSeries,
            OrgInvDate: originalRef?.OrgInvDate,
            ChangeReason: changeReason,
            InvoiceDetails: lines);
    }

    public static Template FromWire(TemplateDto dto, bool defaultWithCode)
    {
        ArgumentNullException.ThrowIfNull(dto);
        bool withCode = defaultWithCode;
        if (dto.InvSeries is { Length: > 1 })
        {
            var c = char.ToUpperInvariant(dto.InvSeries[1]);
            if (c == 'C') withCode = true;
            else if (c == 'K') withCode = false;
        }

        return new Template(
            IPTemplateID: dto.IPTemplateID,
            InvSeries: dto.InvSeries,
            TemplateName: dto.TemplateName,
            InvTemplateNo: dto.InvTemplateNo,
            TemplateType: dto.TemplateType,
            IsActive: !dto.Inactive,
            UsesTaxAuthorityCode: withCode,
            IsMoreVATRate: dto.IsMoreVATRate);
    }
}

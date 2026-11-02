using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;

namespace MisaConnect.EInvoice.Client.Mapping;

internal static class DtoMapper
{
    public static Invoice ToDomain(InvoiceDto dto)
    {
        var refId = string.IsNullOrEmpty(dto.RefId) ? default : RefId.From(dto.RefId);
        var lines = new List<InvoiceLine>(dto.Lines.Count);
        foreach (var l in dto.Lines)
        {
            VatRate? vat = string.IsNullOrEmpty(l.VatRateName) ? null : VatRate.FromName(l.VatRateName);
            lines.Add(new InvoiceLine(
                InventoryItemType: (InventoryItemType)l.InventoryItemType,
                SortOrder: l.SortOrder,
                Description: l.Description,
                UnitName: l.UnitName,
                Quantity: l.Quantity,
                UnitPrice: l.UnitPrice,
                AmountOC: l.AmountOC,
                Amount: l.Amount,
                AmountWithoutVATOC: l.AmountWithoutVATOC,
                AmountWithoutVAT: l.AmountWithoutVAT,
                VatRate: vat,
                VATAmountOC: l.VATAmountOC,
                VATAmount: l.VATAmount,
                SortOrderView: l.SortOrderView,
                DiscountRate: l.DiscountRate,
                DiscountAmountOC: l.DiscountAmountOC,
                DiscountAmount: l.DiscountAmount));
        }

        return new Invoice(
            RefId: refId,
            Template: dto.Template is null ? null : new TemplateRef(dto.Template.IPTemplateID, dto.Template.InvSeries),
            InvDate: dto.InvDate,
            CreatedDate: dto.CreatedDate,
            ModifiedDate: dto.ModifiedDate,
            Currency: dto.Currency,
            ExchangeRate: dto.ExchangeRate,
            PaymentMethod: dto.PaymentMethod,
            BuyerType: (BuyerType)dto.BuyerType,
            Buyer: new BuyerInfo(
                Name: dto.Buyer.Name,
                TaxCode: dto.Buyer.TaxCode,
                Address: dto.Buyer.Address,
                ContactName: dto.Buyer.ContactName,
                Email: dto.Buyer.Email,
                ReceiverName: dto.Buyer.ReceiverName,
                Mobile: dto.Buyer.Mobile,
                BankAccount: dto.Buyer.BankAccount,
                BankName: dto.Buyer.BankName),
            Lines: lines,
            Totals: new InvoiceTotals(
                dto.Totals.TotalSaleAmountOC,
                dto.Totals.TotalSaleAmount,
                dto.Totals.TotalDiscountAmountOC,
                dto.Totals.TotalDiscountAmount,
                dto.Totals.TotalAmountWithoutVATOC,
                dto.Totals.TotalAmountWithoutVAT,
                dto.Totals.TotalVATAmountOC,
                dto.Totals.TotalVATAmount,
                dto.Totals.TotalAmountOC,
                dto.Totals.TotalAmount,
                dto.Totals.TotalAmountInWords),
            CustomFields: dto.CustomFields);
    }

    public static SaveResultDto ToDto(SaveResult result)
    {
        IReadOnlyList<ValidationFailureDto>? failures = null;
        if (result.LocalFailures is { Count: > 0 })
        {
            var list = new List<ValidationFailureDto>(result.LocalFailures.Count);
            foreach (var f in result.LocalFailures) list.Add(new ValidationFailureDto(f.FieldPath, f.Message, f.RuleId));
            failures = list;
        }
        return new SaveResultDto(
            RefId: result.RefId.Value,
            Outcome: result.Outcome == SaveOutcome.Success ? SaveOutcomeDto.Success : SaveOutcomeDto.Error,
            ErrorCategory: result.Error?.Category.ToString(),
            RawErrorCode: result.Error?.RawCode,
            ErrorMessage: result.Error?.Detail,
            Failures: failures);
    }

    public static PdfDocumentDto ToDto(PdfDocument pdf) => new(pdf.Content, pdf.ContentType);

    public static TemplateDto ToDto(Domain.Templates.Template t) => new(
        t.IPTemplateID, t.InvSeries, t.TemplateName, t.InvTemplateNo, t.TemplateType,
        t.UsesTaxAuthorityCode, t.IsActive, t.IsMoreVATRate);
}

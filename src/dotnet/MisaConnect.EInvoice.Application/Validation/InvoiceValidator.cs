using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.Validation;

/// <summary>
/// Implements the FR-006 + FR-021..FR-030 rule set and the §9 reconciliation
/// from research R-8 / R-9.
/// </summary>
public sealed class InvoiceValidator : IInvoiceValidator
{
    /// <summary>±0.5 OC tolerance (R-9). Master totals are sums of per-line rounded values.</summary>
    public const decimal OcTolerance = 0.5m;

    public IReadOnlyList<ValidationFailure> Validate(Invoice invoice)
    {
        var failures = new List<ValidationFailure>();
        if (invoice is null)
        {
            failures.Add(new ValidationFailure("$", "Invoice is null.", "FR-006"));
            return failures;
        }

        if (string.IsNullOrEmpty(invoice.RefId.Value))
        {
            failures.Add(new ValidationFailure("RefId", "RefId must be supplied or service-generated.", "FR-021"));
        }
        else if (invoice.RefId.Value.Length > RefId.DefaultMaxLength)
        {
            failures.Add(new ValidationFailure("RefId", $"RefId length {invoice.RefId.Value.Length} exceeds the maximum of {RefId.DefaultMaxLength}.", "FR-021"));
        }

        if (string.IsNullOrWhiteSpace(invoice.Currency))
        {
            failures.Add(new ValidationFailure("Currency", "Currency is required.", "FR-006"));
        }
        else if (!string.Equals(invoice.Currency, "VND", StringComparison.OrdinalIgnoreCase) && invoice.ExchangeRate <= 0m)
        {
            failures.Add(new ValidationFailure("ExchangeRate", "ExchangeRate must be supplied and > 0 for non-VND invoices.", "FR-023"));
        }

        if (invoice.BuyerType == BuyerType.Organization && string.IsNullOrWhiteSpace(invoice.Buyer.TaxCode))
        {
            failures.Add(new ValidationFailure("Buyer.TaxCode", "Buyer tax code is required when BuyerType = Organization.", "FR-024"));
        }

        if (string.IsNullOrWhiteSpace(invoice.PaymentMethod))
        {
            failures.Add(new ValidationFailure("PaymentMethod", "PaymentMethod is required.", "FR-027"));
        }

        CheckEmojiFree("Buyer.Name", invoice.Buyer.Name, failures);
        CheckEmojiFree("Buyer.ContactName", invoice.Buyer.ContactName, failures);
        CheckEmojiFree("Buyer.Address", invoice.Buyer.Address, failures);

        if (invoice.Lines is null || invoice.Lines.Count == 0)
        {
            failures.Add(new ValidationFailure("Lines", "At least one invoice line is required.", "FR-006"));
        }
        else
        {
            if (invoice.Lines.Count > BatchSubmission.MaxLinesPerInvoice)
            {
                failures.Add(new ValidationFailure(
                    "Lines",
                    $"Line count {invoice.Lines.Count} exceeds the MISA limit of {BatchSubmission.MaxLinesPerInvoice} per invoice.",
                    "FR-030"));
            }

            for (var i = 0; i < invoice.Lines.Count; i++)
            {
                var line = invoice.Lines[i];
                CheckEmojiFree($"Lines[{i}].Description", line.Description, failures);
                CheckEmojiFree($"Lines[{i}].UnitName", line.UnitName, failures);

                if (line.VatRate is { } vr && !IsAcceptedVat(vr))
                {
                    failures.Add(new ValidationFailure($"Lines[{i}].VatRate", $"VATRateName '{vr.Name}' is not in the accepted set.", "FR-029"));
                }
            }

            // FR-025: discount reconciliation.
            decimal goodsDiscountSum = 0m;
            foreach (var line in invoice.Lines)
            {
                if (line.InventoryItemType == InventoryItemType.Goods && line.DiscountAmountOC is { } amount)
                {
                    goodsDiscountSum += amount;
                }
            }

            if (invoice.Totals is not null &&
                Math.Abs(invoice.Totals.TotalDiscountAmountOC - goodsDiscountSum) > OcTolerance)
            {
                failures.Add(new ValidationFailure(
                    "Totals.TotalDiscountAmountOC",
                    $"Header TotalDiscountAmountOC {invoice.Totals.TotalDiscountAmountOC} does not equal sum of goods-line DiscountAmountOC ({goodsDiscountSum}).",
                    "FR-025"));
            }
        }

        if (invoice.Totals is null)
        {
            failures.Add(new ValidationFailure("Totals", "Invoice totals are required.", "FR-006"));
        }

        return failures;
    }

    public IReadOnlyList<ValidationFailure> ValidateBatch(BatchSubmission batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var all = new List<ValidationFailure>();
        if (batch.Invoices.Count > BatchSubmission.MaxInvoices)
        {
            all.Add(new ValidationFailure(
                "$batch.Count",
                $"Batch size {batch.Invoices.Count} exceeds the MISA limit of {BatchSubmission.MaxInvoices} invoices per request.",
                "FR-030"));
        }

        for (var i = 0; i < batch.Invoices.Count; i++)
        {
            var failures = Validate(batch.Invoices[i]);
            foreach (var f in failures)
            {
                all.Add(new ValidationFailure($"Invoices[{i}].{f.FieldPath}", f.Message, f.RuleId));
            }
        }

        return all;
    }

    private static void CheckEmojiFree(string fieldPath, string? value, List<ValidationFailure> failures)
    {
        if (string.IsNullOrEmpty(value)) return;
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                failures.Add(new ValidationFailure(
                    fieldPath,
                    "Field contains a supplementary-plane code point (likely emoji); MISA rejects these with InvalidXMLContainEmoji.",
                    "FR-028"));
                return;
            }
        }
    }

    private static bool IsAcceptedVat(VatRate vat) =>
        vat == VatRate.Kct
        || vat == VatRate.Kkknt
        || vat == VatRate.Zero
        || vat == VatRate.Five
        || vat == VatRate.Eight
        || vat == VatRate.Ten
        || (vat.OtherRate is { } r && r > 0m);
}

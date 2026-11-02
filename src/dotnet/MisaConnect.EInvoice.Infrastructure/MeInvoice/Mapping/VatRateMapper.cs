using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Mapping;

internal static class VatRateMapper
{
    public static (string Name, int? Numeric) ToWire(VatRate vat) => (vat.Name, vat.Numeric);

    public static VatRate FromWire(int? numeric, string? name)
    {
        if (numeric is { } n)
        {
            return VatRate.FromNumeric(n);
        }
        if (!string.IsNullOrEmpty(name))
        {
            return VatRate.FromName(name);
        }
        throw new ArgumentException("Both numeric and name are null/empty.");
    }
}

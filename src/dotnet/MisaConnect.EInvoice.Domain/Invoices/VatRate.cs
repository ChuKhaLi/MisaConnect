using System.Globalization;

namespace MisaConnect.EInvoice.Domain.Invoices;

/// <summary>
/// MISA VAT-rate value object. Supports both the named form (VATRateName) and
/// the numeric integer form. KHAC variants carry a numeric percent attached.
/// </summary>
public readonly record struct VatRate
{
    public string Name { get; }
    public int? Numeric { get; }
    public decimal? OtherRate { get; }

    private VatRate(string name, int? numeric, decimal? otherRate)
    {
        Name = name;
        Numeric = numeric;
        OtherRate = otherRate;
    }

    public static VatRate Kct { get; } = new("KCT", -1, null);
    public static VatRate Kkknt { get; } = new("KKKNT", -3, null);
    public static VatRate Zero { get; } = new("0%", 0, null);
    public static VatRate Five { get; } = new("5%", 5, null);
    public static VatRate Eight { get; } = new("8%", 8, null);
    public static VatRate Ten { get; } = new("10%", 10, null);

    public static VatRate Other(decimal percent)
    {
        if (percent <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "KHAC rate must be a positive percentage.");
        }

        var formatted = percent.ToString("0.################", CultureInfo.InvariantCulture);
        return new VatRate($"KHAC:{formatted}%", null, percent);
    }

    public static VatRate FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("VAT rate name must be non-empty.", nameof(name));
        }

        switch (name.Trim().ToUpperInvariant())
        {
            case "KCT": return Kct;
            case "KKKNT": return Kkknt;
            case "0%": return Zero;
            case "5%": return Five;
            case "8%": return Eight;
            case "10%": return Ten;
        }

        if (name.StartsWith("KHAC:", StringComparison.OrdinalIgnoreCase))
        {
            var tail = name.Substring("KHAC:".Length).TrimEnd('%');
            if (decimal.TryParse(tail, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
            {
                return Other(rate);
            }
        }

        throw new ArgumentException($"Unknown VAT rate name '{name}'.", nameof(name));
    }

    public static VatRate FromNumeric(int? value) => value switch
    {
        -1 => Kct,
        -3 => Kkknt,
        0 => Zero,
        5 => Five,
        8 => Eight,
        10 => Ten,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown numeric VAT rate '{value}'.")
    };
}

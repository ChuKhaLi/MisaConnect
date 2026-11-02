namespace MisaConnect.EInvoice.Domain.Invoices;

/// <summary>
/// MISA invoice natural key. Caller-supplied or service-generated (FR-021).
/// Lenient validation: non-empty, ≤ MaxLength (default 64). No GUID format
/// requirement on caller-supplied values per the 2026-05-12 clarification.
/// </summary>
public readonly record struct RefId
{
    public const int DefaultMaxLength = 64;

    public string Value { get; }

    private RefId(string value) => Value = value;

    public static RefId From(string value, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("RefId value must be non-empty.", nameof(value));
        }

        if (value.Length > maxLength)
        {
            throw new ArgumentException(
                $"RefId value length {value.Length} exceeds the maximum of {maxLength}.",
                nameof(value));
        }

        return new RefId(value);
    }

    public static RefId NewGuid() => new(Guid.NewGuid().ToString("D"));

    public override string ToString() => Value;
}

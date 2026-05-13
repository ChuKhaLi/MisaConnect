namespace MisaConnect.EInvoice.Domain.Invoices;

public sealed record BatchSubmission
{
    public const int MaxInvoices = 30;
    public const int MaxLinesPerInvoice = 200;

    public IReadOnlyList<Invoice> Invoices { get; }

    private BatchSubmission(IReadOnlyList<Invoice> invoices) => Invoices = invoices;

    public static BatchSubmission From(IReadOnlyList<Invoice> invoices)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        if (invoices.Count is < 1 or > MaxInvoices)
        {
            throw new ArgumentOutOfRangeException(
                nameof(invoices),
                invoices.Count,
                $"BatchSubmission must contain between 1 and {MaxInvoices} invoices.");
        }

        return new BatchSubmission(invoices);
    }
}

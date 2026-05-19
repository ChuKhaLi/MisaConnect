namespace MisaConnect.ESign.Domain.Documents;

public sealed record PdfDocument(byte[] Bytes)
{
    public int Length => Bytes.Length;
}

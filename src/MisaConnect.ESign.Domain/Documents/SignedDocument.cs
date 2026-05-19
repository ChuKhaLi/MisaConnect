namespace MisaConnect.ESign.Domain.Documents;

public sealed record SignedDocument(byte[] Bytes)
{
    public int Length => Bytes.Length;
}

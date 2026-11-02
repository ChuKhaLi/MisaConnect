namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// Per-format hash-response record for XML. Matches the field set MISA
/// §4.15 publishes for the XML response array — XML uses <c>document</c>
/// (raw text), not <c>documentBytes</c>, and carries <c>signatureId</c>
/// (required for the XML attachment row per §4.6).
/// </summary>
public sealed record XmlHashOutput(
    string DocumentId,
    string Document,
    string SignatureId,
    string Digest,
    string Sh)
{
    public SignHashInput ToSignHashInput() => new(DocumentId, Digest);
}

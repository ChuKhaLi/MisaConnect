namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// Shared per-format hash-response record for Word and Excel. MISA §4.15
/// publishes identical fields for both. Carries <c>documentBytes</c>
/// (base64) and the two attachment-required fields <c>mainDom</c> and
/// <c>signatureId</c> (§4.6).
/// </summary>
public sealed record WordExcelHashOutput(
    string DocumentId,
    string DocumentBytes,
    string SignatureId,
    string Digest,
    string MainDom)
{
    public SignHashInput ToSignHashInput() => new(DocumentId, Digest);
}

namespace MisaConnect.ESign.Application.Abstractions;

public sealed record PdfHashOutput(
    string DocumentId,
    string DocumentBytes,
    string DocumentHash,
    string Sh,
    string SignatureName,
    string Digest);

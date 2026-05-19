namespace MisaConnect.ESign.Client.Dtos;

public sealed record SignPdfResultDto(
    byte[] SignedPdf,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc);

using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Client.Dtos;

public sealed record SignPdfResultDto(
    byte[] SignedPdf,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Pdf);

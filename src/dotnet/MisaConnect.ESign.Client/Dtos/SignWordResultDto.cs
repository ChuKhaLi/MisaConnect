using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Client.Dtos;

public sealed record SignWordResultDto(
    byte[] SignedWord,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Word);

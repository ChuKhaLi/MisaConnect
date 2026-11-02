using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Client.Dtos;

public sealed record SignExcelResultDto(
    byte[] SignedExcel,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Excel);

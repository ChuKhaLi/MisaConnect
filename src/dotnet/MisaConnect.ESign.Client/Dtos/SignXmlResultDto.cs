using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Client.Dtos;

public sealed record SignXmlResultDto(
    byte[] SignedXml,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Xml);

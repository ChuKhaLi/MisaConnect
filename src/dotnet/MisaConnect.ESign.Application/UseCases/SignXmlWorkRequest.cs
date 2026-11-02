using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

public sealed record SignXmlWorkRequest(
    string Xml,
    XmlSignatureContext SignatureContext,
    string DocumentId,
    string DocumentName,
    string DataToBeDisplayed);

public sealed record SignXmlWorkResult(
    SignedDocument SignedXml,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Xml);

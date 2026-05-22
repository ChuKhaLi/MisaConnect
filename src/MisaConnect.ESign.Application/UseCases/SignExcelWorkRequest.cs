using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

public sealed record SignExcelWorkRequest(
    byte[] Excel,
    SignatureInfo SignatureInfo,
    string DocumentId,
    string DocumentName,
    string DataToBeDisplayed);

public sealed record SignExcelWorkResult(
    SignedDocument SignedExcel,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Excel);

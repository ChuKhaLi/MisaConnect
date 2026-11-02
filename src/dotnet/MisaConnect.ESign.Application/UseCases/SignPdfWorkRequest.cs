using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

public sealed record SignPdfWorkRequest(
    PdfDocument Pdf,
    SignatureInfo SignatureInfo,
    string DocumentName,
    string DocumentId,
    string DataToBeDisplayed);

public sealed record SignPdfWorkResult(
    SignedDocument SignedPdf,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc);

using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

public sealed record SignWordWorkRequest(
    byte[] Word,
    SignatureInfo SignatureInfo,
    string DocumentId,
    string DocumentName,
    string DataToBeDisplayed);

public sealed record SignWordWorkResult(
    SignedDocument SignedWord,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Word);

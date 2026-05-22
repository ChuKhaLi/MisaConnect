using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.Abstractions;

public interface IMisaESignWireClient
{
    Task<AuthSession> LoginAsync(string userName, string password, CancellationToken ct);

    Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct);

    Task<AuthSession> TwoFactorAuthAsync(string userName, string code, OtpDeliveryChannel otpType, bool remember, CancellationToken ct);

    Task<OtpResendResult> ResendOtpAsync(string userName, string language, CancellationToken ct);

    Task<IReadOnlyList<Certificate>> ListCertificatesByUserIdAsync(string accessToken, CancellationToken ct);

    Task<PdfHashOutput> HashPdfAsync(
        string accessToken,
        Certificate cert,
        byte[] pdfBytes,
        string documentId,
        SignatureInfo signatureInfo,
        CancellationToken ct);

    Task<SignTransaction> SubmitSignHashAsync(
        string accessToken,
        Certificate cert,
        string userId,
        string dataToBeDisplayed,
        SignHashInput hash,
        string documentName,
        CancellationToken ct);

    Task<SignStatusSnapshot> GetSignStatusAsync(string accessToken, string transactionId, CancellationToken ct);

    Task<byte[]> AttachSignatureAsync(
        string accessToken,
        Certificate cert,
        PdfHashOutput hash,
        string signatureData,
        CancellationToken ct);

    // Slice 3 — per-format hash methods (§4.1.1 / §4.1.2 / §4.15)

    Task<XmlHashOutput> HashXmlAsync(
        string accessToken,
        Certificate cert,
        string xmlContent,
        string documentId,
        XmlSignatureContext signatureContext,
        CancellationToken ct);

    Task<WordExcelHashOutput> HashWordAsync(
        string accessToken,
        Certificate cert,
        byte[] wordBytes,
        string documentId,
        SignatureInfo signatureInfo,
        CancellationToken ct);

    Task<WordExcelHashOutput> HashExcelAsync(
        string accessToken,
        Certificate cert,
        byte[] excelBytes,
        string documentId,
        SignatureInfo signatureInfo,
        CancellationToken ct);

    // Slice 3 — per-format attachment methods (§4.6 Doc_Attackment)

    Task<byte[]> AttachSignatureToXmlAsync(
        string accessToken,
        Certificate cert,
        XmlHashOutput hash,
        string signatureData,
        CancellationToken ct);

    Task<byte[]> AttachSignatureToWordExcelAsync(
        string accessToken,
        Certificate cert,
        WordExcelHashOutput hash,
        string signatureData,
        DocumentFormat format,
        CancellationToken ct);
}

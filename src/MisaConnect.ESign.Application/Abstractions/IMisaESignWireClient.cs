using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.Abstractions;

public interface IMisaESignWireClient
{
    Task<AuthSession> LoginAsync(string userName, string password, CancellationToken ct);

    Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct);

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
        PdfHashOutput hash,
        string documentName,
        CancellationToken ct);

    Task<SignStatusSnapshot> GetSignStatusAsync(string accessToken, string transactionId, CancellationToken ct);

    Task<byte[]> AttachSignatureAsync(
        string accessToken,
        Certificate cert,
        PdfHashOutput hash,
        string signatureData,
        CancellationToken ct);
}

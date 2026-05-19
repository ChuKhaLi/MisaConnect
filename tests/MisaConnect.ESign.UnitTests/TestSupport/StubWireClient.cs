using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.UnitTests.TestSupport;

internal sealed class StubWireClient : IMisaESignWireClient
{
    public Func<string, string, CancellationToken, Task<AuthSession>>? OnLogin { get; set; }
    public Func<string, CancellationToken, Task<AuthSession>>? OnRefresh { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<Certificate>>>? OnListCerts { get; set; }
    public Func<string, Certificate, byte[], string, SignatureInfo, CancellationToken, Task<PdfHashOutput>>? OnHash { get; set; }
    public Func<string, Certificate, string, string, PdfHashOutput, string, CancellationToken, Task<SignTransaction>>? OnSubmitSignHash { get; set; }
    public Func<string, string, CancellationToken, Task<SignStatusSnapshot>>? OnGetStatus { get; set; }
    public Func<string, Certificate, PdfHashOutput, string, CancellationToken, Task<byte[]>>? OnAttach { get; set; }

    public int LoginCalls;
    public int RefreshCalls;
    public int ListCertCalls;
    public int HashCalls;
    public int SubmitSignHashCalls;
    public int GetStatusCalls;
    public int AttachCalls;

    public Task<AuthSession> LoginAsync(string userName, string password, CancellationToken ct)
    {
        LoginCalls++;
        return OnLogin!(userName, password, ct);
    }

    public Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        RefreshCalls++;
        return OnRefresh!(refreshToken, ct);
    }

    public Task<IReadOnlyList<Certificate>> ListCertificatesByUserIdAsync(string accessToken, CancellationToken ct)
    {
        ListCertCalls++;
        return OnListCerts!(accessToken, ct);
    }

    public Task<PdfHashOutput> HashPdfAsync(string accessToken, Certificate cert, byte[] pdfBytes, string documentId, SignatureInfo signatureInfo, CancellationToken ct)
    {
        HashCalls++;
        return OnHash!(accessToken, cert, pdfBytes, documentId, signatureInfo, ct);
    }

    public Task<SignTransaction> SubmitSignHashAsync(string accessToken, Certificate cert, string userId, string dataToBeDisplayed, PdfHashOutput hash, string documentName, CancellationToken ct)
    {
        SubmitSignHashCalls++;
        return OnSubmitSignHash!(accessToken, cert, userId, dataToBeDisplayed, hash, documentName, ct);
    }

    public Task<SignStatusSnapshot> GetSignStatusAsync(string accessToken, string transactionId, CancellationToken ct)
    {
        GetStatusCalls++;
        return OnGetStatus!(accessToken, transactionId, ct);
    }

    public Task<byte[]> AttachSignatureAsync(string accessToken, Certificate cert, PdfHashOutput hash, string signatureData, CancellationToken ct)
    {
        AttachCalls++;
        return OnAttach!(accessToken, cert, hash, signatureData, ct);
    }
}

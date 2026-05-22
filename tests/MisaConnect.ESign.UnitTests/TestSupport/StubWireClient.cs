using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.UnitTests.TestSupport;

internal sealed class StubWireClient : IMisaESignWireClient
{
    public Func<string, string, CancellationToken, Task<AuthSession>>? OnLogin { get; set; }
    public Func<string, CancellationToken, Task<AuthSession>>? OnRefresh { get; set; }
    public Func<string, string, OtpDeliveryChannel, bool, CancellationToken, Task<AuthSession>>? OnTwoFactorAuth { get; set; }
    public Func<string, string, CancellationToken, Task<OtpResendResult>>? OnResendOtp { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<Certificate>>>? OnListCerts { get; set; }
    public Func<string, Certificate, byte[], string, SignatureInfo, CancellationToken, Task<PdfHashOutput>>? OnHash { get; set; }
    public Func<string, Certificate, string, string, SignHashInput, string, CancellationToken, Task<SignTransaction>>? OnSubmitSignHash { get; set; }
    public Func<string, string, CancellationToken, Task<SignStatusSnapshot>>? OnGetStatus { get; set; }
    public Func<string, Certificate, PdfHashOutput, string, CancellationToken, Task<byte[]>>? OnAttach { get; set; }

    public Func<string, Certificate, string, string, XmlSignatureContext, CancellationToken, Task<XmlHashOutput>>? OnHashXml { get; set; }
    public Func<string, Certificate, byte[], string, SignatureInfo, CancellationToken, Task<WordExcelHashOutput>>? OnHashWord { get; set; }
    public Func<string, Certificate, byte[], string, SignatureInfo, CancellationToken, Task<WordExcelHashOutput>>? OnHashExcel { get; set; }
    public Func<string, Certificate, XmlHashOutput, string, CancellationToken, Task<byte[]>>? OnAttachXml { get; set; }
    public Func<string, Certificate, WordExcelHashOutput, string, DocumentFormat, CancellationToken, Task<byte[]>>? OnAttachWordExcel { get; set; }

    public int LoginCalls;
    public int RefreshCalls;
    public int ListCertCalls;
    public int HashCalls;
    public int HashXmlCalls;
    public int HashWordCalls;
    public int HashExcelCalls;
    public int SubmitSignHashCalls;
    public int GetStatusCalls;
    public int AttachCalls;
    public int AttachXmlCalls;
    public int AttachWordExcelCalls;
    public int TwoFactorAuthCalls;
    public int ResendOtpCalls;

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

    public Task<XmlHashOutput> HashXmlAsync(string accessToken, Certificate cert, string xmlContent, string documentId, XmlSignatureContext signatureContext, CancellationToken ct)
    {
        HashXmlCalls++;
        return OnHashXml!(accessToken, cert, xmlContent, documentId, signatureContext, ct);
    }

    public Task<WordExcelHashOutput> HashWordAsync(string accessToken, Certificate cert, byte[] wordBytes, string documentId, SignatureInfo signatureInfo, CancellationToken ct)
    {
        HashWordCalls++;
        return OnHashWord!(accessToken, cert, wordBytes, documentId, signatureInfo, ct);
    }

    public Task<WordExcelHashOutput> HashExcelAsync(string accessToken, Certificate cert, byte[] excelBytes, string documentId, SignatureInfo signatureInfo, CancellationToken ct)
    {
        HashExcelCalls++;
        return OnHashExcel!(accessToken, cert, excelBytes, documentId, signatureInfo, ct);
    }

    public Task<SignTransaction> SubmitSignHashAsync(string accessToken, Certificate cert, string userId, string dataToBeDisplayed, SignHashInput hash, string documentName, CancellationToken ct)
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

    public Task<byte[]> AttachSignatureToXmlAsync(string accessToken, Certificate cert, XmlHashOutput hash, string signatureData, CancellationToken ct)
    {
        AttachXmlCalls++;
        return OnAttachXml!(accessToken, cert, hash, signatureData, ct);
    }

    public Task<byte[]> AttachSignatureToWordExcelAsync(string accessToken, Certificate cert, WordExcelHashOutput hash, string signatureData, DocumentFormat format, CancellationToken ct)
    {
        AttachWordExcelCalls++;
        return OnAttachWordExcel!(accessToken, cert, hash, signatureData, format, ct);
    }

    public Task<AuthSession> TwoFactorAuthAsync(string userName, string code, OtpDeliveryChannel otpType, bool remember, CancellationToken ct)
    {
        TwoFactorAuthCalls++;
        return OnTwoFactorAuth!(userName, code, otpType, remember, ct);
    }

    public Task<OtpResendResult> ResendOtpAsync(string userName, string language, CancellationToken ct)
    {
        ResendOtpCalls++;
        return OnResendOtp!(userName, language, ct);
    }
}

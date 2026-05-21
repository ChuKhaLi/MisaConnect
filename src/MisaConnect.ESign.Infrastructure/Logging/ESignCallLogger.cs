using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Infrastructure.Logging;

internal sealed class ESignCallLogger : IMisaESignWireClient
{
    private readonly IMisaESignWireClient _inner;
    private readonly ILogger<ESignCallLogger> _logger;
    private readonly ICorrelationIdAccessor _correlation;

    public ESignCallLogger(IMisaESignWireClient inner, ILogger<ESignCallLogger> logger, ICorrelationIdAccessor correlation)
    {
        _inner = inner;
        _logger = logger;
        _correlation = correlation;
    }

    public Task<AuthSession> LoginAsync(string userName, string password, CancellationToken ct) =>
        Wrap("login-api", "POST", () => _inner.LoginAsync(userName, password, ct));

    public Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct) =>
        Wrap("refreshtoken", "POST", () => _inner.RefreshAsync(refreshToken, ct));

    public Task<AuthSession> TwoFactorAuthAsync(string userName, string code, OtpDeliveryChannel otpType, bool remember, CancellationToken ct) =>
        Wrap("two-factor-auth", "POST", () => _inner.TwoFactorAuthAsync(userName, code, otpType, remember, ct));

    public Task<OtpResendResult> ResendOtpAsync(string userName, string language, CancellationToken ct) =>
        Wrap("resend-otp-auth", "POST", () => _inner.ResendOtpAsync(userName, language, ct));

    public Task<IReadOnlyList<Certificate>> ListCertificatesByUserIdAsync(string accessToken, CancellationToken ct) =>
        Wrap("Certificates/by-userId", "GET", () => _inner.ListCertificatesByUserIdAsync(accessToken, ct));

    public Task<PdfHashOutput> HashPdfAsync(string accessToken, Certificate cert, byte[] pdfBytes, string documentId, SignatureInfo signatureInfo, CancellationToken ct) =>
        Wrap("documents/hash", "POST", () => _inner.HashPdfAsync(accessToken, cert, pdfBytes, documentId, signatureInfo, ct));

    public Task<SignTransaction> SubmitSignHashAsync(string accessToken, Certificate cert, string userId, string dataToBeDisplayed, PdfHashOutput hash, string documentName, CancellationToken ct) =>
        Wrap("Signing/hash", "POST", () => _inner.SubmitSignHashAsync(accessToken, cert, userId, dataToBeDisplayed, hash, documentName, ct));

    public Task<SignStatusSnapshot> GetSignStatusAsync(string accessToken, string transactionId, CancellationToken ct) =>
        Wrap("Signing/status", "GET", () => _inner.GetSignStatusAsync(accessToken, transactionId, ct));

    public Task<byte[]> AttachSignatureAsync(string accessToken, Certificate cert, PdfHashOutput hash, string signatureData, CancellationToken ct) =>
        Wrap("documents/attachment", "POST", () => _inner.AttachSignatureAsync(accessToken, cert, hash, signatureData, ct));

    private async Task<T> Wrap<T>(string endpoint, string method, Func<Task<T>> action)
    {
        var cid = _correlation.Current;
        _logger.LogInformation(
            "ESign call started: endpoint={Endpoint}, method={Method}, correlationId={CorrelationId}",
            endpoint, method, cid);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            sw.Stop();
            _logger.LogInformation(
                "ESign call completed: endpoint={Endpoint}, method={Method}, correlationId={CorrelationId}, durationMs={DurationMs}, outcome={Outcome}",
                endpoint, method, cid, sw.ElapsedMilliseconds, "Success");
            return result;
        }
        catch (ESignException ex)
        {
            sw.Stop();
            _logger.LogWarning(
                "ESign call completed: endpoint={Endpoint}, method={Method}, correlationId={CorrelationId}, durationMs={DurationMs}, outcome={Outcome}, errorCategory={ErrorCategory}, rawCode={RawCode}",
                endpoint, method, cid, sw.ElapsedMilliseconds, "Error", ex.Category, ex.RawCode);
            throw;
        }
        catch (Exception)
        {
            sw.Stop();
            _logger.LogError(
                "ESign call completed: endpoint={Endpoint}, method={Method}, correlationId={CorrelationId}, durationMs={DurationMs}, outcome={Outcome}",
                endpoint, method, cid, sw.ElapsedMilliseconds, "Error");
            throw;
        }
    }
}

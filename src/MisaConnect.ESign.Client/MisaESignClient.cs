using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Client.Mapping;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Client;

internal sealed class MisaESignClient : IMisaESignClient
{
    private string? _pendingUserName;
    private readonly object _pendingLock = new();

    private readonly SignPdf _signPdf;
    private readonly SignXml _signXml;
    private readonly SignWord _signWord;
    private readonly SignExcel _signExcel;
    private readonly ExchangeOtp _exchangeOtp;
    private readonly ResendOtp _resendOtp;
    private readonly OtpSubmissionValidator _otpSubmissionValidator;
    private readonly IOtpProvider? _otpProvider;

    public MisaESignClient(
        SignPdf signPdf,
        SignXml signXml,
        SignWord signWord,
        SignExcel signExcel,
        ExchangeOtp exchangeOtp,
        ResendOtp resendOtp,
        OtpSubmissionValidator otpSubmissionValidator,
        IOtpProvider? otpProvider = null)
    {
        _signPdf = signPdf;
        _signXml = signXml;
        _signWord = signWord;
        _signExcel = signExcel;
        _exchangeOtp = exchangeOtp;
        _resendOtp = resendOtp;
        _otpSubmissionValidator = otpSubmissionValidator;
        _otpProvider = otpProvider;
    }

    public async Task<SignPdfResultDto> SignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workRequest = SignPdfRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await _signPdf.ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            lock (_pendingLock) { _pendingUserName = null; }
            return new SignPdfResultDto(
                SignedPdf: result.SignedPdf.Bytes,
                TransactionId: result.TransactionId,
                CertificateKeyAlias: result.CertificateKeyAlias,
                CompletedAtUtc: result.CompletedAtUtc);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            lock (_pendingLock) { _pendingUserName = ex.Username; }
            throw;
        }
    }

    public async Task<SignXmlResultDto> SignXmlAsync(SignXmlRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workRequest = SignXmlRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await _signXml.ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            lock (_pendingLock) { _pendingUserName = null; }
            return new SignXmlResultDto(
                SignedXml: result.SignedXml.Bytes,
                TransactionId: result.TransactionId,
                CertificateKeyAlias: result.CertificateKeyAlias,
                CompletedAtUtc: result.CompletedAtUtc);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            lock (_pendingLock) { _pendingUserName = ex.Username; }
            throw;
        }
    }

    public async Task<SignWordResultDto> SignWordAsync(SignWordRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workRequest = SignWordRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await _signWord.ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            lock (_pendingLock) { _pendingUserName = null; }
            return new SignWordResultDto(
                SignedWord: result.SignedWord.Bytes,
                TransactionId: result.TransactionId,
                CertificateKeyAlias: result.CertificateKeyAlias,
                CompletedAtUtc: result.CompletedAtUtc);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            lock (_pendingLock) { _pendingUserName = ex.Username; }
            throw;
        }
    }

    public async Task<SignExcelResultDto> SignExcelAsync(SignExcelRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workRequest = SignExcelRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await _signExcel.ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            lock (_pendingLock) { _pendingUserName = null; }
            return new SignExcelResultDto(
                SignedExcel: result.SignedExcel.Bytes,
                TransactionId: result.TransactionId,
                CertificateKeyAlias: result.CertificateKeyAlias,
                CompletedAtUtc: result.CompletedAtUtc);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            lock (_pendingLock) { _pendingUserName = ex.Username; }
            throw;
        }
    }

    public async Task SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken ct = default)
    {
        string? userName;
        lock (_pendingLock) { userName = _pendingUserName; }
        if (string.IsNullOrEmpty(userName))
        {
            throw new InvalidOperationException(
                "SignInWithOtpAsync must be invoked only after catching an AuthenticationFailedException with Requires2FA = true.");
        }

        var submission = new OtpSubmission(otpCode, otpType, remember);
        _otpSubmissionValidator.Validate(submission);

        await _exchangeOtp.ExecuteAsync(userName, submission, ct).ConfigureAwait(false);
        lock (_pendingLock) { _pendingUserName = null; }
    }

    public async Task<OtpResendResultDto> ResendOtpAsync(string? language = null, CancellationToken ct = default)
    {
        string? userName;
        lock (_pendingLock) { userName = _pendingUserName; }
        if (string.IsNullOrEmpty(userName))
        {
            throw new InvalidOperationException(
                "ResendOtpAsync must be invoked only after catching an AuthenticationFailedException with Requires2FA = true.");
        }

        var result = await _resendOtp.ExecuteAsync(userName, language, ct).ConfigureAwait(false);
        return new OtpResendResultDto(
            Success: result.Success,
            RawCode: result.RawCode,
            UserMsg: result.UserMsg,
            DevMsg: result.DevMsg,
            CorrelationId: result.CorrelationId);
    }

    internal static void ClearPendingUserNameForTests() { /* obsolete — pendingUserName is per-instance now */ }
}

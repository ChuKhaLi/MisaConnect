using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Client.Dtos.Webhook;
using MisaConnect.ESign.Client.Mapping;
using MisaConnect.ESign.Client.Webhook;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.Credentials;
using ClientWebhookOutcomeDto = MisaConnect.ESign.Client.Dtos.Webhook.WebhookOutcomeDto;

namespace MisaConnect.ESign.Client;

internal sealed class MisaESignClient : IMisaESignClient
{
    private readonly PendingOtpChallenges _pending = new();
    private readonly EffectiveCredentials _credentials;

    private readonly SignPdf _signPdf;
    private readonly SignXml _signXml;
    private readonly SignWord _signWord;
    private readonly SignExcel _signExcel;
    private readonly ExchangeOtp _exchangeOtp;
    private readonly ResendOtp _resendOtp;
    private readonly OtpSubmissionValidator _otpSubmissionValidator;
    private readonly IOtpProvider? _otpProvider;
    private readonly BeginSignPdf? _beginSignPdf;
    private readonly BeginSignXml? _beginSignXml;
    private readonly BeginSignWord? _beginSignWord;
    private readonly BeginSignExcel? _beginSignExcel;
    private readonly HandleWebhook? _handleWebhook;
    private readonly IOptions<MisaESignOptions>? _options;

    public MisaESignClient(
        SignPdf signPdf,
        SignXml signXml,
        SignWord signWord,
        SignExcel signExcel,
        ExchangeOtp exchangeOtp,
        ResendOtp resendOtp,
        OtpSubmissionValidator otpSubmissionValidator,
        EffectiveCredentials credentials,
        BeginSignPdf? beginSignPdf = null,
        BeginSignXml? beginSignXml = null,
        BeginSignWord? beginSignWord = null,
        BeginSignExcel? beginSignExcel = null,
        HandleWebhook? handleWebhook = null,
        IOptions<MisaESignOptions>? options = null,
        IOtpProvider? otpProvider = null)
    {
        _signPdf = signPdf;
        _signXml = signXml;
        _signWord = signWord;
        _signExcel = signExcel;
        _exchangeOtp = exchangeOtp;
        _resendOtp = resendOtp;
        _otpSubmissionValidator = otpSubmissionValidator;
        _credentials = credentials;
        _otpProvider = otpProvider;
        _beginSignPdf = beginSignPdf;
        _beginSignXml = beginSignXml;
        _beginSignWord = beginSignWord;
        _beginSignExcel = beginSignExcel;
        _handleWebhook = handleWebhook;
        _options = options;
    }

    public async Task<SignPdfResultDto> SignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardPollingAllowed();
        var workRequest = SignPdfRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await _signPdf.ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            ClearPending();
            return new SignPdfResultDto(
                SignedPdf: result.SignedPdf.Bytes,
                TransactionId: result.TransactionId,
                CertificateKeyAlias: result.CertificateKeyAlias,
                CompletedAtUtc: result.CompletedAtUtc);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            MarkPending(ex.Username);
            throw;
        }
    }

    public async Task<SignXmlResultDto> SignXmlAsync(SignXmlRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardPollingAllowed();
        var workRequest = SignXmlRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await _signXml.ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            ClearPending();
            return new SignXmlResultDto(
                SignedXml: result.SignedXml.Bytes,
                TransactionId: result.TransactionId,
                CertificateKeyAlias: result.CertificateKeyAlias,
                CompletedAtUtc: result.CompletedAtUtc);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            MarkPending(ex.Username);
            throw;
        }
    }

    public async Task<SignWordResultDto> SignWordAsync(SignWordRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardPollingAllowed();
        var workRequest = SignWordRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await _signWord.ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            ClearPending();
            return new SignWordResultDto(
                SignedWord: result.SignedWord.Bytes,
                TransactionId: result.TransactionId,
                CertificateKeyAlias: result.CertificateKeyAlias,
                CompletedAtUtc: result.CompletedAtUtc);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            MarkPending(ex.Username);
            throw;
        }
    }

    public async Task<SignExcelResultDto> SignExcelAsync(SignExcelRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardPollingAllowed();
        var workRequest = SignExcelRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await _signExcel.ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            ClearPending();
            return new SignExcelResultDto(
                SignedExcel: result.SignedExcel.Bytes,
                TransactionId: result.TransactionId,
                CertificateKeyAlias: result.CertificateKeyAlias,
                CompletedAtUtc: result.CompletedAtUtc);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            MarkPending(ex.Username);
            throw;
        }
    }

    public async Task SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken ct = default)
    {
        var userName = CurrentPendingUserName();
        if (userName is null)
        {
            throw new InvalidOperationException(
                "SignInWithOtpAsync must be invoked only after catching an AuthenticationFailedException with Requires2FA = true.");
        }

        var submission = new OtpSubmission(otpCode, otpType, remember);
        _otpSubmissionValidator.Validate(submission);

        await _exchangeOtp.ExecuteAsync(userName, submission, ct).ConfigureAwait(false);
        ClearPending();
    }

    public async Task<OtpResendResultDto> ResendOtpAsync(string? language = null, CancellationToken ct = default)
    {
        var userName = CurrentPendingUserName();
        if (userName is null)
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

    public async Task<BeginResultDto> BeginSignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardWebhookAllowed();
        var workRequest = SignPdfRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await RequireBeginSignPdf().ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            ClearPending();
            return new BeginResultDto(result.TransactionId, result.Format);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            MarkPending(ex.Username);
            throw;
        }
    }

    public async Task<BeginResultDto> BeginSignXmlAsync(SignXmlRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardWebhookAllowed();
        var workRequest = SignXmlRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await RequireBeginSignXml().ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            ClearPending();
            return new BeginResultDto(result.TransactionId, result.Format);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            MarkPending(ex.Username);
            throw;
        }
    }

    public async Task<BeginResultDto> BeginSignWordAsync(SignWordRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardWebhookAllowed();
        var workRequest = SignWordRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await RequireBeginSignWord().ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            ClearPending();
            return new BeginResultDto(result.TransactionId, result.Format);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            MarkPending(ex.Username);
            throw;
        }
    }

    public async Task<BeginResultDto> BeginSignExcelAsync(SignExcelRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardWebhookAllowed();
        var workRequest = SignExcelRequestMapper.ToWorkRequest(request);
        try
        {
            var result = await RequireBeginSignExcel().ExecuteAsync(workRequest, ct).ConfigureAwait(false);
            ClearPending();
            return new BeginResultDto(result.TransactionId, result.Format);
        }
        catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is null)
        {
            MarkPending(ex.Username);
            throw;
        }
    }

    public async Task<WebhookHandleResultDto> HandleWebhookAsync(WebhookEnvelopeDto envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var handler = RequireHandleWebhook();
        var result = WebhookEnvelopeParser.TryParse(envelope, out var domain, out var malformedField)
            ? await handler.HandleAsync(domain, ct).ConfigureAwait(false)
            : await handler.HandleMalformedAsync(envelope.TransactionId, malformedField, ct).ConfigureAwait(false);

        return new WebhookHandleResultDto(
            Ack: new WebhookAckDto(result.Ack.ErrorCode, result.Ack.DevMsg, result.Ack.UserMsg),
            Outcome: ToOutcomeDto(result.Outcome));
    }

    private void MarkPending(string? userName)
    {
        if (string.IsNullOrEmpty(userName)) return;
        _pending.Add(_credentials.Get().ClientId, userName);
    }

    private void ClearPending()
    {
        var c = _credentials.Get();
        _pending.Remove(c.ClientId, c.UserName);
    }

    private string? CurrentPendingUserName()
    {
        var c = _credentials.Get();
        return !string.IsNullOrEmpty(c.UserName) && _pending.Contains(c.ClientId, c.UserName) ? c.UserName : null;
    }

    private void GuardPollingAllowed()
    {
        if (_options is null) return;
        if (_options.Value.Webhook.Mode == WebhookMode.Webhook)
        {
            throw new InvalidOperationException(
                "Polling-mode signing is disabled when Misa:ESign:Webhook:Mode == Webhook. Use BeginSign{Format}Async instead.");
        }
    }

    private void GuardWebhookAllowed()
    {
        if (_options is null) return;
        if (_options.Value.Webhook.Mode == WebhookMode.Polling)
        {
            throw new InvalidOperationException(
                "Webhook-mode signing is disabled when Misa:ESign:Webhook:Mode == Polling. Use Sign{Format}Async instead.");
        }
    }

    private BeginSignPdf RequireBeginSignPdf() => _beginSignPdf
        ?? throw new InvalidOperationException("BeginSignPdf not wired — register via AddMisaConnectESign(...).");
    private BeginSignXml RequireBeginSignXml() => _beginSignXml
        ?? throw new InvalidOperationException("BeginSignXml not wired — register via AddMisaConnectESign(...).");
    private BeginSignWord RequireBeginSignWord() => _beginSignWord
        ?? throw new InvalidOperationException("BeginSignWord not wired — register via AddMisaConnectESign(...).");
    private BeginSignExcel RequireBeginSignExcel() => _beginSignExcel
        ?? throw new InvalidOperationException("BeginSignExcel not wired — register via AddMisaConnectESign(...).");
    private HandleWebhook RequireHandleWebhook() => _handleWebhook
        ?? throw new InvalidOperationException("HandleWebhook not wired — register via AddMisaConnectESign(...).");

    private static ClientWebhookOutcomeDto ToOutcomeDto(WebhookOutcome outcome) => outcome switch
    {
        WebhookOutcome.SuccessWithSignedBytes s => new ClientWebhookOutcomeDto.SuccessWithSignedBytes(
            s.TransactionId, s.Format, s.SignedBytes, s.CorrelationId),
        WebhookOutcome.FailureWithError f => new ClientWebhookOutcomeDto.FailureWithError(
            f.TransactionId, f.Format, f.Category.ToString(), f.MisaErrorCode, f.CorrelationId),
        WebhookOutcome.TerminalWithoutFinalize t => new ClientWebhookOutcomeDto.TerminalWithoutFinalize(
            t.TransactionId, t.Format, (WebhookStatusDto)(byte)t.Status, t.MisaErrorCode, t.CorrelationId),
        _ => throw new InvalidOperationException($"Unknown WebhookOutcome variant: {outcome.GetType().Name}"),
    };
}

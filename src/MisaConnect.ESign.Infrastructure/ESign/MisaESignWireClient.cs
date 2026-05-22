using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.ESign.Mapping;
using MisaConnect.ESign.Infrastructure.ESign.Wire;
using DomainCert = MisaConnect.ESign.Domain.Certificates.Certificate;
using WireCertDto = MisaConnect.ESign.Infrastructure.ESign.Wire.CertificateDto;

namespace MisaConnect.ESign.Infrastructure.ESign;

internal sealed class MisaESignWireClient : IMisaESignWireClient
{
    internal const string AuthHeader = "AuthorizationRM";
    internal const string ClientIdHeader = "x-clientId";
    internal const string ClientKeyHeader = "x-clientKey";
    internal const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly HttpClient _http;
    private readonly IOptions<MisaESignOptions> _options;
    private readonly ISystemClock _clock;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly ILogger<MisaESignWireClient> _logger;

    public MisaESignWireClient(
        HttpClient http,
        IOptions<MisaESignOptions> options,
        ISystemClock clock,
        ICorrelationIdAccessor correlation,
        ILogger<MisaESignWireClient> logger)
    {
        _http = http;
        _options = options;
        _clock = clock;
        _correlation = correlation;
        _logger = logger;
    }

    public async Task<AuthSession> LoginAsync(string userName, string password, CancellationToken ct)
    {
        var body = new LoginRequestDto { UserName = userName, Password = password };
        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.AuthLoginApi, attachAuthorization: false);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowMappedAsync(resp, json, ESignHttpRoutes.AuthLoginApi, ct, userName).ConfigureAwait(false);
        }

        var dto = Deserialize<LoginResponseDto>(json);
        if (dto?.Status is { Error: true })
        {
            ThrowFromLoginEnvelope(dto.Status, ESignHttpRoutes.AuthLoginApi, userName);
        }
        if (dto?.Data is null)
        {
            throw new ESignGeneralException(
                ESignErrorCategory.MisaUnknown,
                "EmptyResponse",
                $"MISA {ESignHttpRoutes.AuthLoginApi} returned an empty data block.",
                _correlation.Current);
        }
        return AuthSessionMapper.FromLoginResponse(dto, _clock.UtcNow);
    }

    public async Task<AuthSession> TwoFactorAuthAsync(
        string userName,
        string code,
        OtpDeliveryChannel otpType,
        bool remember,
        CancellationToken ct)
    {
        var body = new TwoFactorAuthRequestDto
        {
            UserName = userName,
            Code = code,
            OtpType = (int)otpType,
            Remember = remember,
        };
        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.AuthTwoFactor, attachAuthorization: false);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            ThrowFromTwoFactorEnvelope(json, (int)resp.StatusCode, resp.StatusCode, userName);
        }

        var dto = Deserialize<LoginResponseDto>(json);
        if (dto?.Status is { Error: true })
        {
            ThrowFromTwoFactorStatus(dto.Status, userName);
        }
        if (dto?.Data is null)
        {
            throw new ESignGeneralException(
                ESignErrorCategory.MisaUnknown,
                "EmptyResponse",
                $"MISA {ESignHttpRoutes.AuthTwoFactor} returned an empty data block.",
                _correlation.Current);
        }
        return AuthSessionMapper.FromTwoFactorAuthResponse(dto, _clock.UtcNow);
    }

    public async Task<OtpResendResult> ResendOtpAsync(string userName, string language, CancellationToken ct)
    {
        var body = new ResendOtpRequestDto { UserName = userName, Language = language };
        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.AuthResendOtp, attachAuthorization: false);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        var statusCode = (int)resp.StatusCode;
        if (statusCode == 429 || statusCode >= 500)
        {
            throw new ESignTransportException(
                lastStatusCode: resp.StatusCode,
                attemptCount: 1,
                detail: $"MISA {ESignHttpRoutes.AuthResendOtp} returned transport failure (statusCode={statusCode}).",
                correlationId: _correlation.Current);
        }

        ResponseError? envelope = null;
        var dto = Deserialize<ResendOtpResponseDto>(json);
        if (dto?.Status is not null)
        {
            envelope = ResponseErrorMapper.FromLoginStatus(dto.Status);
        }
        else if (!string.IsNullOrEmpty(json))
        {
            var errDto = Deserialize<ResponseErrorDto>(json);
            if (errDto is not null)
            {
                envelope = ResponseErrorMapper.ToDomain(errDto);
            }
        }

        return Application.Errors.OtpErrorMapper.MapResendResult(
            statusCode: statusCode,
            envelope: envelope,
            correlationId: _correlation.Current,
            includeRawErrorMessage: _options.Value.Errors.IncludeRawErrorMessage);
    }

    private void ThrowFromTwoFactorEnvelope(string body, int statusCode, HttpStatusCode httpStatus, string userName)
    {
        ResponseError? envelope = null;
        if (!string.IsNullOrEmpty(body))
        {
            var statusDto = Deserialize<LoginResponseDto>(body);
            if (statusDto?.Status is not null)
            {
                envelope = ResponseErrorMapper.FromLoginStatus(statusDto.Status);
            }
            if (envelope is null)
            {
                var errDto = Deserialize<ResponseErrorDto>(body);
                if (errDto is not null)
                {
                    envelope = ResponseErrorMapper.ToDomain(errDto);
                }
            }
        }
        throw Application.Errors.OtpErrorMapper.MapTwoFactor(
            statusCode: statusCode,
            envelope: envelope,
            correlationId: _correlation.Current,
            includeRawErrorMessage: _options.Value.Errors.IncludeRawErrorMessage,
            lastStatusCode: httpStatus,
            attemptCount: 1,
            userName: userName);
    }

    private void ThrowFromTwoFactorStatus(LoginStatusBlockDto status, string userName)
    {
        var envelope = ResponseErrorMapper.FromLoginStatus(status);
        throw Application.Errors.OtpErrorMapper.MapTwoFactor(
            statusCode: status.Code ?? (int)HttpStatusCode.BadRequest,
            envelope: envelope,
            correlationId: _correlation.Current,
            includeRawErrorMessage: _options.Value.Errors.IncludeRawErrorMessage,
            lastStatusCode: null,
            attemptCount: 1,
            userName: userName);
    }

    public async Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var body = new RefreshTokenRequestDto { RefreshToken = refreshToken };
        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.AuthRefreshToken, attachAuthorization: false);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw await BuildAuthFailureAsync(resp, json, ESignHttpRoutes.AuthRefreshToken, ct).ConfigureAwait(false);
        }

        var dto = Deserialize<RefreshTokenResponseDto>(json);
        return AuthSessionMapper.FromRefreshResponse(dto, _clock.UtcNow, previousUserId: string.Empty, previousUsername: string.Empty);
    }

    public async Task<IReadOnlyList<DomainCert>> ListCertificatesByUserIdAsync(string accessToken, CancellationToken ct)
    {
        var req = NewRequest(HttpMethod.Get, ESignHttpRoutes.CertificatesByUserId);
        ApplyAuth(req, accessToken);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowMappedAsync(resp, json, ESignHttpRoutes.CertificatesByUserId, ct).ConfigureAwait(false);
        }

        var dtos = Deserialize<List<WireCertDto>>(json) ?? new List<WireCertDto>();
        try
        {
            return CertificateMapper.ToDomain(dtos);
        }
        catch (InvalidCertChainLengthException ex)
        {
            throw new ESignGeneralException(
                ESignErrorCategory.MisaUnknown,
                "InvalidCertChainLength",
                $"MISA {ESignHttpRoutes.CertificatesByUserId} returned a certificate with a chain of length {ex.ActualLength} (expected 3).",
                _correlation.Current,
                inner: ex);
        }
    }

    public async Task<PdfHashOutput> HashPdfAsync(
        string accessToken,
        DomainCert cert,
        byte[] pdfBytes,
        string documentId,
        SignatureInfo signatureInfo,
        CancellationToken ct)
    {
        var body = new HashRequestDto
        {
            Certificate = cert.CertificateValue,
            CertificateChain = cert.CertificateChain.AsList().ToList(),
            PdfDocs = new List<PdfDocRequestDto>
            {
                new()
                {
                    DocumentId = documentId,
                    FileToSign = Convert.ToBase64String(pdfBytes),
                    SignatureInfo = ToWireSignatureInfo(signatureInfo),
                },
            },
        };

        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.DocumentsHash);
        ApplyAuth(req, accessToken);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowMappedAsync(resp, json, ESignHttpRoutes.DocumentsHash, ct, requestedFormat: DocumentFormat.Pdf).ConfigureAwait(false);
        }

        var dto = Deserialize<HashResponseDto>(json);
        if (dto?.PdfDocs is not { Count: > 0 })
        {
            throw new ESignGeneralException(
                ESignErrorCategory.MisaUnknown,
                "EmptyResponse",
                $"MISA {ESignHttpRoutes.DocumentsHash} returned no pdfDocs.",
                _correlation.Current,
                format: DocumentFormat.Pdf);
        }
        var first = dto.PdfDocs[0];
        return new PdfHashOutput(
            DocumentId: first.DocumentId,
            DocumentBytes: first.DocumentBytes,
            DocumentHash: first.DocumentHash,
            Sh: first.Sh,
            SignatureName: first.SignatureName,
            Digest: first.Digest);
    }

    public async Task<XmlHashOutput> HashXmlAsync(
        string accessToken,
        DomainCert cert,
        string xmlContent,
        string documentId,
        XmlSignatureContext signatureContext,
        CancellationToken ct)
    {
        var body = new HashRequestDto
        {
            Certificate = cert.CertificateValue,
            CertificateChain = cert.CertificateChain.AsList().ToList(),
            XmlDocs = new List<XmlHashDocRequestDto>
            {
                new()
                {
                    DocumentId = documentId,
                    FileToSign = xmlContent,
                    SignatureInfo = XmlSignatureContextMapper.ToWireSignatureInfo(signatureContext),
                },
            },
        };

        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.DocumentsHash);
        ApplyAuth(req, accessToken);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowMappedAsync(resp, json, ESignHttpRoutes.DocumentsHash, ct, requestedFormat: DocumentFormat.Xml).ConfigureAwait(false);
        }

        var dto = Deserialize<HashResponseDto>(json);
        if (dto?.XmlDocs is not { Count: > 0 })
        {
            throw new ESignGeneralException(
                ESignErrorCategory.MisaUnknown,
                "EmptyResponse",
                $"MISA {ESignHttpRoutes.DocumentsHash} returned no xmlDocs.",
                _correlation.Current,
                format: DocumentFormat.Xml);
        }
        var first = dto.XmlDocs[0];
        return new XmlHashOutput(
            DocumentId: first.DocumentId,
            Document: first.Document,
            SignatureId: first.SignatureId,
            Digest: first.Digest,
            Sh: first.Sh);
    }

    public async Task<WordExcelHashOutput> HashWordAsync(
        string accessToken,
        DomainCert cert,
        byte[] wordBytes,
        string documentId,
        SignatureInfo signatureInfo,
        CancellationToken ct) =>
        await HashWordOrExcelAsync(accessToken, cert, wordBytes, documentId, signatureInfo, DocumentFormat.Word, ct).ConfigureAwait(false);

    public async Task<WordExcelHashOutput> HashExcelAsync(
        string accessToken,
        DomainCert cert,
        byte[] excelBytes,
        string documentId,
        SignatureInfo signatureInfo,
        CancellationToken ct) =>
        await HashWordOrExcelAsync(accessToken, cert, excelBytes, documentId, signatureInfo, DocumentFormat.Excel, ct).ConfigureAwait(false);

    private async Task<WordExcelHashOutput> HashWordOrExcelAsync(
        string accessToken,
        DomainCert cert,
        byte[] payload,
        string documentId,
        SignatureInfo signatureInfo,
        DocumentFormat format,
        CancellationToken ct)
    {
        var body = new HashRequestDto
        {
            Certificate = cert.CertificateValue,
            CertificateChain = cert.CertificateChain.AsList().ToList(),
        };
        if (format == DocumentFormat.Word)
        {
            body.WordDocs = new List<WordHashDocRequestDto>
            {
                new()
                {
                    DocumentId = documentId,
                    FileToSign = Convert.ToBase64String(payload),
                    SignatureInfo = ToWireSignatureInfo(signatureInfo),
                },
            };
        }
        else
        {
            body.ExcelDocs = new List<ExcelHashDocRequestDto>
            {
                new()
                {
                    DocumentId = documentId,
                    FileToSign = Convert.ToBase64String(payload),
                    SignatureInfo = ToWireSignatureInfo(signatureInfo),
                },
            };
        }

        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.DocumentsHash);
        ApplyAuth(req, accessToken);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowMappedAsync(resp, json, ESignHttpRoutes.DocumentsHash, ct, requestedFormat: format).ConfigureAwait(false);
        }

        var dto = Deserialize<HashResponseDto>(json);
        if (format == DocumentFormat.Word)
        {
            if (dto?.WordDocs is not { Count: > 0 })
            {
                throw new ESignGeneralException(
                    ESignErrorCategory.MisaUnknown,
                    "EmptyResponse",
                    $"MISA {ESignHttpRoutes.DocumentsHash} returned no wordDocs.",
                    _correlation.Current,
                    format: DocumentFormat.Word);
            }
            var first = dto.WordDocs[0];
            return new WordExcelHashOutput(
                DocumentId: first.DocumentId,
                DocumentBytes: first.DocumentBytes,
                SignatureId: first.SignatureId,
                Digest: first.Digest,
                MainDom: first.MainDom);
        }
        else
        {
            if (dto?.ExcelDocs is not { Count: > 0 })
            {
                throw new ESignGeneralException(
                    ESignErrorCategory.MisaUnknown,
                    "EmptyResponse",
                    $"MISA {ESignHttpRoutes.DocumentsHash} returned no excelDocs.",
                    _correlation.Current,
                    format: DocumentFormat.Excel);
            }
            var first = dto.ExcelDocs[0];
            return new WordExcelHashOutput(
                DocumentId: first.DocumentId,
                DocumentBytes: first.DocumentBytes,
                SignatureId: first.SignatureId,
                Digest: first.Digest,
                MainDom: first.MainDom);
        }
    }

    public async Task<SignTransaction> SubmitSignHashAsync(
        string accessToken,
        DomainCert cert,
        string userId,
        string dataToBeDisplayed,
        SignHashInput hash,
        string documentName,
        CancellationToken ct)
    {
        var body = new SignHashRequestDto
        {
            DataToBeDisplayed = dataToBeDisplayed,
            UserId = userId,
            CertAlias = cert.KeyAlias,
            Documents = new List<SignHashDocumentDto>
            {
                new()
                {
                    DocumentId = hash.DocumentId,
                    FileToSign = hash.Digest,
                    DocumentName = documentName,
                },
            },
        };

        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.SigningHash);
        ApplyAuth(req, accessToken);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowMappedAsync(resp, json, ESignHttpRoutes.SigningHash, ct).ConfigureAwait(false);
        }

        var dto = Deserialize<SignHashResponseDto>(json);
        if (dto is null || string.IsNullOrEmpty(dto.TransactionId))
        {
            throw new ESignGeneralException(
                ESignErrorCategory.MisaUnknown,
                "EmptyResponse",
                $"MISA {ESignHttpRoutes.SigningHash} returned no transactionId.",
                _correlation.Current);
        }
        return new SignTransaction(dto.TransactionId, _clock.UtcNow);
    }

    public async Task<SignStatusSnapshot> GetSignStatusAsync(string accessToken, string transactionId, CancellationToken ct)
    {
        var path = $"{ESignHttpRoutes.SigningStatus}/{Uri.EscapeDataString(transactionId)}";
        var req = NewRequest(HttpMethod.Get, path);
        ApplyAuth(req, accessToken);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowMappedAsync(resp, json, ESignHttpRoutes.SigningStatus, ct).ConfigureAwait(false);
        }

        var dto = Deserialize<SignStatusResponseDto>(json);
        if (dto is null)
        {
            throw new ESignGeneralException(
                ESignErrorCategory.MisaUnknown,
                "EmptyResponse",
                $"MISA {ESignHttpRoutes.SigningStatus} returned an empty body.",
                _correlation.Current);
        }
        return SignStatusMapper.ToSnapshot(dto, transactionId);
    }

    public async Task<byte[]> AttachSignatureAsync(
        string accessToken,
        DomainCert cert,
        PdfHashOutput hash,
        string signatureData,
        CancellationToken ct)
    {
        var body = new AttachmentRequestDto
        {
            Certificate = cert.CertificateValue,
            CertificateChain = cert.CertificateChain.AsList().ToList(),
            PdfDocs = new List<AttachmentPdfDocRequestDto>
            {
                new()
                {
                    Signature = signatureData,
                    DocumentId = hash.DocumentId,
                    DocumentBytes = hash.DocumentBytes,
                    Digest = hash.Digest,
                    SignatureName = hash.SignatureName,
                    Sh = hash.Sh,
                    DocumentHash = hash.DocumentHash,
                },
            },
        };

        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.DocumentsAttachment);
        ApplyAuth(req, accessToken);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowMappedAsync(resp, json, ESignHttpRoutes.DocumentsAttachment, ct, requestedFormat: DocumentFormat.Pdf).ConfigureAwait(false);
        }

        var dto = Deserialize<AttachmentResponseDto>(json);
        var doc = dto?.PdfDocs is { Count: > 0 } ? dto.PdfDocs[0].Document : null;
        if (string.IsNullOrEmpty(doc))
        {
            throw new ESignGeneralException(
                ESignErrorCategory.MisaUnknown,
                "EmptyResponse",
                $"MISA {ESignHttpRoutes.DocumentsAttachment} returned no signed document.",
                _correlation.Current,
                format: DocumentFormat.Pdf);
        }
        return Convert.FromBase64String(doc);
    }

    public async Task<byte[]> AttachSignatureToXmlAsync(
        string accessToken,
        DomainCert cert,
        XmlHashOutput hash,
        string signatureData,
        CancellationToken ct)
    {
        var body = new AttachmentRequestDto
        {
            Certificate = cert.CertificateValue,
            CertificateChain = cert.CertificateChain.AsList().ToList(),
            XmlDocs = new List<XmlAttachmentDocRequestDto>
            {
                new()
                {
                    Signature = signatureData,
                    DocumentId = hash.DocumentId,
                    DocumentBytes = hash.Document,
                    Digest = hash.Digest,
                    SignatureName = string.Empty,
                    Sh = hash.Sh,
                    SignatureId = hash.SignatureId,
                    DocumentHash = string.Empty,
                },
            },
        };

        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.DocumentsAttachment);
        ApplyAuth(req, accessToken);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowMappedAsync(resp, json, ESignHttpRoutes.DocumentsAttachment, ct, requestedFormat: DocumentFormat.Xml).ConfigureAwait(false);
        }

        var dto = Deserialize<AttachmentResponseDto>(json);
        var doc = dto?.XmlDocs is { Count: > 0 } ? dto.XmlDocs[0].Document : null;
        if (string.IsNullOrEmpty(doc))
        {
            throw new ESignGeneralException(
                ESignErrorCategory.AttachmentRejected,
                "MissingSignedDocument",
                $"MISA {ESignHttpRoutes.DocumentsAttachment} returned no signed xml document.",
                _correlation.Current,
                format: DocumentFormat.Xml);
        }
        return Encoding.UTF8.GetBytes(doc);
    }

    public async Task<byte[]> AttachSignatureToWordExcelAsync(
        string accessToken,
        DomainCert cert,
        WordExcelHashOutput hash,
        string signatureData,
        DocumentFormat format,
        CancellationToken ct)
    {
        if (format != DocumentFormat.Word && format != DocumentFormat.Excel)
        {
            throw new ArgumentException(
                $"AttachSignatureToWordExcelAsync requires format Word or Excel, got {format}.",
                nameof(format));
        }

        var entry = new WordExcelAttachmentDocRequestDto
        {
            Signature = signatureData,
            DocumentId = hash.DocumentId,
            DocumentBytes = hash.DocumentBytes,
            Digest = hash.Digest,
            MainDom = hash.MainDom,
            SignatureName = string.Empty,
            Sh = string.Empty,
            SignatureId = hash.SignatureId,
            DocumentHash = string.Empty,
        };

        var body = new AttachmentRequestDto
        {
            Certificate = cert.CertificateValue,
            CertificateChain = cert.CertificateChain.AsList().ToList(),
        };
        if (format == DocumentFormat.Word)
        {
            body.WordDocs = new List<WordExcelAttachmentDocRequestDto> { entry };
        }
        else
        {
            body.ExcelDocs = new List<WordExcelAttachmentDocRequestDto> { entry };
        }

        var req = NewRequest(HttpMethod.Post, ESignHttpRoutes.DocumentsAttachment);
        ApplyAuth(req, accessToken);
        req.Content = JsonContent.Create(body, options: ESignJsonOptions.Wire);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await ReadStringAsync(resp, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowMappedAsync(resp, json, ESignHttpRoutes.DocumentsAttachment, ct, requestedFormat: format).ConfigureAwait(false);
        }

        var dto = Deserialize<AttachmentResponseDto>(json);
        string? doc;
        if (format == DocumentFormat.Word)
        {
            doc = dto?.WordDocs is { Count: > 0 } ? dto.WordDocs[0].Document : null;
        }
        else
        {
            doc = dto?.ExcelDocs is { Count: > 0 } ? dto.ExcelDocs[0].Document : null;
        }
        if (string.IsNullOrEmpty(doc))
        {
            throw new ESignGeneralException(
                ESignErrorCategory.AttachmentRejected,
                "MissingSignedDocument",
                $"MISA {ESignHttpRoutes.DocumentsAttachment} returned no signed {format} document.",
                _correlation.Current,
                format: format);
        }
        return Convert.FromBase64String(doc);
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string relativePath, bool attachAuthorization = true)
    {
        var req = new HttpRequestMessage(method, relativePath);
        req.Headers.TryAddWithoutValidation(ClientIdHeader, _options.Value.ClientId);
        req.Headers.TryAddWithoutValidation(ClientKeyHeader, _options.Value.ClientKey);
        req.Headers.TryAddWithoutValidation(CorrelationIdHeader, _correlation.Current);
        // attachAuthorization is honored by callers via ApplyAuth — login/refresh skip it.
        _ = attachAuthorization;
        return req;
    }

    private static void ApplyAuth(HttpRequestMessage req, string accessToken)
    {
        req.Headers.Remove(AuthHeader);
        req.Headers.TryAddWithoutValidation(AuthHeader, $"Bearer {accessToken}");
    }

    private static async Task<string> ReadStringAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.Content is null) return string.Empty;
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, ESignJsonOptions.Wire);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private async Task ThrowMappedAsync(
        HttpResponseMessage resp,
        string body,
        string endpoint,
        CancellationToken ct,
        string? userName = null,
        DocumentFormat requestedFormat = DocumentFormat.Pdf)
    {
        await Task.CompletedTask;
        _ = ct;
        var statusCode = (int)resp.StatusCode;
        ResponseError? envelope = null;
        if (!string.IsNullOrEmpty(body))
        {
            var dto = Deserialize<ResponseErrorDto>(body);
            if (dto is not null)
            {
                envelope = ResponseErrorMapper.ToDomain(dto);
            }
        }
        throw Application.Errors.ESignErrorMapper.Map(
            endpoint: endpoint,
            statusCode: statusCode,
            envelope: envelope,
            correlationId: _correlation.Current,
            includeRawErrorMessage: _options.Value.Errors.IncludeRawErrorMessage,
            transactionId: null,
            attemptCount: null,
            lastStatusCode: resp.StatusCode,
            userName: userName,
            requestedFormat: requestedFormat);
    }

    private async Task<ESignException> BuildAuthFailureAsync(HttpResponseMessage resp, string body, string endpoint, CancellationToken ct)
    {
        await Task.CompletedTask;
        _ = ct;
        ResponseError? envelope = null;
        if (!string.IsNullOrEmpty(body))
        {
            var dto = Deserialize<ResponseErrorDto>(body);
            if (dto is not null)
            {
                envelope = ResponseErrorMapper.ToDomain(dto);
            }
        }
        return Application.Errors.ESignErrorMapper.Map(
            endpoint: endpoint,
            statusCode: (int)resp.StatusCode,
            envelope: envelope,
            correlationId: _correlation.Current,
            includeRawErrorMessage: _options.Value.Errors.IncludeRawErrorMessage,
            transactionId: null,
            attemptCount: null,
            lastStatusCode: resp.StatusCode);
    }

    private void ThrowFromLoginEnvelope(LoginStatusBlockDto status, string endpoint, string? userName = null)
    {
        var envelope = ResponseErrorMapper.FromLoginStatus(status);
        throw Application.Errors.ESignErrorMapper.Map(
            endpoint: endpoint,
            statusCode: status.Code ?? (int)HttpStatusCode.BadRequest,
            envelope: envelope,
            correlationId: _correlation.Current,
            includeRawErrorMessage: _options.Value.Errors.IncludeRawErrorMessage,
            transactionId: null,
            attemptCount: null,
            lastStatusCode: null,
            userName: userName);
    }

    private static SignatureInfoDto ToWireSignatureInfo(SignatureInfo source)
    {
        var dto = new SignatureInfoDto
        {
            TextColor = source.TextColor,
            PositionX = source.PositionX,
            PositionY = source.PositionY,
            Width = source.Width,
            Height = source.Height,
            FontSize = source.FontSize,
            FontData = source.FontData,
            SignatureImage = source.SignatureImage,
            Page = source.Page,
            SignatureName = source.SignatureName,
            HashAlgorithm = source.HashAlgorithm.ToString(),
            LogoImage = source.LogoImage,
            RenderingMode = source.RenderingMode,
            SignatureDescription = new SignatureDescriptionDto
            {
                SignedBy = source.SignatureDescription.SignedBy,
                ShowSignedDate = source.SignatureDescription.ShowSignedDate,
                Location = source.SignatureDescription.Location,
                Reason = source.SignatureDescription.Reason,
                Contact = source.SignatureDescription.Contact,
                DisplayText = source.SignatureDescription.DisplayText,
            },
        };

        if (source.SignaturePosInfos is not null && source.SignaturePosInfos.Count > 0)
        {
            dto.SignaturePosInfos = source.SignaturePosInfos
                .Select(p => new SignaturePosInfoDto
                {
                    PositionX = p.PositionX,
                    PositionY = p.PositionY,
                    Width = p.Width,
                    Height = p.Height,
                    Page = p.Page,
                })
                .ToList();
        }

        return dto;
    }
}

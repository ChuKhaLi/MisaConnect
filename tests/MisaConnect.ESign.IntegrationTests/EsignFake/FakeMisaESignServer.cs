using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MisaConnect.ESign.IntegrationTests.EsignFake;

/// <summary>
/// In-test ASP.NET Core host that emulates the MISA eSign endpoints used by
/// slice 1, slice 2, and slice 3. The <c>/documents/hash</c> and
/// <c>/documents/attachment</c> route handlers inspect the populated
/// per-format request array (pdfDocs / xmlDocs / wordDocs / excelDocs) and
/// shape the response accordingly per MISA §4.15 / §4.6.
/// </summary>
internal sealed class FakeMisaESignServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Counters _counters = new();
    private readonly Scenario _scenario = new();
    private readonly ConcurrentDictionary<string, int> _statusCallsByTx = new();
    private readonly List<RecordedTwoFactorRequest> _twoFactorBodies = new();
    private readonly List<RecordedResendOtpRequest> _resendBodies = new();
    private readonly List<RecordedFormatRequest> _hashRequests = new();
    private readonly List<RecordedFormatRequest> _attachmentRequests = new();
    private readonly object _twoFactorLock = new();
    private readonly object _formatLock = new();

    public string BaseUrl { get; }
    public Counters Calls => _counters;
    public Scenario Configure => _scenario;
    public byte[] SignedPdfBytes { get; set; } = new byte[] { 0x25, 0x50, 0x44, 0x46, 0xAA, 0xBB };
    public byte[] SignedWordBytes { get; set; } = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x57, 0x4F };
    public byte[] SignedExcelBytes { get; set; } = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x58, 0x4C };
    public string SignedXmlText { get; set; } = "<signed/>";
    public IReadOnlyList<RecordedTwoFactorRequest> TwoFactorBodies
    {
        get { lock (_twoFactorLock) { return _twoFactorBodies.ToArray(); } }
    }
    public IReadOnlyList<RecordedResendOtpRequest> ResendBodies
    {
        get { lock (_twoFactorLock) { return _resendBodies.ToArray(); } }
    }
    public IReadOnlyList<RecordedFormatRequest> HashRequests
    {
        get { lock (_formatLock) { return _hashRequests.ToArray(); } }
    }
    public IReadOnlyList<RecordedFormatRequest> AttachmentRequests
    {
        get { lock (_formatLock) { return _attachmentRequests.ToArray(); } }
    }

    private FakeMisaESignServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public static async Task<FakeMisaESignServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0/");
        builder.Logging.ClearProviders();
        builder.Services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        var app = builder.Build();

        FakeMisaESignServer? self = null;

        app.MapPost("/api/auth/api/v1/auth/login-api", async (HttpContext ctx) =>
        {
            self!._counters.Login++;
            if (self._scenario.LoginAlways401)
            {
                ctx.Response.StatusCode = 401;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"error\":true,\"errorCode\":\"InvalidPassword\"}");
                return;
            }
            if (self._scenario.LoginRequires2FAOnFirstCall && Interlocked.Exchange(ref self._scenario._login122Consumed, 1) == 0)
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"status\":{\"code\":200,\"error\":true,\"errorCode\":\"122\"}}");
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var body = "{\"status\":{\"code\":200,\"error\":false},\"data\":{\"accessToken\":\"raw-token\",\"remoteSigningAccessToken\":\"rs-token\",\"refreshToken\":\"refresh-token\",\"expiresIn\":3600,\"tokenType\":\"Bearer\",\"user\":{\"id\":\"user-id\",\"username\":\"alice\"}}}";
            await ctx.Response.WriteAsync(body);
        });

        app.MapPost("/api/auth/api/v1/auth/two-factor-auth", async (HttpContext ctx) =>
        {
            self!._counters.TwoFactorAuth++;
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            var hasAuthorizationRm = ctx.Request.Headers.ContainsKey("AuthorizationRM");
            lock (self._twoFactorLock)
            {
                self._twoFactorBodies.Add(new RecordedTwoFactorRequest(body, hasAuthorizationRm));
            }
            var errCode = self._scenario.NextTwoFactorErrorCode;
            if (!string.IsNullOrEmpty(errCode))
            {
                ctx.Response.StatusCode = errCode == "122" ? 400 : 400;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync($"{{\"status\":{{\"code\":400,\"error\":true,\"errorCode\":\"{errCode}\"}}}}");
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var ok = "{\"status\":{\"code\":200,\"error\":false},\"data\":{\"accessToken\":\"raw-token-2fa\",\"remoteSigningAccessToken\":\"rs-token-2fa\",\"refreshToken\":\"refresh-token-2fa\",\"expiresIn\":3600,\"tokenType\":\"Bearer\",\"user\":{\"id\":\"user-id\",\"username\":\"alice\"}}}";
            await ctx.Response.WriteAsync(ok);
        });

        app.MapPost("/webdev/api/auth/api/v1/auth/resend-otp-auth", async (HttpContext ctx) =>
        {
            self!._counters.ResendOtp++;
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            var hasAuthorizationRm = ctx.Request.Headers.ContainsKey("AuthorizationRM");
            lock (self._twoFactorLock)
            {
                self._resendBodies.Add(new RecordedResendOtpRequest(body, hasAuthorizationRm));
            }
            switch (self._scenario.ResendOtpMode)
            {
                case ResendOtpResponseMode.Success200:
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"status\":{\"code\":200,\"error\":false},\"data\":{\"user\":{\"username\":\"alice\"}}}");
                    return;
                case ResendOtpResponseMode.TypedFailure200:
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"status\":{\"code\":200,\"error\":true,\"errorCode\":\"RateLimited\",\"devMsg\":\"too soon\",\"userMsg\":\"please wait\"}}");
                    return;
                case ResendOtpResponseMode.TypedFailure4xx:
                    ctx.Response.StatusCode = 400;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"status\":{\"code\":400,\"error\":true,\"errorCode\":\"BadRequest\",\"devMsg\":\"bad\",\"userMsg\":\"u-bad\"}}");
                    return;
                case ResendOtpResponseMode.NoEnvelope4xx:
                    ctx.Response.StatusCode = 400;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("");
                    return;
                case ResendOtpResponseMode.Transport500:
                    ctx.Response.StatusCode = 500;
                    return;
                default:
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.WriteAsync("{\"status\":{\"code\":200,\"error\":false}}");
                    return;
            }
        });

        app.MapPost("/webdev/api/auth/api/v1/auth/refreshtoken", async (HttpContext ctx) =>
        {
            self!._counters.Refresh++;
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var body = "{\"accessToken\":\"raw-token-2\",\"remoteSigningAccessToken\":\"rs-token-2\",\"refreshToken\":\"refresh-token-2\",\"expiresIn\":3600}";
            await ctx.Response.WriteAsync(body);
        });

        app.MapGet("/external/esrm/service/general/api/v1/Certificates/by-userId", async (HttpContext ctx) =>
        {
            var headerAuth = ctx.Request.Headers["AuthorizationRM"].ToString();
            if (self!._scenario.CertsForce401Once && Interlocked.Exchange(ref self._scenario._certs401Consumed, 1) == 0)
            {
                self._counters.Certificates++;
                ctx.Response.StatusCode = 401;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"error\":true,\"errorCode\":\"TokenExpired\"}");
                return;
            }
            self._counters.Certificates++;
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var body = "[{\"userId\":\"user-id\",\"keyAlias\":\"key-alias-1\",\"appName\":\"misa\",\"keyStatus\":\"ACTIVE\",\"certStatus\":\"ACTIVE\",\"certificate\":\"BASE64CERT\",\"certiticateChain\":[\"SIGN\",\"INTERMEDIATE\",\"ROOT\"],\"effectiveDate\":\"2026-01-01T00:00:00Z\",\"expirationDate\":\"2027-01-01T00:00:00Z\",\"emailName\":\"alice@example.com\",\"isAutoSign\":false}]";
            await ctx.Response.WriteAsync(body);
            _ = headerAuth;
        });

        app.MapPost("/external/esrm/service/document/api/v1/documents/hash", async (HttpContext ctx) =>
        {
            self!._counters.Hash++;
            using var reader = new StreamReader(ctx.Request.Body);
            var bodyText = await reader.ReadToEndAsync();
            var requested = DetectFormat(bodyText);
            lock (self._formatLock) { self._hashRequests.Add(new RecordedFormatRequest(bodyText, requested)); }

            if (self._scenario.HashRejectionForFormat is { } rej && rej.format == requested)
            {
                ctx.Response.StatusCode = rej.statusCode;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync($"{{\"errorCode\":\"{rej.errorCode}\",\"devMsg\":\"{rej.devMsg}\",\"userMsg\":\"{rej.userMsg}\"}}");
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(BuildHashResponseBody(requested, self._scenario));
        });

        app.MapPost("/external/esrm/service/signing/api/v1/Signing/hash", async (HttpContext ctx) =>
        {
            self!._counters.SignHash++;
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"transactionId\":\"tx-fake-1\"}");
        });

        app.MapGet("/external/esrm/service/signing/api/v1/Signing/status/{tx}", async (HttpContext ctx, string tx) =>
        {
            self!._counters.SignStatus++;
            var calls = self._statusCallsByTx.AddOrUpdate(tx, 1, (_, v) => v + 1);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";

            if (self._scenario.StatusAlwaysFailed)
            {
                await ctx.Response.WriteAsync($"{{\"status\":\"FAILED\",\"errorCode\":\"ResultBad\",\"errorDescription\":\"sign failed\",\"transactionId\":\"{tx}\"}}");
                return;
            }
            if (self._scenario.StatusAlwaysCancelled)
            {
                await ctx.Response.WriteAsync($"{{\"status\":\"CANCELLED\",\"transactionId\":\"{tx}\"}}");
                return;
            }
            if (self._scenario.StatusAlwaysPending)
            {
                await ctx.Response.WriteAsync($"{{\"status\":\"PENDING\",\"transactionId\":\"{tx}\"}}");
                return;
            }

            var pendingTarget = self._scenario.PendingsBeforeSuccess;
            if (calls <= pendingTarget)
            {
                await ctx.Response.WriteAsync($"{{\"status\":\"PENDING\",\"transactionId\":\"{tx}\"}}");
                return;
            }
            await ctx.Response.WriteAsync($"{{\"status\":\"SUCCESS\",\"transactionId\":\"{tx}\",\"signatures\":[{{\"documentId\":\"doc-1\",\"signature\":\"SIGNATURE-BYTES\"}}]}}");
        });

        app.MapPost("/external/esrm/service/document/api/v1/documents/attachment", async (HttpContext ctx) =>
        {
            self!._counters.Attachment++;
            using var reader = new StreamReader(ctx.Request.Body);
            var bodyText = await reader.ReadToEndAsync();
            var requested = DetectFormat(bodyText);
            lock (self._formatLock) { self._attachmentRequests.Add(new RecordedFormatRequest(bodyText, requested)); }

            if (self._scenario.AttachmentRejectionForFormat is { } rej && rej.format == requested)
            {
                ctx.Response.StatusCode = rej.statusCode;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync($"{{\"errorCode\":\"{rej.errorCode}\",\"devMsg\":\"{rej.devMsg}\",\"userMsg\":\"{rej.userMsg}\"}}");
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(BuildAttachmentResponseBody(requested, self));
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses
            .First();
        self = new FakeMisaESignServer(app, address.TrimEnd('/') + "/");
        SetSelf(app, self);
        return self;
    }

    private static RequestedFormat DetectFormat(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (TryArrayHas(root, "pdfDocs")) return RequestedFormat.Pdf;
            if (TryArrayHas(root, "xmlDocs")) return RequestedFormat.Xml;
            if (TryArrayHas(root, "wordDocs")) return RequestedFormat.Word;
            if (TryArrayHas(root, "excelDocs")) return RequestedFormat.Excel;
        }
        catch (JsonException) { }
        return RequestedFormat.Pdf;
    }

    private static bool TryArrayHas(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0;

    private static string BuildHashResponseBody(RequestedFormat requested, Scenario scenario)
    {
        if (scenario.MultiArrayResponse)
        {
            return "{" +
                "\"pdfDocs\":[{\"documentId\":\"doc-1\",\"documentBytes\":\"DOCBYTES\",\"documentHash\":\"DOCHASH\",\"sh\":\"SH\",\"signatureName\":\"SigName\",\"digest\":\"DIGEST\"}]," +
                "\"xmlDocs\":[{\"documentId\":\"doc-1\",\"document\":\"<doc/>\",\"signatureId\":\"sig-x\",\"digest\":\"DIGEST\",\"sh\":\"SH\"}]," +
                "\"wordDocs\":[{\"documentId\":\"doc-1\",\"documentBytes\":\"WORDBYTES\",\"signatureId\":\"sig-w\",\"digest\":\"DIGEST\",\"mainDom\":\"WORDMAIN\"}]," +
                "\"excelDocs\":[{\"documentId\":\"doc-1\",\"documentBytes\":\"EXCELBYTES\",\"signatureId\":\"sig-e\",\"digest\":\"DIGEST\",\"mainDom\":\"EXCELMAIN\"}]" +
                "}";
        }
        return requested switch
        {
            RequestedFormat.Pdf => "{\"pdfDocs\":[{\"documentId\":\"doc-1\",\"documentBytes\":\"DOCBYTES\",\"documentHash\":\"DOCHASH\",\"sh\":\"SH\",\"signatureName\":\"SigName\",\"digest\":\"DIGEST\"}]}",
            RequestedFormat.Xml => "{\"xmlDocs\":[{\"documentId\":\"doc-1\",\"document\":\"<doc/>\",\"signatureId\":\"sig-x\",\"digest\":\"DIGEST\",\"sh\":\"SH\"}]}",
            RequestedFormat.Word => "{\"wordDocs\":[{\"documentId\":\"doc-1\",\"documentBytes\":\"WORDBYTES\",\"signatureId\":\"sig-w\",\"digest\":\"DIGEST\",\"mainDom\":\"WORDMAIN\"}]}",
            RequestedFormat.Excel => "{\"excelDocs\":[{\"documentId\":\"doc-1\",\"documentBytes\":\"EXCELBYTES\",\"signatureId\":\"sig-e\",\"digest\":\"DIGEST\",\"mainDom\":\"EXCELMAIN\"}]}",
            _ => "{}",
        };
    }

    private static string BuildAttachmentResponseBody(RequestedFormat requested, FakeMisaESignServer self)
    {
        var scenario = self._scenario;
        if (scenario.AttachmentMissingFormatArrayFor == requested)
        {
            return requested switch
            {
                RequestedFormat.Pdf => "{\"pdfDocs\":[]}",
                RequestedFormat.Xml => "{\"xmlDocs\":[]}",
                RequestedFormat.Word => "{\"wordDocs\":[]}",
                RequestedFormat.Excel => "{\"excelDocs\":[]}",
                _ => "{}",
            };
        }
        if (scenario.MultiArrayResponse)
        {
            var pdfB64 = Convert.ToBase64String(self.SignedPdfBytes);
            var wordB64 = Convert.ToBase64String(self.SignedWordBytes);
            var excelB64 = Convert.ToBase64String(self.SignedExcelBytes);
            return "{" +
                $"\"pdfDocs\":[{{\"documentId\":\"doc-1\",\"document\":\"{pdfB64}\"}}]," +
                $"\"xmlDocs\":[{{\"documentId\":\"doc-1\",\"document\":\"{JsonEscape(self.SignedXmlText)}\"}}]," +
                $"\"wordDocs\":[{{\"documentId\":\"doc-1\",\"document\":\"{wordB64}\"}}]," +
                $"\"excelDocs\":[{{\"documentId\":\"doc-1\",\"document\":\"{excelB64}\"}}]" +
                "}";
        }
        return requested switch
        {
            RequestedFormat.Pdf => "{\"pdfDocs\":[{\"documentId\":\"doc-1\",\"document\":\"" + Convert.ToBase64String(self.SignedPdfBytes) + "\"}]}",
            RequestedFormat.Xml => "{\"xmlDocs\":[{\"documentId\":\"doc-1\",\"document\":\"" + JsonEscape(self.SignedXmlText) + "\"}]}",
            RequestedFormat.Word => "{\"wordDocs\":[{\"documentId\":\"doc-1\",\"document\":\"" + Convert.ToBase64String(self.SignedWordBytes) + "\"}]}",
            RequestedFormat.Excel => "{\"excelDocs\":[{\"documentId\":\"doc-1\",\"document\":\"" + Convert.ToBase64String(self.SignedExcelBytes) + "\"}]}",
            _ => "{}",
        };
    }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static void SetSelf(WebApplication app, FakeMisaESignServer self)
    {
        _ = app;
        _ = self;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    /// Slice 4 helper: builds a MISA-shaped webhook envelope JSON body for
    /// driving the sample API's webhook endpoint in integration tests
    /// (§3.8 / §4.9). Auto-generates a messageId if not provided.
    /// </summary>
    public static string BuildSyntheticWebhookBody(
        string transactionId,
        string clientId,
        string documentId = "doc-1",
        string signatureBytes = "SIGNATURE-BYTES",
        string status = "SUCCESS",
        string? messageId = null,
        string? errorCode = null)
    {
        messageId ??= Guid.NewGuid().ToString("N");
        var sigArray = status == "SUCCESS"
            ? "[{\"documentId\":\"" + JsonEscape(documentId) + "\",\"signature\":\"" + JsonEscape(signatureBytes) + "\"}]"
            : "[]";
        var errorField = errorCode is null ? "null" : "\"" + JsonEscape(errorCode) + "\"";
        return "{" +
            "\"messageId\":\"" + JsonEscape(messageId) + "\"," +
            "\"clientId\":\"" + JsonEscape(clientId) + "\"," +
            "\"extraData\":null," +
            "\"status\":\"" + status + "\"," +
            "\"errorCode\":" + errorField + "," +
            "\"transactionId\":\"" + JsonEscape(transactionId) + "\"," +
            "\"signatures\":" + sigArray +
            "}";
    }

    /// <summary>
    /// POSTs a synthetic webhook envelope to <paramref name="webhookUrl"/>
    /// using the provided <see cref="HttpClient"/>. Returns the raw HTTP
    /// response so the caller can assert on status code + body shape.
    /// </summary>
    public static async Task<HttpResponseMessage> PostSyntheticWebhookAsync(
        HttpClient http,
        string webhookUrl,
        string transactionId,
        string clientId,
        string documentId = "doc-1",
        string signatureBytes = "SIGNATURE-BYTES",
        string status = "SUCCESS",
        string? messageId = null,
        string? errorCode = null,
        CancellationToken ct = default)
    {
        var json = BuildSyntheticWebhookBody(transactionId, clientId, documentId, signatureBytes, status, messageId, errorCode);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await http.PostAsync(webhookUrl, content, ct).ConfigureAwait(false);
    }

    public sealed class Counters
    {
        public int Login;
        public int Refresh;
        public int Certificates;
        public int Hash;
        public int SignHash;
        public int SignStatus;
        public int Attachment;
        public int TwoFactorAuth;
        public int ResendOtp;
    }

    public sealed class Scenario
    {
        public int PendingsBeforeSuccess { get; set; } = 1;
        public bool StatusAlwaysFailed { get; set; }
        public bool StatusAlwaysCancelled { get; set; }
        public bool StatusAlwaysPending { get; set; }
        public bool LoginAlways401 { get; set; }
        public bool CertsForce401Once { get; set; }
        public bool LoginRequires2FAOnFirstCall { get; set; }
        public string? NextTwoFactorErrorCode { get; set; }
        public ResendOtpResponseMode ResendOtpMode { get; set; } = ResendOtpResponseMode.Success200;
        public bool MultiArrayResponse { get; set; }
        public RequestedFormat? AttachmentMissingFormatArrayFor { get; set; }
        public (RequestedFormat format, int statusCode, string errorCode, string devMsg, string userMsg)? HashRejectionForFormat { get; set; }
        public (RequestedFormat format, int statusCode, string errorCode, string devMsg, string userMsg)? AttachmentRejectionForFormat { get; set; }
        internal int _certs401Consumed;
        internal int _login122Consumed;
    }
}

public enum ResendOtpResponseMode
{
    Success200,
    TypedFailure200,
    TypedFailure4xx,
    NoEnvelope4xx,
    Transport500,
}

public enum RequestedFormat
{
    Pdf,
    Xml,
    Word,
    Excel,
}

internal sealed record RecordedTwoFactorRequest(string Body, bool HasAuthorizationRm);

internal sealed record RecordedResendOtpRequest(string Body, bool HasAuthorizationRm);

internal sealed record RecordedFormatRequest(string Body, RequestedFormat DetectedFormat);

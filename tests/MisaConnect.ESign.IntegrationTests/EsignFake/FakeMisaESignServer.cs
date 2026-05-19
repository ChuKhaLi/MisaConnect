using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MisaConnect.ESign.IntegrationTests.EsignFake;

/// <summary>
/// In-test ASP.NET Core host that emulates the MISA eSign endpoints used by
/// slice 1. Mirrors the existing eInvoice <c>FakeMisaServer</c>. Endpoints are
/// stateful enough to drive end-to-end happy-path and a small set of
/// scenario configurations (force 401 once, pending->success state machine,
/// terminal failure).
/// </summary>
internal sealed class FakeMisaESignServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Counters _counters = new();
    private readonly Scenario _scenario = new();
    private readonly ConcurrentDictionary<string, int> _statusCallsByTx = new();

    public string BaseUrl { get; }
    public Counters Calls => _counters;
    public Scenario Configure => _scenario;
    public byte[] SignedPdfBytes { get; set; } = new byte[] { 0x25, 0x50, 0x44, 0x46, 0xAA, 0xBB };

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
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var body = "{\"status\":{\"code\":200,\"error\":false},\"data\":{\"accessToken\":\"raw-token\",\"remoteSigningAccessToken\":\"rs-token\",\"refreshToken\":\"refresh-token\",\"expiresIn\":3600,\"tokenType\":\"Bearer\",\"user\":{\"id\":\"user-id\",\"username\":\"alice\"}}}";
            await ctx.Response.WriteAsync(body);
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
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var body = "{\"pdfDocs\":[{\"documentId\":\"doc-1\",\"documentBytes\":\"DOCBYTES\",\"documentHash\":\"DOCHASH\",\"sh\":\"SH\",\"signatureName\":\"SigName\",\"digest\":\"DIGEST\"}]}";
            await ctx.Response.WriteAsync(body);
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
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var pdfBase64 = Convert.ToBase64String(self.SignedPdfBytes);
            await ctx.Response.WriteAsync($"{{\"pdfDocs\":[{{\"documentId\":\"doc-1\",\"document\":\"{pdfBase64}\"}}]}}");
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

    private static void SetSelf(WebApplication app, FakeMisaESignServer self)
    {
        // The endpoint delegates close over the `self` local; we rebind it
        // here via reflection-free static field if needed. Kept as a hook for
        // future per-request scenarios.
        _ = app;
        _ = self;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
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
    }

    public sealed class Scenario
    {
        public int PendingsBeforeSuccess { get; set; } = 1;
        public bool StatusAlwaysFailed { get; set; }
        public bool StatusAlwaysCancelled { get; set; }
        public bool StatusAlwaysPending { get; set; }
        public bool LoginAlways401 { get; set; }
        public bool CertsForce401Once { get; set; }
        internal int _certs401Consumed;
    }
}

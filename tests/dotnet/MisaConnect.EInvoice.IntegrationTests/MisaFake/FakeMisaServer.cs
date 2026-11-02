using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MisaConnect.EInvoice.IntegrationTests.MisaFake;

/// <summary>
/// Slice 2 T034 + slice 5 T020 — disposable in-test ASP.NET Core host listening
/// on a dynamic localhost port. Exposes configurable endpoints for the v2
/// integration surface MISA serves:
/// <list type="bullet">
///   <item><c>POST /webapp/token</c></item>
///   <item><c>DELETE /webapp/delete</c></item>
///   <item><c>POST /webapp/getlist</c> (slice 5 batch lookup)</item>
///   <item><c>POST /webapp/paging</c> (slice 5 standard paging)</item>
///   <item><c>POST /webapp/paging/calculating</c> (slice 5 POS paging)</item>
/// </list>
/// </summary>
internal sealed class FakeMisaServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly List<HttpRequestSnapshot> _capturedRequests = new();
    private (int StatusCode, string JsonBody) _deleteResponse = (200, """{"success":true,"errorCode":null}""");
    private (int StatusCode, string JsonBody) _tokenResponse =
        (200, """{"success":true,"data":"{\"access_token\":\"fake-token\"}"}""");

    // === Slice 5 lookup configuration ===
    // The seeded snapshots collection is keyed by route group ("standard" vs
    // "calculating"). The same snapshot can be seeded under both keys if needed
    // (test scenarios for cross-endpoint disjointness use distinct seeds).
    private readonly Dictionary<string, List<SeededSnapshot>> _seedsByGroup = new(StringComparer.Ordinal)
    {
        ["standard"] = new(),
        ["calculating"] = new(),
    };

    // Optional per-call override functions. When non-null, returns the response body
    // (status + JSON) to return instead of the default seeded lookup. Reset to null
    // to fall back to the seeded data.
    private Func<int, string, (int StatusCode, string JsonBody)?>? _getlistOverride;
    private Func<int, string, (int StatusCode, string JsonBody)?>? _pagingOverride;
    private Func<int, string, (int StatusCode, string JsonBody)?>? _calculatingOverride;

    private int _getlistCallCount;
    private int _pagingCallCount;
    private int _calculatingCallCount;

    // === Slice 6 insert configuration ===
    private readonly Dictionary<string, InsertSeedResult> _insertSeedsByRefId = new(StringComparer.Ordinal);
    private Func<int, string, (int StatusCode, string JsonBody)?>? _insertOverride;
    private int _insertCallCount;

    public string BaseUrl { get; }

    public IReadOnlyList<HttpRequestSnapshot> CapturedRequests => _capturedRequests;

    private FakeMisaServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public static async Task<FakeMisaServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0/");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        FakeMisaServer? self = null;

        app.MapPost("/webapp/token", async (HttpContext ctx) =>
        {
            await self!.CaptureAsync(ctx);
            ctx.Response.StatusCode = self._tokenResponse.StatusCode;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(self._tokenResponse.JsonBody);
        });

        app.MapPost("/webapp/templates", async (HttpContext ctx) =>
        {
            await self!.CaptureAsync(ctx);
            // Single active template, shaped to match MISA's TemplateDto wire format
            // (PascalCase keys, Inactive flag false). Sufficient for any TemplateResolver
            // call in the slice 1/6 fake-server tests.
            const string body = "{\"success\":true,\"data\":\"[{\\\"IPTemplateID\\\":\\\"ipt-fake\\\",\\\"InvSeries\\\":\\\"C26TAA\\\",\\\"TemplateName\\\":\\\"Fake template\\\",\\\"InvTemplateNo\\\":\\\"1\\\",\\\"TemplateType\\\":1,\\\"Inactive\\\":false,\\\"IsMoreVATRate\\\":false}]\",\"error\":null,\"errorCode\":null}";
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(body);
        });

        app.MapDelete("/webapp/delete", async (HttpContext ctx) =>
        {
            await self!.CaptureAsync(ctx);
            ctx.Response.StatusCode = self._deleteResponse.StatusCode;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(self._deleteResponse.JsonBody);
        });

        // === Slice 5 routes ===

        app.MapPost("/webapp/getlist", async (HttpContext ctx) =>
        {
            var body = await self!.CaptureAsync(ctx);
            var idx = ++self._getlistCallCount;

            if (self._getlistOverride is not null)
            {
                var maybe = self._getlistOverride(idx, body);
                if (maybe is { } overridden)
                {
                    ctx.Response.StatusCode = overridden.StatusCode;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(overridden.JsonBody);
                    return;
                }
            }

            var requestedRefIds = ParseRefIdArray(body);
            var seeds = self._seedsByGroup["standard"]
                .Concat(self._seedsByGroup["calculating"])
                .Where(s => requestedRefIds.Contains(s.RefID, StringComparer.Ordinal))
                .ToArray();

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(BuildLookupSuccess(seeds));
        });

        app.MapPost("/webapp/paging", async (HttpContext ctx) =>
        {
            var body = await self!.CaptureAsync(ctx);
            self._pagingCallCount++;
            var overridden = self._pagingOverride?.Invoke(self._pagingCallCount, body);
            if (overridden is { } o)
            {
                ctx.Response.StatusCode = o.StatusCode;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(o.JsonBody);
                return;
            }
            var matching = ApplyPaging(self._seedsByGroup["standard"], body);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(BuildLookupSuccess(matching));
        });

        app.MapPost("/webapp/insert", async (HttpContext ctx) =>
        {
            var body = await self!.CaptureAsync(ctx);
            var idx = ++self._insertCallCount;

            if (self._insertOverride is not null)
            {
                var maybe = self._insertOverride(idx, body);
                if (maybe is { } overridden)
                {
                    ctx.Response.StatusCode = overridden.StatusCode;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(overridden.JsonBody);
                    return;
                }
            }

            var firstRefId = ParseFirstRefIdFromInsertBody(body);
            if (firstRefId is null)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"success":false,"data":null,"error":"missing RefID","errorCode":"InvalidInvoiceData"}""");
                return;
            }

            // Honour an explicit seed if present; otherwise default to echo-success.
            if (self._insertSeedsByRefId.TryGetValue(firstRefId, out var seeded))
            {
                ctx.Response.StatusCode = seeded.StatusCode;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(seeded.JsonBody);
                return;
            }

            var successJson = $"{{\"success\":true,\"data\":\"[{{\\\"RefID\\\":\\\"{JsonEscapeRaw(firstRefId)}\\\"}}]\",\"error\":null,\"errorCode\":null}}";
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(successJson);
        });

        app.MapPost("/webapp/paging/calculating", async (HttpContext ctx) =>
        {
            var body = await self!.CaptureAsync(ctx);
            self._calculatingCallCount++;
            var overridden = self._calculatingOverride?.Invoke(self._calculatingCallCount, body);
            if (overridden is { } o)
            {
                ctx.Response.StatusCode = o.StatusCode;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(o.JsonBody);
                return;
            }
            var matching = ApplyPaging(self._seedsByGroup["calculating"], body);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(BuildLookupSuccess(matching));
        });

        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("FakeMisaServer failed to start: no server addresses feature.");
        var url = addresses.Addresses.First();

        self = new FakeMisaServer(app, url);
        return self;
    }

    private static IReadOnlyList<SeededSnapshot> ApplyPaging(List<SeededSnapshot> seeds, string body)
    {
        var req = ParsePagingRequest(body);
        return seeds
            .Where(s => SnapshotMatchesPagingRequest(s, req))
            .OrderBy(s => s.SortKey)
            .Skip(req.Start)
            .Take(req.Length)
            .ToArray();
    }

    public FakeMisaServer SeedStandardInvoice(SeededSnapshot snap)
    {
        _seedsByGroup["standard"].Add(snap);
        return this;
    }

    public FakeMisaServer SeedCalculatingInvoice(SeededSnapshot snap)
    {
        _seedsByGroup["calculating"].Add(snap);
        return this;
    }

    public void SetGetListOverride(Func<int, string, (int StatusCode, string JsonBody)?>? fn) =>
        _getlistOverride = fn;

    public void SetPagingOverride(Func<int, string, (int StatusCode, string JsonBody)?>? fn) =>
        _pagingOverride = fn;

    public void SetCalculatingOverride(Func<int, string, (int StatusCode, string JsonBody)?>? fn) =>
        _calculatingOverride = fn;

    public int GetListCallCount => _getlistCallCount;
    public int PagingCallCount => _pagingCallCount;
    public int CalculatingCallCount => _calculatingCallCount;

    public int InsertCallCount => _insertCallCount;

    public FakeMisaServer SeedInsertResponse(string refId, InsertSeedResult outcome)
    {
        _insertSeedsByRefId[refId] = outcome;
        return this;
    }

    public void SetInsertOverride(Func<int, string, (int StatusCode, string JsonBody)?>? fn) =>
        _insertOverride = fn;

    public void SetDeleteResponse(int statusCode, string jsonBody) =>
        _deleteResponse = (statusCode, jsonBody);

    public void SetTokenResponse(int statusCode, string jsonBody) =>
        _tokenResponse = (statusCode, jsonBody);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<string> CaptureAsync(HttpContext ctx)
    {
        var headers = ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        ctx.Request.EnableBuffering();
        string body = string.Empty;
        if (ctx.Request.ContentLength is null or > 0)
        {
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
            body = await reader.ReadToEndAsync();
            ctx.Request.Body.Position = 0;
        }

        _capturedRequests.Add(new HttpRequestSnapshot(
            Method: ctx.Request.Method,
            Path: ctx.Request.Path,
            QueryString: ctx.Request.QueryString.ToString(),
            Headers: headers,
            Body: body));

        return body;
    }

    private static string? ParseFirstRefIdFromInsertBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;
            var first = doc.RootElement[0];
            if (first.ValueKind != JsonValueKind.Object) return null;
            if (!first.TryGetProperty("RefID", out var refId)) return null;
            return refId.ValueKind == JsonValueKind.String ? refId.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string JsonEscapeRaw(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static IReadOnlyList<string> ParseRefIdArray(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>(doc.RootElement.GetArrayLength());
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var v = el.GetString();
                    if (!string.IsNullOrEmpty(v)) list.Add(v);
                }
            }
            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private sealed record ParsedPagingRequest(
        int Start,
        int Length,
        string Sort,
        string FromDate,
        string ToDate,
        string? PublishStatus);

    private static ParsedPagingRequest ParsePagingRequest(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new ParsedPagingRequest(
                Start: root.TryGetProperty("Start", out var start) && start.ValueKind == JsonValueKind.Number ? start.GetInt32() : 0,
                Length: root.TryGetProperty("Length", out var len) && len.ValueKind == JsonValueKind.Number ? len.GetInt32() : 100,
                Sort: root.TryGetProperty("Sort", out var sort) && sort.ValueKind == JsonValueKind.String ? (sort.GetString() ?? "InvDate") : "InvDate",
                FromDate: root.TryGetProperty("FromDate", out var fd) && fd.ValueKind == JsonValueKind.String ? (fd.GetString() ?? "") : "",
                ToDate: root.TryGetProperty("ToDate", out var td) && td.ValueKind == JsonValueKind.String ? (td.GetString() ?? "") : "",
                PublishStatus: root.TryGetProperty("PublishStatus", out var ps) && ps.ValueKind == JsonValueKind.String ? ps.GetString() : null);
        }
        catch (JsonException)
        {
            return new ParsedPagingRequest(0, 100, "InvDate", "", "", null);
        }
    }

    private static bool SnapshotMatchesPagingRequest(SeededSnapshot s, ParsedPagingRequest req)
    {
        if (!string.IsNullOrEmpty(req.FromDate) && string.CompareOrdinal(s.InvDate ?? "", req.FromDate) < 0) return false;
        if (!string.IsNullOrEmpty(req.ToDate) && string.CompareOrdinal(s.InvDate ?? "", req.ToDate) > 0) return false;
        if (!string.IsNullOrEmpty(req.PublishStatus) && s.PublishStatus != req.PublishStatus) return false;
        return true;
    }

    private static string BuildLookupSuccess(IReadOnlyList<SeededSnapshot> snapshots)
    {
        var sb = new StringBuilder();
        sb.Append("{\"success\":true,\"errorCode\":null,\"descriptionErrorCode\":null,\"errors\":[],\"data\":[");
        for (var i = 0; i < snapshots.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(snapshots[i].ToJson());
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

/// <summary>
/// Seedable snapshot record used by <see cref="FakeMisaServer.SeedStandardInvoice"/>
/// and <see cref="FakeMisaServer.SeedCalculatingInvoice"/>. Only the fields that
/// slice 5 needs (FR-049 surface plus filter axes) are materialised.
/// </summary>
internal sealed record SeededSnapshot(
    string RefID,
    string? InvoiceTemplateID = null,
    string? InvSeries = null,
    string? InvDate = "2026-05-12",
    string? InvNo = null,
    string? AccountObjectTaxCode = null,
    string? AccountObjectName = null,
    decimal? TotalSaleAmount = null,
    decimal? TotalVATAmount = null,
    decimal? TotalAmount = null,
    decimal? TotalSaleAmountOC = null,
    decimal? TotalVATAmountOC = null,
    decimal? TotalAmountOC = null,
    string? EInvoiceStatus = "1",
    string? PublishStatus = "0",
    string? OrgRefID = null,
    string? CreatedDate = "2026-05-12T10:30:00",
    string? ModifiedDate = "2026-05-12T10:30:00")
{
    public string SortKey => InvDate ?? string.Empty;

    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"RefID\":").Append(JsonString(RefID));
        Append(sb, "InvoiceTemplateID", InvoiceTemplateID);
        Append(sb, "InvSeries", InvSeries);
        Append(sb, "InvDate", InvDate);
        Append(sb, "InvNo", InvNo);
        Append(sb, "AccountObjectTaxCode", AccountObjectTaxCode);
        Append(sb, "AccountObjectName", AccountObjectName);
        AppendNumber(sb, "TotalSaleAmount", TotalSaleAmount);
        AppendNumber(sb, "TotalVATAmount", TotalVATAmount);
        AppendNumber(sb, "TotalAmount", TotalAmount);
        AppendNumber(sb, "TotalSaleAmountOC", TotalSaleAmountOC);
        AppendNumber(sb, "TotalVATAmountOC", TotalVATAmountOC);
        AppendNumber(sb, "TotalAmountOC", TotalAmountOC);
        Append(sb, "EInvoiceStatus", EInvoiceStatus);
        Append(sb, "PublishStatus", PublishStatus);
        Append(sb, "OrgRefID", OrgRefID);
        Append(sb, "CreatedDate", CreatedDate);
        Append(sb, "ModifiedDate", ModifiedDate);
        sb.Append('}');
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string key, string? value)
    {
        sb.Append(',');
        sb.Append('"').Append(key).Append("\":");
        if (value is null) sb.Append("null");
        else sb.Append(JsonString(value));
    }

    private static void AppendNumber(StringBuilder sb, string key, decimal? value)
    {
        sb.Append(',');
        sb.Append('"').Append(key).Append("\":");
        if (value is null) sb.Append("null");
        else sb.Append(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string JsonString(string raw)
    {
        var sb = new StringBuilder(raw.Length + 2);
        sb.Append('"');
        foreach (var c in raw)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}

/// <summary>
/// Seeded outcome for a MISA <c>/webapp/insert</c> response keyed by the
/// first <c>RefID</c> in the request body. Slice 6 amendment tests use
/// this to fixture lifecycle-blocked responses (<c>HasAdjustmentInvoice</c>,
/// <c>InvoiceCannotReplace</c>, …) without writing override functions.
/// </summary>
internal sealed record InsertSeedResult(int StatusCode, string JsonBody)
{
    public static InsertSeedResult Success(string refId) =>
        new(200, $"{{\"success\":true,\"data\":\"[{{\\\"RefID\\\":\\\"{refId}\\\"}}]\",\"error\":null,\"errorCode\":null}}");

    public static InsertSeedResult ErrorEnvelope(string errorCode, string? errorMessage = null) =>
        new(200, $"{{\"success\":false,\"data\":null,\"error\":{(errorMessage is null ? "null" : "\"" + errorMessage.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")},\"errorCode\":\"{errorCode}\"}}");

    public static InsertSeedResult PerEntryError(string refId, string errorCode, string? errorMessage = null) =>
        new(200, $"{{\"success\":false,\"data\":null,\"error\":\"[{{\\\"RefID\\\":\\\"{refId}\\\",\\\"ErrorCode\\\":\\\"{errorCode}\\\",\\\"ErrorMessage\\\":{(errorMessage is null ? "null" : "\\\"" + errorMessage.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\\\"")}}}]\",\"errorCode\":null}}");
}

internal sealed record HttpRequestSnapshot(
    string Method,
    string Path,
    string QueryString,
    IReadOnlyDictionary<string, string> Headers,
    string Body);

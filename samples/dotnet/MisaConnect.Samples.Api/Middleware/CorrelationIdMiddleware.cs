using Microsoft.Extensions.Primitives;
using MisaConnect.EInvoice.Application.Abstractions;

namespace MisaConnect.Samples.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, HttpContextCorrelationIdAccessor accessor)
    {
        var id = ResolveId(context);
        accessor.SetId(id);
        context.Response.Headers[HeaderName] = id;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    private static string ResolveId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var v) && !StringValues.IsNullOrEmpty(v))
        {
            return v.ToString();
        }
        return Guid.NewGuid().ToString("D");
    }
}

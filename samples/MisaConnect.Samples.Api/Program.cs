using System.Text.Json;
using MisaConnect.Samples.Api.Endpoints;
using MisaConnect.Samples.Api.Middleware;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Operations;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddMisaConnectEInvoice(builder.Configuration);
builder.Services.AddScoped<HttpContextCorrelationIdAccessor>();
builder.Services.AddScoped<ICorrelationIdAccessor>(sp => sp.GetRequiredService<HttpContextCorrelationIdAccessor>());

var app = builder.Build();

// Force option validation up-front so the host fails fast on misconfiguration
// (constitution Principle V — environment must be explicit, host refuses to
// start when ambiguous).
var meInvoiceOptions = app.Services.GetRequiredService<IOptions<MisaEInvoiceOptions>>().Value;
var startupLogger = app.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("MisaConnect.Samples.Api.Startup");
startupLogger.LogInformation("MeInvoice environment: {Environment}", meInvoiceOptions.Environment);
startupLogger.LogInformation(
    "MeInvoice endpoints registered: {Endpoints}",
    string.Join(", ", MeInvoiceOperations.All.Select(o => o.HttpRoute)));

app.UseMiddleware<CorrelationIdMiddleware>();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = static async (context, report) =>
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { status = report.Status.ToString() });
        await context.Response.Body.WriteAsync(payload).ConfigureAwait(false);
    },
});

app.MapTemplateEndpoints();
app.MapInvoiceEndpoints();

app.Run();

public partial class Program;

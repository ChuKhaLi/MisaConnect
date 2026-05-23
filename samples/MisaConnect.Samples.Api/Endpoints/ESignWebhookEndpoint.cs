using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.Dtos.Webhook;
using MisaConnect.ESign.Infrastructure.Configuration;

namespace MisaConnect.Samples.Api.Endpoints;

public static class ESignWebhookEndpoint
{
    private const string LoggerCategory = "MisaConnect.Samples.Api.ESignWebhook";

    public static IEndpointRouteBuilder MapESignWebhookEndpoint(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(MisaESignOptions.SectionName).Get<MisaESignOptions>() ?? new MisaESignOptions();
        var basePath = string.IsNullOrEmpty(options.Webhook.Path) ? "/esign/webhook" : options.Webhook.Path;

        if (!string.IsNullOrEmpty(options.Webhook.Secret))
        {
            endpoints.MapPost(basePath + "/{secret}", HandleAsync);
        }
        else
        {
            endpoints.MapPost(basePath, HandleAsync);
        }

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IMisaESignClient client,
        IOptions<MisaESignOptions> optionsAccessor,
        ILoggerFactory loggerFactory,
        WebhookEnvelopeDto envelope,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        var configured = optionsAccessor.Value.Webhook;

        if (!string.IsNullOrEmpty(configured.Secret))
        {
            var routeSecret = context.Request.RouteValues.TryGetValue("secret", out var v) ? v?.ToString() : null;
            if (!FixedTimeEquals(routeSecret, configured.Secret))
            {
                logger.LogWarning("eSign webhook secret-route segment did not match configured secret.");
                return Results.NotFound();
            }
        }

        if (configured.AllowedIps is { Length: > 0 })
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || !IsAllowed(remote, configured.AllowedIps))
            {
                logger.LogWarning(
                    "eSign webhook IP rejection ipClass={IpClass} pathHasSecretSegment={HasSecret}",
                    ClassifyIp(remote),
                    !string.IsNullOrEmpty(configured.Secret));
                return Results.StatusCode((int)HttpStatusCode.Forbidden);
            }
        }

        var result = await client.HandleWebhookAsync(envelope, ct).ConfigureAwait(false);
        return Results.Json(result.Ack);
    }

    private static bool FixedTimeEquals(string? candidate, string secret)
    {
        if (candidate is null) return false;
        var a = Encoding.UTF8.GetBytes(candidate);
        var b = Encoding.UTF8.GetBytes(secret);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static bool IsAllowed(IPAddress remote, IReadOnlyList<string> cidrs)
    {
        foreach (var cidr in cidrs)
        {
            if (IPNetwork.TryParse(cidr, out var net) && net.Contains(remote))
            {
                return true;
            }
        }
        return false;
    }

    internal static string ClassifyIp(IPAddress? ip)
    {
        if (ip is null) return "unknown";
        if (IPAddress.IsLoopback(ip)) return "loopback";
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 10) return "private-rfc1918";
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return "private-rfc1918";
            if (bytes[0] == 192 && bytes[1] == 168) return "private-rfc1918";
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return "private-rfc6598";
        }
        return "public";
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Infrastructure.Configuration;

namespace MisaConnect.Samples.Api.Middleware;

public static class ESignWebhookStartupValidator
{
    public static void EmitWarnIfPubliclyReachable(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<MisaESignOptions>>().Value;
        if (options.Webhook.Mode == WebhookMode.Polling) return;

        var hasSecret = !string.IsNullOrEmpty(options.Webhook.Secret);
        var hasAllowList = options.Webhook.AllowedIps is { Length: > 0 };
        if (hasSecret || hasAllowList) return;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MisaConnect.Samples.Api.ESignWebhook.Startup");
        logger.LogWarning(
            "Webhook endpoint is publicly reachable with no transport-layer auth; configure Misa:ESign:Webhook:Secret and/or :AllowedIps for production");
    }
}

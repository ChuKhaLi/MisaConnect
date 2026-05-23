using MisaConnect.ESign.Infrastructure.Configuration;

namespace MisaConnect.Samples.Api.Diagnostics;

/// <summary>
/// Diagnostic-only redacted view over <see cref="MisaESignOptions"/>. Used
/// when a host wants to log the effective configuration at startup without
/// leaking <c>Misa:ESign:Webhook:Secret</c> or other credential-style values
/// per Constitution Principle VIII + FR-099.
/// </summary>
public static class MisaESignOptionsDebugView
{
    public static IReadOnlyDictionary<string, string?> Redacted(MisaESignOptions options)
    {
        return new Dictionary<string, string?>
        {
            ["Misa:ESign:Environment"] = options.Environment.ToString(),
            ["Misa:ESign:BaseUrl"] = options.BaseUrl,
            ["Misa:ESign:ClientId"] = options.ClientId,
            ["Misa:ESign:ClientKey"] = string.IsNullOrEmpty(options.ClientKey) ? "<unset>" : "<redacted>",
            ["Misa:ESign:UserName"] = options.UserName,
            ["Misa:ESign:Password"] = string.IsNullOrEmpty(options.Password) ? "<unset>" : "<redacted>",
            ["Misa:ESign:Webhook:Mode"] = options.Webhook.Mode.ToString(),
            ["Misa:ESign:Webhook:Path"] = options.Webhook.Path,
            ["Misa:ESign:Webhook:Secret"] = string.IsNullOrEmpty(options.Webhook.Secret) ? "<unset>" : "<redacted>",
            ["Misa:ESign:Webhook:AllowedIps"] = options.Webhook.AllowedIps is { Length: > 0 }
                ? string.Join(",", options.Webhook.AllowedIps)
                : "<unset>",
            ["Misa:ESign:Webhook:Session:Ttl"] = options.Webhook.Session.Ttl.ToString(),
        };
    }
}

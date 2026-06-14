using System.Net;
using Microsoft.Extensions.Options;

namespace MisaConnect.ESign.Infrastructure.Configuration;

public sealed class MisaESignOptionsValidator : IValidateOptions<MisaESignOptions>
{
    public ValidateOptionsResult Validate(string? name, MisaESignOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            errors.Add("Misa:ESign:BaseUrl is required.");
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !IsLocalhostHttp(uri)))
        {
            errors.Add($"Misa:ESign:BaseUrl '{options.BaseUrl}' must be an absolute https URI.");
        }
        else
        {
            var isProductionHost = string.Equals(uri.Host, MisaESignOptions.ProductionHost, StringComparison.OrdinalIgnoreCase);
            switch (options.Environment)
            {
                case ESignEnvironment.Production when !isProductionHost:
                    errors.Add(
                        $"Misa:ESign:BaseUrl host '{uri.Host}' is not the documented production host " +
                        $"'{MisaESignOptions.ProductionHost}' but Environment=Production.");
                    break;
                case ESignEnvironment.Sandbox when isProductionHost:
                    errors.Add(
                        $"Misa:ESign:BaseUrl host '{uri.Host}' is the production host but Environment=Sandbox.");
                    break;
                case ESignEnvironment.Sandbox:
                case ESignEnvironment.Production:
                    break;
                default:
                    errors.Add($"Misa:ESign:Environment '{options.Environment}' is not a known value (Sandbox|Production).");
                    break;
            }
        }

        // The four credentials are required only in Static mode. In Dynamic mode
        // they are supplied per-call via IMisaCredentialsAccessor, so skip these
        // four checks; every other check below still runs in both modes.
        if (options.CredentialsMode == CredentialsMode.Static)
        {
            if (string.IsNullOrWhiteSpace(options.ClientId)) errors.Add("Misa:ESign:ClientId is required.");
            if (string.IsNullOrWhiteSpace(options.ClientKey)) errors.Add("Misa:ESign:ClientKey is required.");
            if (string.IsNullOrWhiteSpace(options.UserName)) errors.Add("Misa:ESign:UserName is required.");
            if (string.IsNullOrWhiteSpace(options.Password)) errors.Add("Misa:ESign:Password is required.");
        }

        if (options.Polling.Interval <= TimeSpan.Zero)
        {
            errors.Add("Misa:ESign:Polling:Interval must be > 0.");
        }
        if (options.Polling.TotalTimeout <= options.Polling.Interval)
        {
            errors.Add("Misa:ESign:Polling:TotalTimeout must be > Polling:Interval.");
        }
        if (options.TransportRetry.MaxAttempts < 1)
        {
            errors.Add("Misa:ESign:TransportRetry:MaxAttempts must be >= 1.");
        }
        if (options.TransportRetry.MaxDelay < options.TransportRetry.BaseDelay)
        {
            errors.Add("Misa:ESign:TransportRetry:MaxDelay must be >= BaseDelay.");
        }

        if (string.IsNullOrWhiteSpace(options.Otp.DefaultResendLanguage))
        {
            errors.Add("Misa:ESign:Otp:DefaultResendLanguage must be non-empty.");
        }

        if (options.Webhook.Session.Ttl <= TimeSpan.Zero)
        {
            errors.Add("Misa:ESign:Webhook:Session:Ttl must be > 0.");
        }

        if (!string.IsNullOrEmpty(options.Webhook.Secret) && options.Webhook.Secret.Length < 32)
        {
            errors.Add("Misa:ESign:Webhook:Secret must be at least 32 characters when set.");
        }

        if (options.Webhook.AllowedIps is { Length: > 0 })
        {
            foreach (var cidr in options.Webhook.AllowedIps)
            {
                if (string.IsNullOrWhiteSpace(cidr) || !IPNetwork.TryParse(cidr, out _))
                {
                    errors.Add($"Misa:ESign:Webhook:AllowedIps entry '{cidr}' is not a valid CIDR range.");
                }
            }
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    /// <summary>
    /// Allow plain http only for loopback addresses (127.0.0.1, ::1, localhost).
    /// This is the seam that makes the in-repo <c>FakeMisaESignServer</c>
    /// integration tests work without a self-signed TLS cert. Production and
    /// sandbox base URLs are still required to be https.
    /// </summary>
    private static bool IsLocalhostHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp
        && (uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
}

using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Infrastructure.Configuration;

namespace MisaConnect.ESign.Infrastructure.Credentials;

/// <summary>
/// Default <see cref="IWebhookCredentialsResolver"/>: in
/// <see cref="CredentialsMode.Static"/> returns the four option credentials; in
/// <see cref="CredentialsMode.Dynamic"/> returns <c>null</c> (the consumer must
/// register its own resolver). Registered via <c>TryAddSingleton</c>.
/// </summary>
internal sealed class OptionsWebhookCredentialsResolver : IWebhookCredentialsResolver
{
    private readonly IOptions<MisaESignOptions> _options;

    public OptionsWebhookCredentialsResolver(IOptions<MisaESignOptions> options) => _options = options;

    public ValueTask<MisaCredentials?> ResolveAsync(string clientId, string userName, CancellationToken ct)
    {
        var o = _options.Value;
        if (o.CredentialsMode == CredentialsMode.Dynamic)
        {
            return ValueTask.FromResult<MisaCredentials?>(null);
        }
        return ValueTask.FromResult<MisaCredentials?>(new MisaCredentials(o.ClientId, o.ClientKey, o.UserName, o.Password));
    }
}

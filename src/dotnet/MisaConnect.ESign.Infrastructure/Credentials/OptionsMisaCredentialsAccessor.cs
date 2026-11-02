using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Infrastructure.Configuration;

namespace MisaConnect.ESign.Infrastructure.Credentials;

/// <summary>
/// Default <see cref="IMisaCredentialsAccessor"/>: returns the four credentials
/// from <see cref="MisaESignOptions"/>. Registered via <c>TryAddSingleton</c> so
/// a consumer that pre-registers its own accessor wins. This default does NOT
/// inspect <see cref="MisaESignOptions.CredentialsMode"/> — it unconditionally
/// returns the option values; Dynamic-mode behaviour comes from a consumer
/// override, never from this default branching on the mode.
/// </summary>
internal sealed class OptionsMisaCredentialsAccessor : IMisaCredentialsAccessor
{
    private readonly IOptions<MisaESignOptions> _options;

    public OptionsMisaCredentialsAccessor(IOptions<MisaESignOptions> options) => _options = options;

    public MisaCredentials Get()
    {
        var o = _options.Value;
        return new MisaCredentials(o.ClientId, o.ClientKey, o.UserName, o.Password);
    }
}

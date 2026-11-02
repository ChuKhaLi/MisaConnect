using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using MisaConnect.EInvoice.Application.Abstractions;

namespace MisaConnect.EInvoice.IntegrationTests.Api;

/// <summary>
/// Shared <see cref="WebApplicationFactory{TEntryPoint}"/> wrapper that:
///  - Injects in-memory MeInvoice options sufficient for option-validation,
///  - Replaces the singleton <see cref="IMeInvoiceClient"/> with a caller-provided stub,
///  - Supports configuration overrides via <see cref="WithConfiguration"/>,
///  - Supports overriding the typed HttpClient base address (used by fake-server tests).
/// </summary>
public sealed class WebFactory : WebApplicationFactory<Program>
{
    private readonly IMeInvoiceClient? _stub;
    private readonly Action<IDictionary<string, string?>>? _configOverride;
    private readonly string? _httpBaseAddressOverride;

    public WebFactory(IMeInvoiceClient? stub = null) : this(stub, null, null) { }

    private WebFactory(
        IMeInvoiceClient? stub,
        Action<IDictionary<string, string?>>? configOverride,
        string? httpBaseAddressOverride)
    {
        _stub = stub;
        _configOverride = configOverride;
        _httpBaseAddressOverride = httpBaseAddressOverride;
    }

    public WebFactory WithConfiguration(Action<IDictionary<string, string?>> configure)
        => new(_stub, configure, _httpBaseAddressOverride);

    public WebFactory WithBaseAddressOverride(string baseAddress)
        => new(_stub, _configOverride, baseAddress);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["Misa:EInvoice:Environment"] = "Sandbox",
                ["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration",
                ["Misa:EInvoice:TaxCode"] = "0000000000",
                ["Misa:EInvoice:UserName"] = "test@example.com",
                ["Misa:EInvoice:Password"] = "placeholder",
                ["Misa:EInvoice:AppId"] = "1",
            };
            _configOverride?.Invoke(dict);
            config.AddInMemoryCollection(dict);
        });

        builder.ConfigureServices(services =>
        {
            if (_stub is not null)
            {
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    if (services[i].ServiceType == typeof(IMeInvoiceClient))
                    {
                        services.RemoveAt(i);
                    }
                }
                services.AddSingleton(_stub);
            }

            if (_httpBaseAddressOverride is not null)
            {
                // The slice 1 DI registers AddHttpClient<MeInvoiceClient>(...) which
                // uses typeof(MeInvoiceClient).Name as the HttpClientFactoryOptions
                // name. MeInvoiceClient is internal so we cannot reference the type
                // here — use the literal "MeInvoiceClient" string.
                services.AddOptions<HttpClientFactoryOptions>("MeInvoiceClient")
                    .Configure(options =>
                    {
                        options.HttpClientActions.Add(http =>
                        {
                            http.BaseAddress = new Uri(_httpBaseAddressOverride.EndsWith('/')
                                ? _httpBaseAddressOverride
                                : _httpBaseAddressOverride + "/");
                        });
                    });
            }
        });
    }
}

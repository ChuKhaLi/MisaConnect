using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MisaConnect.EInvoice.Client;

/// <summary>
/// Entry point for in-process .NET callers that don't already have a generic
/// host / <see cref="IServiceCollection"/> available. Validates configuration
/// synchronously and returns a ready-to-use <see cref="IMisaEInvoiceClient"/>.
/// </summary>
public static class MisaEInvoiceClientFactory
{
    public static IMisaEInvoiceClient Create(Action<MisaEInvoiceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var services = new ServiceCollection();
        services.AddEInvoiceClient(configure);
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMisaEInvoiceClient>();
    }

    public static IMisaEInvoiceClient Create(MisaEInvoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Create(target =>
        {
            target.Environment = options.Environment;
            target.BaseUrl = options.BaseUrl;
            target.TaxCode = options.TaxCode;
            target.UserName = options.UserName;
            target.Password = options.Password;
            target.AppId = options.AppId;
        });
    }
}

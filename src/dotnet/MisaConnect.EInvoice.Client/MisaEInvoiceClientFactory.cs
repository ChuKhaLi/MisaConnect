using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Infrastructure.Configuration;

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

        return Create(target => CopyTo(options, target));
    }

    /// <summary>
    /// Copies every option from <paramref name="source"/> onto <paramref name="target"/>.
    /// Nested option objects are copied by value. A round-trip test fails when an option is
    /// added to <see cref="MisaEInvoiceOptions"/> without updating this method.
    /// </summary>
    internal static void CopyTo(MisaEInvoiceOptions source, MisaEInvoiceOptions target)
    {
        target.Environment = source.Environment;
        target.BaseUrl = source.BaseUrl;
        target.TaxCode = source.TaxCode;
        target.UserName = source.UserName;
        target.Password = source.Password;
        target.AppId = source.AppId;
        target.Delete = new MisaEInvoiceDeleteOptions
        {
            IncludeRawErrorMessage = source.Delete.IncludeRawErrorMessage,
        };
    }
}

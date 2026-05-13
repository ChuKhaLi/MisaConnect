using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MisaConnect.EInvoice.Client.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEInvoiceClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddMisaConnectEInvoice(configuration);
        AddClientFacade(services);
        return services;
    }

    public static IServiceCollection AddEInvoiceClient(this IServiceCollection services, Action<MisaEInvoiceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddMisaConnectEInvoice(configure);
        AddClientFacade(services);
        return services;
    }

    private static void AddClientFacade(IServiceCollection services)
    {
        services.AddScoped<ICorrelationIdAccessor, LibraryCorrelationIdAccessor>();
        services.AddScoped<IMisaEInvoiceClient, MisaEInvoiceClient>();
    }
}

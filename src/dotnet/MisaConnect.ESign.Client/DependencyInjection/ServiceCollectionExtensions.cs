using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Client.Webhook;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.DependencyInjection;
using ApplicationWebhookHook = MisaConnect.ESign.Application.Webhook.IWebhookDeliveryHook;

namespace MisaConnect.ESign.Client.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds <c>Misa:ESign</c> from configuration, registers <see cref="MisaESignOptions"/>
    /// with <c>.ValidateOnStart()</c>, registers all infrastructure adapters, and
    /// registers the public <see cref="IMisaESignClient"/> facade.
    /// </summary>
    public static IServiceCollection AddMisaConnectESign(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddMisaConnectESignCore(configuration);
        AddClientFacade(services);
        return services;
    }

    public static IServiceCollection AddMisaConnectESign(this IServiceCollection services, Action<MisaESignOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddMisaConnectESignCore(configure);
        AddClientFacade(services);
        return services;
    }

    private static void AddClientFacade(IServiceCollection services)
    {
        services.TryAddScoped<ICorrelationIdAccessor, LibraryCorrelationIdAccessor>();
        services.TryAddScoped<IMisaESignClient, MisaESignClient>();
        services.TryAddSingleton<IWebhookDeliveryHook, NullClientWebhookDeliveryHook>();
        services.AddSingleton<ApplicationWebhookHook>(sp =>
            new WebhookDeliveryHookAdapter(sp.GetRequiredService<IWebhookDeliveryHook>()));
    }
}

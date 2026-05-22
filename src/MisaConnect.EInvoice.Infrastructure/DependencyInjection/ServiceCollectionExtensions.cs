using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Application.Templates;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Application.Validation;
using MisaConnect.EInvoice.Infrastructure.Caching;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.Logging;
using MisaConnect.EInvoice.Infrastructure.MeInvoice;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Auth;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Retry;

namespace MisaConnect.EInvoice.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMisaConnectEInvoice(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<MisaEInvoiceOptions>()
            .Bind(configuration.GetSection(MisaEInvoiceOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<MisaEInvoiceOptions>>(_ => new MisaEInvoiceOptionsValidator(configuration));

        AddCoreServices(services, registerValidator: false);

        return services;
    }

    public static IServiceCollection AddMisaConnectEInvoice(this IServiceCollection services, Action<MisaEInvoiceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<MisaEInvoiceOptions>()
            .Configure(configure)
            .ValidateOnStart();

        AddCoreServices(services, registerValidator: true);

        return services;
    }

    private static void AddCoreServices(IServiceCollection services, bool registerValidator = true)
    {
        if (registerValidator)
        {
            services.AddSingleton<IValidateOptions<MisaEInvoiceOptions>, MisaEInvoiceOptionsValidator>();
        }

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IRefIdGenerator, GuidRefIdGenerator>();
        services.AddSingleton<ITokenCache, InMemoryTokenCache>();
        services.AddSingleton<IInvoiceValidator, InvoiceValidator>();

        // The MeInvoiceCallLogger decorator captures ICorrelationIdAccessor whose
        // lifetime is per-request (scoped) in Api and singleton in Client. To
        // honour both safely, use cases + the decorator are registered Scoped.
        services.AddScoped<EnsureAccessToken>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaEInvoiceOptions>>();
            return new EnsureAccessToken(
                sp.GetRequiredService<IMeInvoiceClient>(),
                sp.GetRequiredService<ITokenCache>(),
                sp.GetRequiredService<ISystemClock>(),
                () => WireTaxCode.Compose(options.Value));
        });
        services.AddScoped<ListActiveTemplates>();
        services.AddScoped<ITemplateResolver, TemplateResolver>();
        services.AddScoped<PreviewInvoice>();
        services.AddScoped<SaveDraftInvoices>();
        services.AddScoped<GetDraftPdfByRefId>();
        services.AddSingleton<InvalidTransactionDisambiguator>();
        services.AddSingleton<IDeleteOptionsAccessor, DeleteOptionsAccessor>();
        services.AddScoped<DeleteDraftInvoice>();

        // Slice 5 — read-only lookup operations.
        services.AddScoped<LookupByRefIds>();
        services.AddScoped<LookupStandard>();
        services.AddScoped<LookupCalculating>();

        // Slice 6 — amendment operations.
        services.AddScoped<IssueReplacementInvoice>();
        services.AddScoped<IssueAdjustmentInvoice>();

        // HTTP handlers. BearerTokenHandler takes EnsureAccessToken via a factory so
        // its construction does not pull MeInvoiceClient (which depends on the same
        // handler chain) into the dependency graph eagerly.
        services.AddTransient<ThrottleRetryHandler>();
        services.AddTransient<BearerTokenHandler>(sp => new BearerTokenHandler(
            ensureTokenFactory: () => sp.GetRequiredService<EnsureAccessToken>(),
            cache: sp.GetRequiredService<ITokenCache>(),
            options: sp.GetRequiredService<IOptions<MisaEInvoiceOptions>>()));

        // Typed HTTP client + handler pipeline: outer ThrottleRetryHandler → inner BearerTokenHandler.
        services
            .AddHttpClient<MeInvoiceClient>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<MisaEInvoiceOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
            })
            .AddHttpMessageHandler<ThrottleRetryHandler>()
            .AddHttpMessageHandler<BearerTokenHandler>();

        // Decorator: MeInvoiceCallLogger wraps the concrete MeInvoiceClient.
        services.AddScoped<IMeInvoiceClient>(sp =>
            new MeInvoiceCallLogger(
                inner: sp.GetRequiredService<MeInvoiceClient>(),
                logger: sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MeInvoiceCallLogger>>(),
                correlation: sp.GetRequiredService<ICorrelationIdAccessor>(),
                options: sp.GetRequiredService<IOptions<MisaEInvoiceOptions>>()));
    }
}

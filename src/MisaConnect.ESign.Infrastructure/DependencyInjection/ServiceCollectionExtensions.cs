using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.Infrastructure.Certificates;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.ESign;
using MisaConnect.ESign.Infrastructure.Http;
using MisaConnect.ESign.Infrastructure.Logging;
using MisaConnect.ESign.Infrastructure.Time;

namespace MisaConnect.ESign.Infrastructure.DependencyInjection;

internal static class ServiceCollectionExtensions
{
    internal static IServiceCollection AddMisaConnectESignCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<MisaESignOptions>()
            .Bind(configuration.GetSection(MisaESignOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<IValidateOptions<MisaESignOptions>, MisaESignOptionsValidator>();
        AddCoreServices(services);
        return services;
    }

    internal static IServiceCollection AddMisaConnectESignCore(this IServiceCollection services, Action<MisaESignOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<MisaESignOptions>()
            .Configure(configure)
            .ValidateOnStart();

        services.TryAddSingleton<IValidateOptions<MisaESignOptions>, MisaESignOptionsValidator>();
        AddCoreServices(services);
        return services;
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISystemClock, SystemClock>();
        services.TryAddSingleton<IDelayer, TaskDelayer>();
        services.TryAddSingleton<ITokenCache, InMemoryTokenCache>();
        services.TryAddSingleton<ITokenCacheKeySelector, DefaultTokenCacheKeySelector>();
        services.TryAddSingleton<ICertificateSelector, FirstActiveCertificateSelector>();
        services.AddSingleton<SingleFlightRefresh>();

        services.AddScoped<RefreshAccessToken>();
        services.AddScoped<EnsureAccessToken>(sp =>
        {
            var optionsAccessor = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new EnsureAccessToken(
                wire: sp.GetRequiredService<IMisaESignWireClient>(),
                cache: sp.GetRequiredService<ITokenCache>(),
                keySelector: sp.GetRequiredService<ITokenCacheKeySelector>(),
                clock: sp.GetRequiredService<ISystemClock>(),
                credentialsAccessor: () => (optionsAccessor.Value.UserName, optionsAccessor.Value.Password),
                refreshUseCase: sp.GetRequiredService<RefreshAccessToken>());
        });
        services.AddScoped<ListActiveCertificates>();
        services.AddScoped<HashPdfDocument>();
        services.AddScoped<SubmitSignHash>();
        services.AddScoped<PollSignStatus>();
        services.AddScoped<AttachSignature>();
        services.AddScoped<SignPdfRequestValidator>();
        services.AddScoped<SignPdf>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new SignPdf(
                ensureToken: sp.GetRequiredService<EnsureAccessToken>(),
                listCerts: sp.GetRequiredService<ListActiveCertificates>(),
                certSelector: sp.GetRequiredService<ICertificateSelector>(),
                hashPdf: sp.GetRequiredService<HashPdfDocument>(),
                submitSignHash: sp.GetRequiredService<SubmitSignHash>(),
                pollStatus: sp.GetRequiredService<PollSignStatus>(),
                attachSignature: sp.GetRequiredService<AttachSignature>(),
                clock: sp.GetRequiredService<ISystemClock>(),
                validator: sp.GetRequiredService<SignPdfRequestValidator>(),
                intervalAccessor: () => options.Value.Polling.Interval,
                totalTimeoutAccessor: () => options.Value.Polling.TotalTimeout);
        });

        services.AddTransient<ClientHeadersHandler>();
        services.AddTransient<TransientFailureRetryHandler>();
        services.AddTransient<RemoteSigningAuthHandler>(sp => new RemoteSigningAuthHandler(
            ensureFactory: () => sp.GetRequiredService<EnsureAccessToken>(),
            refreshFactory: () => sp.GetRequiredService<RefreshAccessToken>(),
            cache: sp.GetRequiredService<ITokenCache>(),
            keySelector: sp.GetRequiredService<ITokenCacheKeySelector>(),
            singleFlight: sp.GetRequiredService<SingleFlightRefresh>(),
            options: sp.GetRequiredService<IOptions<MisaESignOptions>>(),
            correlation: sp.GetRequiredService<ICorrelationIdAccessor>()));

        services
            .AddHttpClient<MisaESignWireClient>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<MisaESignOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
            })
            .AddHttpMessageHandler<TransientFailureRetryHandler>()
            .AddHttpMessageHandler<RemoteSigningAuthHandler>()
            .AddHttpMessageHandler<ClientHeadersHandler>();

        services.AddScoped<IMisaESignWireClient>(sp =>
            new ESignCallLogger(
                inner: sp.GetRequiredService<MisaESignWireClient>(),
                logger: sp.GetRequiredService<ILogger<ESignCallLogger>>(),
                correlation: sp.GetRequiredService<ICorrelationIdAccessor>()));
    }
}

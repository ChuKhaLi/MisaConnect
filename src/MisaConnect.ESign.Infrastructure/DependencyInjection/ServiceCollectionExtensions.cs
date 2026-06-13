using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.Infrastructure.Certificates;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.ESign;
using MisaConnect.ESign.Infrastructure.ESign.Webhook;
using MisaConnect.ESign.Infrastructure.Http;
using MisaConnect.ESign.Infrastructure.Logging;
using MisaConnect.ESign.Infrastructure.Sessions;
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
        services.AddScoped<HashXmlDocument>();
        services.AddScoped<HashWordDocument>();
        services.AddScoped<HashExcelDocument>();
        services.AddScoped<SubmitSignHash>();
        services.AddScoped<PollSignStatus>();
        services.AddScoped<AttachSignature>();
        services.AddScoped<AttachSignatureToXml>();
        services.AddScoped<AttachSignatureToWordExcel>();
        services.AddScoped<SignPdfRequestValidator>();
        services.AddScoped<SignXmlRequestValidator>();
        services.AddScoped<SignWordRequestValidator>();
        services.AddScoped<SignExcelRequestValidator>();
        services.AddScoped<OtpSubmissionValidator>();
        services.AddScoped<ExchangeOtp>(sp => new ExchangeOtp(
            wire: sp.GetRequiredService<IMisaESignWireClient>(),
            cache: sp.GetRequiredService<ITokenCache>(),
            keySelector: sp.GetRequiredService<ITokenCacheKeySelector>(),
            singleFlight: (cacheKey, factory, ct) =>
                sp.GetRequiredService<SingleFlightRefresh>().RefreshAsync(cacheKey, factory, ct)));
        services.AddScoped<ResendOtp>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new ResendOtp(
                wire: sp.GetRequiredService<IMisaESignWireClient>(),
                defaultLanguageAccessor: () => options.Value.Otp.DefaultResendLanguage);
        });
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
                totalTimeoutAccessor: () => options.Value.Polling.TotalTimeout,
                otpProvider: sp.GetService<IOtpProvider>(),
                exchangeOtp: sp.GetService<ExchangeOtp>(),
                otpSubmissionValidator: sp.GetService<OtpSubmissionValidator>(),
                correlation: sp.GetService<ICorrelationIdAccessor>());
        });
        services.AddScoped<SignXml>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new SignXml(
                ensureToken: sp.GetRequiredService<EnsureAccessToken>(),
                listCerts: sp.GetRequiredService<ListActiveCertificates>(),
                certSelector: sp.GetRequiredService<ICertificateSelector>(),
                hashXml: sp.GetRequiredService<HashXmlDocument>(),
                submitSignHash: sp.GetRequiredService<SubmitSignHash>(),
                pollStatus: sp.GetRequiredService<PollSignStatus>(),
                attachSignature: sp.GetRequiredService<AttachSignatureToXml>(),
                clock: sp.GetRequiredService<ISystemClock>(),
                validator: sp.GetRequiredService<SignXmlRequestValidator>(),
                intervalAccessor: () => options.Value.Polling.Interval,
                totalTimeoutAccessor: () => options.Value.Polling.TotalTimeout,
                otpProvider: sp.GetService<IOtpProvider>(),
                exchangeOtp: sp.GetService<ExchangeOtp>(),
                otpSubmissionValidator: sp.GetService<OtpSubmissionValidator>(),
                correlation: sp.GetService<ICorrelationIdAccessor>());
        });
        services.AddScoped<SignWord>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new SignWord(
                ensureToken: sp.GetRequiredService<EnsureAccessToken>(),
                listCerts: sp.GetRequiredService<ListActiveCertificates>(),
                certSelector: sp.GetRequiredService<ICertificateSelector>(),
                hashWord: sp.GetRequiredService<HashWordDocument>(),
                submitSignHash: sp.GetRequiredService<SubmitSignHash>(),
                pollStatus: sp.GetRequiredService<PollSignStatus>(),
                attachSignature: sp.GetRequiredService<AttachSignatureToWordExcel>(),
                clock: sp.GetRequiredService<ISystemClock>(),
                validator: sp.GetRequiredService<SignWordRequestValidator>(),
                intervalAccessor: () => options.Value.Polling.Interval,
                totalTimeoutAccessor: () => options.Value.Polling.TotalTimeout,
                otpProvider: sp.GetService<IOtpProvider>(),
                exchangeOtp: sp.GetService<ExchangeOtp>(),
                otpSubmissionValidator: sp.GetService<OtpSubmissionValidator>(),
                correlation: sp.GetService<ICorrelationIdAccessor>());
        });
        services.TryAddSingleton<InMemorySigningSessionStore>();
        services.TryAddSingleton<ISigningSessionStore>(sp => sp.GetRequiredService<InMemorySigningSessionStore>());
        services.TryAddSingleton<IFinalizeLockOwner>(sp => sp.GetRequiredService<InMemorySigningSessionStore>());
        services.TryAddSingleton<NullWebhookDeliveryHook>();
        services.TryAddSingleton<IWebhookDeliveryHook>(sp => sp.GetRequiredService<NullWebhookDeliveryHook>());
        services.TryAddScoped<WebhookEnvelopeValidator>();

        services.AddScoped<BeginSignPdf>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new BeginSignPdf(
                ensureToken: sp.GetRequiredService<EnsureAccessToken>(),
                listCerts: sp.GetRequiredService<ListActiveCertificates>(),
                certSelector: sp.GetRequiredService<ICertificateSelector>(),
                hashPdf: sp.GetRequiredService<HashPdfDocument>(),
                submitSignHash: sp.GetRequiredService<SubmitSignHash>(),
                sessionStore: sp.GetRequiredService<ISigningSessionStore>(),
                clock: sp.GetRequiredService<ISystemClock>(),
                validator: sp.GetRequiredService<SignPdfRequestValidator>(),
                clientIdAccessor: () => options.Value.ClientId,
                ttlAccessor: () => options.Value.Webhook.Session.Ttl,
                logger: sp.GetRequiredService<ILogger<BeginSignPdf>>(),
                otpProvider: sp.GetService<IOtpProvider>(),
                exchangeOtp: sp.GetService<ExchangeOtp>(),
                otpSubmissionValidator: sp.GetService<OtpSubmissionValidator>(),
                correlation: sp.GetService<ICorrelationIdAccessor>());
        });
        services.AddScoped<BeginSignXml>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new BeginSignXml(
                ensureToken: sp.GetRequiredService<EnsureAccessToken>(),
                listCerts: sp.GetRequiredService<ListActiveCertificates>(),
                certSelector: sp.GetRequiredService<ICertificateSelector>(),
                hashXml: sp.GetRequiredService<HashXmlDocument>(),
                submitSignHash: sp.GetRequiredService<SubmitSignHash>(),
                sessionStore: sp.GetRequiredService<ISigningSessionStore>(),
                clock: sp.GetRequiredService<ISystemClock>(),
                validator: sp.GetRequiredService<SignXmlRequestValidator>(),
                clientIdAccessor: () => options.Value.ClientId,
                ttlAccessor: () => options.Value.Webhook.Session.Ttl,
                logger: sp.GetRequiredService<ILogger<BeginSignXml>>(),
                otpProvider: sp.GetService<IOtpProvider>(),
                exchangeOtp: sp.GetService<ExchangeOtp>(),
                otpSubmissionValidator: sp.GetService<OtpSubmissionValidator>(),
                correlation: sp.GetService<ICorrelationIdAccessor>());
        });
        services.AddScoped<BeginSignWord>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new BeginSignWord(
                ensureToken: sp.GetRequiredService<EnsureAccessToken>(),
                listCerts: sp.GetRequiredService<ListActiveCertificates>(),
                certSelector: sp.GetRequiredService<ICertificateSelector>(),
                hashWord: sp.GetRequiredService<HashWordDocument>(),
                submitSignHash: sp.GetRequiredService<SubmitSignHash>(),
                sessionStore: sp.GetRequiredService<ISigningSessionStore>(),
                clock: sp.GetRequiredService<ISystemClock>(),
                validator: sp.GetRequiredService<SignWordRequestValidator>(),
                clientIdAccessor: () => options.Value.ClientId,
                ttlAccessor: () => options.Value.Webhook.Session.Ttl,
                logger: sp.GetRequiredService<ILogger<BeginSignWord>>(),
                otpProvider: sp.GetService<IOtpProvider>(),
                exchangeOtp: sp.GetService<ExchangeOtp>(),
                otpSubmissionValidator: sp.GetService<OtpSubmissionValidator>(),
                correlation: sp.GetService<ICorrelationIdAccessor>());
        });
        services.AddScoped<BeginSignExcel>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new BeginSignExcel(
                ensureToken: sp.GetRequiredService<EnsureAccessToken>(),
                listCerts: sp.GetRequiredService<ListActiveCertificates>(),
                certSelector: sp.GetRequiredService<ICertificateSelector>(),
                hashExcel: sp.GetRequiredService<HashExcelDocument>(),
                submitSignHash: sp.GetRequiredService<SubmitSignHash>(),
                sessionStore: sp.GetRequiredService<ISigningSessionStore>(),
                clock: sp.GetRequiredService<ISystemClock>(),
                validator: sp.GetRequiredService<SignExcelRequestValidator>(),
                clientIdAccessor: () => options.Value.ClientId,
                ttlAccessor: () => options.Value.Webhook.Session.Ttl,
                logger: sp.GetRequiredService<ILogger<BeginSignExcel>>(),
                otpProvider: sp.GetService<IOtpProvider>(),
                exchangeOtp: sp.GetService<ExchangeOtp>(),
                otpSubmissionValidator: sp.GetService<OtpSubmissionValidator>(),
                correlation: sp.GetService<ICorrelationIdAccessor>());
        });
        services.AddScoped<FinalizeFromWebhook>(sp => new FinalizeFromWebhook(
            ensureToken: sp.GetRequiredService<EnsureAccessToken>(),
            listCerts: sp.GetRequiredService<ListActiveCertificates>(),
            certSelector: sp.GetRequiredService<ICertificateSelector>(),
            attachPdf: sp.GetRequiredService<AttachSignature>(),
            attachXml: sp.GetRequiredService<AttachSignatureToXml>(),
            attachWordExcel: sp.GetRequiredService<AttachSignatureToWordExcel>(),
            sessionStore: sp.GetRequiredService<ISigningSessionStore>(),
            lockOwner: sp.GetRequiredService<IFinalizeLockOwner>(),
            deliveryHook: sp.GetRequiredService<IWebhookDeliveryHook>(),
            logger: sp.GetRequiredService<ILogger<FinalizeFromWebhook>>(),
            successAckCodeAccessor: () => MisaWebhookAckCodes.Success));
        services.AddScoped<HandleWebhook>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new HandleWebhook(
                validator: sp.GetRequiredService<WebhookEnvelopeValidator>(),
                sessionStore: sp.GetRequiredService<ISigningSessionStore>(),
                finalizeFromWebhook: sp.GetRequiredService<FinalizeFromWebhook>(),
                deliveryHook: sp.GetRequiredService<IWebhookDeliveryHook>(),
                correlationAccessor: sp.GetRequiredService<ICorrelationIdAccessor>(),
                configuredClientIdAccessor: () => options.Value.ClientId,
                successAckCodeAccessor: () => MisaWebhookAckCodes.Success,
                logger: sp.GetRequiredService<ILogger<HandleWebhook>>());
        });

        services.AddScoped<SignExcel>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MisaESignOptions>>();
            return new SignExcel(
                ensureToken: sp.GetRequiredService<EnsureAccessToken>(),
                listCerts: sp.GetRequiredService<ListActiveCertificates>(),
                certSelector: sp.GetRequiredService<ICertificateSelector>(),
                hashExcel: sp.GetRequiredService<HashExcelDocument>(),
                submitSignHash: sp.GetRequiredService<SubmitSignHash>(),
                pollStatus: sp.GetRequiredService<PollSignStatus>(),
                attachSignature: sp.GetRequiredService<AttachSignatureToWordExcel>(),
                clock: sp.GetRequiredService<ISystemClock>(),
                validator: sp.GetRequiredService<SignExcelRequestValidator>(),
                intervalAccessor: () => options.Value.Polling.Interval,
                totalTimeoutAccessor: () => options.Value.Polling.TotalTimeout,
                otpProvider: sp.GetService<IOtpProvider>(),
                exchangeOtp: sp.GetService<ExchangeOtp>(),
                otpSubmissionValidator: sp.GetService<OtpSubmissionValidator>(),
                correlation: sp.GetService<ICorrelationIdAccessor>());
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
                // ESRM lives at the host root and the auth app under /webdev/; a
                // single base path cannot serve both. Use the ORIGIN of BaseUrl so
                // any configured path (e.g. /webdev/) is discarded; ESignRouteResolver
                // re-derives each endpoint's path. Path-tolerant: no consumer change.
                http.BaseAddress = ESignRouteResolver.Origin(opts.BaseUrl);
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

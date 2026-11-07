using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Client;

public class AddEInvoiceClientServiceCollectionTests
{
    [Fact]
    public void AddEInvoiceClient_with_configuration_registers_resolvable_IEInvoiceClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Misa:EInvoice:Environment"] = "Sandbox",
                ["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration",
                ["Misa:EInvoice:TaxCode"] = "0000000000-001",
                ["Misa:EInvoice:UserName"] = "test@example.com",
                ["Misa:EInvoice:Password"] = "secret",
                ["Misa:EInvoice:AppId"] = "app-1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddEInvoiceClient(configuration);
        using var sp = services.BuildServiceProvider();

        var client = sp.GetService<IMisaEInvoiceClient>();

        Assert.NotNull(client);
        Assert.IsType<MisaEInvoiceClient>(client);
    }

    [Fact]
    public void AddEInvoiceClient_with_action_registers_resolvable_IEInvoiceClient()
    {
        var services = new ServiceCollection();
        services.AddEInvoiceClient(opts =>
        {
            opts.Environment = MeInvoiceEnvironment.Sandbox;
            opts.BaseUrl = "https://testapi.meinvoice.vn/api/integration";
            opts.TaxCode = "0000000000-001";
            opts.UserName = "test@example.com";
            opts.Password = "secret";
            opts.AppId = "app-1";
        });
        using var sp = services.BuildServiceProvider();

        var client = sp.GetService<IMisaEInvoiceClient>();

        Assert.NotNull(client);
    }

    // 2.9.0: defaults are registered with TryAdd…, so a consumer registration wins before or after the call.

    [Fact]
    public void Correlation_accessor_registered_before_AddEInvoiceClient_wins()
    {
        var services = new ServiceCollection();
        var mine = new FixedCorrelation();
        services.AddScoped<ICorrelationIdAccessor>(_ => mine);
        services.AddEInvoiceClient(ConfigureValid);

        Assert.Same(mine, ResolveScoped<ICorrelationIdAccessor>(services));
    }

    [Fact]
    public void Correlation_accessor_registered_after_AddEInvoiceClient_wins()
    {
        var services = new ServiceCollection();
        services.AddEInvoiceClient(ConfigureValid);
        var mine = new FixedCorrelation();
        services.AddScoped<ICorrelationIdAccessor>(_ => mine);

        Assert.Same(mine, ResolveScoped<ICorrelationIdAccessor>(services));
    }

    [Fact]
    public void Correlation_accessor_default_is_LibraryCorrelationIdAccessor()
    {
        var services = new ServiceCollection();
        services.AddEInvoiceClient(ConfigureValid);

        Assert.IsType<LibraryCorrelationIdAccessor>(ResolveScoped<ICorrelationIdAccessor>(services));
    }

    [Fact]
    public void Token_cache_registered_before_AddEInvoiceClient_wins()
    {
        var services = new ServiceCollection();
        var mine = new NullTokenCache();
        services.AddSingleton<ITokenCache>(mine);
        services.AddEInvoiceClient(ConfigureValid);

        Assert.Same(mine, ResolveScoped<ITokenCache>(services));
    }

    [Fact]
    public void Token_cache_registered_after_AddEInvoiceClient_wins()
    {
        var services = new ServiceCollection();
        services.AddEInvoiceClient(ConfigureValid);
        var mine = new NullTokenCache();
        services.AddSingleton<ITokenCache>(mine);

        Assert.Same(mine, ResolveScoped<ITokenCache>(services));
    }

    private static void ConfigureValid(MisaEInvoiceOptions opts)
    {
        opts.Environment = MeInvoiceEnvironment.Sandbox;
        opts.BaseUrl = "https://testapi.meinvoice.vn/api/integration";
        opts.TaxCode = "0000000000-001";
        opts.UserName = "test@example.com";
        opts.Password = "secret";
        opts.AppId = "app-1";
    }

    private static T ResolveScoped<T>(IServiceCollection services)
        where T : notnull
    {
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    private sealed class FixedCorrelation : ICorrelationIdAccessor
    {
        public string CorrelationId => "fixed";
    }

    private sealed class NullTokenCache : ITokenCache
    {
        public Task<AccessToken?> TryGetAsync(string key, CancellationToken ct) =>
            Task.FromResult<AccessToken?>(null);
        public Task SetAsync(string key, AccessToken value, CancellationToken ct) =>
            Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct) =>
            Task.CompletedTask;
    }
}

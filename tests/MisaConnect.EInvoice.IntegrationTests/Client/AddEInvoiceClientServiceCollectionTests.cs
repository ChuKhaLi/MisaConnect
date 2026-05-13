using System.Collections.Generic;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
}

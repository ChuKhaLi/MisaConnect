using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Api;

public class HealthEndpointTests : IClassFixture<HealthEndpointTests.TestFactory>
{
    private readonly TestFactory _factory;

    public HealthEndpointTests(TestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GET_health_returns_200_and_Healthy()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
    }

    public sealed class TestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Misa:EInvoice:Environment"] = "Sandbox",
                    ["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration",
                    ["Misa:EInvoice:TaxCode"] = "0000000000",
                    ["Misa:EInvoice:UserName"] = "test@example.com",
                    ["Misa:EInvoice:Password"] = "placeholder",
                    ["Misa:EInvoice:AppId"] = "test-app",
                });
            });
        }
    }
}

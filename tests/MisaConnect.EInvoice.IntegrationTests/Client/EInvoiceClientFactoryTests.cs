using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.IntegrationTests.Sandbox;
using Microsoft.Extensions.Options;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Client;

public class EInvoiceClientFactoryTests
{
    [Fact]
    public void Create_with_invalid_environment_url_pair_throws_OptionsValidationException()
    {
        Assert.Throws<OptionsValidationException>(() =>
            MisaEInvoiceClientFactory.Create(opts =>
            {
                opts.Environment = MeInvoiceEnvironment.Production;
                opts.BaseUrl = "https://testapi.meinvoice.vn/api/integration"; // sandbox URL with prod env
                opts.TaxCode = "0000000000-001";
                opts.UserName = "x";
                opts.Password = "y";
                opts.AppId = "z";
            }));
    }

    [Fact]
    public void Create_with_missing_required_field_throws_OptionsValidationException()
    {
        Assert.Throws<OptionsValidationException>(() =>
            MisaEInvoiceClientFactory.Create(opts =>
            {
                opts.Environment = MeInvoiceEnvironment.Sandbox;
                opts.BaseUrl = "https://testapi.meinvoice.vn/api/integration";
                opts.TaxCode = "";
                opts.UserName = "x";
                opts.Password = "y";
                opts.AppId = "z";
            }));
    }

    [SandboxFact]
    public void Create_with_sandbox_credentials_returns_non_null_client()
    {
        Assert.True(SandboxCredentials.TryLoad(out var creds, out _));

        var client = MisaEInvoiceClientFactory.Create(opts =>
        {
            opts.Environment = MeInvoiceEnvironment.Sandbox;
            opts.BaseUrl = "https://testapi.meinvoice.vn/api/integration";
            opts.TaxCode = creds!.TaxCode;
            opts.UserName = creds.UserName;
            opts.Password = creds.Password;
            opts.AppId = creds.AppId;
        });

        Assert.NotNull(client);
    }
}

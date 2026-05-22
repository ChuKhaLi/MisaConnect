using Microsoft.Extensions.Options;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Configuration;

public class MeInvoiceOptionsValidationTests
{
    private static MisaEInvoiceOptions ValidSandbox() => new()
    {
        Environment = MeInvoiceEnvironment.Sandbox,
        BaseUrl = "https://testapi.meinvoice.vn/api/integration",
        TaxCode = "0000000000",
        UserName = "test@example.com",
        Password = "secret",
        AppId = "app-1",
    };

    private static MisaEInvoiceOptions ValidProduction() => new()
    {
        Environment = MeInvoiceEnvironment.Production,
        BaseUrl = "https://api.meinvoice.vn/api/integration",
        TaxCode = "0000000000",
        UserName = "prod@example.com",
        Password = "secret",
        AppId = "app-1",
    };

    [Fact]
    public void Valid_sandbox_options_pass()
    {
        var validator = new MisaEInvoiceOptionsValidator();
        var result = validator.Validate(name: null, options: ValidSandbox());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Valid_production_options_pass()
    {
        var validator = new MisaEInvoiceOptionsValidator();
        var result = validator.Validate(name: null, options: ValidProduction());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Production_environment_with_sandbox_url_fails()
    {
        var opts = ValidProduction();
        opts.BaseUrl = "https://testapi.meinvoice.vn/api/integration";

        var result = new MisaEInvoiceOptionsValidator().Validate(name: null, options: opts);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Sandbox_environment_with_production_url_fails()
    {
        var opts = ValidSandbox();
        opts.BaseUrl = "https://api.meinvoice.vn/api/integration";

        var result = new MisaEInvoiceOptionsValidator().Validate(name: null, options: opts);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("TaxCode")]
    [InlineData("UserName")]
    [InlineData("Password")]
    [InlineData("AppId")]
    [InlineData("BaseUrl")]
    public void Missing_required_field_fails(string field)
    {
        var opts = ValidSandbox();
        switch (field)
        {
            case "TaxCode": opts.TaxCode = ""; break;
            case "UserName": opts.UserName = ""; break;
            case "Password": opts.Password = ""; break;
            case "AppId": opts.AppId = ""; break;
            case "BaseUrl": opts.BaseUrl = ""; break;
        }

        var result = new MisaEInvoiceOptionsValidator().Validate(name: null, options: opts);

        Assert.False(result.Succeeded);
    }
}

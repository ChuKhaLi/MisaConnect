using Microsoft.Extensions.Configuration;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Configuration;

/// <summary>
/// Slice 2 T007 — assert the nested <c>Misa:Delete:*</c> options sub-record
/// binds from configuration, defaults <c>IncludeRawErrorMessage</c> to
/// <c>false</c>, and that the validator rejects unknown sub-keys (research
/// R-DEL-06: a typo must fail at startup, not silently leave the flag
/// off).
/// </summary>
public class MeInvoiceOptionsValidatorDeleteTests
{
    [Fact]
    public void IncludeRawErrorMessage_defaults_to_false()
    {
        var opts = new MisaEInvoiceOptions();
        Assert.NotNull(opts.Delete);
        Assert.False(opts.Delete.IncludeRawErrorMessage);
    }

    [Fact]
    public void IncludeRawErrorMessage_binds_from_configuration_true()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Misa:EInvoice:Delete:IncludeRawErrorMessage"] = "true",
            })
            .Build();

        var opts = new MisaEInvoiceOptions();
        config.GetSection(MisaEInvoiceOptions.SectionName).Bind(opts);

        Assert.True(opts.Delete.IncludeRawErrorMessage);
    }

    [Fact]
    public void Valid_options_with_Delete_subsection_pass()
    {
        var opts = ValidSandbox();
        opts.Delete = new MisaEInvoiceDeleteOptions { IncludeRawErrorMessage = true };

        var configRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Misa:EInvoice:Delete:IncludeRawErrorMessage"] = "true",
            })
            .Build();

        var validator = new MisaEInvoiceOptionsValidator(configRoot);
        var result = validator.Validate(name: null, options: opts);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Unknown_sub_key_under_Delete_fails_validation()
    {
        var configRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Misa:EInvoice:Delete:Includerawerrormessage"] = "true",   // wrong case-folded camel
                ["Misa:EInvoice:Delete:NotARealKey"] = "anything",
            })
            .Build();

        var validator = new MisaEInvoiceOptionsValidator(configRoot);
        var result = validator.Validate(name: null, options: ValidSandbox());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("NotARealKey", StringComparison.OrdinalIgnoreCase));
    }

    private static MisaEInvoiceOptions ValidSandbox() => new()
    {
        Environment = MeInvoiceEnvironment.Sandbox,
        BaseUrl = "https://testapi.meinvoice.vn/api/integration",
        TaxCode = "0000000000",
        UserName = "test@example.com",
        Password = "secret",
        AppId = "app-1",
    };
}

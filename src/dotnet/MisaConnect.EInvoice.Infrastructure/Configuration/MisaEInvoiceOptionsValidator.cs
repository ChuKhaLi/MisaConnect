using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MisaConnect.EInvoice.Infrastructure.Configuration;

public sealed class MisaEInvoiceOptionsValidator : IValidateOptions<MisaEInvoiceOptions>
{
    private readonly IConfiguration? _configuration;

    public MisaEInvoiceOptionsValidator()
    {
    }

    public MisaEInvoiceOptionsValidator(IConfiguration? configuration)
    {
        _configuration = configuration;
    }

    public ValidateOptionsResult Validate(string? name, MisaEInvoiceOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            errors.Add("Misa:EInvoice:BaseUrl is required.");
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"Misa:EInvoice:BaseUrl '{options.BaseUrl}' must be an absolute https URI.");
        }
        else
        {
            var expectedHost = options.Environment switch
            {
                MeInvoiceEnvironment.Sandbox => MisaEInvoiceOptions.SandboxHost,
                MeInvoiceEnvironment.Production => MisaEInvoiceOptions.ProductionHost,
                _ => null,
            };

            if (expectedHost is null)
            {
                errors.Add($"Misa:EInvoice:Environment '{options.Environment}' is not a known value (Sandbox|Production).");
            }
            else if (!string.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Misa:EInvoice:BaseUrl host '{uri.Host}' does not match the chosen environment " +
                    $"({options.Environment} expects host '{expectedHost}').");
            }
        }

        if (string.IsNullOrWhiteSpace(options.TaxCode))
        {
            errors.Add("Misa:EInvoice:TaxCode is required.");
        }

        if (string.IsNullOrWhiteSpace(options.UserName))
        {
            errors.Add("Misa:EInvoice:UserName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            errors.Add("Misa:EInvoice:Password is required.");
        }

        if (string.IsNullOrWhiteSpace(options.AppId))
        {
            errors.Add("Misa:EInvoice:AppId is required.");
        }

        if (_configuration is not null)
        {
            ValidateDeleteSubKeys(_configuration, errors);
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateDeleteSubKeys(IConfiguration root, List<string> errors)
    {
        var section = root.GetSection($"{MisaEInvoiceOptions.SectionName}:Delete");
        if (!section.GetChildren().Any())
        {
            return;
        }

        var allowed = typeof(MisaEInvoiceDeleteOptions)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var child in section.GetChildren())
        {
            if (!allowed.Contains(child.Key))
            {
                errors.Add(
                    $"Misa:EInvoice:Delete:{child.Key} is not a recognised option " +
                    $"(allowed: {string.Join(", ", allowed)}).");
            }
        }
    }
}

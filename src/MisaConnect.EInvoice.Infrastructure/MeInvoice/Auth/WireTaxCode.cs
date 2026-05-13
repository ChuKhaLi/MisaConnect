using MisaConnect.EInvoice.Infrastructure.Configuration;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Auth;

/// <summary>
/// Compose / decompose the wire-format taxcode <c>{TaxCode}-{AppId}</c>
/// (research R-2). Split uses the rightmost hyphen for defensive handling.
/// </summary>
public static class WireTaxCode
{
    public static string Compose(MisaEInvoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.TaxCode) || string.IsNullOrEmpty(options.AppId))
        {
            throw new InvalidOperationException("MisaEInvoiceOptions.TaxCode and AppId are both required.");
        }
        return $"{options.TaxCode}-{options.AppId}";
    }

    public static bool TryParse(string wireTaxCode, out string taxCode, out string appId)
    {
        taxCode = string.Empty;
        appId = string.Empty;

        if (string.IsNullOrEmpty(wireTaxCode))
        {
            return false;
        }

        var idx = wireTaxCode.LastIndexOf('-');
        if (idx <= 0 || idx >= wireTaxCode.Length - 1)
        {
            return false;
        }

        taxCode = wireTaxCode[..idx];
        appId = wireTaxCode[(idx + 1)..];
        return true;
    }
}

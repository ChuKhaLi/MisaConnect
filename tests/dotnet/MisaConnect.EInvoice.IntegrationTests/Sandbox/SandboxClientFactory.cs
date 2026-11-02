using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Infrastructure.Configuration;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

internal static class SandboxClientFactory
{
    public static IMisaEInvoiceClient Create()
    {
        if (!SandboxCredentials.TryLoad(out var creds, out var reason))
        {
            throw new InvalidOperationException(reason);
        }
        return MisaEInvoiceClientFactory.Create(opts =>
        {
            opts.Environment = MeInvoiceEnvironment.Sandbox;
            opts.BaseUrl = "https://testapi.meinvoice.vn/api/integration";
            opts.TaxCode = creds!.TaxCode;
            opts.UserName = creds.UserName;
            opts.Password = creds.Password;
            opts.AppId = creds.AppId;
        });
    }
}

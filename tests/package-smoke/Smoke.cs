using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;

/// <summary>
/// Registers and resolves both client facades from the packed packages. This loads every
/// bundled assembly's dependencies (options binding, HttpClient factory, logging, JSON), so a
/// dependency missing from a package's nuspec fails here with FileNotFoundException.
/// </summary>
internal static class Smoke
{
    public static int Run(IServiceCollection services)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Misa:ESign:Environment"] = "Sandbox",
            ["Misa:ESign:BaseUrl"] = "https://esign-sandbox.example.vn/",
            ["Misa:ESign:ClientId"] = "c",
            ["Misa:ESign:ClientKey"] = "k",
            ["Misa:ESign:UserName"] = "u",
            ["Misa:ESign:Password"] = "p",
            ["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration/",
            ["Misa:EInvoice:TaxCode"] = "0101243150",
            ["Misa:EInvoice:AppId"] = "app",
            ["Misa:EInvoice:UserName"] = "u",
            ["Misa:EInvoice:Password"] = "p",
        }).Build();

        services.AddLogging();
        services.AddMisaConnectESign(config);
        services.AddEInvoiceClient(config);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var esign = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var einvoice = scope.ServiceProvider.GetRequiredService<IMisaEInvoiceClient>();
        Console.WriteLine($"SMOKE OK: {esign.GetType().Name}, {einvoice.GetType().Name}");
        return 0;
    }
}

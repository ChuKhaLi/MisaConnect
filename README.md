# MisaConnect

Community .NET SDK for MISA cloud APIs. Currently covers **MISA MeInvoice** (eInvoice). MISA eSign is planned for v2.0.

> Status: pre-release. Public API may change before v1.0.0.

## Install

```
dotnet add package MisaConnect.EInvoice
```

## Quickstart

```csharp
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddUserSecrets<Program>()
    .Build();

var services = new ServiceCollection();
services.AddLogging();
services.AddMisaConnectEInvoice(config);

await using var scope = services.BuildServiceProvider().CreateAsyncScope();

var listTemplates = scope.ServiceProvider.GetRequiredService<ListActiveTemplates>();
var templates = await listTemplates.ExecuteAsync(invoiceWithCode: true, CancellationToken.None);
foreach (var t in templates) Console.WriteLine($"{t.InvSeries} - {t.TemplateName}");
```

Bind credentials under the `Misa:EInvoice` configuration section (env vars, user-secrets, or appsettings):

```json
{
  "Misa": {
    "EInvoice": {
      "Environment": "Sandbox",
      "BaseUrl": "https://testapi.meinvoice.vn/api/integration",
      "TaxCode": "0000000000",
      "UserName": "your-misa-user",
      "Password": "your-misa-password",
      "AppId": "your-misa-app-id"
    }
  }
}
```

See [docs/sandbox-setup.md](docs/sandbox-setup.md) for sandbox credential setup and [docs/getting-started.md](docs/getting-started.md) for the full walkthrough.

## Supported operations

| Operation | Use case | Status |
| --- | --- | --- |
| Acquire access token | `EnsureAccessToken` | ✅ v1.0 |
| List active templates | `ListActiveTemplates` | ✅ v1.0 |
| Preview invoice (PDF) | `PreviewInvoice` | ✅ v1.0 |
| Save draft invoices | `SaveDraftInvoices` | ✅ v1.0 |
| Get draft PDF by RefId | `GetDraftPdfByRefId` | ✅ v1.0 |
| Delete draft invoice | `DeleteDraftInvoice` | ✅ v1.0 |
| Lookup by RefId (cascade) | `LookupByRefIds` | ✅ v1.0 |
| Lookup paginated (standard) | `LookupStandard` | ✅ v1.0 |
| Lookup paginated (calculating) | `LookupCalculating` | ✅ v1.0 |
| Issue replacement | `IssueReplacementInvoice` | ✅ v1.0 |
| Issue adjustment | `IssueAdjustmentInvoice` | ✅ v1.0 |
| Issue invoice (cấp số / sign) | — | 🛑 not yet |
| MISA eSign integration | — | 🛑 v2.0 |

## Project layout

```
src/
  MisaConnect.EInvoice.Domain/         entities, value objects, errors
  MisaConnect.EInvoice.Application/    use cases + port interfaces
  MisaConnect.EInvoice.Infrastructure/ MISA HTTP client + DI
  MisaConnect.EInvoice.Client/         consumer-facing facade (the NuGet package)
samples/
  MisaConnect.Samples.Console/         minimal DI wiring + ListTemplates demo
  MisaConnect.Samples.Api/             ASP.NET Core minimal-API reference host
tests/
  MisaConnect.EInvoice.UnitTests/      315 unit tests
  MisaConnect.EInvoice.IntegrationTests/ sandbox + fake-server tests
  MisaConnect.EInvoice.TestSupport/    shared fixtures
docs/
  misa-api-reference/                  copies of MISA public reference docs
```

## Documentation

- [Getting started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [Configuration](docs/configuration.md)
- [Sandbox setup](docs/sandbox-setup.md)
- [MISA API reference](docs/misa-api-reference/)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Slice-driven development via Spec Kit under `specs/`.

## License

MIT — see [LICENSE](LICENSE).

<p align="center">
  <img src="https://raw.githubusercontent.com/ChuKhaLi/MisaConnect/main/icon.png" alt="MisaConnect" width="128" height="128" />
</p>

<h1 align="center">MisaConnect.EInvoice</h1>

[![NuGet](https://img.shields.io/nuget/v/MisaConnect.EInvoice.svg?label=NuGet)](https://www.nuget.org/packages/MisaConnect.EInvoice)
[![Downloads](https://img.shields.io/nuget/dt/MisaConnect.EInvoice.svg)](https://www.nuget.org/packages/MisaConnect.EInvoice)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ChuKhaLi/MisaConnect/blob/main/LICENSE)

Community .NET SDK for the **MISA MeInvoice** (eInvoice) HTTP API. Provides a port-and-adapter facade for token acquisition, template lookup, invoice preview/save/PDF/delete, lookup, and amendments. Targets .NET 8.

> Looking for digital-signature operations (PDF/XML/Word/Excel signing, 2FA/OTP, webhooks)? Install the sibling [`MisaConnect.ESign`](https://www.nuget.org/packages/MisaConnect.ESign) package.

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

## Configuration

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

For production, switch `Environment` to `Production` and `BaseUrl` to `https://api.meinvoice.vn/api/integration`. The options validator refuses to start if the environment and base-URL host disagree.

## Supported operations

| Operation | Use case | Status |
| --- | --- | --- |
| Acquire access token | `EnsureAccessToken` | v1.0 |
| List active templates | `ListActiveTemplates` | v1.0 |
| Preview invoice (PDF) | `PreviewInvoice` | v1.0 |
| Save draft invoices | `SaveDraftInvoices` | v1.0 |
| Get draft PDF by RefId | `GetDraftPdfByRefId` | v1.0 |
| Delete draft invoice | `DeleteDraftInvoice` | v1.0 |
| Lookup by RefId (cascade) | `LookupByRefIds` | v1.0 |
| Lookup paginated (standard) | `LookupStandard` | v1.0 |
| Lookup paginated (calculating) | `LookupCalculating` | v1.0 |
| Issue replacement | `IssueReplacementInvoice` | v1.0 |
| Issue adjustment | `IssueAdjustmentInvoice` | v1.0 |

## Documentation

- [Getting started](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/einvoice/getting-started.md)
- [Configuration reference](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/einvoice/configuration.md)
- [Sandbox setup](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/einvoice/sandbox-setup.md)
- [Architecture overview](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/architecture.md)
- [MISA API reference](https://github.com/ChuKhaLi/MisaConnect/tree/main/docs/misa-api-reference/)
- [CHANGELOG](https://github.com/ChuKhaLi/MisaConnect/blob/main/CHANGELOG.md)

## License

MIT — see [LICENSE](https://github.com/ChuKhaLi/MisaConnect/blob/main/LICENSE).

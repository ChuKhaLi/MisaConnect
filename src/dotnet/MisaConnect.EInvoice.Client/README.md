![MisaConnect](https://raw.githubusercontent.com/ChuKhaLi/MisaConnect/main/icon.png)

# MisaConnect.EInvoice

[![NuGet](https://img.shields.io/nuget/v/MisaConnect.EInvoice.svg?label=NuGet)](https://www.nuget.org/packages/MisaConnect.EInvoice)
[![Downloads](https://img.shields.io/nuget/dt/MisaConnect.EInvoice.svg)](https://www.nuget.org/packages/MisaConnect.EInvoice)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ChuKhaLi/MisaConnect/blob/main/LICENSE)

Community .NET SDK for the **MISA MeInvoice** (hóa đơn điện tử) API: invoice and e-ticket templates, preview, drafts, lookups, replacement and adjustment invoices. Targets .NET 8 and .NET 10.

📖 **Full documentation:** <https://chukhali.github.io/MisaConnect/einvoice/dotnet/>

## Install

```bash
dotnet add package MisaConnect.EInvoice
```

Requires `Microsoft.Extensions.*` **10.0.14 or later** (also on .NET 8).

## Quickstart

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Client.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddEInvoiceClient(builder.Configuration); // section "Misa:EInvoice"
using var host = builder.Build();

await using var scope = host.Services.CreateAsyncScope();
var client = scope.ServiceProvider.GetRequiredService<IMisaEInvoiceClient>();

foreach (var t in await client.ListTemplatesAsync())
    Console.WriteLine($"{t.InvSeries} - {t.TemplateName}");
```

A console app needs `dotnet add package Microsoft.Extensions.Hosting` (ASP.NET Core apps already have it). Configuration (appsettings, user-secrets or environment variables) needs `Misa:EInvoice` with `Environment`, `BaseUrl`, `TaxCode` (without the AppID), `UserName`, `Password`, `AppId` — see [Configuration](https://chukhali.github.io/MisaConnect/einvoice/dotnet/configuration).

## Operations

All on `IMisaEInvoiceClient`:

| Method | Purpose |
| --- | --- |
| `ListTemplatesAsync` | Active invoice templates. |
| `ListTicketTemplatesAsync` | One page of e-ticket templates. |
| `PreviewAsync` | PDF preview without saving. |
| `SaveDraftAsync` | Save drafts; one result per invoice. |
| `GetDraftPdfAsync` | PDF of a saved draft. |
| `DeleteDraftAsync` | Delete a draft (outcome-returning). |
| `LookupByRefIdAsync` | Look up invoices by RefId. |
| `LookupStandardAsync` / `LookupCalculatingAsync` | Paged lookup by date range. |
| `IssueReplacementAsync` / `IssueAdjustmentAsync` | Replacement / adjustment invoices. |

Details, errors and examples: [Operations](https://chukhali.github.io/MisaConnect/einvoice/dotnet/operations) · [Errors](https://chukhali.github.io/MisaConnect/einvoice/dotnet/errors) · [Concepts](https://chukhali.github.io/MisaConnect/einvoice/#concepts) · [Sandbox setup](https://chukhali.github.io/MisaConnect/einvoice/dotnet/sandbox-setup).

Calling MISA from outside Vietnam? Read [this first](https://chukhali.github.io/MisaConnect/einvoice/#calling-misa-from-outside-vietnam).

Sibling packages: [`MisaConnect.ESign`](https://www.nuget.org/packages/MisaConnect.ESign) (digital signatures) · TypeScript: [`@misaconnect/einvoice`](https://www.npmjs.com/package/@misaconnect/einvoice).

## License

MIT. Community project, not affiliated with MISA JSC.

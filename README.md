# MisaConnect

Community .NET SDK for MISA cloud APIs. Currently covers **MISA MeInvoice** (eInvoice) and **MISA eSign**, released in v2.0.

> Status: v1.0.0 released on NuGet. Targets .NET 8. See the [CHANGELOG](CHANGELOG.md).

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
| MISA eSign integration | — | ✅ v2.0 (separate `MisaConnect.ESign` package — see below) |

## MisaConnect.ESign (v2.0)

The `MisaConnect.ESign` product family covers end-to-end PDF / XML / Word / Excel signing, two-factor (OTP) authentication, and webhook-mode (non-blocking) signing via the MISA eSign RemoteSigning API. Released in v2.0; installable separately:

```
dotnet add package MisaConnect.ESign --version 2.0.0
```

| Operation | Facade method | Status |
| --- | --- | --- |
| Sign PDF end-to-end (login → list certs → hash → sign → poll → attach) | `IMisaESignClient.SignPdfAsync` | ✅ v2.0 |
| 2FA / OTP — explicit completion of a captured challenge | `IMisaESignClient.SignInWithOtpAsync` | ✅ v2.0 |
| 2FA / OTP — request re-delivery | `IMisaESignClient.ResendOtpAsync` | ✅ v2.0 |
| 2FA / OTP — transparent (DI-registered `IOtpProvider`) | `Application.Abstractions.IOtpProvider` | ✅ v2.0 |
| Sign XML (XAdES) end-to-end (string + bytes overloads) | `IMisaESignClient.SignXmlAsync` | ✅ v2.0 |
| Sign Word (OOXML `.docx`) end-to-end | `IMisaESignClient.SignWordAsync` | ✅ v2.0 |
| Sign Excel (OOXML `.xlsx`) end-to-end | `IMisaESignClient.SignExcelAsync` | ✅ v2.0 |
| Begin webhook-mode sign (no polling) | `IMisaESignClient.BeginSign{Pdf,Xml,Word,Excel}Async` | ✅ v2.0 |
| Handle inbound MISA webhook envelope | `IMisaESignClient.HandleWebhookAsync` | ✅ v2.0 |

Bind options under the `Misa:ESign` configuration section, then call `services.AddMisaConnectESign(IConfiguration)`. See [specs/001-misa-esign-pdf-sign-flow/quickstart.md](specs/001-misa-esign-pdf-sign-flow/quickstart.md) for the full walkthrough, and [specs/004-misa-esign-webhook/quickstart.md](specs/004-misa-esign-webhook/quickstart.md) for webhook-mode setup (mode config, `IWebhookDeliveryHook` registration, sample-API transport-layer auth).

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

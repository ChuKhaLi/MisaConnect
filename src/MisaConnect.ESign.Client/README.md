![MisaConnect](https://raw.githubusercontent.com/ChuKhaLi/MisaConnect/main/icon.png)

# MisaConnect.ESign

📖 **Docs:** <https://chukhali.github.io/MisaConnect/esign/>

[![NuGet](https://img.shields.io/nuget/v/MisaConnect.ESign.svg?label=NuGet)](https://www.nuget.org/packages/MisaConnect.ESign)
[![Downloads](https://img.shields.io/nuget/dt/MisaConnect.ESign.svg)](https://www.nuget.org/packages/MisaConnect.ESign)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ChuKhaLi/MisaConnect/blob/main/LICENSE)

Community .NET SDK for the **MISA eSign RemoteSigning** HTTP API. End-to-end PDF / XML / Word / Excel signing, two-factor (OTP) authentication, and webhook-mode (non-blocking) signing — all behind a single `IMisaESignClient` facade. Targets .NET 8.

> Looking for invoice operations (templates, save/preview/lookup/amend)? Install the sibling [`MisaConnect.EInvoice`](https://www.nuget.org/packages/MisaConnect.EInvoice) package.

## Install

```
dotnet add package MisaConnect.ESign
```

## Quickstart

```csharp
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddMisaConnectESign(builder.Configuration);

var host = builder.Build();
var esign = host.Services.GetRequiredService<IMisaESignClient>();

var result = await esign.SignPdfAsync(new SignPdfRequestDto(
    Pdf: await File.ReadAllBytesAsync("input.pdf"),
    DocumentName: "input.pdf",
    SignerName: "Nguyễn Văn A",
    Location: "Hà Nội",
    Reason: "Phê duyệt hợp đồng",
    Contact: "nguyenvana@example.com",
    LogoImageBase64: null,
    DataToBeDisplayed: "<p>Phê duyệt hợp đồng số 2026/05/18-01</p>",
    Page: 1,
    PositionX: 100, PositionY: 100, Width: 200, Height: 80,
    RenderingMode: 1,
    ShowSignedDate: true), CancellationToken.None);

await File.WriteAllBytesAsync("output.signed.pdf", result.SignedPdf);
```

Behind that one call the SDK runs login → certificate listing → server-side hash → sign submission → status polling → signature attachment, with cached tokens, transparent 401-refresh, and exponential-backoff transport retries.

## Configuration

Bind credentials under the `Misa:ESign` configuration section (env vars, user-secrets, or appsettings):

```json
{
  "Misa": {
    "ESign": {
      "Environment": "Sandbox",
      "BaseUrl": "https://<sandbox-host-issued-by-misa>/",
      "ClientId": "<your-clientId>",
      "ClientKey": "<your-clientKey>",
      "UserName": "<your-misa-user>",
      "Password": "<your-misa-password>",
      "Polling":        { "Interval": "00:00:02", "TotalTimeout": "00:01:00" },
      "TransportRetry": { "MaxAttempts": 3, "BaseDelay": "00:00:00.200", "MaxDelay": "00:00:02" },
      "Errors":         { "IncludeRawErrorMessage": false },
      "Webhook":        { "Mode": "Both", "Session": { "Ttl": "24:00:00" } }
    }
  }
}
```

For production, switch `Environment` to `Production` and `BaseUrl` to `https://esignapp.misa.vn/`. The options validator refuses to start if the environment and base-URL host disagree.

`BaseUrl` should be the **host root**. The SDK normalizes it to its origin (scheme + host), so any path you include — e.g. a trailing `/webdev/` — is tolerated and ignored for routing: ESRM endpoints always resolve at the host root, and the auth app under `/webdev/`. The login/two-factor location follows `Environment` (Sandbox ⇒ under `/webdev/`, Production ⇒ host root). Override it with the optional `AuthUnderWebdev` (`true`/`false`) only if a specific tenant differs from its environment default:

```json
"Misa": { "ESign": { "AuthUnderWebdev": true } }
```

## Supported operations

| Operation | Facade method |
| --- | --- |
| Sign PDF end-to-end (login → list certs → hash → sign → poll → attach) | `IMisaESignClient.SignPdfAsync` |
| 2FA / OTP — explicit completion of a captured challenge | `IMisaESignClient.SignInWithOtpAsync` |
| 2FA / OTP — request re-delivery | `IMisaESignClient.ResendOtpAsync` |
| 2FA / OTP — transparent (DI-registered `IOtpProvider`) | `Application.Abstractions.IOtpProvider` |
| Sign XML (XAdES) end-to-end (string + bytes overloads) | `IMisaESignClient.SignXmlAsync` |
| Sign Word (OOXML `.docx`) end-to-end | `IMisaESignClient.SignWordAsync` |
| Sign Excel (OOXML `.xlsx`) end-to-end | `IMisaESignClient.SignExcelAsync` |
| Begin webhook-mode sign (no polling) | `IMisaESignClient.BeginSign{Pdf,Xml,Word,Excel}Async` |
| Handle inbound MISA webhook envelope | `IMisaESignClient.HandleWebhookAsync` |

## Documentation

- [Getting started](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/esign/getting-started.md)
- [Configuration reference](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/esign/configuration.md)
- [Sandbox setup](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/esign/sandbox-setup.md)
- [Architecture overview](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/architecture.md)
- [MISA API reference](https://github.com/ChuKhaLi/MisaConnect/tree/main/docs/misa-api-reference/)
- [CHANGELOG](https://github.com/ChuKhaLi/MisaConnect/blob/main/CHANGELOG.md)

## License

MIT — see [LICENSE](https://github.com/ChuKhaLi/MisaConnect/blob/main/LICENSE).

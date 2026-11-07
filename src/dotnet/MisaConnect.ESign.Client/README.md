![MisaConnect](https://raw.githubusercontent.com/ChuKhaLi/MisaConnect/main/icon.png)

# MisaConnect.ESign

[![NuGet](https://img.shields.io/nuget/v/MisaConnect.ESign.svg?label=NuGet)](https://www.nuget.org/packages/MisaConnect.ESign)
[![Downloads](https://img.shields.io/nuget/dt/MisaConnect.ESign.svg)](https://www.nuget.org/packages/MisaConnect.ESign)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ChuKhaLi/MisaConnect/blob/main/LICENSE)

Community .NET SDK for **MISA eSign RemoteSigning** (chữ ký số từ xa): sign PDF, XML, Word and Excel documents, two-factor sign-in (OTP), webhook-mode signing, per-call credentials. Targets .NET 8 and .NET 10.

📖 **Full documentation:** <https://chukhali.github.io/MisaConnect/esign/dotnet/>

## Install

```bash
dotnet add package MisaConnect.ESign
```

Requires `Microsoft.Extensions.*` **10.0.14 or later** (also on .NET 8).

## Quickstart

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Client.Dtos;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddMisaConnectESign(builder.Configuration); // section "Misa:ESign"
using var host = builder.Build();

await using var scope = host.Services.CreateAsyncScope(); // IMisaESignClient is scoped
var esign = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

var result = await esign.SignPdfAsync(new SignPdfRequestDto(
    Pdf: await File.ReadAllBytesAsync("input.pdf"),
    DocumentName: "input.pdf",
    SignerName: "Nguyễn Văn A",
    Location: "Hà Nội",
    Reason: "Phê duyệt hợp đồng",
    Contact: "nguyenvana@example.com",
    LogoImageBase64: Convert.ToBase64String(await File.ReadAllBytesAsync("logo.png")),
    DataToBeDisplayed: "Phê duyệt hợp đồng",
    Page: 1,
    PositionX: 100, PositionY: 100, Width: 200, Height: 80,
    RenderingMode: 1,
    ShowSignedDate: true));

await File.WriteAllBytesAsync("output.signed.pdf", result.SignedPdf);
```

A console app needs `dotnet add package Microsoft.Extensions.Hosting` (ASP.NET Core apps already have it). One call runs login → certificate → hash → sign → poll → attach, with cached tokens and retries. Configuration keys: [Configuration](https://chukhali.github.io/MisaConnect/esign/dotnet/configuration).

## Operations

All on `IMisaESignClient`:

| Method | Purpose |
| --- | --- |
| `SignPdfAsync` / `SignXmlAsync` / `SignWordAsync` / `SignExcelAsync` | Sign and wait for the signer's approval. |
| `SignInWithOtpAsync` / `ResendOtpAsync` | Two-factor sign-in (explicit OTP). |
| `BeginSignPdfAsync` / `BeginSignXmlAsync` / `BeginSignWordAsync` / `BeginSignExcelAsync` | Webhook mode: start signing and return. |
| `HandleWebhookAsync` | Webhook mode: process one MISA delivery. |

Details and flows: [Operations](https://chukhali.github.io/MisaConnect/esign/dotnet/operations) · [Two-factor sign-in](https://chukhali.github.io/MisaConnect/esign/dotnet/two-factor) · [Webhook mode](https://chukhali.github.io/MisaConnect/esign/dotnet/webhooks) · [Credentials](https://chukhali.github.io/MisaConnect/esign/dotnet/credentials) · [Errors](https://chukhali.github.io/MisaConnect/esign/dotnet/errors) · [Concepts](https://chukhali.github.io/MisaConnect/esign/#concepts).

Signing needs an ACTIVE remote-signing certificate on the MISA account. Live signing is not yet verified against MISA (tested against an in-repo fake server).

Two-factor accounts can also answer OTP challenges transparently: register an `IOtpProvider` and the SDK completes the sign-in itself. See [Two-factor sign-in](https://chukhali.github.io/MisaConnect/esign/dotnet/two-factor).

Sibling packages: [`MisaConnect.EInvoice`](https://www.nuget.org/packages/MisaConnect.EInvoice) (e-invoices) · TypeScript: [`@misaconnect/esign`](https://www.npmjs.com/package/@misaconnect/esign).

## License

MIT. Community project, not affiliated with MISA JSC.

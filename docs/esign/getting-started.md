---
title: Getting started
layout: default
parent: ESign
nav_order: 1
---

# Getting started

This guide walks through installing `MisaConnect.ESign`, wiring DI, and signing your first PDF via the MISA eSign RemoteSigning API.

## 1. Install

```
dotnet add package MisaConnect.ESign
```

Targets .NET 8 and .NET 10. Depends only on `Microsoft.Extensions.{Configuration,DependencyInjection,Http,Options,Logging.Abstractions}` and `System.Text.Json` — no Polly, no third-party HTTP plumbing.

**Requirements:** .NET 8 or .NET 10 (each package ships a build for both). The packages depend on `Microsoft.Extensions.*` **10.0.14 or later**, which also runs on .NET 8. If your app pins a `Microsoft.Extensions.*` package below 10.0.14, restore fails with `NU1605` (package downgrade) naming it; raise that reference to 10.0.14 or later.

## 2. Add configuration

`MisaConnect.ESign` binds the `Misa:ESign` configuration section. Use whichever provider you prefer — `appsettings.json`, user-secrets, env vars, or vault providers.

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
      "Errors":         { "IncludeRawErrorMessage": false }
    }
  }
}
```

Sandbox credentials come from MISA — see [sandbox-setup.md](sandbox-setup.md). For production, switch `Environment` to `Production` and `BaseUrl` to `https://esignapp.misa.vn/`. The options validator refuses to start if the environment and base-URL host disagree.

## 3. Wire DI

```csharp
using MisaConnect.ESign.Client.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// In ASP.NET or the Generic Host, use builder.Configuration instead.
// AddJsonFile and AddEnvironmentVariables need the Microsoft.Extensions.Configuration.Json
// and Microsoft.Extensions.Configuration.EnvironmentVariables packages.
IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddMisaConnectESign(configuration);
```

In a hosted application (ASP.NET, Generic Host, Worker Service), call it on `builder.Services` like any other DI registration. That single call binds `MisaESignOptions`, registers all default port adapters, wires the typed `HttpClient` pipeline (transient-retry → bearer-auth → client-headers), and registers the `IMisaESignClient` facade.

## 4. Sign a PDF

```csharp
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Domain.Errors;

const string LogoPngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

await using var scope = services.BuildServiceProvider().CreateAsyncScope();
var esign = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

try
{
    var result = await esign.SignPdfAsync(new SignPdfRequestDto(
        Pdf: await File.ReadAllBytesAsync("input.pdf"),
        DocumentName: "input.pdf",
        SignerName: "Nguyễn Văn A",
        Location: "Hà Nội",
        Reason: "Phê duyệt hợp đồng",
        Contact: "nguyenvana@example.com",
        LogoImageBase64: LogoPngBase64, // base64 of a PNG (this is a 1x1 placeholder)
        DataToBeDisplayed: "<p>Phê duyệt hợp đồng số 2026/05/18-01</p>",
        Page: 1,
        PositionX: 100, PositionY: 100, Width: 200, Height: 80,
        RenderingMode: 1,
        ShowSignedDate: true), CancellationToken.None);

    await File.WriteAllBytesAsync("output.signed.pdf", result.SignedPdf);
}
catch (NoActiveCertificateException)        { /* user has no ACTIVE remote-signing cert */ }
catch (AuthenticationFailedException ex)    when (ex.Requires2FA) { /* MISA wants OTP */ }
catch (SignTerminalStateException)          { /* FAILED / CANCELLED on MISA's side */ }
catch (SignTimeoutException)                { /* user didn't confirm within Polling.TotalTimeout */ }
catch (ESignTransportException)             { /* transient — back off and retry later */ }
```

Behind that one `SignPdfAsync` call the SDK runs login → certificate listing → server-side hash → sign submission → status polling → signature attachment, with cached tokens, transparent 401-refresh, and exponential-backoff transport retries.

## 5. Provide correlation IDs

Each call goes through a logging decorator that captures an `ICorrelationIdAccessor`. In an ASP.NET host, register a per-request accessor that reads the `X-Correlation-ID` header. In a console app, register a static one (see `samples/dotnet/MisaConnect.Samples.Api`).

## 6. Two-factor authentication (OTP)

Two flavors:

- **Transparent** — register an `IOtpProvider` DI implementation that fetches the OTP from your channel (SMS gateway, TOTP, interactive prompt). The SDK calls it automatically when MISA challenges with 2FA, no consumer code change needed in `SignPdfAsync`.
- **Explicit** — catch `AuthenticationFailedException` with `Requires2FA = true`, then call `IMisaESignClient.SignInWithOtpAsync` with the captured challenge + the user-supplied OTP. Use `IMisaESignClient.ResendOtpAsync` to request re-delivery.

## 7. Multi-format signing

`SignXmlAsync`, `SignWordAsync`, and `SignExcelAsync` (each takes one request DTO; for XML build it with `SignXmlRequest.FromString(...)` or `SignXmlRequest.FromUtf8Bytes(...)`) mirror the `SignPdfAsync` shape for XML (XAdES), OOXML `.docx`, and OOXML `.xlsx` documents.

## 8. Webhook-mode (non-blocking) signing

For long-running document signs, use `BeginSign{Pdf,Xml,Word,Excel}Async` to initiate without polling. MISA then POSTs the completion envelope to your webhook endpoint; the SDK's `HandleWebhookAsync` validates it, confirms the outcome with MISA (`Signing/status`) and routes it to your registered `IWebhookDeliveryHook`. See [configuration.md](configuration.md#webhook-ack-codes) for the ACK codes. See [specs/004-misa-esign-webhook/quickstart.md](../../specs/004-misa-esign-webhook/quickstart.md) for the end-to-end webhook setup (mode config, sample-API transport-layer auth, ngrok-based local-dev flow).

## Next steps

- [Architecture overview](../architecture.md)
- [Configuration reference](configuration.md)
- [Sandbox setup](sandbox-setup.md)
- [`samples/dotnet/MisaConnect.Samples.Api`](../../samples/dotnet/MisaConnect.Samples.Api/) — runnable ASP.NET Core reference host

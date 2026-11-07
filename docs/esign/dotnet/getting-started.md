---
title: Getting started
parent: .NET
grand_parent: ESign
nav_order: 1
---

# Getting started

This guide installs `MisaConnect.ESign`, wires DI, and signs a first PDF through MISA eSign RemoteSigning.

## 1. Install

```bash
dotnet add package MisaConnect.ESign
```

Targets .NET 8 and .NET 10. Depends only on `Microsoft.Extensions.{Configuration,DependencyInjection,Http,Options,Logging.Abstractions}` and `System.Text.Json` — no Polly, no third-party HTTP plumbing.

**Requirements:** .NET 8 or .NET 10 (the package ships a build for both). The package depends on `Microsoft.Extensions.*` **10.0.14 or later**, which also runs on .NET 8. If your app pins a `Microsoft.Extensions.*` package below 10.0.14, restore fails with `NU1605` (package downgrade) naming it; raise that reference to 10.0.14 or later.

You also need MISA credentials (client ID / key, user name / password, base URL) and an account with an `ACTIVE` certificate — see [Sandbox setup](./sandbox-setup) and [Certificates](../index#certificates).

## 2. Add configuration

`MisaConnect.ESign` binds the `Misa:ESign` configuration section. Use whichever provider you prefer — `appsettings.json`, user secrets, environment variables or a vault provider. Keep the secrets out of `appsettings.json`.

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

Sandbox credentials come from MISA — see [Sandbox setup](./sandbox-setup). For production, switch `Environment` to `Production` and `BaseUrl` to `https://esignapp.misa.vn/`. The options validator refuses to start if the environment and the base-URL host disagree. Every key is described in [Configuration](./configuration).

## 3. Wire DI

In a hosted application (ASP.NET Core, Generic Host, Worker Service), register the SDK on `builder.Services`:

<!-- snippet: partial -->
```csharp
using MisaConnect.ESign.Client.DependencyInjection;

builder.Services.AddMisaConnectESign(builder.Configuration);
```

That single call binds `MisaESignOptions` from `Misa:ESign` (validated at startup), registers every default port adapter, wires the typed `HttpClient` pipeline (transient retry → bearer auth → client headers), and registers the `IMisaESignClient` facade (scoped). A second overload, `AddMisaConnectESign(Action<MisaESignOptions>)`, configures the options in code; the console example below uses it.

## 4. Sign a PDF

A complete console program. It reads the sandbox credentials from the same `MISACONNECT_ESIGN_SANDBOX_*` environment variables the integration tests use:

```csharp
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Configuration;

var services = new ServiceCollection();
services.AddMisaConnectESign(o =>
{
    o.Environment = ESignEnvironment.Sandbox;
    o.BaseUrl = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_BASE_URL") ?? "";
    o.ClientId = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_CLIENT_ID") ?? "";
    o.ClientKey = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY") ?? "";
    o.UserName = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_USERNAME") ?? "";
    o.Password = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_PASSWORD") ?? "";
});

// A 1x1 PNG placeholder; use your own logo (base64 of a PNG). It must not be empty.
const string LogoPngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
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
        LogoImageBase64: LogoPngBase64,
        DataToBeDisplayed: "<p>Phê duyệt hợp đồng số 2026/05/18-01</p>",
        Page: 1,
        PositionX: 100, PositionY: 100, Width: 200, Height: 80,
        RenderingMode: 1,
        ShowSignedDate: true));

    await File.WriteAllBytesAsync("output.signed.pdf", result.SignedPdf);
    Console.WriteLine($"Signed: transaction {result.TransactionId}, certificate {result.CertificateKeyAlias}");
}
catch (NoActiveCertificateException)                        { /* the user has no ACTIVE remote-signing certificate */ }
catch (AuthenticationFailedException ex) when (ex.Requires2FA) { /* MISA wants an OTP: see Two-factor sign-in */ }
catch (SignTerminalStateException)                          { /* FAILED / CANCELLED on MISA's side */ }
catch (SignTimeoutException)                                { /* the signer did not approve within Polling.TotalTimeout */ }
catch (ESignTransportException)                             { /* transient: back off and retry later */ }
```

The signer gets an approval request on the MISA eSign app; `SignPdfAsync` returns once they approve. Behind that one call the SDK runs sign-in → certificate listing → server-side hash → sign submission → status polling → signature attachment, with cached tokens, transparent 401 refresh and exponential-backoff transport retries — see [How signing works](../index#how-signing-works). All exceptions are listed on [Errors](./errors).

## 5. Provide correlation IDs

Every call carries a correlation ID (in the `X-Correlation-Id` header, the call log and every exception) taken from `ICorrelationIdAccessor` (`MisaConnect.ESign.Application.Abstractions`). The default is a new GUID per DI scope. In an ASP.NET host, register a scoped accessor that returns the request's ID (for example from an `X-Correlation-ID` header); in a console app a fixed or per-operation value is enough. The value must not be empty.

```csharp
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Client.DependencyInjection;

var services = new ServiceCollection();
services.AddScoped<ICorrelationIdAccessor, FixedCorrelationIdAccessor>();
services.AddMisaConnectESign(o => { /* … */ });

internal sealed class FixedCorrelationIdAccessor : ICorrelationIdAccessor
{
    public string Current { get; } = $"batch-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
}
```

The runnable ASP.NET Core host [`samples/dotnet/MisaConnect.Samples.Api`](https://github.com/ChuKhaLi/MisaConnect/tree/main/samples/dotnet/MisaConnect.Samples.Api) shows a per-request accessor.

## 6. Two-factor authentication (OTP)

Two flavours:

- **Transparent** — register an `IOtpProvider` that fetches the OTP from your channel (SMS gateway, TOTP, interactive prompt). The SDK calls it automatically when MISA asks for an OTP; `SignPdfAsync` needs no change.
- **Explicit** — catch `AuthenticationFailedException` with `Requires2FA = true`, then call `IMisaESignClient.SignInWithOtpAsync` with the user-supplied OTP and repeat the sign call. `ResendOtpAsync` asks MISA to send the code again.

Full examples: [Two-factor sign-in](./two-factor).

## 7. Multi-format signing

`SignXmlAsync`, `SignWordAsync` and `SignExcelAsync` each take one request DTO and mirror `SignPdfAsync` for XML (XAdES), OOXML `.docx` and OOXML `.xlsx`. For XML, build the request with `SignXmlRequest.FromString(...)` or `SignXmlRequest.FromUtf8Bytes(...)`. See [Operations](./operations).

## 8. Webhook-mode (non-blocking) signing

For signings that may take the signer a while, use `BeginSign{Pdf,Xml,Word,Excel}Async` to start without polling. MISA then POSTs the completion envelope to your webhook endpoint; `HandleWebhookAsync` validates it, confirms the outcome with MISA (`Signing/status`), attaches the signature and routes the result to your `IWebhookDeliveryHook`. Endpoint, ACK codes and the processing rules: [Webhook mode](./webhooks).

## Next steps

- [Configuration](./configuration)
- [Operations](./operations)
- [Sandbox setup](./sandbox-setup)
- [Architecture overview](../../architecture)
- [`samples/dotnet/MisaConnect.Samples.Api`](https://github.com/ChuKhaLi/MisaConnect/tree/main/samples/dotnet/MisaConnect.Samples.Api) — runnable ASP.NET Core reference host

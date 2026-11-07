---
title: .NET
parent: ESign
nav_order: 1
has_children: true
permalink: /esign/dotnet/
---

# ESign for .NET

```bash
dotnet add package MisaConnect.ESign
```

`MisaConnect.ESign` is the .NET SDK for MISA eSign RemoteSigning: PDF / XML / Word / Excel signing, two-factor (OTP) sign-in and webhook-mode (non-blocking) signing, all behind one facade, `IMisaESignClient`. Register it with `services.AddMisaConnectESign(configuration)` (section `Misa:ESign`) and resolve `IMisaESignClient` from DI. It targets .NET 8 and .NET 10 and depends only on `Microsoft.Extensions.*` (10.0.14 or later) and `System.Text.Json`. The concepts behind it (certificates, the sign flow, OTP, webhook ACKs, credentials modes) are explained once on the [ESign page](../#concepts).

| Method | Purpose |
| --- | --- |
| `SignPdfAsync` | Sign a PDF and wait for the signer's approval. |
| `SignXmlAsync` | Same for XML (XAdES); build the request from a string or UTF-8 bytes. |
| `SignWordAsync`, `SignExcelAsync` | Same for Word (`.docx`) and Excel (`.xlsx`). |
| `SignInWithOtpAsync` | Explicit 2FA: answer a pending OTP challenge. |
| `ResendOtpAsync` | Ask MISA to send the OTP again. |
| `BeginSignPdfAsync`, `BeginSignXmlAsync`, `BeginSignWordAsync`, `BeginSignExcelAsync` | Webhook mode: start signing and return a transaction id. |
| `HandleWebhookAsync` | Webhook mode: process one MISA delivery and produce the ACK. |

Details of every method are on [Operations](./operations).

- [Getting started](./getting-started)
- [Configuration](./configuration)
- [Operations](./operations)
- [Two-factor sign-in](./two-factor)
- [Webhook mode](./webhooks)
- [Credentials](./credentials)
- [Errors](./errors)
- [Sandbox setup](./sandbox-setup)

Need invoice operations? See [EInvoice](../../einvoice/). The design of the ports and adapters is in [Architecture](../../architecture#esign-ports).

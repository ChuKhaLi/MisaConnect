---
title: ESign
layout: default
nav_order: 3
has_children: true
permalink: /esign/
---

# MisaConnect.ESign

Community .NET SDK for the **MISA eSign RemoteSigning** HTTP API. End-to-end PDF / XML / Word / Excel signing, two-factor (OTP) authentication, and webhook-mode (non-blocking) signing — all behind a single `IMisaESignClient` facade. Targets .NET 8.

```
dotnet add package MisaConnect.ESign
```

## Guides

- [Getting started](./getting-started) — install, configure DI, and sign your first PDF via MISA eSign RemoteSigning.
- [Configuration](./configuration) — every `MisaESignOptions` setting explained.
- [Sandbox setup](./sandbox-setup) — provisioning a MISA eSign sandbox tenant and wiring credentials for integration tests.

## Facade methods

| Method | Purpose |
| --- | --- |
| `SignPdfAsync` | One-shot synchronous PDF signature. |
| `SignXmlAsync` (string + bytes overloads) | One-shot synchronous XML signature. |
| `SignWordAsync`, `SignExcelAsync` | One-shot synchronous Office-document signature. |
| `SignInWithOtpAsync` | Submit an OTP returned to the signer's device (2FA flow). |
| `ResendOtpAsync` | Re-request an OTP if the previous one expired. |
| `BeginSignPdfAsync`, `BeginSignXmlAsync`, `BeginSignWordAsync`, `BeginSignExcelAsync` | Webhook (non-blocking) variants — return a tracking handle and deliver the signed artifact via your callback. |
| `HandleWebhookAsync` | Parse and validate a MISA eSign webhook payload inside your callback endpoint. |

All methods live on `IMisaESignClient` (resolved via DI) and preserve MISA's wire format verbatim.

## Sibling package

Need invoice operations? See [`MisaConnect.EInvoice`](../einvoice/).

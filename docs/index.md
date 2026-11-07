---
title: Home
layout: default
nav_order: 1
description: "Community .NET and TypeScript SDKs for MISA cloud APIs — EInvoice and ESign."
permalink: /
---

# MisaConnect
{: .fs-9 }

Community SDKs for MISA cloud APIs, for .NET and TypeScript. Four independent packages ship from this repo: one per MISA product and language, with the same operations in both languages.
{: .fs-5 .fw-300 }

[Get started with EInvoice](./einvoice/){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[Get started with ESign](./esign/){: .btn .fs-5 .mb-4 .mb-md-0 .mr-2 }
[View on GitHub](https://github.com/ChuKhaLi/MisaConnect){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## Packages

| Product | .NET (NuGet) | TypeScript (npm) |
| --- | --- | --- |
| **[EInvoice](./einvoice/)** — MISA MeInvoice: templates, e-ticket templates, invoice preview / save / PDF / delete, lookups, replacement and adjustment invoices. | [`MisaConnect.EInvoice`](https://www.nuget.org/packages/MisaConnect.EInvoice) · [guide](./einvoice/dotnet/) | [`@misaconnect/einvoice`](https://www.npmjs.com/package/@misaconnect/einvoice) · [guide](./einvoice/typescript/) |
| **[ESign](./esign/)** — MISA eSign RemoteSigning: PDF / XML / Word / Excel signing, two-factor sign-in (OTP), webhook-mode signing, per-call credentials. | [`MisaConnect.ESign`](https://www.nuget.org/packages/MisaConnect.ESign) · [guide](./esign/dotnet/) | [`@misaconnect/esign`](https://www.npmjs.com/package/@misaconnect/esign) · [guide](./esign/typescript/) |

[![MisaConnect.EInvoice](https://img.shields.io/nuget/v/MisaConnect.EInvoice.svg?label=MisaConnect.EInvoice)](https://www.nuget.org/packages/MisaConnect.EInvoice)
[![MisaConnect.ESign](https://img.shields.io/nuget/v/MisaConnect.ESign.svg?label=MisaConnect.ESign)](https://www.nuget.org/packages/MisaConnect.ESign)
[![@misaconnect/einvoice](https://img.shields.io/npm/v/@misaconnect/einvoice.svg?label=%40misaconnect%2Feinvoice)](https://www.npmjs.com/package/@misaconnect/einvoice)
[![@misaconnect/esign](https://img.shields.io/npm/v/@misaconnect/esign.svg?label=%40misaconnect%2Fesign)](https://www.npmjs.com/package/@misaconnect/esign)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ChuKhaLi/MisaConnect/blob/main/LICENSE)

The .NET packages target **.NET 8 and .NET 10**; the npm packages run on **Node.js 22 or later** (ESM and CommonJS) and have no runtime dependencies. Each package is independent: install only what you need.

## Install

```bash
dotnet add package MisaConnect.EInvoice    # or MisaConnect.ESign
```

```bash
npm install @misaconnect/einvoice          # or @misaconnect/esign (pnpm / yarn work too)
```

## What's next

- **Issuing e-invoices?** Start at [EInvoice](./einvoice/), then pick [.NET](./einvoice/dotnet/getting-started) or [TypeScript](./einvoice/typescript/getting-started).
- **Signing documents?** Start at [ESign](./esign/), then pick [.NET](./esign/dotnet/getting-started) or [TypeScript](./esign/typescript/getting-started).
- **Curious about the design?** Read [Architecture](./architecture).

## About this project

MisaConnect is community-maintained. It is not affiliated with or endorsed by MISA JSC. Source, issues, and contributions are on [GitHub](https://github.com/ChuKhaLi/MisaConnect). Licensed under [MIT](https://github.com/ChuKhaLi/MisaConnect/blob/main/LICENSE).

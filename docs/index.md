---
title: Home
layout: default
nav_order: 1
description: "Community .NET SDK for MISA cloud APIs — EInvoice and ESign, shipped as independent NuGet packages."
permalink: /
---

# MisaConnect
{: .fs-9 }

Community .NET SDK for MISA cloud APIs. Two independent NuGet packages ship from this repo, each a port-and-adapter facade over a separate MISA product.
{: .fs-5 .fw-300 }

[Get started with EInvoice](./einvoice/){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[Get started with ESign](./esign/){: .btn .fs-5 .mb-4 .mb-md-0 .mr-2 }
[View on GitHub](https://github.com/ChuKhaLi/MisaConnect){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## Packages

| Package | Covers | Docs |
| --- | --- | --- |
| [`MisaConnect.EInvoice`](https://www.nuget.org/packages/MisaConnect.EInvoice) | MISA MeInvoice — token acquisition, template lookup, invoice preview / save / PDF / delete, lookup, replacement & adjustment invoices. | [EInvoice docs](./einvoice/) |
| [`MisaConnect.ESign`](https://www.nuget.org/packages/MisaConnect.ESign) | MISA eSign RemoteSigning — PDF / XML / Word / Excel signing, 2FA / OTP (explicit + transparent), webhook-mode (non-blocking) signing. | [ESign docs](./esign/) |

[![MisaConnect.EInvoice](https://img.shields.io/nuget/v/MisaConnect.EInvoice.svg?label=MisaConnect.EInvoice)](https://www.nuget.org/packages/MisaConnect.EInvoice)
[![Downloads](https://img.shields.io/nuget/dt/MisaConnect.EInvoice.svg?label=downloads)](https://www.nuget.org/packages/MisaConnect.EInvoice)
[![MisaConnect.ESign](https://img.shields.io/nuget/v/MisaConnect.ESign.svg?label=MisaConnect.ESign)](https://www.nuget.org/packages/MisaConnect.ESign)
[![Downloads](https://img.shields.io/nuget/dt/MisaConnect.ESign.svg?label=downloads)](https://www.nuget.org/packages/MisaConnect.ESign)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ChuKhaLi/MisaConnect/blob/main/LICENSE)

Both packages target **.NET 8** and share the same layered architecture, options-pattern configuration, swappable ports, and wire-format fidelity with MISA's published APIs.

## Install

```
dotnet add package MisaConnect.EInvoice    # invoice operations
dotnet add package MisaConnect.ESign       # digital-signature operations
```

The two packages are entirely independent — install whichever you need (or both).

## What's next

- **New to MisaConnect.EInvoice?** Start with the [EInvoice getting-started guide](./einvoice/getting-started).
- **New to MisaConnect.ESign?** Start with the [ESign getting-started guide](./esign/getting-started).
- **Curious about the design?** Read [Architecture](./architecture) — both packages share the same layered ports-and-adapters pattern.

## About this project

MisaConnect is a community-maintained .NET SDK. It is not affiliated with or endorsed by MISA JSC. Source, issues, and contributions are on [GitHub](https://github.com/ChuKhaLi/MisaConnect). Licensed under [MIT](https://github.com/ChuKhaLi/MisaConnect/blob/main/LICENSE).

---
title: .NET
parent: EInvoice
nav_order: 1
has_children: true
permalink: /einvoice/dotnet/
---

# EInvoice for .NET

```bash
dotnet add package MisaConnect.EInvoice
```

[`MisaConnect.EInvoice`](https://www.nuget.org/packages/MisaConnect.EInvoice) is the community .NET
SDK for the **MISA MeInvoice** HTTP API. It targets .NET 8 and .NET 10 and needs
`Microsoft.Extensions.*` 10.0.14 or later. Register it with
`services.AddEInvoiceClient(configuration)` (section `Misa:EInvoice`) and call MISA through one
interface, `IMisaEInvoiceClient`, which has eleven methods: list templates and ticket templates,
preview, save / fetch / delete drafts, three lookups, and issue replacement or adjustment invoices.
Token acquisition, caching, one retry on transient failures, and structured logging with
correlation IDs are built in. The MISA concepts behind the methods (tax code + AppID, with-code
invoices, templates, RefId, paging, error categories) are explained once on the
[EInvoice page](../index#concepts).

- [Getting started](./getting-started): install, configure, first call.
- [Configuration](./configuration): every `Misa:EInvoice` key, validation, swappable ports.
- [Operations](./operations): every `IMisaEInvoiceClient` method with inputs, results and errors.
- [Errors](./errors): `MeInvoiceException`, outcome-returning operations, raw error messages.
- [Sandbox setup](./sandbox-setup): MISA sandbox credentials, integration tests, production cutover.

Also: [Architecture](../../architecture) (layers and the EInvoice ports),
[`samples/dotnet/`](https://github.com/ChuKhaLi/MisaConnect/tree/main/samples/dotnet) (a console
app and an ASP.NET Core API), [CHANGELOG](https://github.com/ChuKhaLi/MisaConnect/blob/main/CHANGELOG.md).
Need digital signatures? See [ESign for .NET](../../esign/dotnet/).

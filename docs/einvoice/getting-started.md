---
title: Getting started
layout: default
parent: EInvoice
nav_order: 1
---

# Getting started

This guide walks through installing `MisaConnect.EInvoice`, wiring DI, and calling your first MISA API.

## 1. Install

```
dotnet add package MisaConnect.EInvoice
```

## 2. Add configuration

`MisaConnect.EInvoice` binds the `Misa:EInvoice` configuration section. Use whichever configuration provider you prefer — `appsettings.json`, user-secrets, environment variables, or vault providers.

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

Sandbox credentials are obtained from MISA — see [sandbox-setup.md](sandbox-setup.md). For production, switch `Environment` to `Production` and `BaseUrl` to `https://api.meinvoice.vn/api/integration`.

## 3. Wire DI

```csharp
using MisaConnect.EInvoice.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddMisaConnectEInvoice(builder.Configuration);
```

In a hosted application (ASP.NET, Generic Host, Worker Service), call it on `builder.Services` like any other DI registration.

## 4. Resolve a use case

```csharp
using MisaConnect.EInvoice.Application.UseCases;

await using var scope = services.BuildServiceProvider().CreateAsyncScope();
var listTemplates = scope.ServiceProvider.GetRequiredService<ListActiveTemplates>();
var templates = await listTemplates.ExecuteAsync(invoiceWithCode: true, CancellationToken.None);
```

All use cases live in `MisaConnect.EInvoice.Application.UseCases`. See the [package README](../../src/MisaConnect.EInvoice.Client/README.md#supported-operations) for the full list.

## 5. Provide correlation IDs

Each call goes through a logging decorator that captures an `ICorrelationIdAccessor`. In an ASP.NET host, register a per-request accessor that reads the `X-Correlation-ID` header. In a console app, register a static one (see `samples/MisaConnect.Samples.Console`).

## Next steps

- [Architecture overview](../architecture.md)
- [Configuration reference](configuration.md)
- [Sandbox setup](sandbox-setup.md)
- [`samples/`](../../samples/) — runnable examples.

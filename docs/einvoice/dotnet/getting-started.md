---
title: Getting started
parent: .NET
grand_parent: EInvoice
nav_order: 1
---

# Getting started

This guide installs `MisaConnect.EInvoice`, configures it, registers it in DI and makes a first
call through `IMisaEInvoiceClient`.

## 1. Install

```bash
dotnet add package MisaConnect.EInvoice
```

**Requirements:** .NET 8 or .NET 10 (the package ships a build for each). The package depends on
`Microsoft.Extensions.*` **10.0.14 or later**, which also runs on .NET 8. If your app pins a
`Microsoft.Extensions.*` package below 10.0.14, restore fails with `NU1605` (package downgrade)
naming it; raise that reference to 10.0.14 or later.

## 2. Add configuration

The SDK binds the `Misa:EInvoice` configuration section. Use whichever configuration provider you
prefer: `appsettings.json`, user secrets, environment variables or a vault provider.

```json
{
  "Misa": {
    "EInvoice": {
      "Environment": "Sandbox",
      "BaseUrl": "https://testapi.meinvoice.vn/api/integration",
      "TaxCode": "0101243150",
      "UserName": "your-misa-user",
      "Password": "your-misa-password",
      "AppId": "your-app-id"
    }
  }
}
```

`TaxCode` is the bare tax code, without the AppID: MISA's "Mã số thuế" `6868686868-267` is
`TaxCode` `6868686868` plus `AppId` `267` (see [Tax code and AppID](../index#tax-code-and-appid)).
Keep the password out of `appsettings.json` in real projects (user secrets, environment variables
such as `Misa__EInvoice__Password`, or a vault). Sandbox credentials come from MISA; see
[Sandbox setup](./sandbox-setup). For production, set `Environment` to `Production` and `BaseUrl` to
`https://api.meinvoice.vn/api/integration`. Every key is described in
[Configuration](./configuration).

## 3. Register the client

In a hosted application (ASP.NET Core, Generic Host, Worker Service), register it on
`builder.Services`:

<!-- snippet: partial -->
```csharp
builder.Services.AddEInvoiceClient(builder.Configuration);
```

`AddEInvoiceClient` (namespace `MisaConnect.EInvoice.Client.DependencyInjection`) registers the
SDK's services (it calls `AddMisaConnectEInvoice`), a default correlation-ID accessor, and
`IMisaEInvoiceClient` as a **scoped** service. In a web app, inject `IMisaEInvoiceClient` into
your endpoints or controllers. The options are validated when the host starts; a missing key or a
host/environment mismatch stops startup with an `OptionsValidationException`.

## 4. Make a first call

A complete console program. It builds configuration in memory so that it runs with no extra
packages; in a real app use `appsettings.json` and user secrets as above.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Client;

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Misa:EInvoice:Environment"] = "Sandbox",
        ["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration",
        ["Misa:EInvoice:TaxCode"] = "0101243150",
        ["Misa:EInvoice:UserName"] = "your-misa-user",
        ["Misa:EInvoice:Password"] = Environment.GetEnvironmentVariable("MISA_PASSWORD") ?? "your-misa-password",
        ["Misa:EInvoice:AppId"] = "your-app-id",
    })
    .Build();

var services = new ServiceCollection();
services.AddLogging();
services.AddEInvoiceClient(configuration);

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var client = scope.ServiceProvider.GetRequiredService<IMisaEInvoiceClient>();

IReadOnlyList<TemplateDto> templates = await client.ListTemplatesAsync(withCode: true);
foreach (var t in templates)
{
    Console.WriteLine($"{t.InvSeries} ({t.IPTemplateID}) - {t.TemplateName}");
}
```

Outside a host, options are validated on first use: the first call throws
`OptionsValidationException` if the configuration is incomplete. Resolve `IMisaEInvoiceClient`
from a scope (one per unit of work, such as a request or a job), because it and the SDK's use cases
are scoped.

### Without dependency injection

`MisaEInvoiceClientFactory.Create` builds a ready client from options, for code that has no
`IServiceCollection`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Infrastructure.Configuration;

IMisaEInvoiceClient client = MisaEInvoiceClientFactory.Create(o =>
{
    o.Environment = MeInvoiceEnvironment.Sandbox;
    o.BaseUrl = "https://testapi.meinvoice.vn/api/integration";
    o.TaxCode = "0101243150";
    o.UserName = "your-misa-user";
    o.Password = "your-misa-password";
    o.AppId = "your-app-id";
});

var templates = await client.ListTemplatesAsync();
Console.WriteLine($"{templates.Count} active template(s)");
```

There is also an overload that takes a `MisaEInvoiceOptions` instance. The factory keeps its own
service provider alive for the client's lifetime, so create one client and reuse it.

## 5. Correlation IDs

Every MISA call is logged with a correlation ID taken from `ICorrelationIdAccessor`
(`MisaConnect.EInvoice.Application.Abstractions`). `AddEInvoiceClient` registers a default that
generates a new GUID per DI scope. In ASP.NET Core, register your own scoped accessor that returns
the request's `X-Correlation-ID` header (or your tracing ID), before or after `AddEInvoiceClient`;
yours wins either way (since 2.9.0):

<!-- snippet: partial -->
```csharp
using MisaConnect.EInvoice.Application.Abstractions;

public sealed class RequestCorrelationIdAccessor(IHttpContextAccessor http) : ICorrelationIdAccessor
{
    public string CorrelationId =>
        http.HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? http.HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString("D");
}

// builder.Services.AddHttpContextAccessor();
// builder.Services.AddScoped<ICorrelationIdAccessor, RequestCorrelationIdAccessor>();
```

`samples/dotnet/MisaConnect.Samples.Api` shows a middleware-based version
(`CorrelationIdMiddleware` + `HttpContextCorrelationIdAccessor`). Logs never contain tokens,
credentials, the configured tax code, buyer data, line content or amounts.

## Next steps

- [Operations](./operations): what each method sends, validates and returns.
- [Errors](./errors): handling `MeInvoiceException` and outcome values.
- [Configuration](./configuration): all options, validation rules and DI overrides.
- [Sandbox setup](./sandbox-setup): getting MISA sandbox credentials.
- [Architecture overview](../../architecture).
- [`samples/dotnet/`](https://github.com/ChuKhaLi/MisaConnect/tree/main/samples/dotnet): runnable
  console and ASP.NET Core examples.

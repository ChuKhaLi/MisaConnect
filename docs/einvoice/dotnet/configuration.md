---
title: Configuration
parent: .NET
grand_parent: EInvoice
nav_order: 2
---

# Configuration reference

`AddEInvoiceClient(configuration)` binds the `Misa:EInvoice` section
(`MisaEInvoiceOptions.SectionName`) to `MisaEInvoiceOptions`
(`MisaConnect.EInvoice.Infrastructure.Configuration`). The configuration binder ignores key case,
but match the table so that keys line up with logs and validator messages.

## `Misa:EInvoice` keys

| Key | Type | Required | Description |
| --- | --- | --- | --- |
| `Environment` | enum `MeInvoiceEnvironment` | yes | `Sandbox` (the default when the key is missing) or `Production`. Drives the host check on `BaseUrl`. |
| `BaseUrl` | string | yes | Absolute `https://` URL. Sandbox: `https://testapi.meinvoice.vn/api/integration`. Production: `https://api.meinvoice.vn/api/integration`. |
| `TaxCode` | string | yes | Issuer tax code (mã số thuế), **without** the AppID. See [Tax code and AppID](../index#tax-code-and-appid). |
| `UserName` | string | yes | MISA meInvoice user. |
| `Password` | string | yes | MISA meInvoice password. |
| `AppId` | string | yes | MISA app ID. |
| `Delete:IncludeRawErrorMessage` | bool | no | Default `false`. When `true`, raw MISA error text is surfaced on delete and lookup outcomes and amendment change reasons are logged (redacted). See [Raw error messages](./errors#raw-error-messages). Off by default to keep vendor text out of consumer logs. |

The sandbox and production hosts are also available as constants:
`MisaEInvoiceOptions.SandboxHost` (`testapi.meinvoice.vn`) and `MisaEInvoiceOptions.ProductionHost`
(`api.meinvoice.vn`).

## Configuring in code

`AddEInvoiceClient` has an overload that takes an `Action<MisaEInvoiceOptions>`, for options that
come from somewhere other than `IConfiguration`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Infrastructure.Configuration;

var services = new ServiceCollection();
services.AddLogging();
services.AddEInvoiceClient(o =>
{
    o.Environment = MeInvoiceEnvironment.Production;
    o.BaseUrl = "https://api.meinvoice.vn/api/integration";
    o.TaxCode = "0101243150";
    o.UserName = "your-misa-user";
    o.Password = "your-misa-password";
    o.AppId = "your-app-id";
    o.Delete.IncludeRawErrorMessage = false;
});
```

`MisaEInvoiceClientFactory.Create(...)` accepts the same delegate or a `MisaEInvoiceOptions`
instance (see [Getting started](./getting-started#without-dependency-injection)).

## Recommended sources

| Source | Use for |
| --- | --- |
| `appsettings.json` | Non-secret defaults (environment, base URL). |
| User secrets (`dotnet user-secrets`) | Local development credentials. |
| Environment variables (`Misa__EInvoice__TaxCode=...`) | CI, containers, sandboxes. |
| Key Vault / Secret Manager | Production credentials. |

Never commit real credentials. The sample `appsettings.json` files ship with empty placeholders.

## Validator behaviour

`MisaEInvoiceOptionsValidator` runs at startup in a host (`ValidateOnStart`), and on first use of the
options outside a host. It fails with an `OptionsValidationException` listing every problem:

- A missing `BaseUrl`, `TaxCode`, `UserName`, `Password` or `AppId`.
- `BaseUrl` not an absolute `https://` URL.
- An `Environment` value other than `Sandbox` / `Production`.
- Host/environment mismatch (e.g. `Production` with the `testapi.meinvoice.vn` host).
- Unknown sub-keys under `Misa:EInvoice:Delete` (catches typos that would otherwise silently
  disable a flag). This check runs only with the `IConfiguration` overload.

## Custom DI overrides

The SDK's collaborators are Application-layer ports with default adapters; the full list, with
defaults and reasons to swap, is in [Architecture → EInvoice ports](../../architecture#einvoice-ports).
Register your own adapter before or after `AddEInvoiceClient` / `AddMisaConnectEInvoice`: both work
because the defaults are registered with `TryAdd*` (`ISystemClock`, `IRefIdGenerator`,
`ITokenCache`, `IInvoiceValidator`, `IDeleteOptionsAccessor` and `ITemplateResolver` in
`AddMisaConnectEInvoice`; `ICorrelationIdAccessor` in `AddEInvoiceClient`). This holds since
**2.9.0**; in 2.8.0 and earlier a registration made before the call was overridden, so register
after it there. `IMeInvoiceClient` (the HTTP adapter) is not a `TryAdd*` default: replace it after
the call.

<!-- snippet: partial -->
```csharp
services.AddSingleton<ITokenCache, RedisTokenCache>();
services.AddEInvoiceClient(configuration);
```

Register `ITokenCache` as a **singleton**. The bearer-token handler resolves it inside the
`IHttpClientFactory` handler scope, not the request scope, so a scoped cache would be shared per
handler lifetime (about two minutes), not per request.

### `ITokenCache`

`ITokenCache` (`MisaConnect.EInvoice.Application.Abstractions`) stores the MISA bearer token, keyed
by the wire tax code `<TaxCode>-<AppId>`. The default `InMemoryTokenCache` is per process; use a
distributed cache to share one token across instances.

<!-- snippet: partial -->
```csharp
using MisaConnect.EInvoice.Application.Abstractions;

public interface ITokenCache
{
    Task<AccessToken?> TryGetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, AccessToken value, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}

// public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
```

The SDK stores a token with `ExpiresAtUtc` one hour short of MISA's 14 days, and signs in again
when a cached token has less than one hour left. After a 401 it removes the entry, signs in once
more and retries the request once; a second 401 throws `Authentication` (`UnAuthorize`).

### Other ports

- `IRefIdGenerator.NewRefId()`: RefIds for invoices that have none (default: a GUID).
- `ITemplateResolver`: which template an invoice without one is issued on (default: the only active
  template; see [Templates and series](../index#templates-and-series)).
- `IInvoiceValidator`: pre-flight validation before any MISA call.
- `IDeleteOptionsAccessor.IncludeRawErrorMessage`: read the raw-error opt-in at runtime instead of
  from options.
- `ICorrelationIdAccessor.CorrelationId`: the correlation ID logged per call (see
  [Getting started](./getting-started), step 5).
- `ISystemClock`: time source for token expiry.

## HTTP behaviour

- One typed `HttpClient` (`IHttpClientFactory`) with `BaseAddress` = `BaseUrl`.
- Every request is retried **once** after a 429, a 5xx, a timeout or a connection failure, after
  about 1.25–1.75 s. A second failure becomes `MisaThrottled`, `MisaUnavailable` or
  `TransportFailed`. Your own cancellation is never retried and surfaces as
  `OperationCanceledException`.
- The `HttpClient` timeout is the .NET default (100 s). From outside Vietnam, large requests can hang
  until it expires; see [Calling MISA from outside Vietnam](../index#calling-misa-from-outside-vietnam).

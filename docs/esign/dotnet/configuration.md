---
title: Configuration
parent: .NET
grand_parent: ESign
nav_order: 2
---

# Configuration reference

`AddMisaConnectESign(IConfiguration)` binds the `Misa:ESign` section (`MisaESignOptions.SectionName`) to `MisaESignOptions` (namespace `MisaConnect.ESign.Infrastructure.Configuration`). The binder is case-insensitive in practice, but match the casing below for consistency with logs and validator messages. `AddMisaConnectESign(Action<MisaESignOptions>)` sets the same options in code.

## `Misa:ESign` keys

| Key | Type | Required | Description |
| --- | --- | --- | --- |
| `Environment` | enum | yes | `Sandbox` or `Production` (`ESignEnvironment`). Drives the host check on `BaseUrl` and the default `login` / `two-factor` location (Sandbox ⇒ under `/webdev/`, Production ⇒ host root). |
| `BaseUrl` | string | yes | Absolute `https://` URL — the **host root** (plain `http://` is accepted only for loopback hosts: `localhost`, `127.0.0.1`, `::1`). Sandbox: issued with your credentials. Production: `https://esignapp.misa.vn/`. Normalized to its origin, so any path you include (e.g. a trailing `/webdev/`) is tolerated and ignored for routing. |
| `AuthUnderWebdev` | bool? | no | Overrides where `login` / `two-factor` are served. `null` (default) derives from `Environment`; `true` forces `/webdev/`, `false` forces the host root. Does not affect refresh / resend (always `/webdev/`) or ESRM (always host root). Set it only if a specific tenant differs from its environment default. |
| `ClientId` | string | Static mode | MISA client ID. |
| `ClientKey` | string | Static mode | MISA client key. |
| `UserName` | string | Static mode | MISA user. |
| `Password` | string | Static mode | MISA password. |
| `CredentialsMode` | enum | no | `Static` (default) or `Dynamic`. `Static` reads the four credentials (`ClientId` / `ClientKey` / `UserName` / `Password`) from these options. `Dynamic` means the four credentials are supplied per call by a consumer-registered `IMisaCredentialsAccessor`, and the startup validator no longer requires the four static values (every other validation still runs). An absent key resolves to `Static`. See [Credentials](./credentials). |

For example, to force `login` / `two-factor` under `/webdev/` for a tenant that needs it (routing is explained under [How signing works](../index#how-signing-works)):

```json
"Misa": { "ESign": { "AuthUnderWebdev": true } }
```

### `Polling` (synchronous PDF / XML / Word / Excel signing)

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Polling:Interval` | TimeSpan | `00:00:02` | How often the SDK polls `Signing/status` after submission. Must be > 0. |
| `Polling:TotalTimeout` | TimeSpan | `00:01:00` | Maximum wall-clock time the SDK waits for the signer to approve on the MISA eSign app. Throws `SignTimeoutException` if exceeded. Must be greater than `Interval`. |

### `TransportRetry` (transient transport failures)

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `TransportRetry:MaxAttempts` | int | `3` | Total request attempts (including the first) for transient errors (429 / 5xx / connection failure / timeout). Must be ≥ 1. |
| `TransportRetry:BaseDelay` | TimeSpan | `00:00:00.200` | Base delay for exponential backoff. |
| `TransportRetry:MaxDelay` | TimeSpan | `00:00:02` | Cap for the exponential backoff; must be ≥ `BaseDelay`. `Retry-After` headers on 429 are honoured regardless. |

### `Errors`

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Errors:IncludeRawErrorMessage` | bool | `false` | When `true`, MISA's raw `userMsg` / `devMsg` strings (and per-property `validationFailures`) are included in the thrown exception's `Detail`. Off by default to keep vendor messages out of consumer logs. |

### `Otp` (2FA defaults)

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Otp:DefaultResendLanguage` | string | `en-US` | Language tag passed to `resend-otp-auth` when the `ResendOtpAsync` call does not override it. Must be non-empty; MISA is the authority on supported values. |

### `Webhook` (webhook-mode signing)

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Webhook:Mode` | enum | `Both` | `Polling` (block-and-wait only), `Webhook` (non-blocking only), or `Both`. Calling a disabled mode throws `InvalidOperationException`: `BeginSign*Async` when `Polling`, `Sign*Async` when `Webhook`. |
| `Webhook:Session:Ttl` | TimeSpan | `24:00:00` | Lifetime of the in-memory signing-session record. Must be > 0. Increase it if MISA's retry window exceeds 24 h. |
| `Webhook:Path` | string | `/esign/webhook` | URL path the sample API's webhook endpoint mounts at. Read by your host, not by the SDK. |
| `Webhook:Secret` | string | `null` | Shared-secret URL segment for the sample API endpoint, at least 32 characters when set. The endpoint then mounts at `{Path}/{Secret}`, and a POST to the bare `{Path}` returns `404`. Keep it in user secrets or a vault. |
| `Webhook:AllowedIps` | string[] | `null` | CIDR allowlist for inbound webhook POSTs in the sample API, e.g. `["203.0.113.0/24"]`. Each entry must be a valid CIDR range. |
| `Webhook:DeliverValidationFailuresToHook` | bool | `true` | When `true`, webhook validation failures (malformed envelope, client id mismatch, unknown transaction, incomplete or mismatched success) are also delivered to your `IWebhookDeliveryHook` as a `FailureWithError` outcome. `false` answers them with an ACK only and never calls the hook. **The default becomes `false` in 3.0**: a public webhook endpoint lets anyone trigger hook calls with forged payloads. Set it to `false` now unless your hook needs those failures. |

`Path`, `Secret` and `AllowedIps` are validated by the SDK at startup but enforced by the webhook endpoint you host (the sample API does it); the SDK itself does not authenticate webhook requests. How webhooks are processed, the ACK codes and Dynamic-mode rules are on [Webhook mode](./webhooks).

## Recommended sources

| Source | Use for |
| --- | --- |
| `appsettings.json` | Non-secret defaults (environment, base URL, polling / retry tuning). |
| User secrets (`dotnet user-secrets`) | Local development credentials and the webhook secret. |
| Environment variables (`Misa__ESign__UserName=...`) | CI, containers, sandboxes. |
| Key Vault / Secret Manager | Production credentials. |

Never commit real credentials. Sample appsettings ship with empty placeholders.

## Validator behaviour

`MisaESignOptionsValidator` runs at startup (`ValidateOnStart`) and fails fast on:

- Missing required keys (`BaseUrl`, `ClientId`, `ClientKey`, `UserName`, `Password`). The `ClientId` / `ClientKey` / `UserName` / `Password` requirement applies in **`Static` mode only** — in `Dynamic` mode these four checks are skipped because credentials are supplied per call via `IMisaCredentialsAccessor`.
- `BaseUrl` not an absolute `https://` URL (plain `http://` is allowed for loopback hosts only).
- Host / environment mismatch (`Production` with a host other than `esignapp.misa.vn`, or `Sandbox` with the production host).
- Out-of-range polling / retry values (see the tables above), an empty `Otp:DefaultResendLanguage`, a non-positive `Webhook:Session:Ttl`, a `Webhook:Secret` shorter than 32 characters, or an invalid `Webhook:AllowedIps` entry.

The `BaseUrl`, host / environment, polling, transport-retry, OTP and webhook validations run in both credentials modes.

## Custom DI overrides

`AddMisaConnectESign` registers its defaults with `TryAdd*`, so a registration made **before** the call always wins. Most ports can also be registered just after the call (the last registration is the one resolved), and `IWebhookDeliveryHook` may be registered before or after. `IMisaCredentialsAccessor` and `IWebhookCredentialsResolver` **must** be registered before. Example:

<!-- snippet: partial -->
```csharp
services.AddSingleton<ITokenCache, RedisTokenCache>();
services.AddSingleton<ICertificateSelector, PickByIssuerDnSelector>();
services.AddSingleton<IOtpProvider, MyOtpProvider>();
services.AddSingleton<IWebhookDeliveryHook, MyDeliveryHook>();
services.AddSingleton<IWebhookCredentialsResolver, MyWebhookCredentialsResolver>(); // before only
services.AddMisaConnectESign(config);
```

The ports live in `MisaConnect.ESign.Application.Abstractions`, except `IWebhookDeliveryHook` (`MisaConnect.ESign.Client.Webhook`). Custom token caches and the credentials accessor have extra rules: see [Credentials](./credentials). The full port list is in [Architecture → ESign ports](../../architecture#esign-ports).

A custom certificate selector receives the active certificates and returns one:

```csharp
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Domain.Certificates;

var services = new ServiceCollection();
services.AddSingleton<ICertificateSelector, LatestExpirySelector>();
services.AddMisaConnectESign(o => { /* … */ });

internal sealed class LatestExpirySelector : ICertificateSelector
{
    public Task<Certificate> SelectAsync(IReadOnlyList<Certificate> activeCertificates, CancellationToken ct) =>
        Task.FromResult(activeCertificates.OrderByDescending(c => c.ExpirationDate).First());
}
```

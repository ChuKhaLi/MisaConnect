---
title: Configuration
layout: default
parent: ESign
nav_order: 2
---

# Configuration reference

`MisaConnect.ESign` binds the `Misa:ESign` section to `MisaESignOptions`. All keys are case-sensitive in this table (the binder is, in practice, case-insensitive — but match the table for consistency with logs and validator messages).

## `Misa:ESign` keys

| Key | Type | Required | Description |
| --- | --- | --- | --- |
| `Environment` | enum | yes | `Sandbox` or `Production`. Drives the host-validation check on `BaseUrl` and the default `login`/`two-factor` location (Sandbox ⇒ under `/webdev/`, Production ⇒ host root). |
| `BaseUrl` | string | yes | Absolute `https://` URL — the **host root**. Sandbox: issued with your credentials. Production: `https://esignapp.misa.vn/`. Normalized to its origin, so any path you include (e.g. a trailing `/webdev/`) is tolerated and ignored for routing. |
| `AuthUnderWebdev` | bool? | no | Overrides where `login`/`two-factor` are served. `null` (default) derives from `Environment`; `true` forces `/webdev/`, `false` forces the host root. Does not affect refresh/resend (always `/webdev/`) or ESRM (always host root). |
| `ClientId` | string | yes | MISA client ID. |
| `ClientKey` | string | yes | MISA client key. |
| `UserName` | string | yes | MISA user. |
| `Password` | string | yes | MISA password. |

### `Polling` (synchronous PDF/XML/Word/Excel signing)

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Polling:Interval` | TimeSpan | `00:00:02` | How often the SDK polls `/Signing/status/{transactionId}` after submission. |
| `Polling:TotalTimeout` | TimeSpan | `00:01:00` | Maximum wall-clock time the SDK waits for the end user to confirm the sign on their MISA eSign mobile app. Throws `SignTimeoutException` if exceeded. |

### `TransportRetry` (transient transport failures)

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `TransportRetry:MaxAttempts` | int | `3` | Total request attempts (incl. the first) for transient errors (429 / 5xx / connection failure / timeout). |
| `TransportRetry:BaseDelay` | TimeSpan | `00:00:00.200` | Base delay for exponential backoff. |
| `TransportRetry:MaxDelay` | TimeSpan | `00:00:02` | Cap for the exponential backoff. `Retry-After` headers on 429 are honored regardless. |

### `Errors`

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Errors:IncludeRawErrorMessage` | bool | `false` | When `true`, MISA's raw `userMsg` / `devMsg` strings are surfaced on the thrown exception. Off by default to keep vendor messages out of consumer logs. |

### `Otp` (2FA defaults)

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Otp:DefaultResendLanguage` | string | `en-US` | Language tag passed to `/resend-otp-auth` when the consumer's `ResendOtpAsync` call does not override it. MISA is the authority on supported values. |

### `Webhook` (webhook-mode signing — slice 4)

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Webhook:Mode` | enum | `Both` | `Polling` (block-and-wait only), `Webhook` (non-blocking only), or `Both`. Refuses the disabled mode with `InvalidOperationException`. |
| `Webhook:Session:Ttl` | TimeSpan | `24:00:00` | Lifetime of the in-memory signing-session record. Increase if MISA's retry window exceeds 24h. |
| `Webhook:Path` | string | `/esign/webhook` | Sample-API URL path the webhook endpoint mounts at. |
| `Webhook:Secret` | string | `null` | Sample-API shared-secret URL segment. When set, the endpoint mounts at `{Path}/{Secret}` and any POST to the bare `{Path}` returns `404`. Use `dotnet user-secrets` to set in production. |
| `Webhook:AllowedIps` | string[] | `null` | Sample-API CIDR allowlist for inbound webhook POSTs. Example: `["203.0.113.0/24"]`. |

See [specs/004-misa-esign-webhook/quickstart.md](../../specs/004-misa-esign-webhook/quickstart.md) for the full webhook-mode walkthrough including hook registration, transport-layer auth, and end-to-end testing.

## Recommended sources

| Source | Use for |
| --- | --- |
| `appsettings.json` | Non-secret defaults (environment, base URL, polling/retry tuning). |
| User secrets (`dotnet user-secrets`) | Local development credentials & webhook secret. |
| Environment variables (`Misa__ESign__UserName=...`) | CI, containers, sandboxes. |
| Key Vault / Secret Manager | Production credentials. |

Never commit real credentials. Sample appsettings ship with empty placeholders.

## Validator behaviour

`MisaESignOptionsValidator` runs at startup (`ValidateOnStart`) and fails fast on:

- Missing required keys (`BaseUrl`, `ClientId`, `ClientKey`, `UserName`, `Password`).
- `BaseUrl` not absolute `https://`.
- Host/environment mismatch (e.g. `Production` env with a sandbox host).
- Out-of-range polling / retry values.

## Custom DI overrides

Register your own adapter before `AddMisaConnectESign`, or just after — both work because `AddMisaConnectESign` uses `TryAdd*` semantics for swappable services. Example:

```csharp
services.AddSingleton<ITokenCache, RedisTokenCache>();
services.AddSingleton<ICertificateSelector, PickByIssuerDnSelector>();
services.AddSingleton<IOtpProvider, MyOtpProvider>();
services.AddSingleton<IWebhookDeliveryHook, MyDeliveryHook>();
services.AddMisaConnectESign(config);
```

See [docs/architecture.md](../architecture.md#esign-ports) for the full port list.

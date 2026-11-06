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
| `BaseUrl` | string | yes | Absolute `https://` URL — the **host root** (plain `http://` is accepted only for loopback hosts: `localhost`, `127.0.0.1`, `::1`). Sandbox: issued with your credentials. Production: `https://esignapp.misa.vn/`. Normalized to its origin, so any path you include (e.g. a trailing `/webdev/`) is tolerated and ignored for routing. |
| `AuthUnderWebdev` | bool? | no | Overrides where `login`/`two-factor` are served. `null` (default) derives from `Environment`; `true` forces `/webdev/`, `false` forces the host root. Does not affect refresh/resend (always `/webdev/`) or ESRM (always host root). |
| `ClientId` | string | yes | MISA client ID. |
| `ClientKey` | string | yes | MISA client key. |
| `UserName` | string | yes | MISA user. |
| `Password` | string | yes | MISA password. |
| `CredentialsMode` | enum | no | `Static` (default) or `Dynamic`. `Static` reads the four credentials (`ClientId`/`ClientKey`/`UserName`/`Password`) from these options. `Dynamic` means the four credentials are supplied per-call via a consumer-registered `IMisaCredentialsAccessor`, and the startup validator no longer requires the four static values (every other validation still runs). An absent key resolves to `Static`. |

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
| `Webhook:DeliverValidationFailuresToHook` | bool | `true` | When `true`, webhook validation failures (malformed envelope, client id mismatch, unknown transaction, incomplete or mismatched success) are also delivered to your `IWebhookDeliveryHook` as a `FailureWithError` outcome. `false` answers them with an ACK only and never calls the hook. **The default becomes `false` in 3.0**: a public webhook endpoint lets anyone trigger hook calls with forged payloads. Set it to `false` now unless your hook needs those failures. |

### How a webhook is processed

The webhook is only a trigger. For each accepted webhook the SDK calls MISA's `Signing/status` (one extra call per webhook) and decides from MISA's answer, not from the webhook body: on `SUCCESS` it attaches MISA's signature; on `FAILED` / `CANCELLED` the outcome carries MISA's status and error code. The first terminal state of a transaction (success, failed or cancelled) is stored once; later webhooks for it return the originally returned ACK and do not call the hook again. Finalize never prompts for an OTP: a two-factor requirement, an authentication error or a certificate error during finalize becomes a `webhook.finalize_failed` ACK.

`HandleWebhookAsync` does not throw for malformed input or finalize failures; it returns an ACK and an outcome. Two exceptions propagate: caller cancellation, and exceptions thrown by **your** `IWebhookDeliveryHook` while delivering a success or terminal outcome. The success is cached before the hook runs, so MISA's retry receives the cached ACK and the hook is not called again for that transaction. (Exceptions thrown by the hook while delivering a validation-failure outcome are logged and swallowed.)

### Webhook ACK codes

`HandleWebhookAsync` returns a `WebhookAck`. Success uses the configured success code (`"0"` by default). Failures use these codes:

| ACK `errorCode` | Returned when |
| --- | --- |
| `webhook.malformed` | The body is missing or has an empty `messageId`, `clientId` or `transactionId`, an unknown `status`, or a null entry in `signatures` (a non-array `signatures` value fails JSON binding before the SDK sees it). Also the fallback for an unspecified validation failure. |
| `webhook.client_id_mismatch` | The webhook's `clientId` differs from the configured `ClientId`. |
| `webhook.unknown_transaction` | No signing session is recorded for that `transactionId`. |
| `webhook.incomplete_success` | `status` is `SUCCESS` but `signatures[]` is empty or invalid. |
| `webhook.document_id_mismatch` | A `signatures[].documentId` does not match the session. |
| `webhook.finalize_failed` | Confirming or attaching the signature failed (MISA status call, credentials, two-factor, certificate, a non-final or signature-less status, or any other unexpected error). MISA may retry. |

### Dynamic mode and webhooks

- **Client id check.** In `Dynamic` mode the webhook `clientId` check is skipped only when `ClientId` is left blank. If a Dynamic configuration also sets `ClientId`, webhooks for any other `clientId` are rejected with `webhook.client_id_mismatch`. With a blank `ClientId`, the session lookup by `(clientId, transactionId)` is the spoof check.
- **Credentials.** A Dynamic-mode session records the `clientId` and `userName` it was started with (never secrets). On the webhook, the SDK asks `IWebhookCredentialsResolver.ResolveAsync(clientId, userName, ct)` for that principal's credentials. The default resolver returns `null` in Dynamic mode (so finalize answers `webhook.finalize_failed`); register your own **before** `AddMisaConnectESign`. In `Static` mode the options credentials are used.
- **Custom `ISigningSessionStore`.** A persistent store must persist and restore `SigningSession.UserName` and `SigningSession.CachedTerminal` (both init-only properties). A store that rebuilds sessions through the constructor drops `UserName`, and Dynamic-mode webhooks then fail with `MissingPrincipal`. `CacheTerminalAsync` has a default no-op implementation: a store that does not implement it keeps the 2.4 behaviour (failed/cancelled terminals are re-delivered on every webhook).

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

- Missing required keys (`BaseUrl`, `ClientId`, `ClientKey`, `UserName`, `Password`). The `ClientId`/`ClientKey`/`UserName`/`Password` requirement applies in **`Static` mode only** — in `Dynamic` mode these four checks are skipped because credentials are supplied per-call via `IMisaCredentialsAccessor`.
- `BaseUrl` not an absolute `https://` URL (plain `http://` is allowed for loopback hosts only).
- Host/environment mismatch (e.g. `Production` env with a sandbox host).
- Out-of-range polling / retry values.

The `BaseUrl`, host/environment, polling, transport-retry, OTP, and webhook validations run in both modes.

## Custom DI overrides

`AddMisaConnectESign` registers its defaults with `TryAdd*`, so a registration made **before** the call always wins. Most ports can also be registered just after the call, and `IWebhookDeliveryHook` may be registered before or after. `IMisaCredentialsAccessor` and `IWebhookCredentialsResolver` MUST be registered before. Example:

```csharp
services.AddSingleton<ITokenCache, RedisTokenCache>();
services.AddSingleton<ICertificateSelector, PickByIssuerDnSelector>();
services.AddSingleton<IOtpProvider, MyOtpProvider>();
services.AddSingleton<IWebhookDeliveryHook, MyDeliveryHook>();
services.AddSingleton<IWebhookCredentialsResolver, MyWebhookCredentialsResolver>(); // before only
services.AddMisaConnectESign(config);
```

### `ITokenCache` — custom caches

A custom token cache implements `TryGetAsync`, `SetAsync` and `RemoveAsync`. Since 2.7, the SDK evicts a stale token through `RemoveIfMatchesAsync(key, expectedValue, ct)`, which removes the entry only while it still holds the token that MISA rejected, so a token another flow has just stored is never evicted. The default implementation reads, compares and removes, which leaves a small race; a distributed cache that supports compare-and-delete (for example a Redis Lua script or `WATCH`/`MULTI`) should override it. Token-cache calls made during a shared refresh are not cancellable, so a custom cache must bound its own I/O with timeouts.

### `IMisaCredentialsAccessor` — register-before only

To supply MISA credentials per signing call (e.g. per-person credentials), set `CredentialsMode = Dynamic` and register a custom `IMisaCredentialsAccessor`. The default `OptionsMisaCredentialsAccessor` is registered via `TryAddSingleton`, so a consumer override only wins when it is registered **before** `AddMisaConnectESign`:

```csharp
services.AddSingleton<IMisaCredentialsAccessor, MyAmbientCredentialsAccessor>();
services.AddMisaConnectESign(config); // CredentialsMode: Dynamic
```

Unlike the other ports above, this one is **register-before only** — do not register it "just after". A plain `AddSingleton` after the SDK's own `TryAddSingleton` appends a *second* descriptor: the options-default accessor stays constructible and both accessors surface through `IEnumerable<IMisaCredentialsAccessor>`, so it is undefined which one resolves. Registering before guarantees exactly one accessor is in effect.

**Lifetime contract:** the accessor MUST be singleton-registrable and resolve the current call's credentials from ambient (`AsyncLocal`) or options state read inside `Get()`. It MUST NOT be scoped — the SDK resolves the accessor from singleton collaborators, so a scoped registration is a captive-dependency failure at build. `Get()` is read per-call regardless of lifetime, so an ambient-backed singleton is sufficient and the only safe shape.

See [docs/architecture.md](../architecture.md#esign-ports) for the full port list.

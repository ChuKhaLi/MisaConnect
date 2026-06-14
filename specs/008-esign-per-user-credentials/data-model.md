# Phase 1 Data Model: Per-User Credentials Seam

This is a stateless SDK seam; the "data model" is the small set of contract types and the one option value, plus the per-call resolution flow. No persistence, no entities with lifecycle.

## Types

### `MisaCredentials` (public record — Application.Abstractions)

One signer's full MISA credential set, resolved per-call.

| Field | Type | Secret? | Notes |
|-------|------|---------|-------|
| `ClientId` | `string` | No | App/tenant dimension; stamped as `x-clientId`; part of the token-cache key. |
| `ClientKey` | `string` | **Yes** | App/tenant dimension; stamped as `x-clientKey`. Redacted in `ToString()`. |
| `UserName` | `string` | No | Per-user dimension; login body; part of the token-cache key. |
| `Password` | `string` | **Yes** | Per-user dimension; login body. Redacted in `ToString()`. |

- **Shape**: positional `sealed record` with all four required. Single record so one per-call resolution feeds headers + login + cache key consistently. [Request D3 — intentionally coarse, all-four]
- **Validation rules**: none on the record itself (it is a transport container). Non-emptiness of `UserName`/`ClientId` is enforced downstream by `DefaultTokenCacheKeySelector.Compose()` (fail-fast).
- **`ToString()` override**: emits `UserName` and `ClientId` in clear text; emits `ClientKey` and `Password` as `<redacted>`. Guards against the positional-record auto-`ToString()` leaking secrets. [Constitution VIII]

### `IMisaCredentialsAccessor` (public port — Application.Abstractions)

```text
MisaCredentials Get();
```

- **Contract**: resolves the credentials for the CURRENT call. Implementations MUST be safe to invoke per-call (the SDK calls it inside the awaited pipeline so a consumer `AsyncLocal` flows in) and MUST NOT cache the result on a singleton.
- **Lifetime**: implementations MUST be singleton-registrable and resolve from ambient (`AsyncLocal`) or options state read inside `Get()`. MUST NOT be scoped (the SDK resolves it from singleton collaborators; a scoped accessor is a captive-dependency build failure).
- **Synchronous + parameterless**: mirrors `ITokenCacheKeySelector.Compose()` and `ICorrelationIdAccessor.Current`. A pure ambient/options read; no `Task`/`CancellationToken`.

### `OptionsMisaCredentialsAccessor` (internal sealed default — Infrastructure.Credentials)

- Ctor-injects `IOptions<MisaESignOptions>`; `Get()` returns `new MisaCredentials(o.ClientId, o.ClientKey, o.UserName, o.Password)`.
- Does **not** inspect `CredentialsMode` (Infrastructure-only invariant — the default always returns the option values; dynamic behavior comes from a consumer override).
- Registered `services.TryAddSingleton<IMisaCredentialsAccessor, OptionsMisaCredentialsAccessor>()` in `AddCoreServices`.

### `CredentialsMode` (public enum — Infrastructure.Configuration)

| Value | Numeric | Meaning |
|-------|---------|---------|
| `Static` | `0` (default) | Read the four credentials from options; the validator requires them at startup. |
| `Dynamic` | `1` | Credentials supplied per-call via `IMisaCredentialsAccessor`; the validator does NOT require the four static values. |

- Added as `MisaESignOptions.CredentialsMode { get; set; } = CredentialsMode.Static;`. Bound automatically via the existing `.Bind(...)`; an absent key binds to `Static` (zero-value) ⇒ existing appsettings byte-identical.
- Read **only** by `MisaESignOptionsValidator`. No Application type references it.

## Per-call resolution flow (one logical sign)

```text
consumer sets AsyncLocal<MisaCredentials> (its accessor reads it)
        │
        ▼  (one stable snapshot for the whole awaited call)
 ┌──────────────────────────── awaited SDK pipeline ────────────────────────────┐
 │  DefaultTokenCacheKeySelector.Compose()  → Get() → {UserName}|{ClientId}|{host}│  (throws if UserName/ClientId empty)
 │  EnsureAccessToken login closure         → Get() → LoginAsync(UserName,Password)│
 │  MisaESignWireClient.NewRequest          → Get() → x-clientId / x-clientKey     │
 │  ClientHeadersHandler.SendAsync          → Get() → x-clientId / x-clientKey     │
 └───────────────────────────────────────────────────────────────────────────────┘
   All four read the SAME ambient snapshot ⇒ headers ↔ login body ↔ cache key consistent (no torn read).
```

- The SDK reads `Get()` independently at each consumer (no SDK-side caching). Consistency relies on the consumer's value being stable for the call. [Clarification #1]
- Default (Static, no override) ⇒ all four read the options ⇒ byte-identical to 2.1.1.

## Token-cache key

- **Shape unchanged**: `{UserName}|{ClientId}|{host}` (host derived from `BaseUrl`).
- **Source changed**: `UserName`/`ClientId` now come from `IMisaCredentialsAccessor.Get()` per-call instead of `IOptions` directly.
- **Fail-fast**: `Compose()` throws `InvalidOperationException` if the resolved `UserName` or `ClientId` is null/empty — no `||host` collapse, no cross-user bleed. [Request D2]

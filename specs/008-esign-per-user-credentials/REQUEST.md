# Change Request: Per-User Credentials Seam for MisaConnect.ESign

| Field | Value |
| --- | --- |
| **Requesting repo** | `elims-backend` (ELIMS — consumer of `MisaConnect.ESign.Client`) |
| **Target package** | `MisaConnect.ESign` |
| **Proposed version** | `2.2.0` — SemVer **MINOR** (additive, non-breaking; default behavior byte-identical) |
| **Status** | Requested |
| **Date** | `2026-06-15` |
| **Spec slice** | `specs/008-esign-per-user-credentials/` |

---

## 1. Summary

ELIMS is moving MISA eSign from a single shared org service account to **per-person credentials**: each signer carries their own MISA `ClientId` / `ClientKey` / `UserName` / `Password`, stored encrypted in ELIMS and decrypted per-sign — fully unattended, with no per-sign secret entry by the user. This request asks the SDK to add a **first-class credentials accessor port** (`IMisaCredentialsAccessor` + `MisaCredentials`), with a default that reads `IOptions<MisaESignOptions>` so existing single-account consumers are byte-identical, plus a `MisaESignOptions.CredentialsMode { Static, Dynamic }` switch so the validator skips the four static-cred checks when a consumer supplies credentials dynamically. The port must be consulted **per-call inside the awaited HTTP pipeline** (login credentials, `x-clientId`/`x-clientKey` headers, and the token-cache key), mirroring exactly how `ICertificateSelector` is already consulted, so an `AsyncLocal` set by ELIMS around each SDK call flows in and isolates each signer's login + token cache slot.

---

## 2. Motivation / consumer context

### 2.1 The ELIMS per-person model

Today ELIMS signs through one MISA org service account configured in `Misa:ESign`. The new model gives **each staff member their own MISA credential set** — an org cert they personally have permission to use, treated like a personal cert. The four secrets (`ClientId`, `ClientKey`, `UserName`, `Password`) are stored **encrypted per staff** (AES-256-GCM, the same `PinKeyProtector` scheme ELIMS already uses for its internal signing key, `elims.services/Impl/Signature/PinKeyProtector.cs`), and **decrypted per-sign** into plaintext that lives only for the duration of one awaited SDK call. There is no per-sign OTP / password prompt — signing is unattended.

In the per-user model **there is no app-global `UserName`/`Password`/`ClientId`/`ClientKey`**. Only `BaseUrl` and `Environment` remain global. ELIMS's `Program.cs` already registers `AddMisaConnectESign` conditionally (gated on `Misa:ESign:BaseUrl` being set) precisely because the SDK calls `.ValidateOnStart()` and the current validator hard-fails on empty creds.

### 2.2 The proven seam ELIMS already relies on

ELIMS already overrides `ICertificateSelector` with `ElimsCertificateSelector` (`elims.services/Impl/Signature/ElimsCertificateSelector.cs`), registered `AddSingleton` **before** `AddMisaConnectESign`. That selector reads `MisaCertSelection.DesiredAlias`, an `AsyncLocal<string?>` set via `using (MisaCertSelection.Use(alias))` around the awaited `client.SignPdfAsync(...)` call (`MisaEsignProvider.cs:75-79`). This **proves** the `AsyncLocal` flows from the provider, through the SDK's MS-DI child scope, into a singleton selector resolved deep inside the sign pipeline. The credentials seam is the **exact same shape**: an `AsyncLocal<MisaCredentials>` set by the ELIMS provider around each awaited SDK entry, read by an `IMisaCredentialsAccessor` the SDK consults per-call.

### 2.3 Why per-instance credential containers were rejected in favor of a port

An alternative considered was passing a per-instance credentials container into each public `Sign*`/`ListActiveCertificates`/relink call (or constructing a credential-bound client instance). It was rejected because:

- **It would change the public surface of every entry point** (`SignPdfAsync`, list-certs, OTP relink, …) rather than adding a single orthogonal seam — a larger, breaking-shaped change instead of an additive one.
- **It would not flow into the existing internal seams** (`DefaultTokenCacheKeySelector`, `ClientHeadersHandler`, the `EnsureAccessToken` login closure) without threading a parameter through every layer, whereas those seams already resolve collaborators from DI and run in the caller's async-flow.
- **It diverges from the established pattern.** The SDK already solved "per-call ambient input read deep in the pipeline" once — `ICertificateSelector` + `MisaCertSelection` `AsyncLocal`. A credentials **port** with a default-reads-options adapter is the faithful mirror of `ITokenCacheKeySelector` + `DefaultTokenCacheKeySelector` and `ICertificateSelector` + `FirstActiveCertificateSelector`. It keeps the whole change additive: default consumers are untouched; ELIMS swaps one TryAdd default.

---

## 3. Proposed public API

All new public types follow the canonical ESign port shape: the **port interface is public in `…Application/Abstractions`** (Application may reference Domain + `Microsoft.Extensions.Logging.Abstractions` only); the **default adapter is `internal sealed` in `…Infrastructure`**, ctor-injecting `IOptions<MisaESignOptions>` exactly like `DefaultTokenCacheKeySelector`; registration uses `TryAddSingleton` in `AddCoreServices` so a consumer pre-registration wins. The configuration enum + option live next to `ESignEnvironment` in `…Infrastructure/Configuration`.

### 3.1 `MisaCredentials` (record — public, Application)

```csharp
namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// One signer's full MISA credential set. Carries both the app-dimension
/// credentials (ClientId/ClientKey) and the user-dimension credentials
/// (UserName/Password) so a single per-call resolution feeds header
/// injection, the login body, and the token-cache key consistently.
/// </summary>
public sealed record MisaCredentials(
    string ClientId,
    string ClientKey,
    string UserName,
    string Password)
{
    // MUST override the positional-record auto-ToString(), which would otherwise
    // emit ALL members in plaintext (ClientKey + Password). A structured-logging
    // sink, an exception-context capture, or any $"{creds}" interpolation would
    // dump the secrets. Redact the two secret-bearing members. See §9 acceptance
    // line "never logs the resolved credential values" + Constitution Principle VIII.
    public override string ToString() =>
        $"MisaCredentials {{ UserName = {UserName}, ClientId = {ClientId}, ClientKey = <redacted>, Password = <redacted> }}";
}
```

> **Why Application, not Domain:** `MisaCredentials` lives in `Application.Abstractions` (not `Domain`) because it is a **consumer-extension / DI contract type** paired with the `IMisaCredentialsAccessor` port — the same layer and rationale as the existing public `AccessToken` record and the `ICertificateSelector`/`ITokenCacheKeySelector` ports. `Domain` remains zero-dependency and holds only protocol/entity types (`AuthSession`, `Certificate`, `WebhookEnvelope`); putting a transport/DI concern there would wrongly elevate it into the domain model and gain nothing (Domain has no consumers needing it). The port obviously cannot be Domain either — Domain has no DI/port concept; ports live in `Application.Abstractions` by Principle II.

### 3.2 `IMisaCredentialsAccessor` (port — public, Application)

```csharp
namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// Resolves the MISA credentials for the CURRENT call. Implementations MUST
/// be safe to invoke per-call (the SDK calls it inside the awaited HTTP
/// pipeline so a consumer's AsyncLocal flows in) and MUST NOT cache the
/// result on a singleton. The SDK never logs the returned values.
/// </summary>
public interface IMisaCredentialsAccessor
{
    MisaCredentials Get();
}
```

> `Get()` is **synchronous and parameterless**, mirroring `ITokenCacheKeySelector.Compose()` and `ICorrelationIdAccessor.Current`. Credential resolution is a pure ambient read (an `AsyncLocal` lookup or an `IOptions` read); no `Task`/`CancellationToken` is required, and a sync accessor invoked inside the awaited `SendAsync`/`ExecuteAsync` is exactly how `ICorrelationIdAccessor` already behaves.

### 3.3 `OptionsMisaCredentialsAccessor` (default adapter — `internal sealed`, Infrastructure)

```csharp
namespace MisaConnect.ESign.Infrastructure.Credentials; // dedicated adapter folder — see below

internal sealed class OptionsMisaCredentialsAccessor : IMisaCredentialsAccessor
{
    private readonly IOptions<MisaESignOptions> _options;

    public OptionsMisaCredentialsAccessor(IOptions<MisaESignOptions> options) => _options = options;

    public MisaCredentials Get()
    {
        var o = _options.Value;
        return new MisaCredentials(o.ClientId, o.ClientKey, o.UserName, o.Password);
    }

    // NOTE: the default adapter does NOT inspect CredentialsMode. It always
    // returns the options values; Dynamic-mode behavior is achieved purely by a
    // consumer pre-registering its own IMisaCredentialsAccessor, never by this
    // default branching on the mode. (See the §3.4 Infrastructure-only invariant.)
}
```

> **Folder/namespace placement (decision D1, §11.1):** put the adapter in a dedicated **`Infrastructure/Credentials/`** folder + namespace **`MisaConnect.ESign.Infrastructure.Credentials`**, mirroring the established convention that each default adapter sits in a feature-named folder — `Infrastructure/Caching/DefaultTokenCacheKeySelector` and `Infrastructure/Certificates/FirstActiveCertificateSelector`. Placing a credentials **adapter** inside `Infrastructure/Configuration/` would blur the "configuration POCO/validator" area with the "adapter" area and be the odd-one-out versus that convention. (The `CredentialsMode` **enum** stays in `Infrastructure/Configuration/` next to `ESignEnvironment` because it is an options value, not an adapter — see §3.4.)

Registered in `AddCoreServices` alongside the other defaults (`ServiceCollectionExtensions.cs:59-61`):

```csharp
services.TryAddSingleton<IMisaCredentialsAccessor, OptionsMisaCredentialsAccessor>();
```

`TryAddSingleton` is the documented override mechanism: a consumer that registers its own `IMisaCredentialsAccessor` **before** `AddMisaConnectESign` already owns the descriptor, so the SDK's `TryAdd` is a no-op and the consumer wins — identical to how ELIMS overrides `ICertificateSelector` and `ITokenCache` today.

> **Override ordering is register-before only (for a secret-bearing accessor).** For `IMisaCredentialsAccessor` the supported override is **register the consumer accessor BEFORE `AddMisaConnectESign`**. The "or just after" pattern that the shipped `docs/esign/configuration.md` mentions for other ports is misleading here: a plain `AddSingleton` *after* the SDK's `TryAddSingleton` appends a **second** descriptor — a single `GetRequiredService` happens to resolve the last (consumer's) one, but `IEnumerable<IMisaCredentialsAccessor>` resolution surfaces **both** and the options-default accessor remains constructible. For a credentials port that is strictly worse (two registered accessors, one reading static options). §8 corrects the docs wording accordingly.

> **Lifetime contract (load-bearing, document on the port's XML doc):** `IMisaCredentialsAccessor` implementations **MUST be singleton-registrable** and resolve credentials from **ambient (`AsyncLocal`) or options state read inside `Get()`** — they **MUST NOT be scoped**. The SDK resolves the accessor from **singleton** collaborators (e.g. `DefaultTokenCacheKeySelector`, which is a singleton ctor-injecting `IOptions`); a scoped accessor would be a **captive dependency** and fail ASP.NET's scope-validation at build. Because `Get()` is read **per-call** regardless of DI lifetime, an ambient-based singleton is both sufficient and the only safe shape. (This is the same posture as `ICorrelationIdAccessor`; that port's facade registers `TryAddScoped` as the documented exception, but the **credentials default is `TryAddSingleton`** and consumer overrides must be singleton-safe.)

### 3.4 `CredentialsMode` (enum — public, Infrastructure/Configuration) + option

```csharp
namespace MisaConnect.ESign.Infrastructure.Configuration; // next to ESignEnvironment

public enum CredentialsMode
{
    Static = 0,   // default — read ClientId/ClientKey/UserName/Password from options
    Dynamic = 1   // credentials supplied per-call via IMisaCredentialsAccessor
}
```

Added to `MisaESignOptions` (`MisaESignOptions.cs`, alongside the four cred properties at L22-25):

```csharp
/// <summary>
/// Static (default): the four credentials are read from these options and the
/// validator requires them at startup. Dynamic: credentials are supplied
/// per-call via IMisaCredentialsAccessor; the validator does NOT require the
/// four static credential values (BaseUrl/Environment/Polling/... still apply).
/// </summary>
public CredentialsMode CredentialsMode { get; set; } = CredentialsMode.Static;
```

Binding is automatic via the existing `.Bind(...)` in both `AddMisaConnectESignCore` overloads — no DI code change for binding. Because `Static = 0` is the enum zero-value, an unset/absent key binds to `Static`, keeping existing appsettings byte-identical.

> **Public-in-Infrastructure is by-design, not a layering violation.** `CredentialsMode` is a **public** enum in `MisaConnect.ESign.Infrastructure.Configuration`, following the exact same precedent as the existing public `MisaESignOptions`, `ESignEnvironment`, and `AuthUnderWebdev` that already live there as public option surface. The seam-pattern shorthand "Infrastructure types are internal" applies to **adapters/handlers**, not to the bound option/enum surface a consumer must set in `Configure(...)`. Its visibility is deliberate.

> **CredentialsMode is an Infrastructure-only concern (load-bearing invariant).** It is read **ONLY** by `MisaESignOptionsValidator` (and is bound on `MisaESignOptions`). **No Application-layer type references `CredentialsMode`** — doing so would force a forbidden Application→Infrastructure dependency, and an Application use-case (e.g. `EnsureAccessToken`) cannot even see the enum. In particular: the `EnsureAccessToken` use-case MUST NOT branch on it, and the default `OptionsMisaCredentialsAccessor` (§3.3) MUST NOT inspect it — it unconditionally returns the options values. **Dynamic behavior is achieved purely by the consumer pre-registering its own `IMisaCredentialsAccessor`; the mode flag only relaxes the startup validator** (and may optionally be read by the Infrastructure DI factory). A naive `if (options.CredentialsMode == Static)` inside any Application consumer is a layering violation and must be rejected in review.

---

## 4. Required consumer (SDK) changes

The full set of credential consumers, classified by sign-path. **v1 of the ELIMS per-user feature is synchronous-poll only** (`SignPdfAsync` + list-certs + `EnsureAccessToken` + relink); the begin-sign/webhook async path is **explicitly deferred** (ELIMS does not use it today).

| # | Component | File:line | Change | Sign-path classification |
| --- | --- | --- | --- | --- |
| 1 | `ClientHeadersHandler.SendAsync` | `Infrastructure/Http/ClientHeadersHandler.cs:27-34` | **Gains an `IMisaCredentialsAccessor` ctor dependency** (current ctor: `IOptions<MisaESignOptions>`, `ICorrelationIdAccessor`). Read `ClientId` (L29) and `ClientKey` (L33) from `IMisaCredentialsAccessor.Get()` invoked **inside `SendAsync`** (per-call, so the caller's `AsyncLocal` flows in) instead of `_options.Value.ClientId/.ClientKey`. Keep the existing `if (!request.Headers.Contains(...))` guards (retained only so a future pre-stamped header still wins — see note) and the `X-Correlation-Id` injection unchanged. | **In scope — v1 sync.** Runs on every outbound MISA call. |
| 2 | `MisaESignWireClient.NewRequest` | `Infrastructure/ESign/MisaESignWireClient.cs:750-751` | **Gains an `IMisaCredentialsAccessor` ctor dependency** (today reads `_options.Value` directly at `NewRequest`). The authoritative `x-clientId`/`x-clientKey` injection (its `TryAddWithoutValidation` wins over the handler due to the handler's `Contains` guard) must also read from `IMisaCredentialsAccessor.Get()` per request, or the static global key leaks back in. **Both #1 and #2 must change** (double-injection). | **In scope — v1 sync.** Central credential consumer on the sync path. |
| 3 | `EnsureAccessToken` credentials closure (DI factory) | `Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs:73` | Change `credentialsAccessor: () => (optionsAccessor.Value.UserName, optionsAccessor.Value.Password)` to resolve `IMisaCredentialsAccessor` and return `(c.UserName, c.Password)` from `Get()` **at call time** (the closure is already invoked lazily inside `EnsureAccessToken.ExecuteAsync` at `EnsureAccessToken.cs:63`). **Keep the `EnsureAccessToken` ctor's `Func<(string,string)>` parameter unchanged** — wire the port only at this DI seam. | **In scope — v1 sync.** Feeds `LoginAsync` body (UserName/Password). |
| 4 | `DefaultTokenCacheKeySelector.Compose` | `Infrastructure/Caching/DefaultTokenCacheKeySelector.cs:16-21` | **Gains an `IMisaCredentialsAccessor` ctor dependency** (today ctor-injects `IOptions<MisaESignOptions>`). Derive `UserName` and `ClientId` for the key `{UserName}|{ClientId}|{host}` from `IMisaCredentialsAccessor.Get()` (per-call) instead of `_options.Value`. **Critical for correctness:** if left static, two signers collide on one cache slot → cross-user token bleed (ELIMS's `RedisTokenCache` stores verbatim under `Compose()`). The accessor and the login closure (#3) **must read the same ambient snapshot** so the token is written under a key matching the creds it was minted from. **Empty-string collapse guard (decided contract — see §5.3):** `Compose()` MUST **throw `InvalidOperationException`** if the resolved `UserName` or `ClientId` is null/empty, so a missing-ambient bug surfaces loudly instead of collapsing to `||host` and silently bleeding tokens across identities. | **In scope — v1 sync.** Used by `EnsureAccessToken` (login/cache) and `RemoteSigningAuthHandler` (401 refresh). |
| 5 | `MisaESignOptionsValidator.Validate` | `Infrastructure/Configuration/MisaESignOptionsValidator.cs:44-47` | Wrap **only** the four cred checks (`ClientId`/`ClientKey`/`UserName`/`Password`) in `if (options.CredentialsMode == CredentialsMode.Static) { … }`. Everything else (BaseUrl, Environment/host coupling, Polling, TransportRetry, Otp, Webhook) stays outside the guard and still validates in Dynamic mode. | **In scope — v1 sync.** Startup gate; see §6. |
| 6 | `EnsureAccessToken` ctor `Func<(string,string)>` | `Application/UseCases/EnsureAccessToken.cs:17,26,33,63` | **No change.** The use-case keeps its `Func` ctor parameter; the port is adapted to the `Func` at the DI seam (#3). This keeps all existing unit tests green (they construct `EnsureAccessToken` directly with `() => ("user","pass")`). | **In scope — but unchanged.** |
| 7 | `RemoteSigningAuthHandler` (401 refresh key) | `Infrastructure/Http/RemoteSigningAuthHandler.cs:66` | **No code change required.** It computes the cache key via `_keySelector.Compose()`; once #4 is per-call, the refresh single-flight is correctly per-user automatically. It reads no `ClientId`/`ClientKey` fields directly. | **In scope — but unchanged** (rides #4). |
| 8 | `ExchangeOtp` / `ResendOtp` (relink path) | `Application/UseCases/ExchangeOtp.cs:36,47`; `ResendOtp.cs` | These already take `userName` as an explicit arg and compose the cache key via the same `ITokenCacheKeySelector`. Once #4 is accessor-driven, the OTP-minted token lands under the correct per-user key **provided** ELIMS sets the credentials `AsyncLocal` around `ExchangeOtp`/`ResendOtp`. **No SDK code change beyond #4** for the cache **key**. **But correctness on relink also rides #1/#2:** the `x-clientId`/`x-clientKey` headers on the wire calls these use-cases drive are stamped per-call by the handler/wire-client (#1/#2) from the same accessor — so ELIMS **must** also set the credentials `AsyncLocal` around `ExchangeOtp`/`ResendOtp` (not merely for the key). The seam must stay key-selector-driven AND accessor-driven on these use cases. | **In scope (consistency) — relink.** Lower priority for ELIMS; seam must remain consistent (header stamping rides #1/#2, not just #4 — consistent with §7). |
| 9 | `BeginSignPdf/Xml/Word/Excel` `clientIdAccessor` | `Application/UseCases/BeginSign*.cs`; wired `ServiceCollectionExtensions.cs:183,203,223,243` | `() => options.Value.ClientId` stamps the `SigningSession.ClientId` for webhook correlation — **not a credential sent to MISA**. **DEFERRED.** Leave reading static options. Only relevant if/when ELIMS adopts webhook-mode per-user. | **Deferred — webhook/begin-sign.** Out of scope for v1. |
| 10 | `HandleWebhook` `configuredClientIdAccessor` | `Application/UseCases/HandleWebhook.cs:53`; wired `ServiceCollectionExtensions.cs:272` | `() => options.Value.ClientId` is the expected `ClientId` the inbound webhook envelope must match (anti-spoof) — not an outbound credential. **DEFERRED.** Caveat: if per-user sessions later carry per-user `ClientId`s, this static-global comparison would reject them and would itself need per-session logic. | **Deferred — webhook/begin-sign.** Out of scope for v1. |

> **Deferred token consumer — `FinalizeFromWebhook`** (`ServiceCollectionExtensions.cs:251-262`): this use-case also calls `EnsureAccessToken`, so on the async webhook flow it acquires a token via the same login/cache machinery (#3/#4). It is **DEFERRED with the webhook/begin-sign path** (rows 9-10): ELIMS v1 is synchronous-poll only and never drives the webhook finalize flow. When per-user webhook mode is adopted later, `FinalizeFromWebhook` would need the credentials `AsyncLocal` set around it too (it inherits the same `EnsureAccessToken` ambient dependency as the sync path).

> **Same-instance / no-torn-read invariant (load-bearing):** `ClientHeadersHandler` (#1), `MisaESignWireClient` (#2), the `EnsureAccessToken` login closure (#3), and `DefaultTokenCacheKeySelector` (#4) ALL gain/use an `IMisaCredentialsAccessor` and ALL call `Get()` **per request**. Because every one of them runs in the **caller's async-flow**, a single consumer `AsyncLocal<MisaCredentials>` scope yields **identical `MisaCredentials` to all four** — so the `x-clientId` stamped on the wire matches the `UserName`/`Password` of the login body matches the `{UserName}|{ClientId}|{host}` cache key. If #3 and #4 ever resolved **different** snapshots, a token minted from signer A's creds would be written under a key composed from a different signer's identity (torn read). The handler retains its `Contains` guard **only** for forward-compat pre-stamping; post-change, handler and wire client read the same per-call accessor so the guard is harmless (identical values). The cleanest guarantee is for all four to resolve the **same** `IMisaCredentialsAccessor` instance per call; ELIMS sets one `AsyncLocal<MisaCredentials>` scope around the whole awaited SDK call covering token acquisition, header stamping, cache keying, and cert selection.

---

## 5. Validator + `CredentialsMode` behavior

### 5.1 Exact change

`MisaESignOptionsValidator.cs:44-47` is today:

```csharp
if (string.IsNullOrWhiteSpace(options.ClientId)) errors.Add("Misa:ESign:ClientId is required.");
if (string.IsNullOrWhiteSpace(options.ClientKey)) errors.Add("Misa:ESign:ClientKey is required.");
if (string.IsNullOrWhiteSpace(options.UserName)) errors.Add("Misa:ESign:UserName is required.");
if (string.IsNullOrWhiteSpace(options.Password)) errors.Add("Misa:ESign:Password is required.");
```

Becomes:

```csharp
if (options.CredentialsMode == CredentialsMode.Static)
{
    if (string.IsNullOrWhiteSpace(options.ClientId)) errors.Add("Misa:ESign:ClientId is required.");
    if (string.IsNullOrWhiteSpace(options.ClientKey)) errors.Add("Misa:ESign:ClientKey is required.");
    if (string.IsNullOrWhiteSpace(options.UserName)) errors.Add("Misa:ESign:UserName is required.");
    if (string.IsNullOrWhiteSpace(options.Password)) errors.Add("Misa:ESign:Password is required.");
}
```

Nothing else in `Validate` changes. The `BaseUrl` validity (L12-20), `BaseUrl`/`Environment`/host coupling (L21-42), Polling (L49-56), TransportRetry (L57-64), Otp (L66-69) and Webhook (L71-90) checks all stay outside the guard and continue to run in **both** modes.

> **`CredentialsMode` is intentionally coarse (all-four, by design).** The guard wraps **all four** cred checks together. Conceptually `ClientId`/`ClientKey` are the **app/tenant** dimension and `UserName`/`Password` the **per-user** dimension (per the credential map), so a single flag means a `Dynamic` consumer must supply **all four** via the accessor even if it only needs a dynamic `UserName`/`Password`. This is **deliberate**: ELIMS treats all four as per-user, and a split app-vs-user mode is **out of scope** (decision **D3**, §11.1). The maintainer accepts the all-four semantics on purpose. This is **not** a backward-compat break — default `Static` is unaffected.

### 5.2 Proof the default-Static path is byte-identical

1. **The new property defaults to `CredentialsMode.Static` (enum value `0`)**, so any appsettings/`Configure` that does not mention `CredentialsMode` binds to `Static` exactly as before. No config change for existing consumers.
2. **Under `Static` the four checks execute in the same order, appending the same error strings.** They are independent `errors.Add(...)` statements with no shared local state, so wrapping them in a guard that is always true (the default) is behaviorally identical.
3. **`ValidateOnStart()` wiring is untouched** (`ServiceCollectionExtensions.cs:32` and `:47`). The validator still runs at host startup; in Dynamic mode it simply produces zero credential errors. Every reader of the four creds in `AddCoreServices` is a lazy `IOptions<>.Value` read inside a `Func`/factory lambda/`DelegatingHandler` — **nothing dereferences creds at ServiceProvider build / host start** — so relaxing the validator does not surface a hidden second startup failure.

### 5.3 Empty-credential fail-fast (decided contract, not an open question)

Relaxing the startup validator in `Dynamic` mode removes the startup-time guarantee that `UserName`/`ClientId` are non-empty. To prevent a silent `||host` cache-key collapse → cross-user token bleed (the exact failure this feature exists to prevent), the SDK **DECIDES** the following as a binding correctness property of the seam, not an implementation detail:

- **`DefaultTokenCacheKeySelector.Compose()` MUST throw `InvalidOperationException` when the resolved `UserName` or `ClientId` is null/empty** (any path that composes a cache key). A missing-ambient bug (consumer forgot to set the `AsyncLocal`, or default-`Static` with blank options under a misconfig) then surfaces **loudly** at the call site instead of colliding identities in the shared (Redis) cache.
- This makes the SDK's default key selector **defensive by construction**; the consumer is not relied upon to guarantee non-empty values.
- This promotes former Open Question 2 to a **stated acceptance criterion** (§9) and is recorded as decision **D2** (§11.1).

---

## 6. Backward-compatibility analysis

| Mechanism | Effect on existing single-account consumers |
| --- | --- |
| **Default accessor reads options** | `OptionsMisaCredentialsAccessor.Get()` returns exactly `options.ClientId/ClientKey/UserName/Password`. Header injection, login body, and cache key are unchanged for any consumer that does not register its own accessor. |
| **`TryAddSingleton` registration** | A consumer that never registers `IMisaCredentialsAccessor` always gets the options-reading default. A consumer that pre-registers one wins. No collision, no double-descriptor. |
| **`CredentialsMode` default `Static`** | Existing configs bind to `Static`; the validator still requires the four creds; the cache-key format `{UserName}|{ClientId}|{host}` is unchanged. No recompile, no reconfigure. |
| **`EnsureAccessToken` `Func` ctor preserved** | The use-case constructor signature is untouched; only the DI factory closure changes to source the `Func` from the port. Unit tests that build `EnsureAccessToken` directly are unaffected. |

### Existing tests — green/break assessment

| Test | File | Outcome | Why |
| --- | --- | --- | --- |
| `EnsureAccessTokenTests` (4 facts: cold-cache login+write; hot-cache reuse; surfaces-122-without-password-leak; expired→proactive-refresh) | `tests/…/Authentication/EnsureAccessTokenTests.cs` | **Stays green** | Build `EnsureAccessToken` with `() => ("user","pass")`; the `Func` ctor is preserved. The password-no-leak assertion is unaffected. |
| `MisaESignClientUsernameContextTests` (`BuildClient` private helper + 2 OTP-guard facts) | `tests/…/Authentication/MisaESignClientUsernameContextTests.cs` | **Stays green** | `BuildClient` constructs `EnsureAccessToken` passing the `Func<(string,string)> credentialsAccessor` (the 5-arg call site, `refreshUseCase` defaulted); the parameter is preserved, so unchanged. |
| `MisaESignOptionsValidatorTests` (15 facts incl. `ValidOptions`, `Missing_credentials_fail`, `Defaults_with_complete_inputs_pass`) | `tests/…/Configuration/MisaESignOptionsValidatorTests.cs` | **Stays green** | `ValidOptions()` leaves `CredentialsMode` unset → `Static`, so the four checks still run. `Missing_credentials_fail` (blank UserName) still fails because the default is `Static`. |
| `MisaESignOptionsDebugViewTests.Redacted_does_not_contain_the_configured_secret_value` | `tests/…/IntegrationTests/SampleApi/…` | **Stays green** | The four properties remain; redaction list unchanged. (Caveat: only extend if a new secret-bearing config key is introduced — none is.) |
| `TestServiceProvider` E2E fixture | `tests/…/IntegrationTests/EndToEnd/TestServiceProvider.cs` | **Stays green** | Configures `ClientId/ClientKey/UserName/Password`; default `Static` reads options exactly as today. Becomes the natural home for a new Dynamic-mode E2E test. |
| `DefaultTokenCacheKeySelector` | — | **No existing direct test** | Currently uncovered (tests use a `StaticKeySelector` stub returning `"k"`). New coverage is purely additive (§9). |
| `ClientHeadersHandler` | — | **No existing direct test** | Currently uncovered (only exercised indirectly via `FakeMisaESignServer`). New coverage is additive (§9). |

**Constraint to keep all of the above green:** wire `IMisaCredentialsAccessor` **only at the Infrastructure/DI seam** (the `EnsureAccessToken` factory, `DefaultTokenCacheKeySelector`, `ClientHeadersHandler`/`MisaESignWireClient`). **Preserve the `EnsureAccessToken` ctor's `Func<(string userName, string password)> credentialsAccessor` parameter unchanged** (type and position). `EnsureAccessToken` has a single ctor with an optional trailing `refreshUseCase` param, so both the 5-arg call site (`MisaESignClientUsernameContextTests`, `refreshUseCase` defaulted) and the 6-arg call site (`EnsureAccessTokenTests`, passing `RefreshAccessToken`) depend on that **`credentialsAccessor` parameter**, not on any specific arg count. Retyping the use-case ctor to take the port would break both test files.

---

## 7. Consumer-side usage example (ELIMS)

ELIMS registers its accessor **before** `AddMisaConnectESign` (TryAdd lets it win) and sets `CredentialsMode = Dynamic`, then wraps each SDK call in an `AsyncLocal` ambient — the same shape as the proven `MisaCertSelection.Use(...)`. For a **secret-bearing** credentials accessor the supported override is **register-before only** (not "or just after"); see §3.3 and the §8 docs correction.

> **Order matters (single most common integration mistake for this pattern):** register **all** overrides — `IMisaCredentialsAccessor`, `ICertificateSelector`, `ITokenCache` — **BEFORE** calling `AddMisaConnectESign`. `AddMisaConnectESign(configure)` runs `AddCoreServices` internally **after** your registrations, so each `TryAdd*` is a **no-op** for the three already-owned descriptors and your override wins. If `AddMisaConnectESign` runs **first**, its `TryAdd` defaults occupy the descriptors and your later `AddSingleton` either is silently ignored (for the `GetRequiredService` resolve path) or appends a second descriptor — for a credentials port that means an extra options-reading accessor is still constructible. Always register-before.

```csharp
// --- ambient credential selection (mirrors MisaCertSelection) ---
public static class MisaCredentialSelection
{
    private static readonly AsyncLocal<MisaCredentials?> _current = new();
    public static MisaCredentials? Current => _current.Value;

    public static IDisposable Use(MisaCredentials creds)
    {
        var prev = _current.Value;
        _current.Value = creds;
        return new Pop(prev);
    }

    private sealed class Pop(MisaCredentials? prev) : IDisposable
    {
        public void Dispose() => _current.Value = prev;
    }
}

// --- ELIMS accessor: read the per-call ambient, fall back to throw if unset ---
internal sealed class ElimsMisaCredentialsAccessor : IMisaCredentialsAccessor
{
    public MisaCredentials Get() =>
        MisaCredentialSelection.Current
        ?? throw new InvalidOperationException(
            "No MISA credentials set for the current call. Wrap the SDK call in MisaCredentialSelection.Use(...).");
}

// --- Program.cs: register BEFORE AddMisaConnectESign so TryAdd yields to us ---
services.AddSingleton<IMisaCredentialsAccessor, ElimsMisaCredentialsAccessor>();
services.AddSingleton<ICertificateSelector, ElimsCertificateSelector>();
services.AddSingleton<ITokenCache, RedisTokenCache>();
services.AddMisaConnectESign(opts =>
{
    opts.Environment = ESignEnvironment.Sandbox;
    opts.BaseUrl = baseUrl;             // only global keys remain
    opts.CredentialsMode = CredentialsMode.Dynamic;  // skip the 4 static-cred checks
    // ClientId/ClientKey/UserName/Password intentionally left empty
});

// --- provider: decrypt per-sign, push into the ambient for one awaited call ---
var creds = _pinKeyProtector.DecryptCredentials(staffId); // plaintext, scope-lived
using (MisaCredentialSelection.Use(creds))
using (MisaCertSelection.Use(desiredAlias))
{
    return await client.SignPdfAsync(dto); // accessor read per-call inside the pipeline
}
```

ELIMS must set the credentials `AsyncLocal` around **every** SDK entry it calls — not just sign: `SignPdfRemoteAsync`, `GetAccountHealthAsync`, `ListActiveCertsAsync`, `BeginRelinkAsync` (incl. its force-path `cache.RemoveAsync(keySelector.Compose())`), `SubmitOtpAsync`, `ResendOtpAsync` — so the per-user cache key is composed correctly in each. The ambient survives into the SDK's `scopeFactory.CreateScope()` child scopes (same mechanism `ElimsCorrelationIdAccessor` already relies on).

---

## 8. Test + documentation deliverables (SDK side)

### New unit tests (all additive — none break existing facts)

- `tests/…/Caching/DefaultTokenCacheKeySelectorTests.cs` (**new file — currently zero coverage**):
  - Static-mode regression lock: key == `{UserName}|{ClientId}|{host}` from options.
  - Dynamic-mode isolation: a stub `IMisaCredentialsAccessor` returning two different `(UserName, ClientId)` values yields **two different keys** (no collision).
  - **Empty fail-fast (§5.3):** a stub accessor returning empty `UserName` (and, separately, empty `ClientId`) makes `Compose()` **throw `InvalidOperationException`** — no `||host` collapse.
- `tests/…/Http/ClientHeadersHandlerTests.cs` (**new file — currently zero coverage**):
  - Static mode emits `options.ClientId`/`options.ClientKey` (regression lock).
  - Dynamic mode emits the accessor's `ClientId`/`ClientKey`.
- `tests/…/Abstractions/MisaCredentialsTests.cs` (**new file**):
  - **Secret-redaction lock (§3.1):** `new MisaCredentials("cid","SECRET_KEY","user","SECRET_PWD").ToString()` contains **neither** `"SECRET_KEY"` **nor** `"SECRET_PWD"` (asserts `<redacted>` for both `ClientKey` and `Password`), and still surfaces `UserName`/`ClientId`. Guards against the positional-record auto-`ToString()` leaking secrets via structured logging / interpolation.
- `MisaESignOptionsValidatorTests`: **add** facts for `CredentialsMode = Dynamic` — empty `ClientId/ClientKey/UserName/Password` validates **Succeed**, while `BaseUrl`/`Environment`/Polling violations still **Fail**. Existing 15 facts unchanged.
- E2E (integration): in `tests/…/EndToEnd/`, register a custom `IMisaCredentialsAccessor` (Dynamic mode) against `FakeMisaESignServer` (`TestServiceProvider.cs` is the config seam) proving the Dynamic-mode login + per-user key path end-to-end.

### CHANGELOG `[Unreleased]` entry

Add under `MisaConnect.ESign`, referencing `specs/008-esign-per-user-credentials/`:

```markdown
## [Unreleased]

`MisaConnect.ESign` — adds a per-user credentials seam so a consumer can supply
MISA credentials per-call instead of from static options. Additive, non-breaking;
default behavior (CredentialsMode=Static) is byte-identical. See `specs/008-esign-per-user-credentials/`.

#### Added

- `MisaConnect.ESign.Application.Abstractions.IMisaCredentialsAccessor` (port) and
  `MisaCredentials` (record: ClientId/ClientKey/UserName/Password). Resolves the
  current call's MISA credentials. `MisaCredentials.ToString()` is overridden to
  **redact `ClientKey` and `Password`** (the positional-record default would leak
  them). Default `OptionsMisaCredentialsAccessor` (internal, `Infrastructure/Credentials/`)
  reads `MisaESignOptions`, registered via `TryAddSingleton` so a consumer override
  wins (overrides MUST be registered **before** `AddMisaConnectESign` and MUST be
  singleton-safe / ambient-based). **Additive, non-breaking.**
- `MisaESignOptions.CredentialsMode { Static, Dynamic }` (default `Static`).
  In `Dynamic` mode the validator does not require the four static credential
  values; all other validation (BaseUrl/Environment/Polling/…) is unchanged.
  **Additive, non-breaking.**

#### Changed

- `ClientHeadersHandler`, `MisaESignWireClient` request building, the
  `EnsureAccessToken` login closure, and `DefaultTokenCacheKeySelector` now resolve
  credentials through `IMisaCredentialsAccessor` per-call. With the default accessor
  the resolved values equal the static options, so header injection, the login body,
  and the token-cache key shape `{UserName}|{ClientId}|{host}` are unchanged.

#### Notes

- The begin-sign/webhook `clientId` accessors (`BeginSign*`, `HandleWebhook`) still
  read static options; per-user `clientId` on the webhook correlation path is deferred.
```

### Spec slice + contracts (`specs/008-esign-per-user-credentials/`)

Following the `specs/006-fix-esrm-routing/` exemplar: `spec.md`, `plan.md`, `tasks.md`, and `contracts/public-surface.md`. The `public-surface.md` must carry the header `**Package**: MisaConnect.ESign · **Family version impact**: MINOR (2.1.x → 2.2.0)`, an `## Added` section with a fenced `csharp` block per new type (the `MisaCredentials` block **including its redacting `ToString()` override**) plus Binding/Default/Backward-compatibility bullets, a note that `MisaCredentials` is placed in `Application.Abstractions` (not Domain) alongside `AccessToken`, the override-ordering contract (register-before only) and accessor lifetime contract (singleton-safe / ambient, never scoped), the `CredentialsMode` Infrastructure-only invariant, and the `Compose()` empty-credential fail-fast contract (§5.3); an `## Unchanged (explicitly)` section (`IMisaESignClient`, facade methods, DTOs, error mappers, bearer selection, **and the cache-key shape in Static mode**), and a `## Semver / release obligations` section.

### README + `docs/esign/configuration.md`

- `docs/esign/configuration.md`:
  - Add a `CredentialsMode` row to the `Misa:ESign` keys table (Type `enum`, default `Static`; Static = read the four creds from options, Dynamic = supplied per-call via `IMisaCredentialsAccessor`).
  - In **Validator behaviour** (L76-81): note the `ClientId`/`ClientKey`/`UserName`/`Password` requirement applies in **Static mode only**.
  - In **Custom DI overrides** (L87-92): add `IMisaCredentialsAccessor` to the swappable-port example alongside `ITokenCache`/`ICertificateSelector`/`IOtpProvider` — **and tighten the existing "register before … OR JUST AFTER — both work because … TryAdd*" wording at L85**. The "or just after" guidance is misleading for `TryAdd`: a plain `AddSingleton` after the SDK's `TryAddSingleton` appends a **second** descriptor (`GetRequiredService` happens to resolve the last one, but `IEnumerable<T>` surfaces both and the options-default stays constructible). State explicitly that for a **credentials accessor** the supported override is **register-before only**, and make the new `IMisaCredentialsAccessor` example **register before `AddMisaConnectESign`** — matching ELIMS's actual `Program.cs` pattern. Do not add `IMisaCredentialsAccessor` to a block that still says "or just after".
  - Document the **lifetime contract** for `IMisaCredentialsAccessor` overrides (must be singleton-registrable / ambient-based, never scoped — the SDK resolves it from singleton collaborators; a scoped accessor is a captive-dependency failure). Mirror the §3.3 lifetime note.
- README parity: root `README.md` and `src/MisaConnect.ESign.Client/README.md` — mention the new port in the configuration prose/links.

### Version bump

- `src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj` `<Version>`: `2.1.1` → **`2.2.0`** (single version source; AssemblyInfo is generated). MINOR per the three new public types with a behavior-preserving default. (Precedent: slice 006 bumped to 2.1.0 for one additive option; slice 007 stayed a patch because it added no public surface — that pattern does **not** apply here.)

---

## 9. Acceptance criteria

The SDK PR must satisfy all of the following:

- [ ] `IMisaCredentialsAccessor` (public, `…Application.Abstractions`) and `MisaCredentials` record (public, same namespace) exist with the signatures in §3.
- [ ] `OptionsMisaCredentialsAccessor` (`internal sealed`, Infrastructure) reads `IOptions<MisaESignOptions>` and is registered via `services.TryAddSingleton<IMisaCredentialsAccessor, OptionsMisaCredentialsAccessor>()` in `AddCoreServices`.
- [ ] `MisaESignOptions.CredentialsMode { Static, Dynamic }` exists with `Static = 0` (default); binds automatically via the existing `.Bind(...)`.
- [ ] `ClientHeadersHandler.SendAsync` (#1) and `MisaESignWireClient.NewRequest` (#2) read `ClientId`/`ClientKey` from the accessor **per-call inside the awaited pipeline**; the `EnsureAccessToken` DI closure (#3) and `DefaultTokenCacheKeySelector.Compose` (#4) read `UserName`/`ClientId` (and password) from the accessor — all per-call, no eager/singleton snapshot.
- [ ] `MisaESignOptionsValidator` gates **only** the four cred checks on `CredentialsMode == Static`; all other checks run in both modes.
- [ ] **Default-Static path is byte-identical:** with no `IMisaCredentialsAccessor` registered and `CredentialsMode` unset, header values, login body, cache-key string, and validator errors are exactly as in 2.1.1.
- [ ] **Existing single-account E2E is unchanged** — the `FakeMisaESignServer` E2E suite passes without modification.
- [ ] All existing unit tests stay green (`EnsureAccessTokenTests` ×4, `MisaESignClientUsernameContextTests` ×2 `[Fact]` OTP-guard methods — `BuildClient` is a private helper, not a fact, `MisaESignOptionsValidatorTests` ×15, `MisaESignOptionsDebugViewTests`) — i.e. the `EnsureAccessToken` ctor's `Func<(string userName, string password)> credentialsAccessor` parameter is preserved.
- [ ] **Dynamic-mode proof test:** a test registers an `IMisaCredentialsAccessor` whose `Get()` returns **two different `MisaCredentials`** across two ambient scopes and asserts:
  - [ ] two **different logins** are issued (two distinct `UserName`/`Password` bodies to `/login-api`),
  - [ ] two **different token-cache keys** are composed (no collision — distinct `{UserName}|{ClientId}|{host}`),
  - [ ] two **different `x-clientId` headers** (and `x-clientKey`) are stamped on the outbound requests.
- [ ] **Empty-credential fail-fast (§5.3):** `DefaultTokenCacheKeySelector.Compose()` throws `InvalidOperationException` when the resolved `UserName` or `ClientId` is null/empty (no `||host` collapse), proven by a unit test.
- [ ] **`MisaCredentials.ToString()` redacts secrets:** the record overrides the auto-generated positional `ToString()` so it contains **neither** the `Password` **nor** the `ClientKey` value (emits `<redacted>` for both), proven by `MisaCredentialsTests`.
- [ ] New `DefaultTokenCacheKeySelectorTests` and `ClientHeadersHandlerTests` exist (Static regression-lock + Dynamic isolation), and `MisaCredentialsTests` exists (secret-redaction lock).
- [ ] CHANGELOG `[Unreleased]` entry, `2.2.0` version bump, `docs/esign/configuration.md` + README updates (incl. the "register-before" override-ordering correction and the accessor lifetime contract), and `specs/008-…/contracts/public-surface.md` are present.
- [ ] Both ESign suites (unit + integration) green under `TreatWarningsAsErrors`; `dotnet format` clean.
- [ ] The SDK never logs the resolved credential values (verify `ESignCallLogger`/correlation logging does not serialize `x-clientId`/`x-clientKey` header values, **and** that `MisaCredentials.ToString()` redacts `ClientKey`/`Password` so no structured-logging sink or interpolation can leak them) — Constitution Principle VIII.

---

## 10. Out of scope / deferred

- **Per-user `clientId` on the webhook / begin-sign async path.** `BeginSignPdf/Xml/Word/Excel` (`clientIdAccessor`) and `HandleWebhook` (`configuredClientIdAccessor`) keep reading `options.Value.ClientId`. These are a session-correlation tag and an anti-spoof match value, not outbound credentials. ELIMS v1 is **synchronous-poll only** and does not use webhook/begin-sign. (Deferred caveat: if per-user sessions later carry per-user `ClientId`s, the `HandleWebhook` static-global comparison would reject them and would need per-session logic.)
- **Changing the public `Sign*`/list-certs/OTP entry-point signatures** to take credentials. Rejected (§2.3) — the port + `AsyncLocal` is the chosen seam.
- **Multi-format coverage beyond what already exists.** No new document formats; the existing PDF/XML/Word/Excel surface is unchanged.
- **Retyping the `EnsureAccessToken` use-case constructor** to take the port. Explicitly avoided to keep existing tests green: the ctor's `Func<(string userName, string password)> credentialsAccessor` parameter (type and position) is **preserved unchanged** — both the 5-arg and 6-arg call sites depend on it — and the port is adapted to that `Func` at the DI seam only.
- **A new secret store / encryption mechanism.** Credential storage and per-sign decryption are an **ELIMS-side** concern (`PinKeyProtector` AES-256-GCM); the SDK only consumes plaintext via the accessor per-call and must not cache it.

---

## 11. Decisions taken in this request + remaining open questions

Three former open questions are now **decided** in the body of this request (recorded here so the maintainer sees the rationale rather than re-litigating them); the rest remain genuinely open.

### 11.1 Decided (binding contract — see referenced sections)

- **D1 — Default-adapter placement (was Q1):** `OptionsMisaCredentialsAccessor` lives in a dedicated **`Infrastructure/Credentials/`** folder + namespace `MisaConnect.ESign.Infrastructure.Credentials`, mirroring `Infrastructure/Caching/DefaultTokenCacheKeySelector` and `Infrastructure/Certificates/FirstActiveCertificateSelector` (each default adapter in a feature-named folder, **not** in `Configuration/`). The `CredentialsMode` enum stays in `Infrastructure/Configuration/` next to `ESignEnvironment` (it is an options value, not an adapter). See §3.3.
- **D2 — Empty-credential fail-fast (was Q2):** `DefaultTokenCacheKeySelector.Compose()` **MUST throw `InvalidOperationException`** when the resolved `UserName` or `ClientId` is null/empty — the SDK key selector is **defensive by construction**, so a missing-ambient bug surfaces loudly instead of collapsing to `||host` and bleeding tokens across identities. This is a stated acceptance criterion, not a consumer responsibility. See §5.3 / §9.
- **D3 — `MisaCredentials` shape, all-four (was Q4):** keep all four fields on one record so a single per-call resolution drives headers + login + key consistently. `CredentialsMode` is intentionally **coarse** (Dynamic relaxes all four creds together); a split app-dimension (`ClientId`/`ClientKey`) vs user-dimension (`UserName`/`Password`) mode is **out of scope** (§5.1). ELIMS treats all four as per-user.

### 11.2 Still open

1. **Single-call resolution guarantee:** is it acceptable for the SDK to call `Get()` multiple times per logical sign (once in the key selector, once in the login closure, once per `SendAsync`), relying on the consumer's `AsyncLocal` to return a stable value — or would you prefer the SDK resolve once per scope and cache within the scope (not the singleton)? ELIMS's `AsyncLocal` is stable for the call's duration, so multiple reads are fine for us, but please confirm the intended contract. (The §4 same-instance/no-torn-read invariant assumes a stable per-call value either way.)
2. **OTP relink consistency:** do you want the Dynamic seam wired through `ExchangeOtp`/`ResendOtp` in this slice (key selector is already shared), or deferred with the webhook path? ELIMS's primary v1 model is unattended org-account signing, so relink is lower priority — but the cache-key seam being accessor-driven (#4) already covers it as long as ELIMS sets the ambient around those calls (which §4 row 8 / §7 now require for header stamping too).
3. **Diagnostics:** does `MisaESignOptionsDebugView` need a `CredentialsMode` line, and should it assert "no static creds present in Dynamic mode" as a sanity surface? (No new secret-bearing key is introduced, so the redaction map is unchanged either way.)

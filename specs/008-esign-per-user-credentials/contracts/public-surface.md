# Contract: Public Surface Change

**Package**: `MisaConnect.ESign` · **Family version impact**: MINOR (`2.1.x` → `2.2.0`)

## Added

### `MisaCredentials` (record — Application.Abstractions)

Placed in `Application.Abstractions` (not `Domain`), alongside the existing public `AccessToken` record — it is a consumer-extension/DI contract type paired with the `IMisaCredentialsAccessor` port, not a Domain protocol/entity type.

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
    // emit ALL members in plaintext (ClientKey + Password). Redact the two
    // secret-bearing members so structured logging / interpolation cannot leak them.
    public override string ToString() =>
        $"MisaCredentials {{ UserName = {UserName}, ClientId = {ClientId}, ClientKey = <redacted>, Password = <redacted> }}";
}
```

### `IMisaCredentialsAccessor` (port — Application.Abstractions)

```csharp
namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// Resolves the MISA credentials for the CURRENT call. Implementations MUST be
/// safe to invoke per-call (the SDK calls it inside the awaited HTTP pipeline so
/// a consumer's AsyncLocal flows in) and MUST NOT cache the result on a singleton.
/// Implementations MUST be singleton-registrable and resolve from ambient
/// (AsyncLocal) or options state read inside Get(); they MUST NOT be scoped
/// (the SDK resolves the accessor from singleton collaborators). The SDK never
/// logs the returned values.
/// </summary>
public interface IMisaCredentialsAccessor
{
    MisaCredentials Get();
}
```

### `CredentialsMode` (enum — Infrastructure.Configuration) + `MisaESignOptions.CredentialsMode`

Public-in-Infrastructure by design — the same precedent as the existing public `MisaESignOptions`, `ESignEnvironment`, and `AuthUnderWebdev`. The "Infrastructure types are internal" shorthand applies to adapters/handlers, not to the bound option/enum surface a consumer sets in `Configure(...)`.

```csharp
namespace MisaConnect.ESign.Infrastructure.Configuration; // next to ESignEnvironment

public enum CredentialsMode
{
    Static = 0,   // default — read ClientId/ClientKey/UserName/Password from options
    Dynamic = 1   // credentials supplied per-call via IMisaCredentialsAccessor
}

public sealed class MisaESignOptions
{
    // … existing members …

    /// <summary>
    /// Static (default): the four credentials are read from these options and the
    /// validator requires them at startup. Dynamic: credentials are supplied
    /// per-call via IMisaCredentialsAccessor; the validator does NOT require the
    /// four static credential values (BaseUrl/Environment/Polling/... still apply).
    /// </summary>
    public CredentialsMode CredentialsMode { get; set; } = CredentialsMode.Static;
}
```

- **Binding**: `Misa:ESign:CredentialsMode` (configuration), or set in the `AddMisaConnectESign(...)` options delegate. Bound automatically via the existing `.Bind(...)`.
- **Default**: `Static` (enum zero-value) — an absent key binds to `Static`, so existing appsettings are byte-identical.
- **Default accessor**: `OptionsMisaCredentialsAccessor` (internal, `Infrastructure/Credentials/`) reads `IOptions<MisaESignOptions>`, registered via `services.TryAddSingleton<IMisaCredentialsAccessor, OptionsMisaCredentialsAccessor>()`. A consumer that pre-registers its own accessor wins.
- **Backward compatibility**: additive and optional. With no accessor registered and `CredentialsMode` unset, the resolved credentials equal the static options, so header injection, the login body, and the token-cache-key shape `{UserName}|{ClientId}|{host}` are unchanged. No breaking change.

### Behavioral contracts (load-bearing)

- **Override ordering — register-before only.** For this secret-bearing port the supported override is registering the consumer accessor **before** `AddMisaConnectESign`. A plain `AddSingleton` *after* the SDK's `TryAddSingleton` appends a second descriptor (the options-default stays constructible and `IEnumerable<IMisaCredentialsAccessor>` surfaces both). The shipped docs' "or just after" wording is corrected for this port.
- **Accessor lifetime — singleton-safe / ambient, never scoped.** The SDK resolves the accessor from singleton collaborators; a scoped accessor is a captive-dependency failure at build. `Get()` is read per-call regardless of lifetime, so an ambient-based singleton is sufficient and the only safe shape.
- **`CredentialsMode` is Infrastructure-only.** It is read only by `MisaESignOptionsValidator`. No Application type references it; the default accessor does not inspect it. Dynamic behavior comes purely from a consumer override; the mode only relaxes the startup validator.
- **`Compose()` empty-credential fail-fast.** `DefaultTokenCacheKeySelector.Compose()` throws `InvalidOperationException` when the resolved `UserName` or `ClientId` is null/empty (no `||host` collapse / cross-user token bleed).
- **Per-call resolution, no SDK caching.** The SDK reads `Get()` per-call at each consumer (login, headers, cache key) and does not cache; consistency relies on the consumer returning a stable value for the call.

## Unchanged (explicitly)

- No change to `IMisaESignClient` or any facade method signature (`SignPdfAsync`, `SignInWithOtpAsync`, `ResendOtpAsync`, `SignXmlAsync`, `SignWordAsync`, `SignExcelAsync`, `BeginSign*Async`, `HandleWebhookAsync`).
- No change to any DTO, envelope shape, field casing, or Domain type.
- No change to the error mappers / endpoint-identifier constants or to bearer selection (remote-signing access token).
- **Token-cache-key shape in Static mode is unchanged** (`{UserName}|{ClientId}|{host}`); only the *source* of the values becomes per-call.
- `EnsureAccessToken` use-case constructor (the `Func<(string userName, string password)> credentialsAccessor` parameter, type + position) is unchanged — the port is adapted to the `Func` at the DI seam only.
- The begin-sign/webhook `clientId` accessors (`BeginSign*`, `HandleWebhook`) still read static options (deferred; correlation/anti-spoof tags, not outbound credentials).

## Semver / release obligations (FR-016)

- `CHANGELOG.md` `[Unreleased]` entry under `MisaConnect.ESign`, referencing `specs/008-esign-per-user-credentials/`.
- MINOR version bump to `2.2.0` on the `MisaConnect.ESign.Client` package (`<Version>` single source; AssemblyInfo generated).
- `docs/esign/configuration.md`: add the `CredentialsMode` keys-table row; note the four-credential requirement applies in Static mode only; add `IMisaCredentialsAccessor` to the custom-DI override example with **register-before** ordering (correcting the "or just after" wording) and the singleton/ambient lifetime contract.
- README parity (repo root + `src/MisaConnect.ESign.Client/README.md`): mention the new port in the configuration prose/links.

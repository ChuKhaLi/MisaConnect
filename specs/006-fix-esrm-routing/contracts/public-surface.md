# Contract: Public Surface Change

**Package**: `MisaConnect.ESign` · **Family version impact**: MINOR (`2.0.x` → `2.1.0`)

## Added

### `MisaESignOptions.AuthUnderWebdev`

```csharp
namespace MisaConnect.ESign.Infrastructure.Configuration;

public sealed class MisaESignOptions
{
    // … existing members …

    /// <summary>
    /// Overrides where the login and two-factor endpoints are served.
    /// <c>null</c> (default) derives from <see cref="Environment"/>:
    /// Sandbox ⇒ under <c>/webdev/</c>, Production ⇒ at the host root.
    /// <c>true</c> forces <c>/webdev/</c>; <c>false</c> forces the host root.
    /// Does not affect refresh/resend (always <c>/webdev/</c>) or ESRM (always host root).
    /// </summary>
    public bool? AuthUnderWebdev { get; set; }
}
```

- **Binding**: `Misa:ESign:AuthUnderWebdev` (configuration), or set in the `AddMisaConnectESign(...)` options delegate.
- **Default**: `null`.
- **Backward compatibility**: additive and optional. Existing consumers that do not set it get behavior derived from their existing `Environment` value. No breaking change.

## Unchanged (explicitly)

- No change to `IMisaESignClient`, facade methods, DTOs, or any Domain type.
- No change to `ESignErrorMapper` / `OtpErrorMapper` endpoint-identifier constants.
- The bearer token selection (remote-signing access token) is unchanged.

## Semver / release obligations (FR-011)

- `CHANGELOG.md` `[Unreleased]` entry under `MisaConnect.ESign`.
- Minor version bump to `2.1.0` on the `MisaConnect.ESign` package.
- README (repo + Client package): clarify `BaseUrl` = host root (path tolerated) and document `AuthUnderWebdev`.

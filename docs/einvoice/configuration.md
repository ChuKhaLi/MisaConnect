# Configuration reference

`MisaConnect.EInvoice` binds the `Misa:EInvoice` section to `MisaEInvoiceOptions`. All keys are case-sensitive (the binder is, in practice, case-insensitive — but match the table for consistency with logs and validator messages).

## `Misa:EInvoice` keys

| Key | Type | Required | Description |
| --- | --- | --- | --- |
| `Environment` | enum | yes | `Sandbox` or `Production`. Drives the host-validation check on `BaseUrl`. |
| `BaseUrl` | string | yes | Absolute `https://` URL. Sandbox: `https://testapi.meinvoice.vn/api/integration`. Production: `https://api.meinvoice.vn/api/integration`. |
| `TaxCode` | string | yes | Issuer tax code (mã số thuế). |
| `UserName` | string | yes | MISA user. |
| `Password` | string | yes | MISA password. |
| `AppId` | string | yes | MISA app ID. |
| `Delete:IncludeRawErrorMessage` | bool | no | Defaults to `false`. When `true`, raw MISA error messages are surfaced via `DeleteDraftOutcome.RawErrorMessage`. Off by default to keep vendor errors out of consumer logs. |

## Recommended sources

| Source | Use for |
| --- | --- |
| `appsettings.json` | Non-secret defaults (environment, base URL). |
| User secrets (`dotnet user-secrets`) | Local development credentials. |
| Environment variables (`Misa__EInvoice__TaxCode=...`) | CI, containers, sandboxes. |
| Key Vault / Secret Manager | Production credentials. |

Never commit real credentials. Sample appsettings ship with empty placeholders.

## Validator behaviour

`MisaEInvoiceOptionsValidator` runs at startup (`ValidateOnStart`) and fails fast on:

- Missing required keys.
- `BaseUrl` not absolute `https://`.
- Host/environment mismatch (e.g. `Production` env with `testapi.meinvoice.vn` host).
- Unknown sub-keys under `Misa:EInvoice:Delete` (catches typos that would otherwise silently disable a flag).

## Custom DI overrides

Register your own adapter before `AddMisaConnectEInvoice`, or just after — both work because `AddMisaConnectEInvoice` uses `TryAdd*` semantics for swappable services. Example:

```csharp
services.AddSingleton<ITokenCache, RedisTokenCache>();
services.AddMisaConnectEInvoice(config);
```

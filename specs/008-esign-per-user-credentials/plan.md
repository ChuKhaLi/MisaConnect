# Implementation Plan: Per-User Credentials Seam for MisaConnect.ESign

**Branch**: `008-esign-per-user-credentials` | **Date**: 2026-06-15 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/008-esign-per-user-credentials/spec.md` + change request [`REQUEST.md`](./REQUEST.md)

## Summary

`MisaConnect.ESign` reads the four MISA credentials (`ClientId`, `ClientKey`, `UserName`, `Password`) from static options and uses them for every call. This slice adds a **per-call credentials seam** so a consumer can supply credentials for the current call instead — without changing any public signing method — by following the SDK's existing port-and-adapter + ambient pattern (the proven `ICertificateSelector` + cert-selection `AsyncLocal`).

**Technical approach** (from spec + clarifications):

1. **New port + record (public, Application.Abstractions).** `IMisaCredentialsAccessor.Get() : MisaCredentials` and `MisaCredentials(ClientId, ClientKey, UserName, Password)` — a positional record whose `ToString()` is overridden to redact `ClientKey` and `Password`. Same layer/rationale as the existing public `AccessToken` record and the `ICertificateSelector`/`ITokenCacheKeySelector` ports.
2. **Default adapter (internal sealed, Infrastructure/Credentials/).** `OptionsMisaCredentialsAccessor` ctor-injects `IOptions<MisaESignOptions>` and returns the four option values unconditionally (it does **not** inspect `CredentialsMode`). Registered `services.TryAddSingleton<IMisaCredentialsAccessor, OptionsMisaCredentialsAccessor>()` in `AddCoreServices`, so a consumer that pre-registers its own accessor wins.
3. **`CredentialsMode` enum (public, Infrastructure/Configuration/)** next to `ESignEnvironment`: `Static = 0` (default), `Dynamic = 1`. Bound automatically via the existing `.Bind(...)`. Added as `MisaESignOptions.CredentialsMode` (default `Static`).
4. **Per-call resolution at the four sync-path consumers** — all read `IMisaCredentialsAccessor.Get()` inside the awaited pipeline (no eager/singleton snapshot, no SDK-side caching per the clarification):
   - `ClientHeadersHandler.SendAsync` — `ClientId`/`ClientKey` headers.
   - `MisaESignWireClient.NewRequest` — authoritative `x-clientId`/`x-clientKey` stamping.
   - `EnsureAccessToken` DI closure — `(UserName, Password)` for the login body (the use-case ctor `Func` is **preserved**; only the DI seam changes).
   - `DefaultTokenCacheKeySelector.Compose` — `{UserName}|{ClientId}|{host}`, **throwing `InvalidOperationException`** when the resolved `UserName` or `ClientId` is null/empty (fail-fast, defensive by construction).
5. **Validator relaxation.** Wrap **only** the four credential checks in `if (options.CredentialsMode == CredentialsMode.Static) { … }`; every other check runs in both modes. `CredentialsMode` is read **only** by the validator — no Application type references it.
6. **OTP relink rides the same seam** (clarification: in scope for consistency) — no new public surface; the cache-key (#4) and header stamping (#1/#2) cover it once the consumer sets the ambient around `ExchangeOtp`/`ResendOtp`.
7. **Deferred:** the begin-sign/webhook `clientId` accessors keep reading static options (session-correlation/anti-spoof tags, not outbound credentials).
8. **Release:** `MisaConnect.ESign` `2.1.1` → `2.2.0` (MINOR — three new public types, behavior-preserving default), CHANGELOG `[Unreleased]`, docs (configuration.md + READMEs incl. the register-before override-ordering correction and the singleton/ambient lifetime contract), and `contracts/public-surface.md`.

## Technical Context

**Language/Version**: C# / .NET 8.0 (`Directory.Build.props`; `Nullable` enabled, `TreatWarningsAsErrors=true`)
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection` (`TryAddSingleton`), `Microsoft.Extensions.Options`, `Microsoft.Extensions.Http` (`DelegatingHandler` chain). No new package dependencies.
**Storage**: N/A (stateless SDK; token cache via existing `ITokenCache`; credentials never cached by the SDK).
**Testing**: xUnit. `tests/MisaConnect.ESign.UnitTests` (fast, no network) and `tests/MisaConnect.ESign.IntegrationTests` (in-repo `FakeMisaESignServer` + `TestServiceProvider`; `[SandboxFact]` tests skip cleanly).
**Target Platform**: Cross-platform .NET library (consumed via NuGet).
**Project Type**: Library (product family `MisaConnect.ESign`, layered Domain → Application → Infrastructure → Client).
**Performance Goals**: No new hot-path cost beyond an ambient/options read per credential consumer (O(1)); the SDK may read `Get()` multiple times per logical call (clarification: per-call reads, no caching). Unit suite stays < 30s.
**Constraints**: Default path (no accessor registered, `CredentialsMode` unset) MUST be byte-identical to 2.1.1; existing tests pass without fixture changes; `Infrastructure` stays `internal` except the public option/enum + DI entry point; no secrets in logs or `ToString()`; `EnsureAccessToken` ctor `Func<(string,string)>` parameter preserved.
**Scale/Scope**: 4 new source files + 6 edited source files in `MisaConnect.ESign` (Application + Infrastructure), 3 new unit-test files + additive validator facts + a Dynamic-mode E2E test, docs + version bump. Single-package MINOR release.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I — Layered architecture | ✅ PASS | Port + record are pure Application.Abstractions (Domain stays zero-dependency; the port has no Application→Infrastructure edge). `CredentialsMode` is read only by the Infrastructure validator — no Application type references it (avoids a forbidden Application→Infrastructure dependency). Adapter + edits live in Infrastructure. |
| II — Small, stable public surface | ✅ PASS (minor) | Three new public types on the consumer-extension surface: `IMisaCredentialsAccessor`, `MisaCredentials` (Application.Abstractions, alongside `AccessToken`), and `CredentialsMode` + `MisaESignOptions.CredentialsMode` (Infrastructure.Configuration, alongside the existing public `MisaESignOptions`/`ESignEnvironment`). CHANGELOG entry + MINOR bump required (FR-016). |
| III — Port-and-adapter | ✅ PASS | This IS the port-and-adapter pattern: new swappable collaborator (credentials accessor) as an Application interface with an Infrastructure default registered via `TryAddSingleton`. Faithful mirror of `ITokenCacheKeySelector`/`ICertificateSelector`. |
| IV — Wire format mirrors MISA verbatim | ✅ PASS | No DTO/envelope/casing/date change. The login body, `x-clientId`/`x-clientKey` headers, and cache-key shape are unchanged; only the *source* of the values becomes per-call. |
| V — Slice-driven | ✅ PASS | This slice (`008-esign-per-user-credentials`) with spec/clarify/plan/contracts/tasks. |
| VI — Tests are the spec | ✅ PASS | New unit tests (cache-key isolation + fail-fast, header per-call, secret redaction, validator Dynamic-mode facts) run without network; Dynamic-mode E2E uses the in-repo fake server (no MISA-boundary mocks); sandbox facts skip cleanly. |
| VII — Semver discipline | ✅ PASS | Additive, non-breaking ⇒ MINOR (`2.1.1` → `2.2.0`). Default behavior byte-identical; no breaking public change. |
| VIII — No secret/PII leakage | ✅ PASS | `MisaCredentials.ToString()` redacts `ClientKey`/`Password`; the SDK never logs credential values or credential header values (FR-009/FR-010). |

**Result**: No violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/008-esign-per-user-credentials/
├── REQUEST.md           # Source change request (consumer-supplied)
├── spec.md              # Feature spec (/speckit-specify + /speckit-clarify)
├── plan.md              # This file (/speckit-plan)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── public-surface.md  # Phase 1 output (public-surface change contract)
├── checklists/
│   └── requirements.md  # Spec quality checklist (passed)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/MisaConnect.ESign.Application/Abstractions/
├── MisaCredentials.cs                     # NEW — public record (4 fields) + redacting ToString()
├── IMisaCredentialsAccessor.cs            # NEW — public port: MisaCredentials Get()
└── AccessToken.cs                         # unchanged (sibling precedent for placement)

src/MisaConnect.ESign.Infrastructure/
├── Credentials/
│   └── OptionsMisaCredentialsAccessor.cs  # NEW — internal sealed default (reads IOptions<MisaESignOptions>)
├── Configuration/
│   ├── CredentialsMode.cs                 # NEW — public enum { Static=0, Dynamic=1 }
│   ├── MisaESignOptions.cs                # + CredentialsMode property (default Static)
│   └── MisaESignOptionsValidator.cs       # gate the 4 cred checks on CredentialsMode==Static
├── Http/
│   └── ClientHeadersHandler.cs            # + IMisaCredentialsAccessor ctor dep; per-call ClientId/ClientKey
├── ESign/
│   └── MisaESignWireClient.cs             # + IMisaCredentialsAccessor ctor dep; per-call x-clientId/x-clientKey in NewRequest
├── Caching/
│   └── DefaultTokenCacheKeySelector.cs    # + IMisaCredentialsAccessor ctor dep; per-call key + empty fail-fast
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs     # TryAddSingleton accessor; EnsureAccessToken closure resolves the port

src/MisaConnect.ESign.Application/UseCases/
└── EnsureAccessToken.cs                    # UNCHANGED — ctor Func<(string,string)> credentialsAccessor preserved

tests/MisaConnect.ESign.UnitTests/
├── Abstractions/MisaCredentialsTests.cs           # NEW — secret-redaction lock
├── Caching/DefaultTokenCacheKeySelectorTests.cs   # NEW — static regression + dynamic isolation + empty fail-fast
├── Http/ClientHeadersHandlerTests.cs              # NEW — static regression + dynamic header values
└── Configuration/MisaESignOptionsValidatorTests.cs # + Dynamic-mode facts (existing 15 unchanged)

tests/MisaConnect.ESign.IntegrationTests/EndToEnd/
└── (new) Dynamic-mode E2E via TestServiceProvider + FakeMisaESignServer (two signers → 2 logins/keys/header-sets)

docs/esign/configuration.md                  # CredentialsMode row; validator note; custom-DI override (register-before) + lifetime contract
README.md + src/MisaConnect.ESign.Client/README.md  # mention the new port
CHANGELOG.md                                 # [Unreleased] entry under MisaConnect.ESign
src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj  # <Version> 2.1.1 → 2.2.0
```

**Structure Decision**: Single product-family change. New consumer-extension surface in `MisaConnect.ESign.Application.Abstractions` (port + record, beside `AccessToken`); the default adapter in a dedicated `Infrastructure/Credentials/` folder (mirroring `Infrastructure/Caching/` and `Infrastructure/Certificates/`); the `CredentialsMode` enum/option in `Infrastructure/Configuration/` (an options value, not an adapter). All credential reads move behind the port at the four sync-path seams; the begin-sign/webhook `clientId` path is deferred. The `EnsureAccessToken` use-case constructor is deliberately untouched (the port is adapted to its `Func` at the DI seam) to keep existing tests green.

## Complexity Tracking

> No constitution violations — section intentionally empty.

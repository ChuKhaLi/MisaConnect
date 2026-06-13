# Implementation Plan: Fix ESRM Routing & Silent-Failure Hardening

**Branch**: `006-fix-esrm-routing` | **Date**: 2026-06-13 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/006-fix-esrm-routing/spec.md`

## Summary

The `MisaConnect.ESign` wire client composes every request as a relative URI against a single `HttpClient.BaseAddress = Misa:ESign:BaseUrl`. When a consumer's base URL carries a path (e.g. `…/webdev/`), ESRM requests resolve under `/webdev/external/esrm/…` — MISA returns the SPA (`200 text/html`), which `Deserialize<T>` silently swallows into an empty certificate list ("no active certificate"). The bearer token is already correct (Defect B refuted).

**Technical approach** (approved design):
1. **Normalize `BaseAddress` to the origin** (`scheme://host/`) at HttpClient registration, discarding any configured path. This alone fixes Defect A (ESRM resolves at root) and Defect C (refresh/resend no longer double-prefix). Path-tolerant ⇒ zero consumer config change.
2. **Centralize route resolution.** Keep `ESignHttpRoutes.*` values as canonical *endpoint identifiers* (unchanged — the error mappers match them by exact equality). Add a resolver that maps a canonical route to its *request path*, prepending `webdev/` to **login/two-factor only** when auth-under-webdev is in effect. The endpoint id passed to error mappers stays canonical, so error dispatch is untouched.
3. **`AuthUnderWebdev` option** (`bool?`, default `null`): `null` ⇒ derive from `Environment` (`Sandbox` ⇒ `/webdev/`, `Production` ⇒ root); `true`/`false` force it. New public-surface member ⇒ minor bump.
4. **Defect D guard**: on a `2xx` ESRM JSON response whose content type is not JSON (or body is unparseable), throw a clear `ESignException` (endpoint + content-type + sanitized body snippet) instead of coalescing to an empty list. Scoped to the ESRM JSON-response boundary; `Deserialize<T>`'s global swallow semantics are left intact for other callers.
5. **Defect B**: no change (verified correct).
6. Update `FakeMisaESignServer` + tests to stop masking the defects; update README + CHANGELOG; target `MisaConnect.ESign 2.1.0`.

## Technical Context

**Language/Version**: C# / .NET 8.0 (`Directory.Build.props`; `Nullable` enabled, `TreatWarningsAsErrors=true`)
**Primary Dependencies**: `Microsoft.Extensions.Http` (`IHttpClientFactory` + `DelegatingHandler` chain), `Microsoft.Extensions.Options`, `System.Text.Json`. No new package dependencies.
**Storage**: N/A (stateless SDK; token cache is in-memory via existing `ITokenCache`).
**Testing**: xUnit. `tests/MisaConnect.ESign.UnitTests` (fast, no network) and `tests/MisaConnect.ESign.IntegrationTests` (in-repo `FakeMisaESignServer` + `[SandboxFact]` sandbox tests that skip cleanly).
**Target Platform**: Cross-platform .NET library (consumed via NuGet).
**Project Type**: Library (product family `MisaConnect.ESign`, layered Domain → Application → Infrastructure → Client).
**Performance Goals**: No change to hot paths; route resolution is O(1) string work per request. Unit suite stays < 30s.
**Constraints**: No new public types beyond the one option member; `Infrastructure` stays `internal` except DI-touching types; no secrets in logs/exceptions; wire shapes unchanged.
**Scale/Scope**: ~5 source files touched in `Infrastructure`, 1 option member, error mappers untouched, plus fake-server + new tests. Single-package (`MisaConnect.ESign`) minor release.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I — Layered architecture | ✅ PASS | Change lives in Infrastructure (HttpClient wiring, wire client, route resolver, options) + the public option. No layer reaches across; Domain/Application untouched except they remain dependency-free. |
| II — Small, stable public surface | ✅ PASS (minor) | One new public member `MisaESignOptions.AuthUnderWebdev` (DI options type — an allowed public surface). Requires CHANGELOG entry + minor bump (handled in FR-011). |
| III — Port-and-adapter | ✅ PASS | No new swappable collaborator; route resolution is internal Infrastructure logic driven by existing options. |
| IV — Wire format mirrors MISA verbatim | ✅ PASS | DTOs/envelopes unchanged. Routing is corrected to match the official doc's documented URLs; no field/shape changes. |
| V — Slice-driven | ✅ PASS | This slice (`006-fix-esrm-routing`) with spec/plan/contracts/tasks. |
| VI — Tests are the spec | ✅ PASS | New unit tests (resolved-URL assertions, content-type guard) run without network; integration tests use the in-repo fake (no MISA-boundary mocks); sandbox facts skip cleanly. |
| VII — Semver discipline | ✅ PASS | Non-breaking additive option ⇒ MINOR (`2.0.x` → `2.1.0`). No breaking public change. |
| VIII — No secret/PII leakage | ✅ PASS | FR-007: the new Defect-D exception includes only endpoint + content-type + a sanitized body snippet (response body, never the `AuthorizationRM` header or credentials). |

**Result**: No violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/006-fix-esrm-routing/
├── spec.md              # Feature spec (/speckit-specify)
├── bug-report.md        # Verified, redacted source bug report (moved out of public docs)
├── plan.md              # This file (/speckit-plan)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (public-surface + routing-behavior contracts)
├── checklists/
│   └── requirements.md  # Spec quality checklist (passed)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/MisaConnect.ESign.Infrastructure/
├── Configuration/
│   ├── MisaESignOptions.cs              # + AuthUnderWebdev (bool?) public member
│   └── ESignEnvironment.cs              # unchanged (Sandbox=0, Production=1)
├── ESign/
│   ├── ESignHttpRoutes.cs               # values UNCHANGED (canonical endpoint ids); may add login/two-factor webdev-prefixed forms or a resolver consumes them
│   ├── ESignRouteResolver.cs            # NEW — canonical route → request path (prepend webdev/ for login/two-factor when applicable)
│   └── MisaESignWireClient.cs           # use resolver for request path; pass canonical id to error mappers; add ESRM 2xx content-type guard
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs   # BaseAddress = origin(BaseUrl); flow AuthUnderWebdev to wire client/resolver

src/MisaConnect.ESign.Application/Errors/
├── ESignErrorMapper.cs                  # UNCHANGED (endpoint ids stay canonical)
└── OtpErrorMapper.cs                    # UNCHANGED

tests/MisaConnect.ESign.UnitTests/
└── (new) route-resolution + content-type-guard + AuthUnderWebdev-derivation tests

tests/MisaConnect.ESign.IntegrationTests/
├── EsignFake/FakeMisaESignServer.cs     # serve ESRM only at root; 200 text/html under /webdev/external/esrm/...; assert AuthorizationRM == remoteSigningAccessToken; sandbox-vs-production login mapping
└── EndToEnd/                            # cover /webdev/-base config + Defect D + topology switch

README / CHANGELOG (repo root + Client package)  # BaseUrl=host root (path tolerated), document AuthUnderWebdev, [Unreleased] entry, 2.1.0
```

**Structure Decision**: Single product-family library change confined to `MisaConnect.ESign.Infrastructure` plus the public options type and tests. The only architectural addition is `ESignRouteResolver` (Infrastructure-internal), introduced to keep request-path composition separate from the canonical endpoint identifiers the error mappers depend on.

## Complexity Tracking

> No constitution violations — section intentionally empty.

# EInvoice TryAdd ports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** EInvoice default ports are registered with `TryAdd…`, so a consumer override works before or after `AddMisaConnectEInvoice` / `AddEInvoiceClient`; ship as 2.9.0.

**Architecture:** Change 7 registrations (6 in Infrastructure DI, 1 in Client DI) from `Add…` to `TryAdd…` with unchanged lifetimes; pin with DI unit tests; update docs and release metadata.

**Tech Stack:** .NET 8/10, Microsoft.Extensions.DependencyInjection 10.0.14 (`Microsoft.Extensions.DependencyInjection.Extensions.TryAdd*`), xunit.v3 on MTP.

**Spec:** `docs/superpowers/specs/2026-11-07-einvoice-tryadd-ports-design.md`

## Global Constraints

- Lifetimes unchanged: `ISystemClock`, `IRefIdGenerator`, `ITokenCache`, `IInvoiceValidator`, `IDeleteOptionsAccessor` singleton; `ITemplateResolver`, `ICorrelationIdAccessor` scoped.
- Keep `Add…` for: `IValidateOptions<MisaEInvoiceOptions>`, `TimeProvider.System`, use cases, `EnsureAccessToken`, handlers, typed client, `IMeInvoiceClient` decorator, `InvalidTransactionDisambiguator`.
- No public API change. No ESign / TypeScript code change.
- `TreatWarningsAsErrors=true`; `dotnet format MisaConnect.slnx` clean.
- Version 2.9.0 in both `src/dotnet/*/…Client.csproj`.

## Review Focus

1. Register-after still wins (last descriptor) — covered by the "after" test per port.
2. `AddEInvoiceClient` (Client) path, not only `AddMisaConnectEInvoice` — correlation accessor test goes through `AddEInvoiceClient`; one Infrastructure port test also goes through it.
3. Calling `AddMisaConnectEInvoice` twice does not register duplicate defaults — test: `GetServices<ITokenCache>()` count is 1.
4. Both overloads (`IConfiguration` and `Action<>`) — tests use the `Action<>` overload; one smoke test uses `IConfiguration`.
5. Defaults still resolve with no overrides — "none" test per port.

---

### Task 1: TryAdd registrations + tests

**Files:**
- Modify: `src/dotnet/MisaConnect.EInvoice.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` (AddCoreServices)
- Modify: `src/dotnet/MisaConnect.EInvoice.Client/DependencyInjection/ServiceCollectionExtensions.cs` (AddClientFacade)
- Create: `tests/dotnet/MisaConnect.EInvoice.UnitTests/Infrastructure/PortRegistrationOrderTests.cs`

**Interfaces:** Produces nothing new; public entry points unchanged.

- [ ] **Step 1: Write failing tests** in `PortRegistrationOrderTests`:
  - `[Theory]` over the 6 Infrastructure ports `Registration_before_AddMisaConnectEInvoice_wins(port)`: register a test double (singleton/scoped as the default's lifetime) **before** `AddMisaConnectEInvoice(o => …)`, build provider, resolve in a scope → `Assert.IsType<Double>`.
  - Same theory `Registration_after_AddMisaConnectEInvoice_wins`.
  - Same theory `Default_is_resolved_without_override` → `Assert.IsType<DefaultType>` (`SystemClock`, `GuidRefIdGenerator`, `InMemoryTokenCache`, `InvoiceValidator`, `TemplateResolver`, `DeleteOptionsAccessor`).
  - `Correlation_accessor_registered_before_AddEInvoiceClient_wins`, `…_after_…_wins`, `…_default_is_LibraryCorrelationIdAccessor`.
  - `Token_cache_registered_before_AddEInvoiceClient_wins` (Client path).
  - `Calling_AddMisaConnectEInvoice_twice_registers_one_token_cache` → `GetServices<ITokenCache>().Count() == 1`.
  - `IConfiguration_overload_honours_registration_before` for `ITokenCache`.
  Test doubles: tiny private classes, or NSubstitute/hand-rolled per project convention.
- [ ] **Step 2:** `dotnet test --project tests/dotnet/MisaConnect.EInvoice.UnitTests --filter "FullyQualifiedName~PortRegistrationOrderTests"` → the "before" and "twice" tests FAIL; "after"/"default" PASS.
- [ ] **Step 3:** Switch the 7 registrations to `TryAddSingleton` / `TryAddScoped` (`using Microsoft.Extensions.DependencyInjection.Extensions;`).
- [ ] **Step 4:** Same filter → all PASS; then full EInvoice unit + integration projects green.
- [ ] **Step 5:** Commit `feat(einvoice): register default ports with TryAdd so overrides work before or after`.

### Task 2: Docs + release 2.9.0

**Files:**
- Modify: `docs/architecture.md:63` and `:78` — EInvoice: "before or after `AddMisaConnectEInvoice` (defaults use `TryAdd…`)"; ESign line drops nothing but no longer contrasted ("ESign differs" removed).
- Modify: `docs/einvoice/configuration.md` "Custom DI overrides" — keep text (now true); add "since 2.9.0; in 2.8.0 and earlier only register-after works".
- Check: README / `docs/einvoice/getting-started*` for "after `AddMisaConnectEInvoice`" wording.
- Modify: both Client csproj `<Version>2.9.0</Version>`.
- Modify: `CHANGELOG.md` — new `## [2.9.0] - 2026-11-07` with summary, `### Changed` (EInvoice TryAdd, list of ports, `IEnumerable<>` note), `### Fixed` (configuration.md example now works), "ESign: no change", and the current `[Unreleased]` repo notes moved under it; `[Unreleased]` left empty.

- [ ] **Step 1:** Make the edits.
- [ ] **Step 2:** `dotnet build MisaConnect.slnx` (0 warnings), `dotnet format MisaConnect.slnx --verify-no-changes`, all four test projects green, `bash scripts/package-smoke.sh` if runnable locally.
- [ ] **Step 3:** Commit `docs+release: 2.9.0 — EInvoice TryAdd ports`; push branch, open PR.

# EInvoice: register default ports with `TryAdd…` — design (2026-11-07)

Owner asked for it on 2026-11-07 ("Làm 1 + 2 đi", then "Ra 2.9.0 luôn đi"). Follows up
`specs/009-fix-einvoice-inventory-defects/research.md` R4 (out-of-scope note).

## Why

`AddMisaConnectEInvoice` / `AddEInvoiceClient` register every default with `Add…`. A consumer
who registers their own `ITokenCache` (or another port) **before** the call is silently
overridden by the library default; only "register after" works. ESign uses `TryAdd…` for its
ports, so the two families disagree and `docs/architecture.md` has to explain the difference.
Goal: in EInvoice, either order works, as in ESign.

## Goals

1. In `MisaConnect.EInvoice.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`,
   register these defaults with `TryAdd…` (same lifetime as today):
   - `ISystemClock` → `SystemClock` (singleton)
   - `IRefIdGenerator` → `GuidRefIdGenerator` (singleton)
   - `ITokenCache` → `InMemoryTokenCache` (singleton)
   - `IInvoiceValidator` → `InvoiceValidator` (singleton)
   - `ITemplateResolver` → `TemplateResolver` (scoped)
   - `IDeleteOptionsAccessor` → `DeleteOptionsAccessor` (singleton)
2. In `MisaConnect.EInvoice.Client/DependencyInjection/ServiceCollectionExtensions.cs`,
   `ICorrelationIdAccessor` → `LibraryCorrelationIdAccessor` uses `TryAddScoped` (as ESign Client).
3. Result: a consumer's registration wins whether it is made before or after the call.
   "After" keeps working, because a later `Add…` still wins for single-service resolution.
4. Unit/integration tests prove, for each port in 1–2: a registration made before the call wins,
   one made after wins, and with none the default is resolved.
5. Docs: `docs/architecture.md` EInvoice lead sentence says "before or after" (`TryAdd…`), and the
   ESign sentence no longer says "ESign differs". Check README / getting-started pages for the
   same "after" wording and update it.
6. Release **2.9.0** for both packages (they ship in lock-step): `<Version>` 2.9.0 in both Client
   csproj files, `CHANGELOG.md` `[2.9.0]` with the EInvoice change under "Changed" and "ESign: no
   change"; the existing `[Unreleased]` repo notes move into it.

## Non-goals

- Use cases, `EnsureAccessToken`, HTTP handlers, the typed `MeInvoiceClient`, the
  `IMeInvoiceClient` decorator and `InvalidTransactionDisambiguator` keep `Add…` (ESign does the
  same for its use cases). Swapping `IMeInvoiceClient` "after" still works.
- No change to ESign code, to TypeScript, or to the public API surface.

## Calls made by the agent (owner may override)

- **`IValidateOptions<MisaEInvoiceOptions>` stays `Add…`.** Options validators are a
  multi-registration; `TryAdd` would drop the library validator whenever the app registers any
  validator of its own first.
- **`TimeProvider.System` stays `AddSingleton`**, as in ESign. It is not a documented port
  (`ISystemClock` is).
- **Version is a minor (2.9.0)**, not a patch: DI behaviour changes. Only one observable
  difference beyond "before now wins": resolving `IEnumerable<ITokenCache>` (etc.) after an
  app's earlier registration yields one entry instead of two.

- **No lifetime guard.** `TryAdd…` checks only the service type, as in ESign; a consumer's
  registration keeps whatever lifetime they gave it.
- **Tests live in `MisaConnect.EInvoice.UnitTests`** (it already sees Infrastructure and Client
  internals), go through the public `AddMisaConnectEInvoice` / `AddEInvoiceClient`, one test per
  port per case (before / after / none), no network.
- No library factory resolves a port eagerly (all are `sp => …` lambdas resolved at use), so
  "after wins" holds for every listed port. No existing test depends on duplicate descriptors.

## Found while designing

`docs/einvoice/configuration.md` ("Custom DI overrides") already claims `TryAdd*` semantics and
shows a register-before example — which does **not** work in 2.8.0. 2.9.0 makes it true; the
CHANGELOG lists it under "Fixed".

## Release

Same as earlier releases: PR → owner merges → agent tags `v2.9.0` only after an explicit go-ahead
→ owner approves `nuget-publish`. `scripts/package-smoke.sh` runs in CI/release as usual.

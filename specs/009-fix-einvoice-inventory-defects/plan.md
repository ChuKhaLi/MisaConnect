# Implementation Plan: Fix MisaConnect.EInvoice inventory defects

**Branch**: `009-fix-einvoice-inventory-defects` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/009-fix-einvoice-inventory-defects/spec.md`

## Summary

Four small, independent fixes in the `MisaConnect.EInvoice` family, each driven by a failing unit test first:

1. `MisaEInvoiceClientFactory.Create(options)` copies the nested `Delete` options too, guarded by a reflection test that every public settable option property round-trips.
2. `MeInvoiceCallLogger` drops the `customerTaxCode` structured field from all start/complete templates.
3. `MeInvoiceErrorMapper` maps `InvoiceQuantityTooLarge` / `XMLTooLong` to `BatchOrSize`; `SaveDraftInvoices` throws `BatchOrSize` for >30 invoices and `Validation`/`ValidationFailed` for an empty batch.
4. `docs/architecture.md` describes the built-in `ICorrelationIdAccessor` default.

Plus: the TypeScript drift guard and README "Differences" lose the batch-size deviation; `CHANGELOG.md` `[Unreleased]` records everything.

## Technical Context

**Language/Version**: C# latest on .NET 8 (`net8.0` test target; SDK 10 per `global.json`); TypeScript 5 for the drift-guard update
**Primary Dependencies**: Microsoft.Extensions.DependencyInjection / Options / Logging; xUnit; vitest (TS)
**Storage**: N/A
**Testing**: `dotnet test tests/dotnet/MisaConnect.EInvoice.UnitTests` (TDD for fixes 1–3); integration suite regression; `npm test -w @misaconnect/einvoice` for the drift guard
**Target Platform**: NuGet library (`MisaConnect.EInvoice`)
**Project Type**: library (layered Domain → Application → Infrastructure → Client)
**Performance Goals**: N/A (no hot-path change)
**Constraints**: `TreatWarningsAsErrors=true`; unit tests < 30 s, no network; no wire-format change
**Scale/Scope**: ~4 production files, ~6 test files, 2 docs files, 2 TS files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I — Layered architecture | ✅ | Each fix stays in its layer: factory (Client), logger (Infrastructure), mapper + use case (Application). No new references. |
| II — Small, stable public surface | ✅ | No public type/member added or removed. Observable behaviour changes (log fields, error category) get CHANGELOG entries. |
| III — Port-and-adapter | ✅ | No collaborator changes. |
| IV — Wire format verbatim | ✅ | No DTO/wire change. |
| V — Slice-driven | ✅ | This slice (spec → plan → tasks). |
| VI — Tests are the spec | ✅ | Failing unit test before each fix; integration suite re-run; no network in unit tests. |
| VII — Semver discipline | ⚠️ justified | Fix 3 changes the category integrators see for two codes and the >30 check. It is a bug fix (the dedicated category already exists and is documented), so it is allowed in a non-major release, but release notes must flag it. See research R3 for the version recommendation. |
| VIII — Logs never leak secrets/PII | ✅ (this slice fixes a violation) | Fix 2 removes a value the SDK's own scrubber classes as sensitive. |

Post-design re-check (after Phase 1): unchanged — all ✅, VII still flagged and justified.

## Project Structure

### Documentation (this feature)

```text
specs/009-fix-einvoice-inventory-defects/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── behaviour.md     # Phase 1: observable behaviour before/after
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/dotnet/
├── MisaConnect.EInvoice.Client/MisaEInvoiceClientFactory.cs           # fix 1
├── MisaConnect.EInvoice.Infrastructure/Logging/MeInvoiceCallLogger.cs # fix 2
├── MisaConnect.EInvoice.Application/Errors/MeInvoiceErrorMapper.cs    # fix 3 (map)
└── MisaConnect.EInvoice.Application/UseCases/SaveDraftInvoices.cs     # fix 3 (client-side check)

tests/dotnet/
├── MisaConnect.EInvoice.UnitTests/Configuration/ClientFactoryOptionsRoundTripTests.cs  # new (fix 1)
├── MisaConnect.EInvoice.UnitTests/Logging/CallLoggerTaxCodeTests.cs                    # new (fix 2)
├── MisaConnect.EInvoice.UnitTests/ErrorMapping/MeInvoiceErrorMapperTests.cs            # update (fix 3)
├── MisaConnect.EInvoice.UnitTests/UseCases/SaveDraftInvoicesTests.cs                   # update + new empty-batch test
└── MisaConnect.EInvoice.IntegrationTests/Api/SaveDraftEndpointTests.cs                 # regression only (still 400 + code)

src/typescript/einvoice/
├── test/unit/error-mapper.test.ts   # BATCH_OR_SIZE_DEVIATIONS → empty
└── README.md                        # drop the batch-size "Differences" bullet

docs/architecture.md                 # fix 4
CHANGELOG.md                         # [Unreleased]
```

**Structure Decision**: Existing layered .NET layout; no new projects. Fix 1's test lives in UnitTests if the factory is reachable from there (Client is referenced by UnitTests — verify in T001); otherwise in IntegrationTests/Client next to `EInvoiceClientFactoryTests`.

## Complexity Tracking

No constitution violations requiring justification beyond the Principle VII note above.

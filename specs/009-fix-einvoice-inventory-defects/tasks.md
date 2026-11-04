# Tasks: Fix MisaConnect.EInvoice inventory defects

**Input**: Design documents from `specs/009-fix-einvoice-inventory-defects/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/behaviour.md, quickstart.md

**Tests**: Required (spec: "Unit tests required for 1–3 (TDD)"). Each story writes a failing test first, then the fix.

**Organization**: One phase per user story; the four stories touch disjoint files and can be done in any order.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [x] T001 Record the baseline: run `dotnet build MisaConnect.slnx`, `dotnet test tests/dotnet/MisaConnect.EInvoice.UnitTests`, `dotnet test tests/dotnet/MisaConnect.EInvoice.IntegrationTests` and note pass/skip counts in this file under "Baseline" (expected from the handoff: Unit 315/315; Integration 69 passed / 27 skipped)

## Phase 2: Foundational

None — no shared prerequisite. (Factory test placement resolved in research R1: `MisaConnect.EInvoice.UnitTests` does not reference the Client project, so the factory test goes in `tests/dotnet/MisaConnect.EInvoice.IntegrationTests/Client/`, which does; it uses no network.)

---

## Phase 3: User Story 1 — Factory honours delete options (P1) 🎯 MVP

**Goal**: `MisaEInvoiceClientFactory.Create(options)` carries every option, including `Delete`.
**Independent Test**: reflection round-trip test passes; deleting any copy line makes it fail.

- [x] T002 [US1] Write failing test `tests/dotnet/MisaConnect.EInvoice.IntegrationTests/Client/ClientFactoryOptionsRoundTripTests.cs`: build a source `MisaEInvoiceOptions` with every public settable property (recursing into nested option classes such as `MisaEInvoiceDeleteOptions`) set to a non-default value via reflection (strings → unique text, bools → true, enums → a non-default member), call `MisaEInvoiceClientFactory.CopyTo(source, target)` on a fresh target, and assert every leaf property equal; also assert `target.Delete` is not the same reference as `source.Delete`. Add a second fact that `Create(options)` with `Delete.IncludeRawErrorMessage = true` resolves `IOptions<MisaEInvoiceOptions>` with the flag true — if `IMisaEInvoiceClient` gives no route to the provider, cover this through `CopyTo` only and note it in the test comment
- [x] T003 [US1] Fix `src/dotnet/MisaConnect.EInvoice.Client/MisaEInvoiceClientFactory.cs`: extract `internal static void CopyTo(MisaEInvoiceOptions source, MisaEInvoiceOptions target)` copying all six top-level settings plus `target.Delete = new MisaEInvoiceDeleteOptions { IncludeRawErrorMessage = source.Delete.IncludeRawErrorMessage }`; `Create(options)` calls `Create(target => CopyTo(options, target))`. Run T002 → green

**Checkpoint**: US1 complete; FR-001, FR-002, SC-001.

---

## Phase 4: User Story 2 — Tax code never logged (P1)

**Goal**: no call-log entry contains the configured tax code.
**Independent Test**: capturing logger across success / MISA error / unexpected exception sees no tax code.

- [x] T004 [P] [US2] Write failing test `tests/dotnet/MisaConnect.EInvoice.UnitTests/Logging/CallLoggerTaxCodeTests.cs` using `CapturingLogger<MeInvoiceCallLogger>` (see `tests/dotnet/MisaConnect.EInvoice.UnitTests/Delete/DeleteLogShapeTests.cs` for the setup pattern) with `TaxCode = "0109876543"`: (a) a stub inner client that succeeds, (b) one that throws `MeInvoiceException`, (c) one that throws `InvalidOperationException`; for each, with `Delete.IncludeRawErrorMessage` false and true, assert no entry's `Message` contains `0109876543` and no structured state value equals it, and that no entry has a `TaxCode` key. Exercise both decorator code paths (a delete/amendment call that takes the change-reason overload, and a plain call such as `ListTemplatesAsync`)
- [x] T005 [US2] Fix `src/dotnet/MisaConnect.EInvoice.Infrastructure/Logging/MeInvoiceCallLogger.cs`: remove `customerTaxCode={TaxCode}` and its argument from every log template (both run helpers, every branch), delete the `taxCode` locals, and drop `customerTaxCode` from the class XML doc. Run T004 → green
- [x] T006 [US2] Run the existing log-shape tests (`dotnet test tests/dotnet/MisaConnect.EInvoice.UnitTests --filter "FullyQualifiedName~LogShape"`) and confirm every other field still asserts (FR-004); fix only assertions that referenced `customerTaxCode`, if any

**Checkpoint**: US2 complete; FR-003, FR-004, SC-002.

---

## Phase 5: User Story 3 — Batch-size limits use BatchOrSize (P2)

**Goal**: MISA batch-size codes and the client-side >30 check report `BatchOrSize`; empty batch reports an accurate `Validation`.
**Independent Test**: mapper theory + SaveDraft tests.

- [ ] T007 [P] [US3] Update failing tests in `tests/dotnet/MisaConnect.EInvoice.UnitTests/ErrorMapping/MeInvoiceErrorMapperTests.cs`: change the `InvoiceQuantityTooLarge` and `XMLTooLong` `InlineData` rows to `MeInvoiceErrorCategory.BatchOrSize`, and add mixed-case rows (`xmltoolong`, `INVOICEQUANTITYTOOLARGE`) expecting `BatchOrSize` with the raw code preserved
- [ ] T008 [P] [US3] Update failing tests in `tests/dotnet/MisaConnect.EInvoice.UnitTests/UseCases/SaveDraftInvoicesTests.cs`: `Batch_size_31_rejected_before_MISA` expects `BatchOrSize` + `InvoiceQuantityTooLarge` + failure rule `FR-030`; add `Batch_size_30_is_accepted` (no throw, batch reaches the stub) and `Empty_batch_rejected_before_MISA` expecting `Validation`, `RawErrorCode == "ValidationFailed"`, failure rule `FR-030`, message containing "empty" and not "exceeds", and `stub.LastBatch` null
- [ ] T009 [US3] Fix `src/dotnet/MisaConnect.EInvoice.Application/Errors/MeInvoiceErrorMapper.cs` lines 56–57: map both codes to `MeInvoiceErrorCategory.BatchOrSize`. Run T007 → green
- [ ] T010 [US3] Fix `src/dotnet/MisaConnect.EInvoice.Application/UseCases/SaveDraftInvoices.cs`: split the `invoices.Count is < 1 or > BatchSubmission.MaxInvoices` guard into an empty-batch branch (`Validation`, raw `ValidationFailed`, message "Batch is empty; submit between 1 and {Max} invoices.") and an over-limit branch (`BatchOrSize`, raw `InvoiceQuantityTooLarge`, existing message); both keep rule id `FR-030`. Run T008 → green
- [ ] T011 [US3] Regression: run `dotnet test tests/dotnet/MisaConnect.EInvoice.IntegrationTests --filter "FullyQualifiedName~SaveDraftEndpointTests"`; `POST_api_invoices_draft_batch_31_returns_400_with_FR_030` must stay green unchanged (sample maps `BatchOrSize` to 400 via the default arm, research R3)
- [ ] T012 [P] [US3] TS sync in `src/typescript/einvoice/test/unit/error-mapper.test.ts`: make `BATCH_OR_SIZE_DEVIATIONS` empty (or remove the deviation branch and its comment); update the `EXACT_ERROR_CODES` comment in `src/typescript/einvoice/src/application/error-mapper.ts` to drop "except … spec 9.3 deviation"; run `npm test -w @misaconnect/einvoice` → drift guard green against the updated .NET mapper
- [ ] T013 [P] [US3] TS README `src/typescript/einvoice/README.md` "Differences from the .NET SDK": remove the batch-size bullet (both the >30 and empty-batch clauses now match .NET); run `npx prettier --check src/typescript/einvoice/README.md`

**Checkpoint**: US3 complete; FR-005–FR-007, FR-009, SC-003.

---

## Phase 6: User Story 4 — Correlation ID docs (P3)

- [ ] T014 [P] [US4] Edit `docs/architecture.md` lines 71 and 87: replace "(you must register)" with "`LibraryCorrelationIdAccessor` (internal; a new GUID per DI scope)" and keep the override use-case column; add one sentence under the table noting that registering your own implementation replaces the default

**Checkpoint**: US4 complete; FR-008.

---

## Phase 7: Polish & Cross-Cutting

- [ ] T015 Add `[Unreleased]` entries to `CHANGELOG.md` under `#### Fixed` for US1, US2, US4 and under a `#### Behaviour change` heading for US3 (old vs new category, empty-batch code, what integrators catching `Validation` must change), citing slice 009 (FR-010)
- [ ] T016 Update `docs/superpowers/specs/2026-11-02-typescript-sdk-design.md` §9: mark items 1–4 as fixed in .NET by slice 009 (one line each), keeping the original text
- [ ] T017 Full verification per `quickstart.md`: `dotnet build MisaConnect.slnx -c Release` (0 warnings), EInvoice unit + integration suites, ESign unit suite (regression), `npm test -w @misaconnect/einvoice`, `npm run lint`, `npm run format:check`; record results under "Baseline" for comparison
- [ ] T018 Run `dotnet format MisaConnect.slnx --verify-no-changes --include` on the changed .cs files only (whole-solution verify reports CRLF noise on Windows; CI on Linux is the gate)

---

## Dependencies & Execution Order

- T001 first.
- US1 (T002→T003), US2 (T004→T005→T006), US3 (T007/T008 → T009/T010 → T011; T012/T013 after T009), US4 (T014) are mutually independent.
- Polish (T015–T018) after all stories.

### Parallel Opportunities

- T004, T007, T008, T014 touch different files and can start together after T001.
- T012 and T013 can run in parallel once T009 lands.

## Implementation Strategy

MVP = US1 + US2 (both P1). US3 is the behaviour change and ships in the same release with the CHANGELOG callout; US4 is docs. Commit per story.

## Baseline

- T001 (2026-11-04, Debug): build 0 warnings; EInvoice.UnitTests 315/315; EInvoice.IntegrationTests 69 passed / 27 skipped.
- T017: _(pending)_

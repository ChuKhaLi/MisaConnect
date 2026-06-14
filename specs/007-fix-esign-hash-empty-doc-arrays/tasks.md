---
description: "Task list for slice 007 — fix ESRM hash/attachment empty-doc-array rejection"
---

# Tasks: Fix ESRM `documents/hash` & `documents/attachment` empty-doc-array rejection

**Input**: Design documents from `specs/007-fix-esign-hash-empty-doc-arrays/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: REQUIRED — per Constitution Principle VI ("Tests are the spec") and spec SC-001…SC-005. TDD: each story's tests are written first and MUST fail before its implementation.

**Organization**: by user story (US1 P1, US2 P2, US3 P3). The three fixes are largely independent; US2 and US3 both edit `MisaESignWireClient.cs` (different methods) — see file-conflict notes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (setup, foundational, polish carry no story label)

## Path Conventions

- Source: `src/MisaConnect.ESign.Infrastructure/ESign/…`, error mapper in `src/MisaConnect.ESign.Application/Errors/`
- Unit tests: `tests/MisaConnect.ESign.UnitTests/…`
- Integration/fake: `tests/MisaConnect.ESign.IntegrationTests/…`
- Confirm exact folders/namespaces in T002 before creating new test files.

---

## Phase 1: Setup (Shared)

**Purpose**: Establish a green baseline and confirm the layout the new tests/impl will use.

- [ ] T001 Build the solution and run the ESign suites to confirm a green baseline: `dotnet build MisaConnect.slnx`, `dotnet test tests/MisaConnect.ESign.UnitTests`, `dotnet test tests/MisaConnect.ESign.IntegrationTests` (sandbox facts skip without creds).
- [ ] T002 Confirm conventions and entry points the later phases use: the six request-build sites in `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs` (`HashPdfAsync`, `HashXmlAsync`, `HashWordOrExcelAsync`, `AttachSignatureAsync`, `AttachSignatureToXmlAsync`, `AttachSignatureToWordExcelAsync`); `ESignJsonOptions.Wire` (the serializer to use in serialization tests); the `documents/hash` + `documents/attachment` handlers and format detection in `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs`; the unit-test folders `tests/MisaConnect.ESign.UnitTests/Signing/` and `.../Errors/`, plus the logging-capture helper in `tests/MisaConnect.ESign.UnitTests/Logging/` + `TestSupport/`.

---

## Phase 2: Foundational (behavior-preserving)

**Purpose**: A small, behavior-preserving cleanup before editing this area. No cross-story prerequisite exists (the three fixes are independent), so this phase is intentionally minimal.

- [ ] T003 [P] Fix the misleading comment at `src/MisaConnect.ESign.Infrastructure/ESign/Mapping/XmlSignatureContextMapper.cs:30-32`: the `Page` omission is the global `DefaultIgnoreCondition = WhenWritingNull` in `ESignJsonOptions`, not a per-property `[JsonIgnore]`. Comment-only; no behavior change (research D4).

**Checkpoint**: Solution still builds; all existing tests still pass.

---

## Phase 3: User Story 1 - Omit unused document-type arrays (Priority: P1) 🎯 MVP

**Goal**: PDF/Word/Excel/XML `documents/hash` and `documents/attachment` requests serialize only the document-type array in use; the other three keys are absent (no empty `[]`), so MISA stops returning HTTP 400 (spec US1, FR-001/002/003).

**Independent Test**: Serialize each flow's request body with `ESignJsonOptions.Wire`; assert the matching array key is present and the other three are absent, with `certificate`/`certificateChain` still present.

### Tests for User Story 1 (write first — MUST fail) ⚠️

- [ ] T004 [P] [US1] Unit test `tests/MisaConnect.ESign.UnitTests/Signing/Wire/HashRequestSerializationTests.cs`: for each of PDF, XML, Word, Excel, serialize a `HashRequestDto` populated as the corresponding build site does, using `ESignJsonOptions.Wire`; assert the matching key (`pdfDocs`/`xmlDocs`/`wordDocs`/`excelDocs`) is present and the other three keys are **absent**, and `certificate`/`certificateChain` are present. Contracts C1/C2.
- [ ] T005 [P] [US1] Unit test `tests/MisaConnect.ESign.UnitTests/Signing/Wire/AttachmentRequestSerializationTests.cs`: same assertions for the four `AttachmentRequestDto` paths (PDF, XML, Word, Excel). Contracts C1/C2.
- [ ] T006 [P] [US1] Integration regression in `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs`: make the `documents/hash` and `documents/attachment` handlers return **HTTP 400** with a MISA-shaped body (`errorCode`, `devMsg`/`userMsg`, and `validationFailures: [{property, failureReason}]`) when any of `pdfDocs`/`xmlDocs`/`wordDocs`/`excelDocs` is **present-but-empty**; otherwise behave as today. Add/extend an `EndToEnd` test asserting `SignPdfAsync` and one non-PDF flow complete successfully. (Existing happy-path E2E sign tests will go RED here — that is the regression guard exposing the bug; US1 impl turns them green.) Spec SC-001/SC-002; this 400 body is reused by US3.

### Implementation for User Story 1

- [ ] T007 [US1] In `src/MisaConnect.ESign.Infrastructure/ESign/Wire/HashDtos.cs`, change `HashRequestDto.PdfDocs`/`XmlDocs`/`WordDocs`/`ExcelDocs` from `List<…> = new()` to `List<…>?` (remove the initializers). Leave `Certificate` and `CertificateChain` non-null. data-model §1.
- [ ] T008 [US1] In `src/MisaConnect.ESign.Infrastructure/ESign/Wire/AttachmentDtos.cs`, change `AttachmentRequestDto.PdfDocs`/`XmlDocs`/`WordDocs`/`ExcelDocs` to `List<…>?` (remove initializers); leave `Certificate`/`CertificateChain` non-null. data-model §1.
- [ ] T009 [US1] Run US1 tests + the full ESign suite; confirm red→green and no regression (the formerly-failing E2E sign tests now pass).

**Checkpoint**: All four hash and four attachment flows omit unused arrays; signing succeeds against the fake. MVP complete.

---

## Phase 4: User Story 2 - Unset `Page` defaults to 1 (logged) (Priority: P2)

**Goal**: On PDF/Word/Excel signing, a null `SignatureInfo.Page` is sent to MISA as `1` and the default is logged; an explicit `Page` is preserved; the existing `Page < 1` validation is unchanged; XML is exempt (spec US2, FR-004/005).

**Independent Test**: Build a wire `SignatureInfoDto` from a `SignatureInfo` with `Page = null` → serialized `Page == 1` and a structured log records the default; with an explicit `Page`, that value is sent and no defaulting log is emitted.

### Tests for User Story 2 (write first — MUST fail) ⚠️

- [ ] T010 [P] [US2] Unit test `tests/MisaConnect.ESign.UnitTests/Signing/Wire/SignatureInfoPageDefaultTests.cs`: exercise the PDF/Word/Excel hash build paths (via the wire client against the fake, or by invoking the mapping) with `SignatureInfo.Page = null` → serialized `Page == 1` AND a structured log entry (captured via the `Logging`/`TestSupport` logger helper) records the default; with explicit `Page = N (≥1)` → `Page == N` and no defaulting log. Contract C3.
- [ ] T011 [P] [US2] Unit test (in the same file or `tests/MisaConnect.ESign.UnitTests/Signing/SignPdfRequestValidatorTests` area) confirming an explicit `Page < 1` is still rejected client-side before any HTTP call (FR-005, guard against regression).

### Implementation for User Story 2

- [ ] T012 [US2] In `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs`, make `ToWireSignatureInfo` an **instance** method (drop `static`) so it can use `_logger`/`_correlation`; set `Page = source.Page ?? 1`; when `source.Page is null`, emit a structured log (Debug/Information, correlation-scoped, no PII) recording that `Page` defaulted to 1 for the requested format. Its call sites (`HashPdfAsync`, `HashWordOrExcelAsync` Word/Excel) are already instance methods. The XML path (`XmlSignatureContextMapper`) is unchanged. data-model §2, research D2.
- [ ] T013 [US2] Run US2 tests + the full ESign suite; confirm red→green and no regression.

**Checkpoint**: A sign with an unset `Page` succeeds and is logged; explicit `Page` preserved; XML unaffected.

---

## Phase 5: User Story 3 - Surface `validationFailures` (gated) (Priority: P3)

**Goal**: MISA's per-property `validationFailures` appear in the exception `Detail` only when `Errors.IncludeRawErrorMessage = true`; synthesized error codes/categories are unchanged; no public-surface change (spec US3, FR-006/007/008/009).

**Independent Test**: Map a 400 body carrying `validationFailures` — with the flag true the detail names each `[property] failureReason`; with the flag false none of it appears; `RawCode`/`Category` are identical in both and unchanged from today.

### Tests for User Story 3 (write first — MUST fail) ⚠️

- [ ] T014 [P] [US3] Unit test `tests/MisaConnect.ESign.UnitTests/Errors/ValidationFailuresDetailTests.cs`: given a `ResponseErrorDto`/400 body with `validationFailures` on the hash endpoint — with `IncludeRawErrorMessage = true`, the thrown `ESignGeneralException.Detail` contains each `[property] failureReason`; with `false`, it contains none of them (and no `devMsg`/`userMsg`); `RawCode` (e.g. `"e400"`) and `Category` (`HashRejected`) are identical in both cases. Absent `validationFailures` ⇒ detail unchanged. Contracts C4/C5/C6.
- [ ] T015 [P] [US3] Unit test that `ResponseErrorDto` deserializes `validationFailures` (`[{property, failureReason}]`, camelCase) from a MISA-shaped 400 body, and that entries with missing fields render defensively without throwing. data-model §3, Contract C6.

### Implementation for User Story 3

- [ ] T016 [US3] In `src/MisaConnect.ESign.Infrastructure/ESign/Wire/ResponseErrorDto.cs`, add an internal `ValidationFailureDto { [JsonPropertyName("property")] string? Property; [JsonPropertyName("failureReason")] string? FailureReason; }` and `[JsonPropertyName("validationFailures")] List<ValidationFailureDto>? ValidationFailures` on `ResponseErrorDto`. Do NOT touch the public domain `ResponseError` record. data-model §3.
- [ ] T017 [US3] In `src/MisaConnect.ESign.Application/Errors/ESignErrorMapper.cs`, add an **internal** `Map` overload taking an extra `string? validationFailuresDetail`; the existing public `Map` delegates to it with `null` (public signature preserved). Thread the value into `BuildDetail`, which appends it **only when `includeRawErrorMessage` is true** (alongside the existing `devMsg`/`userMsg` handling). The `SynthesizeHashCode*`/`SynthesizeAttachmentCode*` paths MUST NOT receive it — synthesized codes stay invariant (FR-007). Contracts C5/C7.
- [ ] T018 [US3] In `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs`, in `ThrowMappedAsync` (and `BuildAuthFailureAsync` if it can carry validationFailures), after deserializing `ResponseErrorDto`, render any `ValidationFailures` to a single sanitized string (`[<property>] <failureReason>; …`, field names + reason text only — no PII/tokens) and pass it to the internal `Map` overload. Render defensively when entries are partial. Contracts C4/C6, Principle VIII.
- [ ] T019 [US3] Run US3 tests + the full ESign suite; confirm red→green and no regression (especially existing error-mapping tests: codes/categories unchanged).

**Checkpoint**: A rejected request is diagnosable under the opt-in flag; default behavior and error codes unchanged.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T020 [P] Add a `CHANGELOG.md` `[Unreleased]` entry under `MisaConnect.ESign`: **Fixed** — (a) `documents/hash`/`documents/attachment` no longer ship empty `xmlDocs`/`wordDocs`/`excelDocs` (etc.) arrays that MISA rejected with HTTP 400; (b) an unset `SignatureInfo.Page` now defaults to `1` (logged) instead of being dropped and rejected. **Note** — MISA `validationFailures` are now included in error detail when `Errors.IncludeRawErrorMessage = true`. Mention target `2.1.1`.
- [ ] T021 Bump the `MisaConnect.ESign` package version `2.1.0 → 2.1.1` in `src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj`.
- [ ] T022 [P] Check README/consumer docs (`src/MisaConnect.ESign.Client/README.md`, `docs/`) for statements affected by the `Page`-optional behavior or `IncludeRawErrorMessage` detail; update only if a documented behavior changed (otherwise record that no doc change was needed).
- [ ] T023 Run `dotnet format MisaConnect.slnx` and the full ESign unit + integration suites; confirm green with `TreatWarningsAsErrors=true` (0 warnings).
- [ ] T024 Validate [quickstart.md](./quickstart.md) end to end (PDF-only body omits the other arrays; unset `Page` → 1; `validationFailures` gated by the flag).

---

## Dependencies & Execution Order

### Phase order
- Setup (P1) → Foundational (P2) → US1 (P3, MVP) → US2 (P4) → US3 (P5) → Polish (P6).
- **Within a story**: write tests first (red), then implement (green).

### Story dependencies
- **US1 (P1)**: depends only on Setup/Foundational. The actual blocking bug fix. **MVP.**
- **US2 (P2)**: independent of US1; touches `MisaESignWireClient.ToWireSignatureInfo`.
- **US3 (P3)**: independent of US1/US2; reuses the fake's 400-with-`validationFailures` body added in T006.

### File-conflict notes (not [P] across these)
- `MisaESignWireClient.cs`: T012 (US2, `ToWireSignatureInfo`) and T018 (US3, error path) — different methods; sequence US2 before US3 (or coordinate the edits).
- `ResponseErrorDto.cs`: T016. `ESignErrorMapper.cs`: T017. `HashDtos.cs`: T007. `AttachmentDtos.cs`: T008. `FakeMisaESignServer.cs`: T006.

## Parallel Opportunities

- T004 + T005 (US1 serialization tests, distinct files) in parallel.
- T010 + T011 (US2 tests) in parallel; T014 + T015 (US3 tests) in parallel.
- T007 + T008 (US1 impl, distinct DTO files) in parallel.
- Polish T020 + T022 in parallel.

## Parallel Example: User Story 1

```bash
# US1 tests (distinct files) together:
Task: "HashRequestSerializationTests.cs — PDF/XML/Word/Excel hash bodies omit unused arrays"
Task: "AttachmentRequestSerializationTests.cs — four attachment paths omit unused arrays"

# US1 implementation (distinct DTO files) together:
Task: "HashDtos.cs — four doc arrays → List<…>?"
Task: "AttachmentDtos.cs — four doc arrays → List<…>?"
```

## Implementation Strategy

- **MVP**: Setup → Foundational → US1 → validate signing succeeds against the fake with the strict empty-array guard. This alone unblocks remote signing.
- **Incremental**: US2 (Page default) and US3 (validationFailures) each add value independently and can land in any order after US1; sequence their `MisaESignWireClient.cs` edits to avoid conflicts.
- Commit after each task or logical group; keep the suite green at every checkpoint.

## Notes

- No public-surface change: the touched request DTOs + `ResponseErrorDto` are `internal`; the new `Map` overload is `internal` (Application→Infrastructure `InternalsVisibleTo` exists); the public `ResponseError` record and public `ESignErrorMapper.Map` signature are untouched (Contract C8/C9). Patch release `2.1.1`.
- FR-007: `validationFailures` feed only `BuildDetail`, never the code-synthesis probe — verify existing error-mapping tests stay green (T019).
- Principle VIII: rendered `validationFailures` and the Page-default log contain field names + reason strings only — never tokens, credentials, or document content.

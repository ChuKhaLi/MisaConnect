# Implementation Plan: Fix ESRM `documents/hash` & `documents/attachment` empty-doc-array rejection

**Branch**: `fix/esign-hash-empty-doc-arrays` | **Date**: 2026-06-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/007-fix-esign-hash-empty-doc-arrays/spec.md`

## Summary

A remote sign (`SignPdfAsync` / `SignWordAsync` / `SignExcelAsync` / `SignXmlAsync`) builds a
`HashRequestDto` (and later an `AttachmentRequestDto`) that populates exactly one document-type
array but leaves the other three at their non-null `new()` default. The wire profile uses
`JsonIgnoreCondition.WhenWritingNull`, which drops only `null` — never empty collections — so the
request ships `"xmlDocs":[],"wordDocs":[],"excelDocs":[]`. MISA's ESRM `documents/hash` rejects
each empty array with HTTP 400 (`Phải có ít nhất 1 tài liệu`). The same empty-list-default pattern
is structurally present on every `documents/attachment` build path. Latent before 2.1.0, exposed by
the 2.1.0 routing fix that first let these requests reach the validating service.

**Technical approach** (approved design — see [research.md](./research.md)):

1. **Omit unused arrays by making them nullable.** Change the four document-type arrays on
   `HashRequestDto` and `AttachmentRequestDto` (`pdfDocs`/`xmlDocs`/`wordDocs`/`excelDocs`) from
   `List<…> = new()` to `List<…>?` with no initializer. Each of the six build sites already assigns
   exactly the one array it uses, so `WhenWritingNull` drops the rest. Mirrors the response DTOs
   (`HashResponseDto`/`AttachmentResponseDto`), which are already `List<…>?`. `Certificate` and
   `CertificateChain` stay non-null (assigned at every site).
2. **Default `SignatureInfo.Page` to 1 (logged) when unset.** In the wire client's
   `ToWireSignatureInfo` (PDF/Word/Excel path), send `Page = source.Page ?? 1`; when the caller left
   it null, emit a structured log recording the default. The existing `Page < 1` client-side
   validation is unchanged. The XML path (`XmlSignatureContextMapper`) has no `Page` and is exempt.
3. **Surface `validationFailures` under the existing opt-in flag.** Add an internal
   `ValidationFailures` field to the internal `ResponseErrorDto`. In the wire client's error path,
   render the `{property, failureReason}` entries to a single sanitized string and pass it to a new
   **internal** `ESignErrorMapper.Map` overload (the public signature is preserved); `BuildDetail`
   appends it **only when `IncludeRawErrorMessage = true`**. The error-code synthesis path
   (`SynthesizeHashCode*`) does not receive it, so synthesized codes are unchanged.
4. **Fix the misleading comment** at `XmlSignatureContextMapper.cs:30-32` that attributes `Page`
   omission to `[JsonIgnore]`; the real mechanism is the global `WhenWritingNull`.
5. Add wire-serialization unit tests + `EsignFake` regression coverage; update `CHANGELOG`; target
   `MisaConnect.ESign 2.1.1` (patch — no public-surface change).

## Technical Context

**Language/Version**: C# / .NET 8.0 (`Directory.Build.props`; `Nullable` enabled, `TreatWarningsAsErrors=true`)
**Primary Dependencies**: `System.Text.Json` (wire serialization, `JsonIgnoreCondition.WhenWritingNull`), `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Http`. No new package dependencies.
**Storage**: N/A (stateless SDK).
**Testing**: xUnit. `tests/MisaConnect.ESign.UnitTests` (fast, no network) for wire-serialization, Page-default, and validation-detail tests; `tests/MisaConnect.ESign.IntegrationTests` (`EsignFake/FakeMisaESignServer` + `[SandboxFact]`) for the end-to-end regression.
**Target Platform**: Cross-platform .NET library (consumed via NuGet).
**Project Type**: Library (product family `MisaConnect.ESign`, layered Domain → Application → Infrastructure → Client).
**Performance Goals**: No hot-path change; serialization emits fewer keys. Unit suite stays < 30s.
**Constraints**: No public-surface change (Principle II); `ResponseError` (public Domain record) and `ESignErrorMapper`'s public signature MUST stay unchanged; synthesized error codes MUST be unchanged (FR-007); no secrets/PII in logs or detail (Principle VIII).
**Scale/Scope**: ~6 source files in `Infrastructure` + 1 internal overload in `Application`; one comment fix; new unit tests + one fake-server regression; `CHANGELOG` + Client `<Version>` bump. Single-package (`MisaConnect.ESign`) patch release.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I — Layered architecture | ✅ PASS | Changes confined to Infrastructure (wire DTOs, wire client, mapper comment) + an Application-internal `ESignErrorMapper` overload. Domain is untouched (the public `ResponseError` record is **not** modified). No layer reaches across. |
| II — Small, stable public surface | ✅ PASS (patch) | Zero public-type changes. Affected request DTOs + `ResponseErrorDto` are `internal`; the new `Map` overload is `internal` (Application→Infrastructure `InternalsVisibleTo` already exists), so the public `ESignErrorMapper.Map` signature is preserved. Only consumer-observable change is richer exception **detail text** under the existing opt-in flag. |
| III — Port-and-adapter | ✅ PASS | No new swappable collaborator. |
| IV — Wire format mirrors MISA verbatim | ✅ PASS | The fix makes the request body **match** MISA's contract (omit unused document arrays). Field casing/shapes unchanged. The new `ValidationFailures` DTO mirrors MISA's response field names (`property`/`failureReason`) verbatim. |
| V — Slice-driven | ✅ PASS | This slice (`007-fix-esign-hash-empty-doc-arrays`) with spec/plan/research/data-model/contracts/quickstart; tasks via `/speckit-tasks`. |
| VI — Tests are the spec | ✅ PASS | New unit tests (serialization, Page default, detail gating) run without network; integration regression uses the in-repo `EsignFake` (no MISA-boundary mocks); sandbox facts skip cleanly. |
| VII — Semver discipline | ✅ PASS | Bug fix with no public-surface change ⇒ PATCH (`2.1.0` → `2.1.1`). |
| VIII — No secret/PII leakage | ✅ PASS | `validationFailures` carry only MISA field names + generic reason strings, gated behind `IncludeRawErrorMessage`; the Page-default log carries no PII; no tokens or document content are logged or surfaced. |

**Result**: No violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/007-fix-esign-hash-empty-doc-arrays/
├── spec.md              # Feature spec (this slice)
├── bug-report.md        # Verified source bug report (moved out of public docs)
├── plan.md              # This file (/speckit-plan)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (wire-serialization + error-detail + public-surface)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/MisaConnect.ESign.Infrastructure/ESign/
├── Wire/
│   ├── HashDtos.cs                  # pdfDocs/xmlDocs/wordDocs/excelDocs → List<…>? (null default); Certificate/CertificateChain unchanged
│   ├── AttachmentDtos.cs            # same four arrays → List<…>? (null default)
│   └── ResponseErrorDto.cs          # + internal ValidationFailures (List<ValidationFailureDto>?) with nested {property, failureReason} DTO
├── MisaESignWireClient.cs           # ToWireSignatureInfo: Page = source.Page ?? 1 + structured log when defaulted (make instance method); ThrowMappedAsync/BuildAuthFailureAsync: render validationFailures → sanitized string → internal Map overload
└── Mapping/
    └── XmlSignatureContextMapper.cs # fix misleading [JsonIgnore] comment (lines 30-32) → reference global WhenWritingNull

src/MisaConnect.ESign.Application/Errors/
└── ESignErrorMapper.cs              # add INTERNAL Map overload (+ validationFailures string) that public Map delegates to; BuildDetail appends it only when includeRawErrorMessage; SynthesizeHashCode* path UNCHANGED

tests/MisaConnect.ESign.UnitTests/Signing/
├── (new) Wire/HashRequestSerializationTests.cs       # PDF/Xml/Word/Excel hash bodies omit unused arrays
├── (new) Wire/AttachmentRequestSerializationTests.cs # PDF/Xml/Word/Excel attachment bodies omit unused arrays
├── (new) Wire/SignatureInfoPageDefaultTests.cs       # null Page → 1 (+ log); explicit Page preserved
└── Errors/ (new) ValidationFailuresDetailTests.cs    # detail includes failures iff IncludeRawErrorMessage; codes unchanged

tests/MisaConnect.ESign.IntegrationTests/EsignFake/
└── FakeMisaESignServer.cs           # documents/hash returns 400 (validationFailures) when an unused array is present-but-empty; 200 when omitted — regression guard

CHANGELOG.md (repo root)              # [Unreleased] Fixed entries + note
src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj  # <Version> 2.1.0 → 2.1.1
```

**Structure Decision**: Single product-family patch confined to `MisaConnect.ESign.Infrastructure`
(wire DTOs + wire client + a mapper comment) plus one Application-internal `ESignErrorMapper`
overload reachable via the existing `InternalsVisibleTo`. No public type changes; no Domain change.

## Complexity Tracking

> No constitution violations — section intentionally empty.

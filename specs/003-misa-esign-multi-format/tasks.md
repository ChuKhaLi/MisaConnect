# Tasks: MISA eSign — Multi-Format Document Signing

**Branch**: `003-misa-esign-multi-format` | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)
**Input**: Design documents from `specs/003-misa-esign-multi-format/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are REQUIRED. Constitution Principle VI ("Tests are the spec") and FR-068 / FR-069 / FR-070 mandate per-format unit, integration, and sandbox coverage. The `TreatWarningsAsErrors=true` invariant from `Directory.Build.props` also applies — every new file must compile without warnings.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps task to user story for traceability (US1, US2, US3); Setup / Foundational / Polish phases have no story label
- All file paths are repository-relative

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the slice-1 + slice-2 codebase is healthy before slice-3 work begins. No new projects or dependencies — slice 3 adds files to the four production projects and two test projects established by slice 1.

- [X] T001 Confirm working tree is on `003-misa-esign-multi-format` and clean by running `git status` from repo root
- [X] T002 [P] Verify baseline build is green by running `dotnet build MisaConnect.slnx` from repo root (warnings break the build under `TreatWarningsAsErrors=true`)
- [X] T003 [P] Verify baseline unit tests pass and stay under 30s by running `dotnet test tests/MisaConnect.ESign.UnitTests/MisaConnect.ESign.UnitTests.csproj` from repo root
- [X] T004 [P] Verify baseline integration tests skip cleanly without sandbox creds by running `dotnet test tests/MisaConnect.ESign.IntegrationTests/MisaConnect.ESign.IntegrationTests.csproj` from repo root

**Checkpoint**: Slice-1 + slice-2 baseline is green. Slice-3 work can proceed without inheriting regressions.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain types, port additions, wire-DTO strengthenings, and slice-1-compatibility shims that MUST exist before any user story can be implemented. Every user story (US1/US2/US3) imports types from this phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete. FR-065 (slice-1 byte-identical PDF path) is enforced as the last gate of this phase — if the existing PDF tests don't pass after the foundational refactors, no user story work begins until they do.

### Domain — new types

- [X] T005 [P] Create `DocumentFormat` byte-backed closed enum (`Unknown = 0, Pdf = 1, Xml = 2, Word = 3, Excel = 4`) per data-model.md §1.1 in [src/MisaConnect.ESign.Domain/Documents/DocumentFormat.cs](src/MisaConnect.ESign.Domain/Documents/DocumentFormat.cs)
- [X] T006 [P] Create `XmlSignatureContext` slim record exposing only XAdES-meaningful fields (`SignatureName`, `HashAlgorithm`, `SignatureDescription`) per data-model.md §1.2 in [src/MisaConnect.ESign.Domain/Signing/XmlSignatureContext.cs](src/MisaConnect.ESign.Domain/Signing/XmlSignatureContext.cs)

### Domain — existing exceptions gain `Format` property

- [X] T007 Edit [src/MisaConnect.ESign.Domain/Errors/ESignException.cs](src/MisaConnect.ESign.Domain/Errors/ESignException.cs) to add (a) optional `DocumentFormat format = DocumentFormat.Unknown` constructor parameter at end and public `Format` property on the abstract base class, AND (b) the same forwarding constructor parameter on the nested `ESignGeneralException` sealed subclass (both classes live in this single file per [src/MisaConnect.ESign.Domain/Errors/ESignException.cs:30-41](src/MisaConnect.ESign.Domain/Errors/ESignException.cs#L30-L41)) per data-model.md §1.4
- [X] T008 [P] Edit `AuthenticationFailedException` to forward `format` to base via additive optional constructor parameter in [src/MisaConnect.ESign.Domain/Errors/AuthenticationFailedException.cs](src/MisaConnect.ESign.Domain/Errors/AuthenticationFailedException.cs)
- [X] T009 [P] Edit `ESignTransportException` to forward `format` to base in [src/MisaConnect.ESign.Domain/Errors/ESignTransportException.cs](src/MisaConnect.ESign.Domain/Errors/ESignTransportException.cs)
- [X] T010 [P] Edit `NoActiveCertificateException` to forward `format` to base in [src/MisaConnect.ESign.Domain/Errors/NoActiveCertificateException.cs](src/MisaConnect.ESign.Domain/Errors/NoActiveCertificateException.cs)
- [X] T011 [P] Edit `SignRejectedException` to forward `format` to base in [src/MisaConnect.ESign.Domain/Errors/SignRejectedException.cs](src/MisaConnect.ESign.Domain/Errors/SignRejectedException.cs)
- [X] T012 [P] Edit `SignTerminalStateException` to forward `format` to base in [src/MisaConnect.ESign.Domain/Errors/SignTerminalStateException.cs](src/MisaConnect.ESign.Domain/Errors/SignTerminalStateException.cs)
- [X] T013 [P] Edit `SignTimeoutException` to forward `format` to base in [src/MisaConnect.ESign.Domain/Errors/SignTimeoutException.cs](src/MisaConnect.ESign.Domain/Errors/SignTimeoutException.cs)
- [X] T014 (Merged into T007 — `ESignGeneralException` is a sealed subclass defined inside [src/MisaConnect.ESign.Domain/Errors/ESignException.cs](src/MisaConnect.ESign.Domain/Errors/ESignException.cs), not a separate file. T007 covers both its base-class edit and the nested subclass's forwarding constructor parameter. This task is preserved as a placeholder so downstream task IDs remain stable; no separate work is needed here.)
- [X] T015 [P] Edit `InvalidOtpException` to forward `format` to base in [src/MisaConnect.ESign.Domain/Errors/InvalidOtpException.cs](src/MisaConnect.ESign.Domain/Errors/InvalidOtpException.cs)
- [X] T016 [P] Edit `ExpiredOtpException` to forward `format` to base in [src/MisaConnect.ESign.Domain/Errors/ExpiredOtpException.cs](src/MisaConnect.ESign.Domain/Errors/ExpiredOtpException.cs)
- [X] T017 [P] Edit `ExhaustedOtpAttemptsException` to forward `format` to base in [src/MisaConnect.ESign.Domain/Errors/ExhaustedOtpAttemptsException.cs](src/MisaConnect.ESign.Domain/Errors/ExhaustedOtpAttemptsException.cs)
- [X] T018 [P] Edit `OtpRejectedException` to forward `format` to base in [src/MisaConnect.ESign.Domain/Errors/OtpRejectedException.cs](src/MisaConnect.ESign.Domain/Errors/OtpRejectedException.cs)

### Application — port and per-format hash-output records

- [X] T019 [P] Create `XmlHashOutput` record (`DocumentId`, `Document`, `SignatureId`, `Digest`, `Sh`) with a `ToSignHashInput()` projection method that returns `new SignHashInput(DocumentId, Digest)` per data-model.md §2.2 + T024 in [src/MisaConnect.ESign.Application/Abstractions/XmlHashOutput.cs](src/MisaConnect.ESign.Application/Abstractions/XmlHashOutput.cs)
- [X] T020 [P] Create `WordExcelHashOutput` record (`DocumentId`, `DocumentBytes`, `SignatureId`, `Digest`, `MainDom`) with a `ToSignHashInput()` projection method that returns `new SignHashInput(DocumentId, Digest)` per data-model.md §2.3 + T024 in [src/MisaConnect.ESign.Application/Abstractions/WordExcelHashOutput.cs](src/MisaConnect.ESign.Application/Abstractions/WordExcelHashOutput.cs)
- [X] T021 Edit `IMisaESignWireClient` to add five new methods (`HashXmlAsync`, `HashWordAsync`, `HashExcelAsync`, `AttachSignatureToXmlAsync`, `AttachSignatureToWordExcelAsync`) per data-model.md §2.1 in [src/MisaConnect.ESign.Application/Abstractions/IMisaESignWireClient.cs](src/MisaConnect.ESign.Application/Abstractions/IMisaESignWireClient.cs)

### Application — error mapper extension

- [X] T022 Edit `ESignErrorMapper.Map(...)` to add `DocumentFormat requestedFormat = DocumentFormat.Pdf` parameter, add `SynthesizeHashCodeForFormat` and `SynthesizeAttachmentCodeForFormat` helpers (synthesized codes `InvalidXmlInput`, `MissingMainDom`, `MissingSignatureId`, `UnsupportedDocumentVariant`), and forward `requestedFormat` into every typed exception construction site per data-model.md §2.11 + [contracts/error-mapping.md §A.10](./contracts/error-mapping.md#a10-per-format--documentshash-and--documentsattachment) in [src/MisaConnect.ESign.Application/Errors/ESignErrorMapper.cs](src/MisaConnect.ESign.Application/Errors/ESignErrorMapper.cs)
- [X] T023 Edit slice-1 `SignPdf` orchestrator and slice-2 `ExchangeOtp` / `ResendOtp` use cases to pass `requestedFormat: DocumentFormat.Pdf` to `ESignErrorMapper.Map(...)` (slice-2 paths reached during a facade call pass `Pdf`; standalone paths keep the default) per data-model.md §1.4 + research.md §R-5
- [X] T024 Introduce `SignHashInput(string DocumentId, string Digest)` Application record. Refactor `SubmitSignHash.ExecuteAsync(...)` and `IMisaESignWireClient.SubmitSignHashAsync(...)` to accept `SignHashInput` in place of `PdfHashOutput hash`; added `ToSignHashInput()` projection on `PdfHashOutput`, `XmlHashOutput`, `WordExcelHashOutput`; updated `SignPdf.cs` call site to convert.

### Infrastructure — wire DTO strengthening

- [X] T025 Edit `HashDtos.cs` to strengthen `HashRequestDto.{XmlDocs, WordDocs, ExcelDocs}` from `List<object>` to typed lists; add `XmlHashDocRequestDto` (XML `FileToSign` is raw text per §4.1.2), `WordHashDocRequestDto`, `ExcelHashDocRequestDto` (base64 `FileToSign` per §4.1.1); strengthen `HashResponseDto.{XmlDocs, WordDocs, ExcelDocs}` to typed lists; add `XmlHashOutputDto` (uses `document`), `WordHashOutputDto`, `ExcelHashOutputDto` (use `documentBytes` + `mainDom`) per data-model.md §3.1 / §3.2 in [src/MisaConnect.ESign.Infrastructure/ESign/Wire/HashDtos.cs](src/MisaConnect.ESign.Infrastructure/ESign/Wire/HashDtos.cs)
- [X] T026 Edit `AttachmentDtos.cs` to strengthen `AttachmentRequestDto.{XmlDocs, WordDocs, ExcelDocs}` from `List<object>` to typed lists; add `XmlAttachmentDocRequestDto` (carries `signatureId`, no `mainDom`) and shared `WordExcelAttachmentDocRequestDto` (carries both `mainDom` and `signatureId`) per MISA §4.6 `Doc_Attackment` (preserving misspelling); add `XmlAttachmentDocResponseDto`, `WordExcelAttachmentDocResponseDto` and the per-format arrays on `AttachmentResponseDto` per data-model.md §3.3 / §3.4 in [src/MisaConnect.ESign.Infrastructure/ESign/Wire/AttachmentDtos.cs](src/MisaConnect.ESign.Infrastructure/ESign/Wire/AttachmentDtos.cs)

### Infrastructure — mapping and scrubber

- [X] T027 [P] Create `XmlSignatureContextMapper.ToWireSignatureInfo(XmlSignatureContext)` that fills MISA-documented "no signature visualization" defaults (zeroed coordinates, `RenderingMode = 0`, `LogoImage = ""`, visual fields null) per FR-060 + data-model.md §3.5 in [src/MisaConnect.ESign.Infrastructure/ESign/Mapping/XmlSignatureContextMapper.cs](src/MisaConnect.ESign.Infrastructure/ESign/Mapping/XmlSignatureContextMapper.cs)
- [X] T028 [P] Edit `ESignLogScrubber` to add JSON-path matchers per data-model.md §3.7 (request: `$.xmlDocs[*].FileToSign` / `$.wordDocs[*].FileToSign` / `$.excelDocs[*].FileToSign` → `<redacted-doc>`; both stages: `$..documentBytes` / `$..document` (scoped to `/documents/*`) / `$..documentHash` / `$..digest` / `$..mainDom` / `$..sh` / `$..signatureId` / `$..signature` → `<redacted>`) per FR-063 / SC-020 in [src/MisaConnect.ESign.Infrastructure/Logging/ESignLogScrubber.cs](src/MisaConnect.ESign.Infrastructure/Logging/ESignLogScrubber.cs)

### Client — slice-1 result DTO gains `Format = Pdf`

- [X] T029 [P] Edit `SignPdfResultDto` to add `DocumentFormat Format = DocumentFormat.Pdf` property (additive; existing call sites unaffected) per public-surface.md §4.6 audit note in [src/MisaConnect.ESign.Client/Dtos/SignPdfResultDto.cs](src/MisaConnect.ESign.Client/Dtos/SignPdfResultDto.cs)

### Slice-1 PDF call sites — propagate `Format = Pdf`

- [X] T030 Edit slice-1 `MisaESignWireClient.HashPdfAsync` and `AttachSignatureAsync` and any other slice-1 wire methods to pass `requestedFormat: DocumentFormat.Pdf` to `ESignErrorMapper.Map(...)` per FR-062 + data-model.md §1.4 in [src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs](src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs)
- [X] T031 Edit slice-1 `SignPdf` orchestrator + `PollSignStatus` + `ListActiveCertificates` to thread `format: DocumentFormat.Pdf` so `SignTimeoutException`, `SignTerminalStateException`, `NoActiveCertificateException` carry it in [src/MisaConnect.ESign.Application/UseCases/SignPdf.cs](src/MisaConnect.ESign.Application/UseCases/SignPdf.cs)

### Foundational regression gate

- [X] T032 Run `dotnet build MisaConnect.slnx` and `dotnet test ... ` from repo root: 0 warnings / 0 errors, 110 unit tests pass, 17 integration + 1 skipped (sandbox-gated) — slice-1 / slice-2 byte-identical (FR-065 / SC-021 gate).

**Checkpoint**: Foundation ready — every shared type exists, slice-1 PDF behavior is unchanged on the wire, and user story implementation can begin in parallel.

---

## Phase 3: User Story 1 — Sign a non-PDF document end-to-end with a single SDK call (Priority: P1) 🎯 MVP

**Goal**: Add three format-specific facade methods (`SignXmlAsync` × 2 overloads, `SignWordAsync`, `SignExcelAsync`) that compose the correct per-format request shape on `/documents/hash`, thread the digest through `/Signing/hash → /Signing/status → /documents/attachment`, and return signed bytes in the consumer-supplied format.

**Independent Test**: Drive the in-repo fake server end-to-end for each non-PDF format with a fixture document; assert (a) the SDK populates only the matching per-format request array on `/documents/hash` and leaves the other three empty, (b) the digest is threaded through the existing signing pipeline, (c) the returned bytes are non-empty and carry the format's signature shape. Demonstrable against the sandbox via three `[SandboxFact]` tests.

### Application — per-format hash use cases (FR-053)

- [X] T033 [P] [US1] Create `HashXmlDocument` use case that calls `IMisaESignWireClient.HashXmlAsync`, asserts non-empty `Document` + `SignatureId` + `Digest` + `Sh` on the returned `XmlHashOutput`, and surfaces `ESignGeneralException(HashRejected, "IncompleteHashResponse", format: Xml)` per FR-053 + data-model.md §2.4 in [src/MisaConnect.ESign.Application/UseCases/HashXmlDocument.cs](src/MisaConnect.ESign.Application/UseCases/HashXmlDocument.cs)
- [X] T034 [P] [US1] Create `HashWordDocument` use case that calls `IMisaESignWireClient.HashWordAsync`, asserts non-empty `DocumentBytes` + `SignatureId` + `Digest` + `MainDom`, surfaces typed `IncompleteHashResponse` with `format: Word` in [src/MisaConnect.ESign.Application/UseCases/HashWordDocument.cs](src/MisaConnect.ESign.Application/UseCases/HashWordDocument.cs)
- [X] T035 [P] [US1] Create `HashExcelDocument` use case symmetric with `HashWordDocument` (`format: Excel`) in [src/MisaConnect.ESign.Application/UseCases/HashExcelDocument.cs](src/MisaConnect.ESign.Application/UseCases/HashExcelDocument.cs)

### Application — per-format attachment use cases (FR-057 / FR-058)

- [X] T036 [P] [US1] Create `AttachSignatureToXml` use case that calls `IMisaESignWireClient.AttachSignatureToXmlAsync`, extracts signed XML bytes from `$.xmlDocs[0].document`, surfaces `ESignGeneralException(AttachmentRejected, "MissingSignedDocument", format: Xml)` if the array is empty per FR-058 + data-model.md §2.5 in [src/MisaConnect.ESign.Application/UseCases/AttachSignatureToXml.cs](src/MisaConnect.ESign.Application/UseCases/AttachSignatureToXml.cs)
- [X] T037 [P] [US1] Create shared `AttachSignatureToWordExcel` use case taking `DocumentFormat format` (Word|Excel), routing to `$.wordDocs[0].document` or `$.excelDocs[0].document`, surfaces typed `MissingSignedDocument` with the requested format per FR-058 + data-model.md §2.6 in [src/MisaConnect.ESign.Application/UseCases/AttachSignatureToWordExcel.cs](src/MisaConnect.ESign.Application/UseCases/AttachSignatureToWordExcel.cs)

### Application — orchestrator work records

- [X] T038 [P] [US1] Create `SignXmlWorkRequest` record (`Xml`, `XmlSignatureContext`, `DocumentId`, `DocumentName`, `DataToBeDisplayed`) per data-model.md §2.8 in [src/MisaConnect.ESign.Application/UseCases/SignXmlWorkRequest.cs](src/MisaConnect.ESign.Application/UseCases/SignXmlWorkRequest.cs)
- [X] T039 [P] [US1] Create `SignWordWorkRequest` record (`Word`, `SignatureInfo`, `DocumentId`, `DocumentName`, `DataToBeDisplayed`) in [src/MisaConnect.ESign.Application/UseCases/SignWordWorkRequest.cs](src/MisaConnect.ESign.Application/UseCases/SignWordWorkRequest.cs)
- [X] T040 [P] [US1] Create `SignExcelWorkRequest` record symmetric with `SignWordWorkRequest` in [src/MisaConnect.ESign.Application/UseCases/SignExcelWorkRequest.cs](src/MisaConnect.ESign.Application/UseCases/SignExcelWorkRequest.cs)
- [X] T041 [P] [US1] Create `SignXmlWorkResult` record (`SignedDocument SignedXml`, `TransactionId`, `CertificateKeyAlias`, `CompletedAtUtc`, `Format = Xml`) per data-model.md §2.9 in [src/MisaConnect.ESign.Application/UseCases/SignXmlWorkResult.cs](src/MisaConnect.ESign.Application/UseCases/SignXmlWorkResult.cs)
- [X] T042 [P] [US1] Create `SignWordWorkResult` record (`Format = Word`) in [src/MisaConnect.ESign.Application/UseCases/SignWordWorkResult.cs](src/MisaConnect.ESign.Application/UseCases/SignWordWorkResult.cs)
- [X] T043 [P] [US1] Create `SignExcelWorkResult` record (`Format = Excel`) in [src/MisaConnect.ESign.Application/UseCases/SignExcelWorkResult.cs](src/MisaConnect.ESign.Application/UseCases/SignExcelWorkResult.cs)

### Application — per-format validators

- [X] T044 [P] [US1] Create `SignXmlRequestValidator` that checks non-empty `Xml`, `DocumentId` (≤ 36 chars), `DocumentName` (≤ 100 chars), `DataToBeDisplayed`, and `SignatureContext` with non-empty `SignatureName`/`SignatureDescription.{SignedBy,Location,Reason,Contact}` (NO visual-position validation) per data-model.md §2.10 in [src/MisaConnect.ESign.Application/Validation/SignXmlRequestValidator.cs](src/MisaConnect.ESign.Application/Validation/SignXmlRequestValidator.cs)
- [X] T045 [P] [US1] Create `SignWordRequestValidator` mirroring `SignPdfRequestValidator` minus the `Page >= 1` PDF-specific check in [src/MisaConnect.ESign.Application/Validation/SignWordRequestValidator.cs](src/MisaConnect.ESign.Application/Validation/SignWordRequestValidator.cs)
- [X] T046 [P] [US1] Create `SignExcelRequestValidator` symmetric with `SignWordRequestValidator` in [src/MisaConnect.ESign.Application/Validation/SignExcelRequestValidator.cs](src/MisaConnect.ESign.Application/Validation/SignExcelRequestValidator.cs)

### Application — orchestrators

- [X] T047 [US1] Create `SignXml` orchestrator threading `SignXmlRequestValidator` → `EnsureAccessToken` → `ListActiveCertificates` → `ICertificateSelector` → `HashXmlDocument` → `SubmitSignHash` → `PollSignStatus` → `AttachSignatureToXml`; inherits slice-2 `IOtpProvider` catch-block verbatim; every constructed exception carries `format: Xml` per data-model.md §2.7 + research.md §R-10 in [src/MisaConnect.ESign.Application/UseCases/SignXml.cs](src/MisaConnect.ESign.Application/UseCases/SignXml.cs)
- [X] T048 [US1] Create `SignWord` orchestrator threading the Word pipeline (`HashWordDocument` → `AttachSignatureToWordExcel(format: Word)`); same 2FA catch-block; `format: Word` on all exceptions in [src/MisaConnect.ESign.Application/UseCases/SignWord.cs](src/MisaConnect.ESign.Application/UseCases/SignWord.cs)
- [X] T049 [US1] Create `SignExcel` orchestrator threading the Excel pipeline (`HashExcelDocument` → `AttachSignatureToWordExcel(format: Excel)`); same 2FA catch-block; `format: Excel` on all exceptions in [src/MisaConnect.ESign.Application/UseCases/SignExcel.cs](src/MisaConnect.ESign.Application/UseCases/SignExcel.cs)

### Infrastructure — wire client method implementations

- [X] T050 [US1] Edit `MisaESignWireClient` to implement `HashXmlAsync` (populate `req.XmlDocs[0]` with `XmlHashDocRequestDto` carrying raw XML text in `FileToSign` per §4.1.2; deserialize `HashResponseDto.XmlDocs[0]` into `XmlHashOutput`; pass `requestedFormat: Xml` to `ESignErrorMapper.Map`) per data-model.md §3.6 in [src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs](src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs)
- [X] T051 [US1] Continuing in [src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs](src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs), implement `HashWordAsync` (populate `req.WordDocs[0]` with base64 `FileToSign` per §4.1.1; deserialize `HashResponseDto.WordDocs[0]` into `WordExcelHashOutput`; `requestedFormat: Word`)
- [X] T052 [US1] Continuing in [src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs](src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs), implement `HashExcelAsync` (symmetric with `HashWordAsync`; `excelDocs` array; `requestedFormat: Excel`)
- [X] T053 [US1] Continuing in [src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs](src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs), implement `AttachSignatureToXmlAsync` (populate `req.XmlDocs[0]` with `XmlAttachmentDocRequestDto` carrying `signatureId` per §4.6; extract `resp.XmlDocs[0].Document` as UTF-8 bytes; `requestedFormat: Xml`)
- [X] T054 [US1] Continuing in [src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs](src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs), implement `AttachSignatureToWordExcelAsync` (validate `format ∈ {Word, Excel}` defensively; populate `req.WordDocs[0]` or `req.ExcelDocs[0]` with `WordExcelAttachmentDocRequestDto` carrying both `mainDom` and `signatureId`; extract `resp.WordDocs[0].Document` or `resp.ExcelDocs[0].Document` as base64-decoded bytes; `requestedFormat: format`)

### Infrastructure — DI registration

- [X] T055 [US1] Edit `ServiceCollectionExtensions.AddMisaConnectESign(...)` to `TryAddScoped` the new use cases (`HashXmlDocument`, `HashWordDocument`, `HashExcelDocument`, `AttachSignatureToXml`, `AttachSignatureToWordExcel`, `SignXmlRequestValidator`, `SignWordRequestValidator`, `SignExcelRequestValidator`) and the three orchestrators (`SignXml`, `SignWord`, `SignExcel`) with lambdas mirroring the existing `SignPdf` registration (including optional `IOtpProvider` resolution) per data-model.md §3.8 in [src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs](src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs)

### Client — DTOs

- [X] T056 [P] [US1] Create `XmlSignatureContextDto` record (`SignatureName`, `HashAlgorithm` as string, `SignatureDescriptionDto`) per public-surface.md §4.7 in [src/MisaConnect.ESign.Client/Dtos/XmlSignatureContextDto.cs](src/MisaConnect.ESign.Client/Dtos/XmlSignatureContextDto.cs)
- [X] T057 [P] [US1] Create `SignatureDescriptionDto` record (`SignedBy`, `ShowSignedDate?`, `Location`, `Reason`, `Contact`, `DisplayText?`) if not already present from slice 1, per public-surface.md §4.8 in [src/MisaConnect.ESign.Client/Dtos/SignatureDescriptionDto.cs](src/MisaConnect.ESign.Client/Dtos/SignatureDescriptionDto.cs)
- [X] T058 [P] [US1] Create `SignXmlRequestDto` record (`Xml?`, `XmlUtf8Bytes?`, `XmlSignatureContextDto`, `DocumentName`, `DataToBeDisplayed`, `DocumentId?`) plus the static `SignXmlRequest.FromString(...)` / `FromUtf8Bytes(...)` factory helpers per public-surface.md §4.1 + data-model.md §4.4 in [src/MisaConnect.ESign.Client/Dtos/SignXmlRequestDto.cs](src/MisaConnect.ESign.Client/Dtos/SignXmlRequestDto.cs)
- [X] T059 [P] [US1] Create `SignWordRequestDto` record mirroring `SignPdfRequestDto`'s flat-field shape (per FR-059, visual position fields forwarded verbatim) per public-surface.md §4.2 in [src/MisaConnect.ESign.Client/Dtos/SignWordRequestDto.cs](src/MisaConnect.ESign.Client/Dtos/SignWordRequestDto.cs)
- [X] T060 [P] [US1] Create `SignExcelRequestDto` record symmetric with `SignWordRequestDto` in [src/MisaConnect.ESign.Client/Dtos/SignExcelRequestDto.cs](src/MisaConnect.ESign.Client/Dtos/SignExcelRequestDto.cs)
- [X] T061 [P] [US1] Create `SignXmlResultDto` record (`SignedXml`, `TransactionId`, `CertificateKeyAlias`, `CompletedAtUtc`, `Format = DocumentFormat.Xml`) per public-surface.md §4.4 in [src/MisaConnect.ESign.Client/Dtos/SignXmlResultDto.cs](src/MisaConnect.ESign.Client/Dtos/SignXmlResultDto.cs)
- [X] T062 [P] [US1] Create `SignWordResultDto` record (`Format = Word`) per public-surface.md §4.5 in [src/MisaConnect.ESign.Client/Dtos/SignWordResultDto.cs](src/MisaConnect.ESign.Client/Dtos/SignWordResultDto.cs)
- [X] T063 [P] [US1] Create `SignExcelResultDto` record (`Format = Excel`) per public-surface.md §4.6 in [src/MisaConnect.ESign.Client/Dtos/SignExcelResultDto.cs](src/MisaConnect.ESign.Client/Dtos/SignExcelResultDto.cs)

### Client — mappers

- [X] T064 [P] [US1] Create `SignXmlRequestMapper.ToWorkRequest(...)` that enforces "exactly one of (`Xml`, `XmlUtf8Bytes`) non-null", UTF-8-decodes `XmlUtf8Bytes` if present (no BOM stripping per Assumption 6), parses `HashAlgorithm` string (default `SHA256`), generates a `DocumentId` if absent, builds `XmlSignatureContext` + `SignXmlWorkRequest` per data-model.md §4.7 in [src/MisaConnect.ESign.Client/Mapping/SignXmlRequestMapper.cs](src/MisaConnect.ESign.Client/Mapping/SignXmlRequestMapper.cs)
- [X] T065 [P] [US1] Create `SignWordRequestMapper.ToWorkRequest(...)` mirroring `SignPdfRequestMapper` (compose slice-1 `SignatureInfo` from flat fields) in [src/MisaConnect.ESign.Client/Mapping/SignWordRequestMapper.cs](src/MisaConnect.ESign.Client/Mapping/SignWordRequestMapper.cs)
- [X] T066 [P] [US1] Create `SignExcelRequestMapper.ToWorkRequest(...)` symmetric with `SignWordRequestMapper` in [src/MisaConnect.ESign.Client/Mapping/SignExcelRequestMapper.cs](src/MisaConnect.ESign.Client/Mapping/SignExcelRequestMapper.cs)

### Client — facade methods

- [X] T067 [US1] Edit `IMisaESignClient` to add three new methods (`SignXmlAsync`, `SignWordAsync`, `SignExcelAsync`) with the documented `<exception>` XML doc comments per public-surface.md §1 in [src/MisaConnect.ESign.Client/IMisaESignClient.cs](src/MisaConnect.ESign.Client/IMisaESignClient.cs)
- [X] T068 [US1] Edit `MisaESignClient` to implement `SignXmlAsync` (resolve `SignXml` orchestrator, map DTO → work request via `SignXmlRequestMapper`, execute, map result → `SignXmlResultDto`, preserve slice-2 `AsyncLocal<string?>` 2FA-captured-userName flow) per data-model.md §4.2 in [src/MisaConnect.ESign.Client/MisaESignClient.cs](src/MisaConnect.ESign.Client/MisaESignClient.cs)
- [X] T069 [US1] Continuing in [src/MisaConnect.ESign.Client/MisaESignClient.cs](src/MisaConnect.ESign.Client/MisaESignClient.cs), implement `SignWordAsync` using `SignWord` orchestrator + `SignWordRequestMapper` + `SignWordResultDto` mapping
- [X] T070 [US1] Continuing in [src/MisaConnect.ESign.Client/MisaESignClient.cs](src/MisaConnect.ESign.Client/MisaESignClient.cs), implement `SignExcelAsync` symmetric with `SignWordAsync`

### Unit tests — XML signing

- [X] T071 [P] [US1] Edit `StubWireClient` test support to add in-memory recording + scriptable response handlers for `HashXmlAsync`, `HashWordAsync`, `HashExcelAsync`, `AttachSignatureToXmlAsync`, `AttachSignatureToWordExcelAsync` per data-model.md §4.7 audit in [tests/MisaConnect.ESign.UnitTests/TestSupport/StubWireClient.cs](tests/MisaConnect.ESign.UnitTests/TestSupport/StubWireClient.cs)
- [X] T072 [P] [US1] Create `HashXmlDocumentTests` covering per-format DTO shaping (`xmlDocs` populated, others empty per FR-052 / SC-017), `FileToSign` carries raw text per §4.1.2, response parsing extracts `XmlHashOutput` per §4.15, `IncompleteHashResponse` synthesis on missing required fields (FR-053) in [tests/MisaConnect.ESign.UnitTests/Signing/Xml/HashXmlDocumentTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Xml/HashXmlDocumentTests.cs)
- [X] T073 [P] [US1] Create `AttachSignatureToXmlTests` covering `Doc_Attackment` shaping per §4.6 with `signatureId` populated, signed-XML extraction from `$.xmlDocs[0].document` per FR-057 in [tests/MisaConnect.ESign.UnitTests/Signing/Xml/AttachSignatureToXmlTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Xml/AttachSignatureToXmlTests.cs)
- [X] T074 [P] [US1] Create `SignXmlOrchestratorTests` covering full slice-3 pipeline against `StubWireClient` for both overloads (`string` + `byte[]` UTF-8), validates `XmlSignatureContext`-to-wire mapping fills MISA defaults per FR-060 in [tests/MisaConnect.ESign.UnitTests/Signing/Xml/SignXmlOrchestratorTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Xml/SignXmlOrchestratorTests.cs)
- [X] T075 [P] [US1] Create `SignXmlRequestValidatorTests` covering all validator rules (non-empty XML, non-empty DocumentName/DocumentId, `SignatureContext.SignatureName` required) in [tests/MisaConnect.ESign.UnitTests/Signing/Xml/SignXmlRequestValidatorTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Xml/SignXmlRequestValidatorTests.cs)
- [X] T076 [P] [US1] Create `SignXmlRequestMapperTests` covering exactly-one-of(Xml, XmlUtf8Bytes) enforcement, UTF-8 decode behavior, default `HashAlgorithm = SHA256`, default `DocumentId` generation in [tests/MisaConnect.ESign.UnitTests/Signing/Xml/SignXmlRequestMapperTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Xml/SignXmlRequestMapperTests.cs)
- [X] T077 [P] [US1] Create `XmlSignatureContextMapperTests` asserting MISA-default-filling per FR-060 (`RenderingMode = 0`, `LogoImage = ""`, visual fields omitted from JSON) in [tests/MisaConnect.ESign.UnitTests/Signing/Xml/XmlSignatureContextMapperTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Xml/XmlSignatureContextMapperTests.cs)

### Unit tests — Word signing

- [X] T078 [P] [US1] Create `HashWordDocumentTests` covering `wordDocs` populated/others empty (SC-017), response extraction (`documentBytes` + `mainDom` + `signatureId` + `digest`), `IncompleteHashResponse` on missing `mainDom` or `signatureId` in [tests/MisaConnect.ESign.UnitTests/Signing/Word/HashWordDocumentTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Word/HashWordDocumentTests.cs)
- [X] T079 [P] [US1] Create `AttachSignatureToWordTests` covering `Doc_Attackment` per §4.6 with both `mainDom` and `signatureId` populated, signed-bytes extraction from `$.wordDocs[0].document` per FR-057 in [tests/MisaConnect.ESign.UnitTests/Signing/Word/AttachSignatureToWordTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Word/AttachSignatureToWordTests.cs)
- [X] T080 [P] [US1] Create `SignWordOrchestratorTests` covering the full pipeline with `StubWireClient`, asserts `SignatureInfo` verbatim forwarding (positions included) per FR-059 in [tests/MisaConnect.ESign.UnitTests/Signing/Word/SignWordOrchestratorTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Word/SignWordOrchestratorTests.cs)
- [X] T081 [P] [US1] Create `SignWordRequestValidatorTests` covering all rules (no `Page >= 1` check; positions allowed) in [tests/MisaConnect.ESign.UnitTests/Signing/Word/SignWordRequestValidatorTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Word/SignWordRequestValidatorTests.cs)

### Unit tests — Excel signing

- [X] T082 [P] [US1] Create `HashExcelDocumentTests` symmetric with `HashWordDocumentTests` but `excelDocs` in [tests/MisaConnect.ESign.UnitTests/Signing/Excel/HashExcelDocumentTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Excel/HashExcelDocumentTests.cs)
- [X] T083 [P] [US1] Create `AttachSignatureToExcelTests` symmetric with `AttachSignatureToWordTests` but `excelDocs` in [tests/MisaConnect.ESign.UnitTests/Signing/Excel/AttachSignatureToExcelTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Excel/AttachSignatureToExcelTests.cs)
- [X] T084 [P] [US1] Create `SignExcelOrchestratorTests` symmetric with `SignWordOrchestratorTests` in [tests/MisaConnect.ESign.UnitTests/Signing/Excel/SignExcelOrchestratorTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Excel/SignExcelOrchestratorTests.cs)
- [X] T085 [P] [US1] Create `SignExcelRequestValidatorTests` symmetric with `SignWordRequestValidatorTests` in [tests/MisaConnect.ESign.UnitTests/Signing/Excel/SignExcelRequestValidatorTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/Excel/SignExcelRequestValidatorTests.cs)

### Integration tests — fake server extension + happy paths

- [X] T086 [US1] Edit `FakeMisaESignServer.cs` to extend `/documents/hash` route handler to inspect populated per-format request array (`xmlDocs[0]` / `wordDocs[0]` / `excelDocs[0]`) and compose a matching response with `§4.15` fields (XML uses `document` + `signatureId` + `digest` + `sh`; Word/Excel use `documentBytes` + `signatureId` + `digest` + `mainDom`); slice-1 PDF path stays byte-identical per data-model.md §3.6 + research.md §R-9 in [tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs](tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs)
- [X] T087 [US1] Continuing in [tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs](tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs), extend `/documents/attachment` route handler to inspect populated per-format request array, populate matching per-format response array with signed bytes (XML response carries text in `document`; Word/Excel responses carry base64 in `document`); slice-1 PDF path stays byte-identical
- [X] T088 [P] [US1] Create `SignXmlHappyPathFakeServerTests` driving the fake end-to-end for XML signing, asserts returned bytes non-empty and carry the source XML threaded through MISA's response shape per FR-053 + FR-057 in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/SignXmlHappyPathFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/SignXmlHappyPathFakeServerTests.cs)
- [X] T089 [P] [US1] Create `SignWordHappyPathFakeServerTests` symmetric with XML, asserts response opens as OOXML in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/SignWordHappyPathFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/SignWordHappyPathFakeServerTests.cs)
- [X] T090 [P] [US1] Create `SignExcelHappyPathFakeServerTests` symmetric with Word in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/SignExcelHappyPathFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/SignExcelHappyPathFakeServerTests.cs)
- [X] T091 [P] [US1] Create `TokenCacheReuseAcrossFormatsFakeServerTests` exercising a sequence of four facade calls (Pdf → Xml → Word → Excel) within one cached token lifetime; asserts exactly one `/login-api` and zero `/Certificates/by-userId` between calls per SC-016 in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/TokenCacheReuseAcrossFormatsFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/TokenCacheReuseAcrossFormatsFakeServerTests.cs)

### Sandbox tests (FR-069 / SC-015 / SC-023)

- [X] T092 [P] [US1] Create `SignXmlSandboxTests` with one `[SandboxFact]` gated on `MISACONNECT_ESIGN_SANDBOX_*`; signs a fixture XML document and asserts the returned bytes carry an embedded XAdES signature; skips cleanly when sandbox creds are absent and fails loudly when credentials are rejected per SC-023 in [tests/MisaConnect.ESign.IntegrationTests/Sandbox/SignXmlSandboxTests.cs](tests/MisaConnect.ESign.IntegrationTests/Sandbox/SignXmlSandboxTests.cs)
- [X] T093 [P] [US1] Create `SignWordSandboxTests` with `[SandboxFact]` signing a fixture Word document and asserting it opens with a valid digital signature in [tests/MisaConnect.ESign.IntegrationTests/Sandbox/SignWordSandboxTests.cs](tests/MisaConnect.ESign.IntegrationTests/Sandbox/SignWordSandboxTests.cs)
- [X] T094 [P] [US1] Create `SignExcelSandboxTests` with `[SandboxFact]` signing a fixture Excel document and asserting it opens with a valid digital signature in [tests/MisaConnect.ESign.IntegrationTests/Sandbox/SignExcelSandboxTests.cs](tests/MisaConnect.ESign.IntegrationTests/Sandbox/SignExcelSandboxTests.cs)
- [X] T095 [P] [US1] Commit fixture files (one minimal XML, one minimal Word `.docx`, one minimal Excel `.xlsx`) under [tests/MisaConnect.ESign.IntegrationTests/Sandbox/Fixtures/](tests/MisaConnect.ESign.IntegrationTests/Sandbox/Fixtures/) for the three sandbox tests + the fake-server happy paths to consume

**Checkpoint**: User Story 1 is fully functional and independently testable. A consumer can call `SignXmlAsync` / `SignWordAsync` / `SignExcelAsync` end-to-end against the fake server and (with sandbox creds) against the MISA sandbox. SC-015, SC-016, SC-017 are demonstrable.

---

## Phase 4: User Story 2 — Return signed bytes in the original format without consumer-side dispatch (Priority: P2)

**Goal**: Confirm and lock in that the SDK selects the correct per-format response array based on the called facade, ignores noise in the other three format arrays, and surfaces a typed "missing signed document" error if the requested array is empty.

**Independent Test**: Drive the fake server to (a) populate multiple per-format response arrays at once and assert each facade returns only its matching array's payload, (b) leave the requested format's array empty on a 2xx response and assert the matching facade raises the typed `MissingSignedDocument` error with the correct `Format` value.

> US1's wire-client implementation (T053–T054) already extracts only the matching per-format array per FR-057, and the application use cases (T036–T037) already raise `MissingSignedDocument` per FR-058. US2 phase consists of the test deliverables that pin and validate that behavior so the contract is enforced going forward.

### Unit tests — per-format response dispatch

- [ ] T096 [P] [US2] Create `PerFormatResponseDispatchTests` that, given a `/documents/attachment` response populating multiple format arrays simultaneously (per US2 acceptance scenario 2), asserts `SignXmlAsync` returns only `$.xmlDocs[0]`, `SignWordAsync` returns only `$.wordDocs[0]`, `SignExcelAsync` returns only `$.excelDocs[0]`, and `SignPdfAsync` returns only `$.pdfDocs[0]`; verifies SC-018 in [tests/MisaConnect.ESign.UnitTests/Signing/PerFormatResponseDispatchTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/PerFormatResponseDispatchTests.cs)
- [ ] T097 [P] [US2] Create `MissingFormatArrayUnitTests` that, given a successful HTTP response with the requested-format array empty/missing, asserts the matching use case raises `ESignGeneralException(AttachmentRejected, "MissingSignedDocument", format: …)` carrying the correlation ID per FR-058 (one `[Theory]` row per format) in [tests/MisaConnect.ESign.UnitTests/Signing/MissingFormatArrayUnitTests.cs](tests/MisaConnect.ESign.UnitTests/Signing/MissingFormatArrayUnitTests.cs)

### Integration tests — multi-array dispatch + missing-array fail mode

- [ ] T098 [US2] Edit `FakeMisaESignServer.cs` to add a scripted mode that emits `/documents/attachment` responses with multiple per-format arrays populated simultaneously (for the multi-array dispatch tests) and another scripted mode that emits empty-requested-array responses (for the missing-array tests), keyed by a per-test selector header or test-arrange method per research.md §R-9 in [tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs](tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs)
- [ ] T099 [P] [US2] Create `MultiArrayResponseDispatchFakeServerTests` driving the fake to return populated `$.xmlDocs[0]` + `$.wordDocs[0]` + `$.excelDocs[0]` simultaneously per US2 acceptance scenario 2; asserts each facade returns only its matching array's payload (SC-018) in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/MultiArrayResponseDispatchFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/MultiArrayResponseDispatchFakeServerTests.cs)
- [ ] T100 [P] [US2] Create `MissingFormatArrayFakeServerTests` driving the fake to return empty `$.xmlDocs` / `$.wordDocs` / `$.excelDocs` on `/documents/attachment`; asserts the matching facade raises `MissingSignedDocument` with the requested format on the exception per FR-058 in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/MissingFormatArrayFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/MissingFormatArrayFakeServerTests.cs)

**Checkpoint**: User Story 2 is independently validated. SC-018 is demonstrable. Consumers can rely on the SDK never silently returning a different format's payload.

---

## Phase 5: User Story 3 — Format-specific error mapping and signing-position semantics (Priority: P3)

**Goal**: Ensure every typed exception from any facade carries the correct `Format` discriminator, MISA's `errorCode` + correlation ID, and (for format-specific rejections) one of the synthesized codes (`InvalidXmlInput`, `MissingMainDom`, `MissingSignatureId`, `UnsupportedDocumentVariant`); and ensure no log line contains source-document bytes, signed-document bytes, or any of the secret JSON fields across all four formats.

**Independent Test**: Drive the fake server with one rejection fixture per format (malformed XML, missing `mainDom` on Excel attachment, missing `signatureId` on XML attachment) and assert (a) each exception carries the correct `Format`, `errorCode`, and correlation ID, (b) the surfaced category matches the documented mapping, (c) captured logs across a full three-format exchange contain zero occurrences of source / signed document bytes or any sensitive field value.

### Unit tests — per-format error mapper

- [ ] T101 [P] [US3] Create `PerFormatErrorMapperTests` with one `[Theory]` row per [contracts/error-mapping.md §A.10](./contracts/error-mapping.md#a10-per-format--documentshash-and--documentsattachment) entry (canonical `errorCode` rows for `InvalidXmlInput` / `MissingMainDom` / `MissingSignatureId` / `UnsupportedDocumentVariant` plus substring-fallback rows for each); verifies SC-019 (every typed exception carries `Format == requested`) in [tests/MisaConnect.ESign.UnitTests/Errors/PerFormatErrorMapperTests.cs](tests/MisaConnect.ESign.UnitTests/Errors/PerFormatErrorMapperTests.cs)
- [ ] T102 [P] [US3] Create `FormatPropertyContractTests` covering every existing typed exception path (slice-1 PDF, slice-2 OTP, slice-3 per-format) — for each, construct the exception via its public surface and assert the resulting `Format` matches the [public-surface.md §5 property-population table](./contracts/public-surface.md#5-additive-format-property-on-every-typed-exception); enforces SC-019 in [tests/MisaConnect.ESign.UnitTests/Errors/FormatPropertyContractTests.cs](tests/MisaConnect.ESign.UnitTests/Errors/FormatPropertyContractTests.cs)
- [ ] T103 [US3] Edit `ESignErrorMapperTests` to add a `Format == DocumentFormat.Pdf` assertion to every existing slice-1 PDF-path test row per FR-062 + SC-019 in [tests/MisaConnect.ESign.UnitTests/Errors/ESignErrorMapperTests.cs](tests/MisaConnect.ESign.UnitTests/Errors/ESignErrorMapperTests.cs)

### Unit tests — log scrubbing

- [ ] T104 [US3] Edit `ESignLogScrubberTests` to add per-format scrub-row coverage per FR-063 / SC-020: XML/Word/Excel hash request `FileToSign` bytes scrubbed; per-format response `document`/`documentBytes` scrubbed; `mainDom` / `sh` / `signatureId` / `digest` / `documentHash` / `signature` all scrubbed across `/documents/hash` and `/documents/attachment` bodies in [tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs](tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs)

### Integration tests — per-format error mapping end-to-end

- [ ] T105 [US3] Edit `FakeMisaESignServer.cs` to support scriptable per-format rejection fixtures (malformed XML on `/documents/hash`, missing-`mainDom` on Excel attachment, missing-`signatureId` on XML attachment, plus the substring-fallback case per format) per research.md §R-9 in [tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs](tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs)
- [ ] T106 [US3] Create `PerFormatErrorMappingFakeServerTests` driving the fake's scripted rejection fixtures per format; each test asserts the surfaced exception carries `Format = Xml | Word | Excel`, the synthesized `RawCode`, and the correlation ID; also asserts captured logs contain none of the secret JSON values across the full three-format exchange (parallel SC-020 enforcement) in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/PerFormatErrorMappingFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/PerFormatErrorMappingFakeServerTests.cs)

**Checkpoint**: User Story 3 is independently validated. SC-019 and SC-020 are demonstrable. Operations teams can branch by `(ExceptionType, Format)` without string-matching MISA's `devMsg`.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Slice-wide invariants (FR-065 regression, layering audit, docs / changelog), validation that the quickstart works as written, and final cleanup.

### Slice-1 PDF regression (FR-065 / SC-021)

- [ ] T107 [P] Create `PdfRegressionFakeServerTests` that re-runs slice-1's PDF happy-path against the fake server end-to-end after slice 3 ships; asserts (a) on-the-wire PDF request shapes are byte-identical to slice 1, (b) `Format == DocumentFormat.Pdf` on every surfaced typed result and exception path, (c) the existing slice-1 sandbox happy-path semantics are preserved per FR-065 + SC-021 in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/PdfRegressionFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/PdfRegressionFakeServerTests.cs)

### Layer audit + dependency hygiene

- [ ] T108 [P] Edit `MisaConnectESignLayerAuditTests` to extend the reflection-based audit to cover slice-3 namespaces (`Application.UseCases.{SignXml, SignWord, SignExcel, HashXmlDocument, HashWordDocument, HashExcelDocument, AttachSignatureToXml, AttachSignatureToWordExcel}`, `Domain.Documents.DocumentFormat`, `Domain.Signing.XmlSignatureContext`, `Application.Abstractions.{XmlHashOutput, WordExcelHashOutput}`) and assert they respect Constitution Principle I in [tests/MisaConnect.ESign.UnitTests/Layering/MisaConnectESignLayerAuditTests.cs](tests/MisaConnect.ESign.UnitTests/Layering/MisaConnectESignLayerAuditTests.cs)

### Documentation + CHANGELOG (Principle II, VII)

- [ ] T109 [P] Update [README.md](README.md) supported-operations table to list `SignXmlAsync` (both overloads), `SignWordAsync`, `SignExcelAsync` alongside existing PDF / OTP entries
- [ ] T110 [P] Add a `[Unreleased]` entry to [CHANGELOG.md](CHANGELOG.md) enumerating each addition per public-surface.md §9 — three new facade methods, the `DocumentFormat` enum, `XmlSignatureContext` + `XmlSignatureContextDto`, the six new request/result DTOs, `SignatureDescriptionDto` (if new), the additive `Format` property on every typed exception and on `SignPdfResultDto`, five new methods on `IMisaESignWireClient`, the two new Application records, and bump the version to `2.0.0-preview.3`

### Quickstart validation (Principle II)

- [ ] T111 Verify each code snippet in [quickstart.md](./quickstart.md) compiles and runs against the in-repo fake server: §2.1 (XML string overload), §2.2 (XML bytes overload), §3 (Word), §4 (Excel), §6 (catch-by-format), §7 (logging guarantees) — either by manual REPL run or by lifting the snippets into [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/QuickstartValidationTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/QuickstartValidationTests.cs)

### Final gate

- [ ] T112 Run `dotnet format MisaConnect.slnx` from repo root and confirm zero diff (required before PR per CLAUDE.md)
- [ ] T113 Run `dotnet build MisaConnect.slnx` from repo root and confirm zero warnings under `TreatWarningsAsErrors=true`
- [ ] T114 Run `dotnet test tests/MisaConnect.ESign.UnitTests/MisaConnect.ESign.UnitTests.csproj` from repo root and confirm full unit suite (slice 1 + slice 2 + slice 3) completes in < 30 seconds total per SC-022
- [ ] T115 Run `dotnet test tests/MisaConnect.ESign.IntegrationTests/MisaConnect.ESign.IntegrationTests.csproj` from repo root and confirm integration suite skips sandbox tests cleanly without `MISACONNECT_ESIGN_SANDBOX_*` env vars per SC-023

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; baseline verification before any code change
- **Foundational (Phase 2)**: Depends on Setup; introduces shared types every story imports; T032 is the regression gate that must be green before any user story starts
- **User Story 1 (Phase 3)**: Depends on Foundational; can start once T032 passes
- **User Story 2 (Phase 4)**: Depends on Foundational + the wire-client implementation tasks in US1 (T053–T054) — the dispatch behavior under test is implemented in US1; US2 phase only adds the tests that validate it
- **User Story 3 (Phase 5)**: Depends on Foundational + the error-mapper edits in Foundational (T022) — the behavior under test is implemented in Foundational; US3 phase adds the tests that lock the per-format-mapping contract
- **Polish (Phase 6)**: Depends on US1 + US2 + US3 being complete

### Within Foundational (Phase 2)

- T005 / T006 / T019 / T020 are independent file creations and can run together
- T007 (`ESignException` base class) must complete before T008–T018 (subclass forwarding)
- T021 (`IMisaESignWireClient` edit) depends on T005, T006, T019, T020
- T022 (error mapper edit) depends on T005
- T023 + T030 + T031 (slice-1 PDF call-site updates) depend on T005 + T022 + T007
- T025 / T026 (wire DTOs) depend on slice-1 `SignatureInfoDto` only — can run together but both touch separate files
- T027 (XmlSignatureContextMapper) depends on T006 + slice-1 `SignatureInfoDto`
- T028 (log scrubber) is independent of the other Foundational edits
- T029 (`SignPdfResultDto.Format = Pdf`) depends on T005
- T024 (`SubmitSignHash` projection) is independent enough to run in parallel with the wire-DTO work
- T032 (regression gate) is the closing checkpoint — runs only after T005–T031 are all done

### Within User Story 1 (Phase 3)

- Use cases (T033–T037), work records (T038–T043), validators (T044–T046) are all independent file creations — can all run in parallel
- Orchestrators (T047–T049) depend on the use cases + work records + validators of their format being done
- Wire client method tasks (T050–T054) all touch `MisaESignWireClient.cs`, so they must run sequentially in that file
- DI registration (T055) depends on the use cases, validators, and orchestrators all existing
- Client DTOs (T056–T063) are independent file creations — can run in parallel
- Client mappers (T064–T066) depend on their format's work request + DTO
- `IMisaESignClient` edit (T067) depends on the request/result DTOs
- `MisaESignClient` edits (T068–T070) all touch one file → sequential; each depends on the corresponding mapper + orchestrator + result DTO
- Unit tests (T071–T085) depend on the wire client + use cases of their format being implemented; T071 is the test support edit and gates T072–T085
- Fake-server extensions (T086–T087) both touch one file → sequential
- Happy-path integration tests (T088–T091) depend on the fake-server extensions + facade implementations
- Sandbox tests (T092–T095) depend on the facade implementations + the committed fixtures

### Within User Story 2 (Phase 4)

- Unit tests (T096, T097) are independent; can run in parallel
- T098 (fake-server extension) blocks the integration tests
- T099 + T100 (integration tests) can run in parallel after T098

### Within User Story 3 (Phase 5)

- Unit tests (T101, T102) are independent of each other but depend on Foundational T022; T103 + T104 each touch existing test files and can run in parallel with each other and with T101/T102
- T105 (fake-server extension) blocks T106
- T106 (integration test) depends on T105 + all the per-format error-mapping logic from Foundational + US1

### Polish (Phase 6)

- T107 / T108 / T109 / T110 are all independent and can run in parallel
- T111 (quickstart validation) depends on all US1/US2/US3 facade work being done
- T112–T115 are the final gates and run sequentially at the end

---

## Parallel Opportunities

- T002 / T003 / T004 (baseline verification) run together
- T005 / T006 / T019 / T020 (new Domain + Application records) run together
- T008–T018 (subclass forwarding) all run together once T007 is done — each touches a separate exception file
- T025 / T026 / T028 (wire DTOs + scrubber) run together — three separate files
- T033 / T034 / T035 (per-format hash use cases) run together
- T036 / T037 (per-format attachment use cases) run together
- T038–T043 (work records) all run together
- T044 / T045 / T046 (validators) run together
- T056–T063 (Client DTOs) all run together
- T064 / T065 / T066 (Client mappers) run together
- T072–T077 (XML unit tests) run together — each is a separate file
- T078–T081 (Word unit tests) run together
- T082–T085 (Excel unit tests) run together
- T088 / T089 / T090 / T091 (happy-path integration tests + token-cache test) run together once T086/T087 are done
- T092 / T093 / T094 / T095 (sandbox tests + fixtures) run together
- T096 / T097 (US2 unit tests) run together
- T099 / T100 (US2 integration tests) run together
- T101 / T102 / T103 / T104 (US3 tests) run together
- T107 / T108 / T109 / T110 (polish tasks) run together

---

## Parallel Example: User Story 1 (per-format symmetry)

```bash
# All per-format hash use cases together:
Task: "[US1] Create HashXmlDocument use case in src/MisaConnect.ESign.Application/UseCases/HashXmlDocument.cs"
Task: "[US1] Create HashWordDocument use case in src/MisaConnect.ESign.Application/UseCases/HashWordDocument.cs"
Task: "[US1] Create HashExcelDocument use case in src/MisaConnect.ESign.Application/UseCases/HashExcelDocument.cs"

# All per-format Client DTOs together:
Task: "[US1] Create SignXmlRequestDto in src/MisaConnect.ESign.Client/Dtos/SignXmlRequestDto.cs"
Task: "[US1] Create SignWordRequestDto in src/MisaConnect.ESign.Client/Dtos/SignWordRequestDto.cs"
Task: "[US1] Create SignExcelRequestDto in src/MisaConnect.ESign.Client/Dtos/SignExcelRequestDto.cs"
Task: "[US1] Create SignXmlResultDto in src/MisaConnect.ESign.Client/Dtos/SignXmlResultDto.cs"
Task: "[US1] Create SignWordResultDto in src/MisaConnect.ESign.Client/Dtos/SignWordResultDto.cs"
Task: "[US1] Create SignExcelResultDto in src/MisaConnect.ESign.Client/Dtos/SignExcelResultDto.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 (Setup) — verify baseline is healthy
2. Phase 2 (Foundational) — add shared types, port methods, wire-DTO strengthening, slice-1 `Format = Pdf` propagation; gate on T032
3. Phase 3 (User Story 1) — three new facade methods end-to-end with unit + fake-server + sandbox coverage per format
4. **STOP and VALIDATE**: Run T091 (token-cache-reuse fake-server test) and (if sandbox creds available) T092–T094 (sandbox tests) — these demonstrate the MVP
5. Cut `2.0.0-preview.3` release-candidate

### Incremental Delivery

1. Phase 1 + Phase 2 → foundation ready (slice-1 regression-safe via T032)
2. + Phase 3 (US1) → consumers can sign XML / Word / Excel; release-candidate (MVP)
3. + Phase 4 (US2) → per-format dispatch contract pinned by tests; SC-018 demonstrable
4. + Phase 5 (US3) → per-format error mapping + log redaction pinned by tests; SC-019 + SC-020 demonstrable
5. + Phase 6 (Polish) → FR-065 regression test, layer audit, README + CHANGELOG, `dotnet format`; ship `2.0.0-preview.3`

### Parallel Team Strategy

With multiple developers, after Foundational T032 passes:

- Developer A: Phase 3 US1 (XML lane — T033, T036, T038, T041, T044, T047, T050, T053, T056, T058, T061, T064, T072–T077, T088, T092)
- Developer B: Phase 3 US1 (Word lane — T034, T037 shared, T039, T042, T045, T048, T051, T054 shared, T059, T062, T065, T078–T081, T089, T093)
- Developer C: Phase 3 US1 (Excel lane — T035, T040, T043, T046, T049, T052, T060, T063, T066, T082–T085, T090, T094) + Phase 4 US2 (T096–T100) once US1 is integrated
- Developer D: Phase 2 cross-cutting (T086–T087 fake-server extension), Phase 5 US3 (T101–T106), Phase 6 (T107–T115)

Cross-developer coordination touchpoints: `MisaESignWireClient.cs` (T050–T054), `MisaESignClient.cs` (T068–T070), `FakeMisaESignServer.cs` (T086, T087, T098, T105), `ServiceCollectionExtensions.cs` (T055), `IMisaESignClient.cs` (T067) — these single-file edits must serialize.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability (Setup / Foundational / Polish phases have no story label)
- The constitution requires unit tests to stay under 30s total across the cumulative slice-1 + slice-2 + slice-3 suite (SC-022) — every new unit test uses the in-process `StubWireClient` and avoids real HTTP
- Integration tests must skip cleanly without `MISACONNECT_ESIGN_SANDBOX_*` env vars (SC-023) — the in-repo fake server is the deterministic harness
- FR-065 / SC-021 are non-negotiable: slice-1 PDF wire shapes and observable failure semantics must be byte-identical after slice 3 ships; T032 (Foundational regression gate) and T107 (Polish PDF regression test) are the enforcement points
- DTO field casing, date formats, and envelope shapes must match MISA's published API verbatim per Constitution Principle IV — including MISA's misspelling `Doc_Attackment` on the attachment shape (T026)
- Commit after each task or logical group; use the slice's `[Spec Kit]` commit prefix per CLAUDE.md / extensions.yml
- Stop at any checkpoint (end of Phase 3, end of Phase 4, end of Phase 5) to validate the story independently before moving to the next priority
- Avoid: vague tasks, same-file conflicts in parallel batches (the call-out lists above name every same-file edit explicitly), cross-story dependencies that would break independent testability

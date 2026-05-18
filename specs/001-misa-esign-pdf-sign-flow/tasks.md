---
description: "Task list for MisaConnect.ESign slice 1 (PDF sign flow)"
---

# Tasks: MISA eSign — PDF Signing Flow (foundational)

**Input**: Design documents from [`specs/001-misa-esign-pdf-sign-flow/`](.)
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The spec mandates a unit suite ([FR-020](./spec.md#functional-requirements)) and an integration suite ([FR-021](./spec.md#functional-requirements), [FR-022](./spec.md#functional-requirements)); SC-002/SC-003/SC-005/SC-006/SC-007 are all test-verified, so test tasks are first-class citizens of this slice and live alongside their implementation tasks (TDD-friendly, not TDD-mandatory — write either order, but both must land in the same PR).

**Organization**: Tasks are grouped by user story. The setup + foundational phases are infrastructure-heavy (this is a new product family — new csprojs, new Domain layer, new DI plumbing). The three user-story phases extend that foundation incrementally: US1 = happy path, US2 = cache + refresh, US3 = typed errors + log scrubbing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User-story label (US1, US2, US3) — only on user-story phases
- Paths in descriptions are relative to repo root and are exact

## Path Conventions

This slice follows the existing layered structure described in [docs/architecture.md](../../docs/architecture.md) and [plan.md §Project Structure](./plan.md). New csprojs live at:

- `src/MisaConnect.ESign.{Domain,Application,Infrastructure,Client}/`
- `tests/MisaConnect.ESign.{UnitTests,IntegrationTests}/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Stand up the six new csprojs, wire them into the solution, and seed the project-reference graph that enforces Constitution Principle I.

- [ ] T001 Create `src/MisaConnect.ESign.Domain/MisaConnect.ESign.Domain.csproj` targeting `net8.0`. No `<PackageReference>` entries. No `<ProjectReference>` entries.
- [ ] T002 Create `src/MisaConnect.ESign.Application/MisaConnect.ESign.Application.csproj` referencing `MisaConnect.ESign.Domain` and the `Microsoft.Extensions.Logging.Abstractions` package only.
- [ ] T003 Create `src/MisaConnect.ESign.Infrastructure/MisaConnect.ESign.Infrastructure.csproj` referencing `MisaConnect.ESign.Application` plus the `Microsoft.Extensions.{Configuration,Configuration.Binder,DependencyInjection.Abstractions,Http,Options,Options.ConfigurationExtensions,Logging.Abstractions}` packages (matches the eInvoice Infrastructure dep set).
- [ ] T004 Create `src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj` referencing `MisaConnect.ESign.Infrastructure`. Add `<PackageId>MisaConnect.ESign</PackageId>` plus the standard NuGet metadata (mirror the eInvoice client csproj).
- [ ] T005 [P] Create `tests/MisaConnect.ESign.UnitTests/MisaConnect.ESign.UnitTests.csproj` referencing all four ESign src csprojs and `MisaConnect.EInvoice.TestSupport`, plus the xUnit/Moq/FluentAssertions stack used by the existing eInvoice unit suite.
- [ ] T006 [P] Create `tests/MisaConnect.ESign.IntegrationTests/MisaConnect.ESign.IntegrationTests.csproj` referencing all four ESign src csprojs, `MisaConnect.EInvoice.TestSupport`, and the same xUnit/sandbox-fact + WebApplicationFactory stack the eInvoice integration project uses (for the in-repo fake server).
- [ ] T007 Add all six new projects to `MisaConnect.slnx` in the correct solution folders (`src/MisaConnect.ESign`, `tests/MisaConnect.ESign`). Verify `dotnet build MisaConnect.slnx` succeeds with zero warnings (warnings-as-errors is set globally in `Directory.Build.props`).
- [ ] T008 [P] Add a `MisaConnectESignLayerAuditTests.cs` file under `tests/MisaConnect.EInvoice.TestSupport/` (or a new shared spot) that enforces the layer rules for the ESign csprojs — Domain has zero outbound refs except `System.*`, Application refs only Domain + `Microsoft.Extensions.Logging.Abstractions`, Infrastructure is internal except for the documented public types. Modeled on the existing eInvoice namespace-audit test.

**Checkpoint**: Six new csprojs build clean, layer-audit test passes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain types, options, ports, the wire DTO surface, and the skeleton DI registration that every user story builds on. Once this phase is done, US1 / US2 / US3 can proceed in parallel if staffed.

**Critical**: No user story work can begin until this phase is complete.

### Domain layer — entities, enums, exceptions

- [ ] T009 [P] Create `AuthSession` immutable record in `src/MisaConnect.ESign.Domain/Authentication/AuthSession.cs` per [data-model.md §1.1](./data-model.md#11-authsession-domainauthentication).
- [ ] T010 [P] Create `Certificate`, `CertificateChain`, and `KeyStatus` (enum: `ACTIVE`, `INACTIVE`) in `src/MisaConnect.ESign.Domain/Certificates/` per [data-model.md §1.2–§1.4](./data-model.md#12-certificate-domaincertificates). `CertificateChain` is a 3-element record `(Signing, Intermediate, Root)`.
- [ ] T011 [P] Create `PdfDocument` (opaque `byte[]` wrapper) and `SignedDocument` (alias / wrapper) in `src/MisaConnect.ESign.Domain/Documents/` per [data-model.md §1.5 / §1.9](./data-model.md#15-pdfdocument-domaindocuments).
- [ ] T012 [P] Create signing primitives in `src/MisaConnect.ESign.Domain/Signing/`: `HashAlgorithm` enum (slice-1: only `SHA256`), `SignatureInfo`, `SignatureDescription`, `SignaturePosInfo`, `SignTransaction`, `SignStatus` enum (`PENDING`, `SUCCESS`, `FAILED`, `CANCELLED`) per [data-model.md §1.6–§1.8](./data-model.md#16-signatureinfo-domainsigning--signaturedescription).
- [ ] T013 [P] Create `ESignErrorCategory` enum and the `ResponseError` value object in `src/MisaConnect.ESign.Domain/Errors/` per [data-model.md §1.11](./data-model.md#111-responseerror-domainerrors) and the categories listed in [research.md §R-8](./research.md#r-8-error-mapping-table--responseerrorerrorcode--typed-exception).
- [ ] T014 [P] Create the abstract `ESignException` base in `src/MisaConnect.ESign.Domain/Errors/ESignException.cs` carrying `Category`, `RawCode`, `CorrelationId`, `Detail`, and an `Exception? inner` constructor parameter.
- [ ] T015 [P] Create the concrete exception subclasses in `src/MisaConnect.ESign.Domain/Errors/`: `AuthenticationFailedException` (adds `Requires2FA`), `NoActiveCertificateException`, `SignRejectedException` (adds `RequiresUserCertSetup`), `SignTerminalStateException` (adds `TerminalStatus`, `TransactionId`), `SignTimeoutException` (adds `TransactionId`, `ElapsedTime`), `ESignTransportException` (adds `LastStatusCode`, `AttemptCount`). All `sealed`; all carry mandatory non-empty `CorrelationId` per [contracts/public-surface.md §6](./contracts/public-surface.md#6-public-exceptions--misaconnectesigndomainerrors).

### Application layer — port interfaces and use-case I/O records

- [ ] T016 [P] Create `AccessToken` record in `src/MisaConnect.ESign.Application/Abstractions/AccessToken.cs` per [data-model.md §2.1](./data-model.md#21-accesstoken-applicationabstractions).
- [ ] T017 [P] Create `ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`, `ISystemClock`, `ICorrelationIdAccessor` interfaces — one file each — in `src/MisaConnect.ESign.Application/Abstractions/` per [contracts/public-surface.md §3](./contracts/public-surface.md#3-port-interfaces-application-layer-public-for-consumer-swap).
- [ ] T018 [P] Create the internal `IDelayer` seam at `src/MisaConnect.ESign.Application/Abstractions/IDelayer.cs` (`Task DelayAsync(TimeSpan, CancellationToken)`) used by the polling loop and the transport-retry handler for deterministic unit testing.
- [ ] T019 [P] Create `IMisaESignWireClient` in `src/MisaConnect.ESign.Application/Abstractions/IMisaESignWireClient.cs` with the seven methods listed in [data-model.md §2.2](./data-model.md#22-port-interfaces), plus the supporting `PdfHashOutput` and `SignStatusSnapshot` records in the same folder.

### Infrastructure layer — options, JSON, routes, wire DTOs

- [ ] T020 [P] Create `ESignEnvironment` enum (`Sandbox = 0`, `Production = 1`) in `src/MisaConnect.ESign.Infrastructure/Configuration/ESignEnvironment.cs`.
- [ ] T021 [P] Create `MisaESignOptions` and its nested classes (`MisaESignPollingOptions`, `MisaESignTransportRetryOptions`, `MisaESignErrorOptions`) in `src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptions.cs` with the `SectionName = "Misa:ESign"` and `ProductionHost = "esignapp.misa.vn"` constants per [contracts/public-surface.md §4](./contracts/public-surface.md#4-configuration--misaesignoptions).
- [ ] T022 Create `MisaESignOptionsValidator : IValidateOptions<MisaESignOptions>` in `src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptionsValidator.cs` enforcing every rule in [data-model.md §4](./data-model.md#4-configuration-shape-infrastructureconfiguration) (non-empty strings, https BaseUrl, environment ↔ host coherence per FR-015, polling sanity, transport-retry sanity).
- [ ] T023 [P] Create the seven endpoint route constants in `src/MisaConnect.ESign.Infrastructure/ESign/ESignHttpRoutes.cs` (`AuthLoginApi`, `AuthRefreshToken`, `CertificatesByUserId`, `DocumentsHash`, `SigningHash`, `SigningStatus`, `DocumentsAttachment`) using the paths in [contracts/wire-envelopes.md](./contracts/wire-envelopes.md).
- [ ] T024 [P] Create `ESignJsonOptions` static in `src/MisaConnect.ESign.Infrastructure/ESign/ESignJsonOptions.cs` exposing the `JsonSerializerOptions` instance (case-sensitive property names, no name policy — MISA's wire DTOs are case-exact per Principle IV) reused by the wire client.
- [ ] T025 [P] Create login wire DTOs in `src/MisaConnect.ESign.Infrastructure/ESign/Wire/LoginDtos.cs` (`LoginRequestDto`, `LoginResponseDto` with the nested `status`, `data`, `user`, `default`, `verifyUser` shapes) per [contracts/wire-envelopes.md §E1](./contracts/wire-envelopes.md#e1-post-apiauthapiv1authlogin-api). All `internal`.
- [ ] T026 [P] Create refresh-token wire DTOs in `src/MisaConnect.ESign.Infrastructure/ESign/Wire/RefreshTokenDtos.cs` (`RefreshTokenRequestDto`, `RefreshTokenResponseDto`) per [contracts/wire-envelopes.md §E2](./contracts/wire-envelopes.md#e2-post-webdevapiauthapiv1authrefreshtoken). Support both envelope and flat shapes per the **FLAG** in §E2.
- [ ] T027 [P] Create certificate wire DTOs in `src/MisaConnect.ESign.Infrastructure/ESign/Wire/CertificateDtos.cs` (`CertificateDto` with the `certiticateChain` typo preserved verbatim per [contracts/wire-envelopes.md §E3](./contracts/wire-envelopes.md#e3-get-externalesrmservicegeneralapiv1certificatesby-userid)).
- [ ] T028 [P] Create hash wire DTOs in `src/MisaConnect.ESign.Infrastructure/ESign/Wire/HashDtos.cs` (`HashRequestDto`, `PdfDocRequestDto`, `SignatureInfoDto`, `SignatureDescriptionDto`, `SignaturePosInfoDto`, `HashResponseDto`, `PdfHashOutputDto`) per [contracts/wire-envelopes.md §E4](./contracts/wire-envelopes.md#e4-post-externalesrmservicedocumentapiv1documentshash). Honor PascalCase outer / camelCase inner per the §E4 inconsistency note.
- [ ] T029 [P] Create sign-hash wire DTOs in `src/MisaConnect.ESign.Infrastructure/ESign/Wire/SignHashDtos.cs` (`SignHashRequestDto`, `SignHashDocumentDto`, `SignHashResponseDto`) per [contracts/wire-envelopes.md §E5](./contracts/wire-envelopes.md#e5-post-externalesrmservicesigningapiv1signinghash).
- [ ] T030 [P] Create sign-status wire DTOs in `src/MisaConnect.ESign.Infrastructure/ESign/Wire/SignStatusDtos.cs` (`SignStatusResponseDto`, `SignStatusSignatureDto`) per [contracts/wire-envelopes.md §E6](./contracts/wire-envelopes.md#e6-get-externalesrmservicesigningapiv1signingstatustransactionid).
- [ ] T031 [P] Create attachment wire DTOs in `src/MisaConnect.ESign.Infrastructure/ESign/Wire/AttachmentDtos.cs` (`AttachmentRequestDto`, `AttachmentPdfDocRequestDto`, `AttachmentResponseDto`, `AttachmentPdfDocResponseDto`) per [contracts/wire-envelopes.md §E7](./contracts/wire-envelopes.md#e7-post-externalesrmservicedocumentapiv1documentsattachment).
- [ ] T032 [P] Create `ResponseErrorDto` in `src/MisaConnect.ESign.Infrastructure/ESign/Wire/ResponseErrorDto.cs` per [contracts/wire-envelopes.md](./contracts/wire-envelopes.md) (the `{ error, errorCode, devMsg, userMsg }` shape used in 4xx bodies and inside login's `status` block).

### Infrastructure layer — wire ↔ Domain mappers

- [ ] T033 [P] Create `AuthSessionMapper` in `src/MisaConnect.ESign.Infrastructure/ESign/Mapping/AuthSessionMapper.cs` translating `LoginResponseDto` / `RefreshTokenResponseDto` → `AuthSession` (computes `ExpiresAtUtc = now + expiresIn`; discards email/phone/firstName/lastName at the boundary per [data-model.md §1.1](./data-model.md#11-authsession-domainauthentication)).
- [ ] T034 [P] Create `CertificateMapper` in `src/MisaConnect.ESign.Infrastructure/ESign/Mapping/CertificateMapper.cs` translating `CertificateDto[]` → `IReadOnlyList<Certificate>`. Defensive: chain length != 3 surfaces `ESignException(MisaUnknown, "InvalidCertChainLength")`.
- [ ] T035 [P] Create `SignStatusMapper` in `src/MisaConnect.ESign.Infrastructure/ESign/Mapping/SignStatusMapper.cs` translating `SignStatusResponseDto` → `SignStatusSnapshot` per [contracts/error-mapping.md §A.6](./contracts/error-mapping.md#a6-signingstatustx-e6).
- [ ] T036 [P] Create `ResponseErrorMapper` in `src/MisaConnect.ESign.Infrastructure/ESign/Mapping/ResponseErrorMapper.cs` translating `ResponseErrorDto` → Domain `ResponseError`.

### Infrastructure layer — base clock, default correlation accessor

- [ ] T037 [P] Create `SystemClock : ISystemClock` (wraps `TimeProvider.System.GetUtcNow()`) in `src/MisaConnect.ESign.Infrastructure/Time/SystemClock.cs`.
- [ ] T038 [P] Create `TaskDelayer : IDelayer` (wraps `Task.Delay`) in `src/MisaConnect.ESign.Infrastructure/Time/TaskDelayer.cs`.

### Infrastructure layer — typed wire client skeleton + DI scaffold

- [ ] T039 Create `MisaESignWireClient : IMisaESignWireClient` skeleton in `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs` — constructor takes a typed `HttpClient` + `ESignJsonOptions` + `ILogger<MisaESignWireClient>` + `IOptions<MisaESignOptions>`. All seven methods throw `NotImplementedException` for now; per-endpoint implementations land in the user-story phases.
- [ ] T040 Create `Infrastructure.DependencyInjection.ServiceCollectionExtensions.AddMisaConnectESignCore` (`internal static`) in `src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`. This binds `MisaESignOptions` from configuration, registers `MisaESignOptionsValidator` with `.ValidateOnStart()`, registers the default `SystemClock` / `TaskDelayer`, and registers the typed `HttpClient` for `MisaESignWireClient` (handlers are added in the user-story phases). Uses `TryAdd*` so consumer registrations win.

### Client layer — facade interface, DTOs, default correlation accessor

- [ ] T041 [P] Create `IMisaESignClient` interface in `src/MisaConnect.ESign.Client/IMisaESignClient.cs` with the `SignPdfAsync` signature + XML doc-comments per [contracts/public-surface.md §2](./contracts/public-surface.md#2-facade--imisaesignclient).
- [ ] T042 [P] Create the public DTOs in `src/MisaConnect.ESign.Client/Dtos/` — `SignPdfRequestDto.cs`, `SignaturePosInfoDto.cs`, `SignPdfResultDto.cs`, `CertificateDto.cs` per [data-model.md §5](./data-model.md#5-client-facing-dtos-clientdtos).
- [ ] T043 [P] Create `LibraryCorrelationIdAccessor : ICorrelationIdAccessor` in `src/MisaConnect.ESign.Client/LibraryCorrelationIdAccessor.cs` — generates a fresh `Guid.NewGuid().ToString()` per scope. Mirrors the eInvoice precedent so ASP.NET hosts can override with a per-request accessor.
- [ ] T044 Create `MisaESignClient : IMisaESignClient` facade skeleton in `src/MisaConnect.ESign.Client/MisaESignClient.cs`. Constructor takes `SignPdf` orchestrator + `SignPdfRequestMapper`. `SignPdfAsync` is `throw new NotImplementedException()` for now — body lands in T079 (US1).
- [ ] T045 Create `SignPdfRequestMapper` in `src/MisaConnect.ESign.Client/Mapping/SignPdfRequestMapper.cs` translating `SignPdfRequestDto` → Application-layer `SignPdfWorkRequest` (auto-generates `DocumentId` GUID if null, applies default `RenderingMode = 0`).
- [ ] T046 Create the **public** `Client.DependencyInjection.ServiceCollectionExtensions.AddMisaConnectESign(IConfiguration)` and `AddMisaConnectESign(Action<MisaESignOptions>)` overloads in `src/MisaConnect.ESign.Client/DependencyInjection/ServiceCollectionExtensions.cs`. Calls `AddMisaConnectESignCore` and additionally registers `IMisaESignClient`, the `LibraryCorrelationIdAccessor`, and (in later phases) the use-case orchestrator + ports. Per [contracts/public-surface.md §1](./contracts/public-surface.md#1-di-entry-point).

### Test infrastructure — EsignFake server skeleton and config validator tests

- [ ] T047 Create `FakeMisaESignServer` HTTP host shell in `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs` — modeled on the existing `tests/MisaConnect.EInvoice.IntegrationTests/MisaFake/FakeMisaServer.cs`. Owns a `WebApplication` instance bound to a free TCP port; exposes `BaseAddress`. Endpoints are filled in per user-story phase.
- [ ] T048 [P] Create endpoint handler stubs in `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeAuthEndpoints.cs`, `FakeCertificateEndpoints.cs`, `FakeDocumentEndpoints.cs`, `FakeSigningEndpoints.cs` — each handler returns a hard-coded canned response shape per [contracts/wire-envelopes.md](./contracts/wire-envelopes.md). State machine for `Signing/status` is added in T058 (US1).
- [ ] T049 [P] Create `FakeMisaESignServerTests.cs` in `tests/MisaConnect.ESign.IntegrationTests/EsignFake/` — sanity checks the fake responds with the documented shapes for each route (uses raw `HttpClient`, no SDK code).
- [ ] T050 [P] Create `tests/MisaConnect.ESign.UnitTests/Configuration/MisaESignOptionsValidatorTests.cs` — one fact per validation rule in [data-model.md §4](./data-model.md#4-configuration-shape-infrastructureconfiguration) (missing fields, non-https BaseUrl, Sandbox-host-equals-production rejected, Production-host-not-production rejected, polling/retry sanity).

**Checkpoint**: `dotnet build` clean. `dotnet test tests/MisaConnect.ESign.UnitTests` runs (only `MisaESignOptionsValidatorTests` so far). Layer-audit test (T008) still passes. Foundation is now ready — US1/US2/US3 can begin in parallel.

---

## Phase 3: User Story 1 — Sign a PDF end-to-end (Priority: P1) — MVP

**Goal**: A consumer developer with valid MISA credentials and an ACTIVE remote-signing cert can call one SDK method (`IMisaESignClient.SignPdfAsync`) with raw PDF bytes and signing context, and receive signed PDF bytes back. The full seven-endpoint pipeline (login → list certs → hash → sign → poll → attach) runs end-to-end.

**Independent Test**: With the sandbox configured and at least one ACTIVE cert on the user, call `SignPdfAsync` with a one-page PDF and assert the returned bytes are a valid signed PDF with an attached digital signature. Repeat against `FakeMisaESignServer` for the deterministic offline equivalent. No consumer code touches MISA endpoints directly.

> US1 covers the **cache-miss / first-call** path. Cached-token reuse and 401-refresh are US2; full per-errorCode mapping and log scrubbing are US3. US1 ships with a minimal error fallback (`ESignException` with the raw `errorCode` echoed) so the happy path is the only path that returns successfully.

### Application layer — use cases (US1)

- [ ] T051 [P] [US1] Create `Application.UseCases.EnsureAccessToken` in `src/MisaConnect.ESign.Application/UseCases/EnsureAccessToken.cs` — login-only path for US1 (no cache lookup yet, no refresh): calls `IMisaESignWireClient.LoginAsync`, returns the `AccessToken`. Constructor takes `IMisaESignWireClient` + `IOptions<MisaESignOptions>` + `ISystemClock` + `ITokenCacheKeySelector`. Refresh integration lands in US2 (T072).
- [ ] T052 [P] [US1] Create `Application.UseCases.ListActiveCertificates` in `src/MisaConnect.ESign.Application/UseCases/ListActiveCertificates.cs` — calls `IMisaESignWireClient.ListCertificatesByUserIdAsync`, filters to `KeyStatus == ACTIVE`, throws `NoActiveCertificateException` (with the current correlation ID) if empty (FR-006/FR-008).
- [ ] T053 [P] [US1] Create `Application.UseCases.HashPdfDocument` in `src/MisaConnect.ESign.Application/UseCases/HashPdfDocument.cs` — delegates to `IMisaESignWireClient.HashPdfAsync` (FR-009). Asserts response `PdfHashOutput.Digest` is non-empty (defensive per [data-model.md §7](./data-model.md#7-validation-rules-cross-layer-summary)).
- [ ] T054 [P] [US1] Create `Application.UseCases.SubmitSignHash` in `src/MisaConnect.ESign.Application/UseCases/SubmitSignHash.cs` — delegates to `IMisaESignWireClient.SubmitSignHashAsync` (FR-010), captures `SignTransaction`.
- [ ] T055 [P] [US1] Create `Application.UseCases.PollSignStatus` in `src/MisaConnect.ESign.Application/UseCases/PollSignStatus.cs` — implements the poll loop from [research.md §R-7](./research.md#r-7-sign-pipeline-orchestration--polling-state-machine) using injected `IDelayer` + `ISystemClock`. PENDING → continue; SUCCESS → return `SignStatusSnapshot`; FAILED/CANCELLED → `SignTerminalStateException`; unknown → `SignTerminalStateException(TerminalStatus = Unknown)`; deadline elapsed → `SignTimeoutException(transactionId, elapsed)`. FR-011/FR-013.
- [ ] T056 [P] [US1] Create `Application.UseCases.AttachSignature` in `src/MisaConnect.ESign.Application/UseCases/AttachSignature.cs` — delegates to `IMisaESignWireClient.AttachSignatureAsync` (FR-012), returns the signed PDF bytes.
- [ ] T057 [P] [US1] Create `Application.Validation.SignPdfRequestValidator` in `src/MisaConnect.ESign.Application/Validation/SignPdfRequestValidator.cs` enforcing all rules in [data-model.md §7](./data-model.md#7-validation-rules-cross-layer-summary) before any MISA call.
- [ ] T058 [US1] Create `Application.UseCases.SignPdf` orchestrator in `src/MisaConnect.ESign.Application/UseCases/SignPdf.cs` — implements the 6-step sequence from [research.md §R-7](./research.md#r-7-sign-pipeline-orchestration--polling-state-machine). Constructor takes `EnsureAccessToken`, `ListActiveCertificates`, `ICertificateSelector`, `HashPdfDocument`, `SubmitSignHash`, `PollSignStatus`, `AttachSignature`, `ISystemClock`, `SignPdfRequestValidator`. Returns `SignPdfWorkResult`. Depends on T051–T057.

### Infrastructure layer — concrete adapters (US1)

- [ ] T059 [P] [US1] Create `Infrastructure.Certificates.FirstActiveCertificateSelector : ICertificateSelector` in `src/MisaConnect.ESign.Infrastructure/Certificates/FirstActiveCertificateSelector.cs` per [research.md §R-6](./research.md#r-6-certificate-selection-default--first-active-in-misas-response-order-matches-the-spec). FR-007.
- [ ] T060 [P] [US1] Create `Infrastructure.Http.ClientHeadersHandler : DelegatingHandler` in `src/MisaConnect.ESign.Infrastructure/Http/ClientHeadersHandler.cs` — injects `x-clientId` / `x-clientKey` from `MisaESignOptions` on every outgoing request, and injects `AuthorizationRM: Bearer <token>` from an injected `IAuthHeaderSource` (a minimal seam US1 uses to read directly from the just-acquired token; US2 will replace the source with the cached/refreshed token resolver). FR-014.
- [ ] T061 [US1] Implement `MisaESignWireClient.LoginAsync` (POST `/api/auth/api/v1/auth/login-api`) in `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs` — serializes `LoginRequestDto`, deserializes `LoginResponseDto`, maps via `AuthSessionMapper`. Adds the `x-clientId`/`x-clientKey` headers but not `AuthorizationRM` (login is the call that fetches the token). Wire-format compliance per [contracts/wire-envelopes.md §E1](./contracts/wire-envelopes.md#e1-post-apiauthapiv1authlogin-api).
- [ ] T062 [US1] Implement `MisaESignWireClient.ListCertificatesByUserIdAsync` (GET `/external/esrm/service/general/api/v1/Certificates/by-userId`) — deserializes the array, maps via `CertificateMapper`. Wire-format compliance per [contracts/wire-envelopes.md §E3](./contracts/wire-envelopes.md#e3-get-externalesrmservicegeneralapiv1certificatesby-userid).
- [ ] T063 [US1] Implement `MisaESignWireClient.HashPdfAsync` (POST `/external/esrm/service/document/api/v1/documents/hash`) — builds `HashRequestDto` with `pdfDocs` populated only (slice 1 PDF-only), serializes with `ESignJsonOptions`, deserializes `HashResponseDto`, returns `PdfHashOutput`. Wire-format compliance per [contracts/wire-envelopes.md §E4](./contracts/wire-envelopes.md#e4-post-externalesrmservicedocumentapiv1documentshash).
- [ ] T064 [US1] Implement `MisaESignWireClient.SubmitSignHashAsync` (POST `/external/esrm/service/signing/api/v1/Signing/hash`) — builds `SignHashRequestDto` (PascalCase outer per the doc), deserializes `SignHashResponseDto`, returns `SignTransaction`. Wire-format compliance per [contracts/wire-envelopes.md §E5](./contracts/wire-envelopes.md#e5-post-externalesrmservicesigningapiv1signinghash).
- [ ] T065 [US1] Implement `MisaESignWireClient.GetSignStatusAsync` (GET `/external/esrm/service/signing/api/v1/Signing/status/{transactionId}`) — deserializes `SignStatusResponseDto`, maps via `SignStatusMapper`. Wire-format compliance per [contracts/wire-envelopes.md §E6](./contracts/wire-envelopes.md#e6-get-externalesrmservicesigningapiv1signingstatustransactionid).
- [ ] T066 [US1] Implement `MisaESignWireClient.AttachSignatureAsync` (POST `/external/esrm/service/document/api/v1/documents/attachment`) — builds `AttachmentRequestDto` with `pdfDocs` only, deserializes `AttachmentResponseDto`, base64-decodes `pdfDocs[0].document` to `byte[]`. Wire-format compliance per [contracts/wire-envelopes.md §E7](./contracts/wire-envelopes.md#e7-post-externalesrmservicedocumentapiv1documentsattachment).
- [ ] T067 [US1] Update `Infrastructure.DependencyInjection.ServiceCollectionExtensions.AddMisaConnectESignCore` (in `src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`) to register: `EnsureAccessToken`, `ListActiveCertificates`, `HashPdfDocument`, `SubmitSignHash`, `PollSignStatus`, `AttachSignature`, `SignPdf`, `SignPdfRequestValidator` (Scoped); `FirstActiveCertificateSelector` (TryAddSingleton). Wire `ClientHeadersHandler` into the typed `HttpClient` pipeline as the innermost handler.

### Client layer — facade implementation (US1)

- [ ] T068 [US1] Implement `MisaESignClient.SignPdfAsync` in `src/MisaConnect.ESign.Client/MisaESignClient.cs` — translates the input DTO via `SignPdfRequestMapper`, calls the `SignPdf` orchestrator, translates the result into `SignPdfResultDto`. Depends on T044 (skeleton) + T058 (orchestrator).
- [ ] T069 [US1] Update `Client.DependencyInjection.ServiceCollectionExtensions.AddMisaConnectESign` (the public overloads in `src/MisaConnect.ESign.Client/DependencyInjection/ServiceCollectionExtensions.cs`) to register `IMisaESignClient` (`Singleton`), `SignPdfRequestMapper` (`Singleton`), `LibraryCorrelationIdAccessor` (`Scoped`, `TryAdd*`). Confirm `AddMisaConnectESign(configuration)` is the **only** consumer-facing call needed per [quickstart.md §4](./quickstart.md#4-register).

### Tests for US1

- [ ] T070 [P] [US1] Create `tests/MisaConnect.ESign.UnitTests/Certificates/CertificateSelectorTests.cs` — covers FR-007 (first-ACTIVE order), FR-008 (empty list → `NoActiveCertificateException`), and mixed ACTIVE/INACTIVE inputs.
- [ ] T071 [P] [US1] Create `tests/MisaConnect.ESign.UnitTests/Hashing/HashPdfDocumentTests.cs` — drives `HashPdfDocument` against a mocked `IMisaESignWireClient`; asserts the wire request body matches [contracts/wire-envelopes.md §E4](./contracts/wire-envelopes.md#e4-post-externalesrmservicedocumentapiv1documentshash) (verbatim field names and shape).
- [ ] T072 [P] [US1] Create `tests/MisaConnect.ESign.UnitTests/Signing/SubmitSignHashTests.cs` — asserts the PascalCase outer field names per [contracts/wire-envelopes.md §E5](./contracts/wire-envelopes.md#e5-post-externalesrmservicesigningapiv1signinghash) and that `transactionId` is captured into `SignTransaction`.
- [ ] T073 [P] [US1] Create `tests/MisaConnect.ESign.UnitTests/Signing/PollSignStatusTests.cs` — exercises the four terminal paths plus PENDING→PENDING→SUCCESS using a fake `IDelayer` + `ISystemClock`. Verifies tests complete in <100 ms each (SC-002 budget).
- [ ] T074 [P] [US1] Create `tests/MisaConnect.ESign.UnitTests/Signing/AttachSignatureTests.cs` — asserts the wire request body matches [contracts/wire-envelopes.md §E7](./contracts/wire-envelopes.md#e7-post-externalesrmservicedocumentapiv1documentsattachment), including `mainDom`/`signatureId` omission rules for PDF.
- [ ] T075 [P] [US1] Create `tests/MisaConnect.ESign.UnitTests/Authentication/EnsureAccessTokenLoginTests.cs` — covers the cache-miss / direct-login path (US1 scope). Cache hit + refresh paths are covered in US2 (T084).
- [ ] T076 [P] [US1] Create `tests/MisaConnect.ESign.UnitTests/Validation/SignPdfRequestValidatorTests.cs` — one fact per validation rule from [data-model.md §7](./data-model.md#7-validation-rules-cross-layer-summary).
- [ ] T077 [P] [US1] Extend `FakeMisaESignServer` (T047/T048) with the `Signing/status` state machine: configurable PENDING→SUCCESS transitions (default: 2 PENDINGs then SUCCESS) so happy-path E2E tests run in seconds. Lives in `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeSigningEndpoints.cs`.
- [ ] T078 [P] [US1] Create `tests/MisaConnect.ESign.IntegrationTests/EndToEnd/SignPdfHappyPathFakeServerTests.cs` — registers `AddMisaConnectESign` pointed at `FakeMisaESignServer.BaseAddress`, calls `SignPdfAsync` with a deterministic one-page PDF fixture, asserts the returned bytes match the fake's pre-baked signed PDF. Verifies the seven endpoints are each hit exactly once.
- [ ] T079 [US1] Create `tests/MisaConnect.ESign.IntegrationTests/EndToEnd/SignPdfHappyPathSandboxTests.cs` — `[SandboxFact]`-decorated. SC-001: end-to-end sign of a one-page PDF against the live MISA eSign sandbox. Skip predicate: both `MISACONNECT_ESIGN_SANDBOX_*` env vars present AND TCP probe to the configured sandbox host succeeds within ~2 s (per [research.md §R-5](./research.md#r-5-sandbox-env-var-prefix--misaconnect_esign_sandbox_-matches-the-spec)). Fail loudly on credential rejection (FR-021).
- [ ] T080 [P] [US1] Create `tests/MisaConnect.ESign.IntegrationTests/Configuration/AddMisaConnectESignTests.cs` — DI registration smoke test: builds an `IServiceProvider` with sandbox env-var-bound config, resolves `IMisaESignClient`, asserts singleton lifetime and that all default ports were registered.

**Checkpoint**: US1 is fully functional and independently testable. A consumer can call `SignPdfAsync` and get a signed PDF back. SC-001 passes against the sandbox (when reachable); against `FakeMisaESignServer` the test runs deterministically. The MVP is shippable now.

---

## Phase 4: User Story 2 — Transparent token lifecycle and refresh on 401 (Priority: P2)

**Goal**: After the first successful login, subsequent `SignPdfAsync` calls within the cached `expiresIn` window perform **zero** login requests. If any signing-pipeline request returns 401, the SDK refreshes the access token via `/auth/refreshtoken` and retries the original request exactly once. Refresh is single-flight per cache key — 8 concurrent 401-observers issue exactly 1 outbound `refreshtoken` request.

**Independent Test**: Against `FakeMisaESignServer`: (a) make two `SignPdfAsync` calls back-to-back and assert exactly one `/login-api` was hit (SC-004); (b) force a 401 mid-pipeline and assert exactly one `/refreshtoken` was hit then the original call succeeded on retry; (c) issue 8 parallel `SignPdfAsync` calls all observing 401 simultaneously and assert exactly one `/refreshtoken` outbound (SC-007).

### Application layer — refresh use case (US2)

- [ ] T081 [US2] Create `Application.UseCases.RefreshAccessToken` in `src/MisaConnect.ESign.Application/UseCases/RefreshAccessToken.cs` — calls `IMisaESignWireClient.RefreshAsync(refreshToken, ct)`, returns the new `AccessToken`. Throws `AuthenticationFailedException` on 4xx per [contracts/error-mapping.md §A.2](./contracts/error-mapping.md#a2-refreshtoken-e2).

### Infrastructure layer — cache, key selector, single-flight, auth handler (US2)

- [ ] T082 [P] [US2] Create `Infrastructure.Caching.InMemoryTokenCache : ITokenCache` in `src/MisaConnect.ESign.Infrastructure/Caching/InMemoryTokenCache.cs` — backed by `ConcurrentDictionary<string, AccessToken>` per [research.md §R-2](./research.md#r-2-token-cache--refresh--single-flight-via-in-process-keyed-semaphore). FR-002 / FR-003.
- [ ] T083 [P] [US2] Create `Infrastructure.Caching.DefaultTokenCacheKeySelector : ITokenCacheKeySelector` in `src/MisaConnect.ESign.Infrastructure/Caching/DefaultTokenCacheKeySelector.cs` — composes `$"{options.UserName}|{options.ClientId}|{new Uri(options.BaseUrl).Host}"`. FR-026.
- [ ] T084 [US2] Create `Infrastructure.Http.SingleFlightRefresh` in `src/MisaConnect.ESign.Infrastructure/Http/SingleFlightRefresh.cs` — `ConcurrentDictionary<string, Lazy<Task<AccessToken>>>` keyed by cache key per [research.md §R-2](./research.md#r-2-token-cache--refresh--single-flight-via-in-process-keyed-semaphore). One method: `Task<AccessToken> RefreshAsync(string cacheKey, Func<CancellationToken, Task<AccessToken>> factory, CancellationToken ct)`. Cleans up the slot on completion (success or failure). FR-028 / SC-007.
- [ ] T085 [US2] Implement `MisaESignWireClient.RefreshAsync` (POST `/webdev/api/auth/api/v1/auth/refreshtoken`) in `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs` — serializes `RefreshTokenRequestDto`, deserializes `RefreshTokenResponseDto` supporting both envelope and flat shapes per the **FLAG** in [contracts/wire-envelopes.md §E2](./contracts/wire-envelopes.md#e2-post-webdevapiauthapiv1authrefreshtoken). Maps via `AuthSessionMapper`. Does NOT include `AuthorizationRM` header.
- [ ] T086 [US2] Create `Infrastructure.Http.RemoteSigningAuthHandler : DelegatingHandler` in `src/MisaConnect.ESign.Infrastructure/Http/RemoteSigningAuthHandler.cs` — on every outbound request, attaches `AuthorizationRM: Bearer <token>` from `ITokenCache` (acquiring via `EnsureAccessToken` on miss); on 401, invalidates the cache entry, calls `SingleFlightRefresh.RefreshAsync(cacheKey, () => RefreshAccessToken.ExecuteAsync(...))`, retries the original request once with the new token, and surfaces `AuthenticationFailedException` (with `Requires2FA = false`) if the refresh itself fails. Single retry budget per [contracts/error-mapping.md §A.2](./contracts/error-mapping.md#a2-refreshtoken-e2). FR-004 / FR-028.
- [ ] T087 [US2] Update `Application.UseCases.EnsureAccessToken` (from T051) to consult `ITokenCache` first, perform proactive refresh when `ExpiresAtUtc <= now` (FR-005), and fall back to login on cold start. After acquisition, write to `ITokenCache` via the composed key.
- [ ] T088 [US2] Update `Infrastructure.DependencyInjection.ServiceCollectionExtensions.AddMisaConnectESignCore` (in `src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`) to register `InMemoryTokenCache` (TryAddSingleton), `DefaultTokenCacheKeySelector` (TryAddSingleton), `SingleFlightRefresh` (Singleton), `RefreshAccessToken` (Scoped), `RemoteSigningAuthHandler` (Transient). Wire `RemoteSigningAuthHandler` into the typed `HttpClient` pipeline **inside** any future transport-retry handler (still outermost in US2 — US3 adds the outer retry handler).
- [ ] T089 [US2] Remove / collapse the temporary `IAuthHeaderSource` seam introduced in T060 — `ClientHeadersHandler` now only injects `x-clientId` / `x-clientKey`; `AuthorizationRM` is owned exclusively by `RemoteSigningAuthHandler`.

### Tests for US2

- [ ] T090 [P] [US2] Create `tests/MisaConnect.ESign.UnitTests/Authentication/EnsureAccessTokenTests.cs` — cache-hit reuses, cache-expired triggers proactive refresh (FR-005), cache-miss triggers login. Uses a fake `ITokenCache` + fake `ISystemClock`.
- [ ] T091 [P] [US2] Create `tests/MisaConnect.ESign.UnitTests/Authentication/RefreshTokenSingleFlightTests.cs` — SC-007 contract test: 8 parallel `SingleFlightRefresh.RefreshAsync` calls with the same cache key fire exactly 1 underlying factory invocation; all 8 waiters get the same `AccessToken` result. A second wave after completion fires a fresh factory invocation (no slot leak). Failure propagation: a failing factory surfaces the same `AuthenticationFailedException` to all 8 waiters.
- [ ] T092 [P] [US2] Create `tests/MisaConnect.ESign.UnitTests/Http/RemoteSigningAuthHandlerTests.cs` — three scenarios: (a) cache hit → header injected, no refresh, single attempt; (b) 401 → refresh succeeds → original request retried once with new token → success; (c) 401 → refresh fails → `AuthenticationFailedException` surfaced, no third attempt.
- [ ] T093 [P] [US2] Create `tests/MisaConnect.ESign.IntegrationTests/EndToEnd/TokenCacheReuseFakeServerTests.cs` — SC-004: two back-to-back `SignPdfAsync` calls against `FakeMisaESignServer`; assert exactly **one** `/login-api` hit and zero `/refreshtoken` hits.
- [ ] T094 [P] [US2] Create `tests/MisaConnect.ESign.IntegrationTests/EndToEnd/RefreshStampedeFakeServerTests.cs` — SC-007: `FakeMisaESignServer` is configured to return 401 on the next `Certificates/by-userId` call for all callers, then succeed; 8 concurrent `SignPdfAsync` calls fire; assert exactly **one** `/refreshtoken` outbound and 8 successful sign completions. Verified by request counters on the fake server.

**Checkpoint**: US1 + US2 both work independently. Token cache reuse and 401-refresh-with-single-flight are observable. SC-004 + SC-007 pass against `FakeMisaESignServer`.

---

## Phase 5: User Story 3 — Typed, observable error surface (Priority: P3)

**Goal**: Every documented MISA `errorCode` for the seven slice-1 endpoints maps to a typed SDK exception carrying `errorCode` + correlation ID. Every outbound MISA request gets a correlation ID; every log entry and surfaced exception references it. Logs never leak access tokens, refresh tokens, `AuthorizationRM` header values, certificate private bytes, raw PDF bytes, or end-user PII at any default log level.

**Independent Test**: Drive the SDK against `FakeMisaESignServer` configured to emit each `errorCode` row from [contracts/error-mapping.md](./contracts/error-mapping.md). Assert (a) the surfaced exception type and `RawCode`/`Category`/`Requires2FA`/`RequiresUserCertSetup` properties match the table; (b) a captured-log scan over the full unit + integration suites contains zero hits for token strings, raw bytes, or PII (SC-006). Drive the transport-retry handler with synthetic 5xx/429/timeout responses to verify FR-023/024/025.

### Application layer — error mapping (US3)

- [ ] T095 [US3] Create `Application.Errors.ESignErrorMapper` in `src/MisaConnect.ESign.Application/Errors/ESignErrorMapper.cs` implementing the full mapping table from [contracts/error-mapping.md §A](./contracts/error-mapping.md#a-mapping-table). One static `Map(string endpoint, int statusCode, ResponseError? envelope, string correlationId, MisaESignOptions options)` entry point that returns the typed `ESignException` subclass. Honors `MisaESignOptions.Errors.IncludeRawErrorMessage` for `Detail` population per [contracts/error-mapping.md §B](./contracts/error-mapping.md#b-property-population-rules). FR-018.

### Infrastructure layer — log scrubbing, call logging, transport retry (US3)

- [ ] T096 [P] [US3] Create `Infrastructure.Logging.ESignLogScrubber` in `src/MisaConnect.ESign.Infrastructure/Logging/ESignLogScrubber.cs` per [research.md §R-9](./research.md#r-9-log-scrubbing--copy-the-einvoice-playbook-extend-the-deny-list). One method: `string Scrub(string raw)` that redacts bearer tokens, refresh tokens, `AuthorizationRM` header bodies, base64 cert chunks, base64 doc bytes / hashes / digests, and PII fields (email / phoneNumber / firstName / lastName). FR-019.
- [ ] T097 [US3] Create `Infrastructure.Logging.ESignCallLogger : IMisaESignWireClient` decorator in `src/MisaConnect.ESign.Infrastructure/Logging/ESignCallLogger.cs` — wraps `MisaESignWireClient`, emits one Information-level structured log entry per call (`{Endpoint}`, `{Method}`, `{StatusCode}`, `{CorrelationId}`, `{DurationMs}` — never the body), one Warning on transient retries, one Error on terminal failures. All string interpolation routes through `ESignLogScrubber`. Mirrors `MeInvoiceCallLogger`.
- [ ] T098 [US3] Create `Infrastructure.Http.TransientFailureRetryHandler : DelegatingHandler` in `src/MisaConnect.ESign.Infrastructure/Http/TransientFailureRetryHandler.cs` per [research.md §R-3](./research.md#r-3-transport-retry-policy--bounded-exponential-backoff-with-full-jitter-separate-budget-from-auth-refresh). Constructor takes `IOptions<MisaESignOptions>` + `IDelayer` + an internal `IRandom` seam. Retries 5xx / 429 / `HttpRequestException` / operation-timeout `TaskCanceledException`. Bounded exponential backoff with full jitter. Honors `Retry-After` on 429 (clamped to `MaxDelay`). On exhaustion → `ESignTransportException(LastStatusCode, AttemptCount)`. Caller cancellation propagates unchanged. FR-023 / FR-024 / FR-025.
- [ ] T099 [US3] Update `MisaESignWireClient` (all seven methods, in `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs`) to: (a) inject a correlation-ID header (`X-Correlation-Id`) sourced from `ICorrelationIdAccessor.Current` on every outbound request; (b) on any 4xx response, parse the body for `ResponseErrorDto`, then call `ESignErrorMapper.Map(...)` and throw the typed exception. Replaces the US1 "echo raw errorCode" fallback. FR-017 / FR-018.
- [ ] T100 [US3] Update `Infrastructure.DependencyInjection.ServiceCollectionExtensions.AddMisaConnectESignCore` (in `src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`) to: register `ESignLogScrubber` (Singleton), register the `IMisaESignWireClient` decorator chain so `ESignCallLogger` wraps `MisaESignWireClient`, and wire `TransientFailureRetryHandler` into the typed `HttpClient` pipeline as the **outermost** handler (order: `TransientFailureRetryHandler → RemoteSigningAuthHandler → ClientHeadersHandler → network`).

### Tests for US3

- [ ] T101 [P] [US3] Create `tests/MisaConnect.ESign.UnitTests/Errors/ESignErrorMapperTests.cs` — one `[Theory]` row per entry in [contracts/error-mapping.md §A.1–§A.8](./contracts/error-mapping.md#a1-login-api-e1). Asserts exception type, `Category`, `RawCode`, `CorrelationId`, plus `Requires2FA` / `RequiresUserCertSetup` / `TerminalStatus` / `AttemptCount` where applicable. SC-005.
- [ ] T102 [P] [US3] Create `tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs` — facts: bearer-token replaced, refresh-token replaced, `AuthorizationRM` body replaced, base64 chunk over 32 chars replaced, email replaced, phone replaced, firstName/lastName replaced; non-sensitive strings (correlation ID, endpoint, status code, duration) preserved.
- [ ] T103 [P] [US3] Create `tests/MisaConnect.ESign.UnitTests/Http/TransientFailureRetryHandlerTests.cs` — facts: 5xx retried up to `MaxAttempts` and then surfaces `ESignTransportException`; 429 with `Retry-After: 5` delays at most `MaxDelay`; transient `HttpRequestException` retried; operation-timeout `TaskCanceledException` (token not cancelled by caller) retried; caller-cancelled `TaskCanceledException` propagated unchanged; `MaxAttempts = 1` disables retries. Uses an injected fake `IDelayer` so tests complete in <50 ms each.
- [ ] T104 [P] [US3] Create `tests/MisaConnect.ESign.IntegrationTests/EndToEnd/TerminalStateFakeServerTests.cs` — three facts against `FakeMisaESignServer`: `Signing/status` returns FAILED → `SignTerminalStateException(TerminalStatus = FAILED, TransactionId)`; returns CANCELLED → ditto; never returns SUCCESS within `Polling.TotalTimeout = 1s` → `SignTimeoutException`. Verifies the `transactionId` is preserved on the exception.
- [ ] T105 [P] [US3] Create `tests/MisaConnect.ESign.UnitTests/Logging/CapturedLogScanTests.cs` — SC-006: drives the orchestrator end-to-end against a stub `IMisaESignWireClient` that returns known-sensitive payloads (synthetic token "TKN-ABCDEF", PII strings "user@example.com", "0987654321", base64-encoded raw bytes), captures all log lines via a test `ILoggerProvider`, asserts zero substring hits across the captured output. Run once with `IncludeRawErrorMessage = true` and once with `false` — both must yield zero hits.

**Checkpoint**: All three user stories are independently functional. SC-005 + SC-006 pass. Every error path is typed; every log line is scrubbed.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, family-level updates, and release-ready hygiene. Nothing functional changes here.

- [ ] T106 [P] Update [docs/architecture.md](../../docs/architecture.md) — fill in the v2.0 `MisaConnect.ESign` row in the future-products table; add a brief paragraph noting the ESign csprojs follow the same layer rules as eInvoice; reference [docs/misa-esign-spec-plan.md](../../docs/misa-esign-spec-plan.md) for the slice roadmap.
- [ ] T107 [P] Update [docs/sandbox-setup.md](../../docs/sandbox-setup.md) — add a "MISA eSign sandbox" section documenting the `MISACONNECT_ESIGN_SANDBOX_USERNAME`, `MISACONNECT_ESIGN_SANDBOX_PASSWORD`, `MISACONNECT_ESIGN_SANDBOX_CLIENTID`, `MISACONNECT_ESIGN_SANDBOX_CLIENTKEY`, `MISACONNECT_ESIGN_SANDBOX_BASEURL` env vars and the `Misa__ESign__*` configuration binder pair, plus a "Production cutover" entry for eSign. Mirror the structure of the existing eInvoice section.
- [ ] T108 [P] Update [README.md](../../README.md) — add a `MisaConnect.ESign` supported-operations table (one row: `SignPdfAsync`), a "Quickstart for ESign" pointer to [quickstart.md](./quickstart.md), and the install command (`dotnet add package MisaConnect.ESign --version 2.0.0-preview.1`).
- [ ] T109 Update [CHANGELOG.md](../../CHANGELOG.md) — add an `[Unreleased]` section entry under "Added" describing the `MisaConnect.ESign` slice-1 surface: `IMisaESignClient.SignPdfAsync`, `services.AddMisaConnectESign(IConfiguration)`, the public port interfaces, the `MisaESignOptions` shape, and the typed exception hierarchy.
- [ ] T110 Verify the namespace-audit / layer-audit test (T008) still passes after every new file landed; tighten the deny-list if any new `Microsoft.Extensions.*` packages were pulled into Domain or Application by accident.
- [ ] T111 Run `dotnet format MisaConnect.slnx` from repo root and commit the result. Required per [CLAUDE.md](../../CLAUDE.md#build--test).
- [ ] T112 Run `dotnet build MisaConnect.slnx` end-to-end with `TreatWarningsAsErrors=true` and confirm zero warnings. Per [CLAUDE.md](../../CLAUDE.md#build--test).
- [ ] T113 Run `dotnet test tests/MisaConnect.ESign.UnitTests` — assert total runtime is under 30 seconds (SC-002) and zero network calls were made (check via a `DelegatingHandler` test fixture that throws on any outbound request).
- [ ] T114 Run `dotnet test tests/MisaConnect.ESign.IntegrationTests` — confirm `[SandboxFact]` tests skip cleanly when `MISACONNECT_ESIGN_SANDBOX_*` env vars are absent (SC-003); when env vars are present, confirm the happy path passes against the sandbox (SC-001).
- [ ] T115 Walk through [quickstart.md](./quickstart.md) §3–§5 against a fresh scratch console app: `dotnet new console`, `dotnet add package MisaConnect.ESign --version 2.0.0-preview.1` (or a `dotnet pack` + local NuGet feed), copy the §5 snippet, point at the sandbox, run, confirm a signed PDF lands on disk. Record any DX papercut as a GitHub issue tagged `MisaConnect.ESign`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start here. Most tasks parallelizable except T007 (slnx update depends on T001–T006) and T008 (namespace-audit depends on csprojs existing).
- **Phase 2 (Foundational)**: Depends on Phase 1. Within Phase 2, the Domain tasks (T009–T015) are fully parallel, as are the Application port tasks (T016–T019), as are the wire DTOs (T025–T032) and mappers (T033–T036). T022 depends on T021; T040 depends on T021/T022/T037/T038/T039. **Blocks all user stories.**
- **Phase 3 (US1)**: Depends on Phase 2 complete. Within US1, T051–T057 are parallel; T058 depends on T051–T057; T061–T066 are each independent of each other but all depend on T039 (skeleton). T067 depends on T051–T066. T068 depends on T058 + T044. T069 depends on T046 + T044. Tests T070–T078 + T080 are independent of each other; T079 depends on T079's `[SandboxFact]` predicate being implemented (carried over from `TestSupport`).
- **Phase 4 (US2)**: Depends on Phase 2 + US1 facade existing (T068). Within US2, T081/T082/T083/T084 are parallel; T085 depends on T026; T086 depends on T081 + T082 + T084; T087 depends on T086; T088 depends on T067 + T086. Tests T090–T094 parallel.
- **Phase 5 (US3)**: Depends on Phase 2 + US1 wire client (T061–T066). Within US3, T095 / T096 / T098 parallel; T097 depends on T096; T099 depends on T095; T100 depends on all of T097/T098/T099. Tests T101–T105 parallel.
- **Phase 6 (Polish)**: Depends on all desired user stories being complete. Within polish, T106–T108 + T110 are parallel; T109 sequential; T111–T115 sequential at the end (each depends on the previous being green).

### User Story Independence

- **US1** can be implemented and demoed without US2 or US3 — happy-path login + sign + return, no cache reuse, minimal error fallback. Ships as MVP.
- **US2** extends US1 with cache + refresh + single-flight. Without US1's facade and wire-client method implementations, US2 has nothing to layer on top of.
- **US3** extends both US1 and US2 with the full error mapper + log scrubber + transport-retry handler. Without US1's wire calls and US2's auth handler, the retry/scrubbing layers have nothing to wrap.

A reasonable parallel-staffing split is: Developer A on US1, Developer B on US2 (starts once US1's wire-client methods exist), Developer C on US3 (starts once US1's wire-client methods + Domain error hierarchy exist).

### Within Each User Story

- Use cases / ports before adapter / handler implementations.
- Adapter / handler implementations before facade wiring.
- Facade wiring before integration tests.
- Unit tests can be written either before or after the production code they cover — but they must land in the same PR.

---

## Parallel Execution Examples

### Phase 2: Domain layer parallelism

```text
# Once T001–T007 (Phase 1) complete, kick off all Domain types in parallel:
- T009: AuthSession.cs
- T010: Certificate.cs / CertificateChain.cs / KeyStatus.cs
- T011: PdfDocument.cs / SignedDocument.cs
- T012: HashAlgorithm.cs + SignatureInfo.cs + SignatureDescription.cs + SignaturePosInfo.cs + SignTransaction.cs + SignStatus.cs
- T013: ESignErrorCategory.cs + ResponseError.cs
- T014: ESignException.cs
- T015: AuthenticationFailedException.cs + NoActiveCertificateException.cs + SignRejectedException.cs + SignTerminalStateException.cs + SignTimeoutException.cs + ESignTransportException.cs
```

### Phase 2: Wire DTO parallelism

```text
# Once T020/T021 land (so the Configuration types are stable), wire DTOs are fully parallel:
- T025: LoginDtos.cs
- T026: RefreshTokenDtos.cs
- T027: CertificateDtos.cs
- T028: HashDtos.cs
- T029: SignHashDtos.cs
- T030: SignStatusDtos.cs
- T031: AttachmentDtos.cs
- T032: ResponseErrorDto.cs
```

### Phase 3: US1 use-case parallelism

```text
# After T040 + T044 land, the Application use cases are independent:
- T051: EnsureAccessToken.cs
- T052: ListActiveCertificates.cs
- T053: HashPdfDocument.cs
- T054: SubmitSignHash.cs
- T055: PollSignStatus.cs
- T056: AttachSignature.cs
- T057: SignPdfRequestValidator.cs
# Then T058 (SignPdf orchestrator) joins them.
```

### Phase 3 + 4 + 5: Cross-story parallelism with 3 developers

```text
# Once Phase 2 is complete:
Developer A: US1 (T051 → T080) — happy path, ~3 days
Developer B: US2 (T081 → T094) — kicks off once T061 (LoginAsync) lands, parallel with the rest of US1
Developer C: US3 (T095 → T105) — kicks off once T015 (exception types) + T061 (LoginAsync) land
# Integrate at T088 (US2 DI update) and T100 (US3 DI update) — these touch the shared DI extension.
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1: Setup (~1 day).
2. Complete Phase 2: Foundational — Domain + ports + wire DTOs + DI skeleton (~3 days).
3. Complete Phase 3: US1 — login + cert select + hash + sign + poll + attach (~3 days).
4. **Stop and validate**: run T078 (fake-server E2E) and T079 (sandbox E2E). If both pass, the MVP is shippable as `MisaConnect.ESign 2.0.0-preview.1`.
5. Optional: publish the preview NuGet for early-adopter consumers to integrate while US2 + US3 land.

### Incremental Delivery

- Preview.1 = Phases 1 + 2 + 3 (US1 only — happy path, no cache, minimal errors).
- Preview.2 = + Phase 4 (US2 — cache reuse + 401 refresh + single-flight).
- Preview.3 = + Phase 5 (US3 — typed errors + log scrubbing + transport retry).
- 2.0.0 stable = + Phase 6 (polish — docs, CHANGELOG, README, sandbox-setup) once the preview line has stabilized.

### Parallel Team Strategy

With three developers, once Phase 2 lands:

- **Dev A** owns US1 (`SignPdf` happy path) + the EsignFake state machine (T077) + the sandbox E2E (T079).
- **Dev B** owns US2 (cache + refresh + single-flight) + SC-007 verification (T094).
- **Dev C** owns US3 (error mapper + log scrubber + transport retry) + SC-005 verification (T101) + SC-006 verification (T105).

Synchronization points: the DI extension is touched by all three (T067 / T088 / T100) — coordinate via short-lived PRs so the registration order stays correct (transport-retry outermost, then auth, then client-headers, then network).

---

## Notes

- **[P]** = different files, no dependencies on incomplete tasks. Run in parallel.
- **[Story]** = US1 / US2 / US3 label maps the task to the user story it advances. Setup, Foundational, and Polish tasks intentionally have no story label.
- Every task includes the exact file path the work lands at.
- Tests are required by the spec (FR-020 / FR-021 / FR-022); SC-002 / SC-005 / SC-006 / SC-007 are test-verified. Unit tests must run in <30 s with no network; integration tests must skip cleanly when the sandbox is unreachable.
- `dotnet format MisaConnect.slnx` (T111) and zero-warning `dotnet build` (T112) are mandatory before PR per [CLAUDE.md](../../CLAUDE.md#build--test).
- Commit after each task or each tight logical group (a use case + its unit test, a wire-client method + its wire-format unit test). Small PRs over large ones.
- The Constitution principles in [.specify/memory/constitution.md](../../.specify/memory/constitution.md) — especially I (layer rules), IV (wire-format fidelity), VI (tests-are-the-spec), VIII (no PII/secrets in logs) — are enforced by tests; if any test starts failing during this slice, **fix the production code, do not relax the test**.

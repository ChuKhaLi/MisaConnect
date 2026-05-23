# Tasks: MISA eSign — Webhook Receiver

**Branch**: `004-misa-esign-webhook` | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)
**Input**: Design documents from `specs/004-misa-esign-webhook/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are REQUIRED. Constitution Principle VI ("Tests are the spec") plus FR-095 / FR-096 / FR-097 / FR-098 mandate full unit + integration + sandbox + sample-API coverage. The `TreatWarningsAsErrors=true` invariant from `Directory.Build.props` also applies — every new file must compile without warnings. SC-033 requires the cumulative unit suite (slices 1+2+3+4) to stay under 30 seconds total.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps task to user story for traceability (US1, US2, US3); Setup / Foundational / Polish phases have no story label
- All file paths are repository-relative

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the slice-1 + slice-2 + slice-3 codebase is healthy before slice-4 work begins. No new projects or dependencies — slice 4 adds files to the four production projects, two test projects, and the sample API established by the prior slices.

- [X] T001 Confirm working tree is on `004-misa-esign-webhook` and clean by running `git status` from repo root
- [X] T002 [P] Verify baseline build is green by running `dotnet build MisaConnect.slnx` from repo root (warnings break the build under `TreatWarningsAsErrors=true`)
- [X] T003 [P] Verify baseline unit tests pass and stay under 30s by running `dotnet test tests/MisaConnect.ESign.UnitTests/MisaConnect.ESign.UnitTests.csproj` from repo root
- [X] T004 [P] Verify baseline integration tests skip cleanly without sandbox creds by running `dotnet test tests/MisaConnect.ESign.IntegrationTests/MisaConnect.ESign.IntegrationTests.csproj` from repo root
- [ ] T005 [P] Tasks-time Postman tie-breaker check per research R-3 and R-7: download the Postman collection at the URL recorded in [research.md R-7](./research.md), grep for `signature`/`hmac`/`x-misa-signature` on webhook endpoints AND confirm success-ACK `errorCode` sentinel; if the collection contradicts Assumption 6 (success sentinel) or Assumption 11 (no signature header), record the finding in [research.md](./research.md) before any implementation tasks proceed

**Checkpoint**: Slice-1 + slice-2 + slice-3 baselines are green. Postman findings (if any) are recorded.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain types, port additions, configuration shape, wire DTOs, the in-memory session store, the validator, the scrubber extensions, the error-mapper extensions, and the slice-1/3 internal refactor (split `Sign{Format}` into `BeginSign{Format}ForPolling` + `AttachSignature{Format}` compositions) that MUST exist before any user story can be implemented. Every user story (US1/US2/US3) imports types from this phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete. FR-089 / FR-091 / SC-032 (slice-1 PDF + slice-3 multi-format byte-identical paths) are enforced as the last gate of this phase — if the existing PDF + XML + Word + Excel integration tests don't pass after the foundational refactor, no user story work begins until they do.

### Domain — new value types

- [X] T006 [P] Create `WebhookStatus` byte-backed closed enum (`Success = 1, Failed = 2, Cancelled = 3`) per [data-model.md §1.2](./data-model.md) in [src/MisaConnect.ESign.Domain/Webhook/WebhookStatus.cs](src/MisaConnect.ESign.Domain/Webhook/WebhookStatus.cs)
- [X] T007 [P] Create `WebhookValidationCategory` byte-backed enum (`None = 0, MalformedEnvelope = 1, ClientIdMismatch = 2, UnknownTransaction = 3, IncompleteSuccessEnvelope = 4, DocumentIdMismatch = 5`) per [data-model.md §1.6](./data-model.md) in [src/MisaConnect.ESign.Domain/Webhook/WebhookValidationCategory.cs](src/MisaConnect.ESign.Domain/Webhook/WebhookValidationCategory.cs)
- [X] T008 [P] Create `WebhookSignature` record (`string DocumentId, string Signature`) per [data-model.md §1.2](./data-model.md) in [src/MisaConnect.ESign.Domain/Webhook/WebhookSignature.cs](src/MisaConnect.ESign.Domain/Webhook/WebhookSignature.cs)
- [X] T009 [P] Create `WebhookEnvelope` sealed record `(string MessageId, string ClientId, IReadOnlyDictionary<string, JsonElement>? ExtraData, WebhookStatus Status, string? ErrorCode, string TransactionId, IReadOnlyList<WebhookSignature> Signatures)` per [data-model.md §1.2](./data-model.md) in [src/MisaConnect.ESign.Domain/Webhook/WebhookEnvelope.cs](src/MisaConnect.ESign.Domain/Webhook/WebhookEnvelope.cs)
- [X] T010 [P] Create `WebhookAck` sealed record `(string ErrorCode, string DevMsg, string UserMsg)` with static `Success(string code)` and `Failure(string code, string devMsg, string userMsg)` factories per [data-model.md §1.3](./data-model.md) in [src/MisaConnect.ESign.Domain/Webhook/WebhookAck.cs](src/MisaConnect.ESign.Domain/Webhook/WebhookAck.cs)
- [X] T011 [P] Create `BeginResult` sealed record `(string TransactionId, DocumentFormat Format)` per [data-model.md §1.5](./data-model.md) in [src/MisaConnect.ESign.Domain/Webhook/BeginResult.cs](src/MisaConnect.ESign.Domain/Webhook/BeginResult.cs)
- [X] T012 [P] Create `WebhookOutcome` abstract record + three nested sealed subclasses (`SuccessWithSignedBytes`, `FailureWithError`, `TerminalWithoutFinalize`) carrying `CorrelationId` on the base + per-subclass fields per [data-model.md §1.4](./data-model.md) in [src/MisaConnect.ESign.Domain/Webhook/WebhookOutcome.cs](src/MisaConnect.ESign.Domain/Webhook/WebhookOutcome.cs)

### Domain — session record + per-format payload union

- [X] T013 [P] Create `PerFormatHashPayload` abstract record with four nested sealed subclasses (`Pdf(PdfHashOutput)`, `Xml(XmlHashOutput)`, `Word(WordExcelHashOutput)`, `Excel(WordExcelHashOutput)`) per [data-model.md §1.1](./data-model.md) in [src/MisaConnect.ESign.Domain/Sessions/PerFormatHashPayload.cs](src/MisaConnect.ESign.Domain/Sessions/PerFormatHashPayload.cs)
- [X] T014 [P] Create `SigningSessionCachedSuccess` sealed record `(string TriggeringMessageId, WebhookAck Ack, byte[] SignedBytes)` per [data-model.md §1.1](./data-model.md) in [src/MisaConnect.ESign.Domain/Sessions/SigningSessionCachedSuccess.cs](src/MisaConnect.ESign.Domain/Sessions/SigningSessionCachedSuccess.cs)
- [X] T015 Create `SigningSession` sealed record `(string ClientId, string TransactionId, DocumentFormat Format, PerFormatHashPayload HashPayload, IReadOnlyList<string> RecordedDocumentIds, DateTimeOffset CreatedAtUtc, TimeSpan Ttl, IReadOnlySet<string> ObservedMessageIds, SigningSessionCachedSuccess? CachedSuccess)` with construction-time invariants (non-empty `ClientId`/`TransactionId`, `Format != Unknown`, `HashPayload` case matches `Format`, `RecordedDocumentIds` non-empty, `Ttl > TimeSpan.Zero`) per [data-model.md §1.1](./data-model.md) in [src/MisaConnect.ESign.Domain/Sessions/SigningSession.cs](src/MisaConnect.ESign.Domain/Sessions/SigningSession.cs) — depends on T013, T014

### Domain — typed exception family

- [X] T016 Create `WebhookValidationException` abstract base inheriting `ESignException` carrying `Category`, `CorrelationId`, `MatchedTransactionId` per [data-model.md §1.7](./data-model.md) in [src/MisaConnect.ESign.Domain/Errors/WebhookValidationException.cs](src/MisaConnect.ESign.Domain/Errors/WebhookValidationException.cs) (base + five `sealed` subclasses `MalformedEnvelopeException`, `ClientIdMismatchException`, `UnknownTransactionException`, `IncompleteSuccessEnvelopeException`, `DocumentIdMismatchException` all in this single file; pre-session-resolution subclasses default `Format = DocumentFormat.Unknown`; post-session subclasses set `Format` from the resolved session)

### Application — port and abstractions

- [X] T017 [P] Create `ISigningSessionStore` port interface (`RegisterAsync`, `TryGetByTransactionIdAsync`, `RecordObservedMessageIdAsync`, `CacheSuccessAsync` — all `ValueTask`-returning with `CancellationToken`) including XML docstring documenting the single-flight contract per FR-078 + [research.md R-2](./research.md) in [src/MisaConnect.ESign.Application/Abstractions/ISigningSessionStore.cs](src/MisaConnect.ESign.Application/Abstractions/ISigningSessionStore.cs)
- [X] T018 [P] Create `IWebhookDeliveryHook` interface (`Task DeliverAsync(WebhookOutcome outcome, CancellationToken ct)`) per [data-model.md §2.3](./data-model.md) in [src/MisaConnect.ESign.Application/Webhook/IWebhookDeliveryHook.cs](src/MisaConnect.ESign.Application/Webhook/IWebhookDeliveryHook.cs)
- [X] T019 [P] Create internal `IFinalizeLockOwner` adapter-side extension contract (`ValueTask<IAsyncDisposable> AcquireFinalizeLockAsync(string clientId, string transactionId, CancellationToken ct)`) per [research.md R-5](./research.md) in [src/MisaConnect.ESign.Application/Abstractions/IFinalizeLockOwner.cs](src/MisaConnect.ESign.Application/Abstractions/IFinalizeLockOwner.cs) (the port-side contract; in-memory adapter implements it, distributed adapters implement equivalent locking)

### Application — validator

- [X] T020 Create `WebhookEnvelopeValidator` with `AssertClientIdMatch(envelope, configuredClientId, correlationId)` and `AssertSuccessShapeIsValid(envelope, session, correlationId)` throwing the right `WebhookValidationException` subclass on failure per [data-model.md §2.2.4](./data-model.md) + FR-082 in [src/MisaConnect.ESign.Application/Validation/WebhookEnvelopeValidator.cs](src/MisaConnect.ESign.Application/Validation/WebhookEnvelopeValidator.cs) — depends on T009, T015, T016

### Application — error mapper extension

- [X] T021 Edit `ESignErrorMapper` to add `MapWebhookValidationToAck(WebhookValidationException ex)` returning `WebhookAck` with the namespaced failure codes per [contracts/error-mapping.md §A.11](./contracts/error-mapping.md) (`webhook.malformed`, `webhook.client_id_mismatch`, `webhook.unknown_transaction`, `webhook.incomplete_success`, `webhook.document_id_mismatch`) — preserve existing slice-1/2/3 mappings unchanged in [src/MisaConnect.ESign.Application/Errors/ESignErrorMapper.cs](src/MisaConnect.ESign.Application/Errors/ESignErrorMapper.cs)

### Application — slice-1 / slice-3 internal refactor (FR-089 / FR-091)

- [ ] T022 Edit slice-1 `Application.UseCases.SignPdf` to extract the pre-`/Signing/hash` half into a private `BeginSignPdfCore` helper method returning `(SigningSession sessionDraft, string transactionId)` and keep the existing `Sign{Format}` orchestrator composing `BeginSignPdfCore → PollSignStatus → AttachSignaturePdf` so behavior remains byte-identical per FR-089 in [src/MisaConnect.ESign.Application/UseCases/SignPdf.cs](src/MisaConnect.ESign.Application/UseCases/SignPdf.cs); the blocking path MUST NOT call `ISigningSessionStore` per [research.md R-1](./research.md)
- [ ] T023 [P] Edit slice-3 `Application.UseCases.SignXml` symmetrically with T022 in [src/MisaConnect.ESign.Application/UseCases/SignXml.cs](src/MisaConnect.ESign.Application/UseCases/SignXml.cs)
- [ ] T024 [P] Edit slice-3 `Application.UseCases.SignWord` symmetrically in [src/MisaConnect.ESign.Application/UseCases/SignWord.cs](src/MisaConnect.ESign.Application/UseCases/SignWord.cs)
- [ ] T025 [P] Edit slice-3 `Application.UseCases.SignExcel` symmetrically in [src/MisaConnect.ESign.Application/UseCases/SignExcel.cs](src/MisaConnect.ESign.Application/UseCases/SignExcel.cs)
- [X] T026 Edit the blocking `SignPdf` / `SignXml` / `SignWord` / `SignExcel` facade entry points to add the `GuardModeIsNotWebhook(options)` guard at the top — throw `InvalidOperationException("polling mode disabled")` when `MisaESignOptions.Webhook.Mode == WebhookMode.Webhook` per FR-093 (applies edits across all four use-case files touched in T022-T025)

### Infrastructure — wire DTOs

- [X] T027 [P] Create internal `WebhookEnvelopeDto` + `WebhookSignatureDto` records with `[JsonPropertyName]` attributes mirroring MISA §3.8 / §4.9 verbatim + `ToDomain()` mapping extension method + `ParseStatus` helper that throws `MalformedEnvelopeException` for unknown status values per [data-model.md §3.1](./data-model.md) in [src/MisaConnect.ESign.Infrastructure/ESign/Webhook/WebhookEnvelopeDto.cs](src/MisaConnect.ESign.Infrastructure/ESign/Webhook/WebhookEnvelopeDto.cs)
- [X] T028 [P] Create internal `WebhookAckDto` record mirroring MISA §4.9 verbatim with `FromDomain(WebhookAck)` static factory per [data-model.md §3.2](./data-model.md) in [src/MisaConnect.ESign.Infrastructure/ESign/Webhook/WebhookAckDto.cs](src/MisaConnect.ESign.Infrastructure/ESign/Webhook/WebhookAckDto.cs)
- [X] T029 [P] Create internal static `MisaWebhookAckCodes` with constants for `Success = "0"` (per [research.md R-3](./research.md); subject to Postman verification in T005), `MalformedEnvelope`, `ClientIdMismatch`, `UnknownTransaction`, `IncompleteSuccessEnvelope`, `DocumentIdMismatch`, `FinalizeFailed` per [data-model.md §3.4](./data-model.md) in [src/MisaConnect.ESign.Infrastructure/ESign/Webhook/MisaWebhookAckCodes.cs](src/MisaConnect.ESign.Infrastructure/ESign/Webhook/MisaWebhookAckCodes.cs)

### Infrastructure — in-memory session store adapter

- [X] T030 Create internal `InMemorySigningSessionStore` implementing both `ISigningSessionStore` and `IFinalizeLockOwner` — `ConcurrentDictionary<(string ClientId, string TransactionId), SessionEntry>` keyed store, per-entry `SemaphoreSlim(1, 1)` for single-flight, TTL eviction on read via `ISystemClock`, atomic `RecordObservedMessageIdAsync` via `record with` copy, `CacheSuccessAsync` writes the cached snapshot once per FR-080, `AcquireFinalizeLockAsync` returns an `IAsyncDisposable` that releases the semaphore on dispose per [data-model.md §3.3](./data-model.md) + [research.md R-5](./research.md) in [src/MisaConnect.ESign.Infrastructure/Sessions/InMemorySigningSessionStore.cs](src/MisaConnect.ESign.Infrastructure/Sessions/InMemorySigningSessionStore.cs) — depends on T015, T017, T019

### Infrastructure — configuration

- [X] T031 [P] Create `WebhookMode` byte-backed enum (`Polling = 1, Webhook = 2, Both = 3`) per [data-model.md §3.5](./data-model.md) in [src/MisaConnect.ESign.Infrastructure/Configuration/WebhookMode.cs](src/MisaConnect.ESign.Infrastructure/Configuration/WebhookMode.cs)
- [X] T032 [P] Create `MisaESignWebhookSessionOptions` class with `TimeSpan Ttl { get; set; } = TimeSpan.FromHours(24);` per Assumption 4 / FR-093 in [src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignWebhookSessionOptions.cs](src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignWebhookSessionOptions.cs)
- [X] T033 [P] Create `MisaESignWebhookOptions` class with `WebhookMode Mode = Both`, `MisaESignWebhookSessionOptions Session = new()`, `string? Path = "/esign/webhook"`, `string? Secret`, `string[]? AllowedIps` properties per [data-model.md §3.5](./data-model.md) + FR-093 / FR-094 / FR-099 / FR-100 in [src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignWebhookOptions.cs](src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignWebhookOptions.cs) — depends on T031, T032
- [X] T034 Edit `MisaESignOptions` to add `public MisaESignWebhookOptions Webhook { get; set; } = new();` per [data-model.md §3.5](./data-model.md) in [src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptions.cs](src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptions.cs) — depends on T033
- [X] T035 Edit `MisaESignOptionsValidator` to add startup-time validation rules: `Webhook.Session.Ttl > TimeSpan.Zero`; `Webhook.Secret` is null OR `Length >= 32`; every entry in `Webhook.AllowedIps` parses as `System.Net.IPNetwork` per FR-099 + FR-100 + [data-model.md §3.5](./data-model.md) in [src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptionsValidator.cs](src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptionsValidator.cs)

### Infrastructure — logging scrubber edits

- [X] T036 Edit `ESignLogScrubber` to add three new redaction rules per FR-087 / FR-100 / [data-model.md §3.5](./data-model.md): (a) JSON-path matchers `$.signatures[*].signature` → `<redacted>` and `$.extraData` → `<redacted>` on inbound webhook envelope captures; (b) structured-logging `IPAddress` reduction to class label (`"loopback"`, `"private-rfc1918"`, `"private-rfc6598"`, `"public"`); (c) literal-value redaction list MUST include the configured `Misa:ESign:Webhook:Secret` value (read from `IOptions<MisaESignOptions>` at scrubber-construction time) in [src/MisaConnect.ESign.Infrastructure/Logging/ESignLogScrubber.cs](src/MisaConnect.ESign.Infrastructure/Logging/ESignLogScrubber.cs)

### Infrastructure — DI wiring

- [X] T037 Edit `ServiceCollectionExtensions.AddMisaConnectESign` to register: `TryAddSingleton<ISigningSessionStore, InMemorySigningSessionStore>()`; `TryAddSingleton<IFinalizeLockOwner>(sp => (IFinalizeLockOwner)sp.GetRequiredService<ISigningSessionStore>())`; `TryAddSingleton<WebhookEnvelopeValidator>()` per [plan.md Project Structure](./plan.md) in [src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs](src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs) — depends on T017, T019, T020, T030

### Foundational — regression gate (FR-089 / FR-091 / SC-032)

- [X] T038 Run the existing slice-1 PDF integration tests (`PdfHappyPathFakeServerTests`, `PdfPollingFakeServerTests`, slice-1 `Sandbox/*` tests) and slice-3 multi-format integration tests (`MultiFormatRegressionFakeServerTests`, slice-3 XML/Word/Excel happy paths) after T022-T026 land; assert all pass byte-identically. Fix the refactor if any regression. This task is a manual gate — re-run `dotnet test tests/MisaConnect.ESign.IntegrationTests/MisaConnect.ESign.IntegrationTests.csproj` from repo root

**Checkpoint**: Foundation ready. ISigningSessionStore, all Domain types, all wire DTOs, configuration, scrubber, error mapper, and the slice-1/3 internal refactor are in place. The blocking paths still pass their existing tests byte-identically.

---

## Phase 3: User Story 1 — Initiate a webhook-mode sign and let MISA push completion (Priority: P1) 🎯 MVP

**Goal**: A consumer can call `BeginSign{Format}Async` to initiate a sign without polling, MISA POSTs the webhook envelope to the consumer's endpoint, the SDK's Application-layer `HandleWebhook` resolves the session and calls `/documents/attachment`, and the signed bytes are delivered to the consumer-supplied `IWebhookDeliveryHook` plus a documented `{ errorCode, devMsg, userMsg }` success ACK is returned to MISA. Covers FR-075, FR-076, FR-077, FR-079, FR-082 (steps 1–3 happy path), FR-083 (Failed/Cancelled short-circuit), FR-084, FR-085, FR-086, FR-088, FR-090, FR-091, FR-092, FR-093 (mode guards), FR-094 (sample-API wiring).

**Independent Test**: End-to-end against the in-repo fake server: invoke `BeginSignPdfAsync` with a one-page fixture PDF → assert non-empty `transactionId` and zero `/Signing/status` calls → POST a synthetic MISA-shaped webhook envelope referencing that `transactionId` and the configured `clientId` to the sample API's webhook endpoint → assert the SDK calls `/documents/attachment` once, returns signed PDF bytes to the consumer's `IWebhookDeliveryHook`, and emits the documented success ACK. Repeat across all four formats (Pdf, Xml, Word, Excel) — SC-025, SC-028, SC-029.

### Tests for User Story 1 (write FIRST — verify they FAIL before implementation)

- [ ] T039 [P] [US1] Create `BeginSignPdfTests` covering FR-076 (session registered AFTER `/Signing/hash` returns), happy path returns `BeginResult { Pdf }`, `/Signing/status` is never called, and mode-guard `Polling` throws `"webhook mode disabled"` in [tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignPdfTests.cs](tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignPdfTests.cs)
- [ ] T040 [P] [US1] Create `BeginSignXmlTests` symmetric with T039 in [tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignXmlTests.cs](tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignXmlTests.cs)
- [ ] T041 [P] [US1] Create `BeginSignWordTests` symmetric in [tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignWordTests.cs](tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignWordTests.cs)
- [ ] T042 [P] [US1] Create `BeginSignExcelTests` symmetric in [tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignExcelTests.cs](tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignExcelTests.cs)
- [ ] T043 [P] [US1] Create `HandleWebhookOrchestratorTests` covering: SUCCESS envelope → finalize + success ACK + delivery hook invoked once with `SuccessWithSignedBytes`; FAILED envelope (FR-083) → no finalize call, delivery hook invoked with `TerminalWithoutFinalize`, success ACK to MISA; CANCELLED envelope → identical to FAILED; correlation ID propagated to every log entry (FR-086) in [tests/MisaConnect.ESign.UnitTests/Webhook/HandleWebhookOrchestratorTests.cs](tests/MisaConnect.ESign.UnitTests/Webhook/HandleWebhookOrchestratorTests.cs)
- [ ] T044 [P] [US1] Create `FinalizeFromWebhookTests` covering one happy-path row per format (`Pdf`, `Xml`, `Word`, `Excel`) asserting the right slice-3 `AttachSignature{...}` use case is invoked with the session's recorded payload + the webhook's `signatures[]` — SC-028 in [tests/MisaConnect.ESign.UnitTests/Webhook/FinalizeFromWebhookTests.cs](tests/MisaConnect.ESign.UnitTests/Webhook/FinalizeFromWebhookTests.cs)
- [ ] T045 [P] [US1] Create `ModeGuardTests` covering FR-093 in all 6 combinations (`Mode = Polling` × 4 `BeginSign{Format}Async` calls → throw; `Mode = Webhook` × 4 `Sign{Format}Async` calls → throw; `Mode = Both` × all 8 calls → succeed) in [tests/MisaConnect.ESign.UnitTests/Webhook/ModeGuardTests.cs](tests/MisaConnect.ESign.UnitTests/Webhook/ModeGuardTests.cs)
- [ ] T046 [P] [US1] Edit `StubWireClient` to record per-format `AttachSignature{Pdf,Xml,WordExcel}Async` invocations so `FinalizeFromWebhookTests` can assert the right outbound call (extension parallel to slice-1 / slice-3 wire-recording patterns) in [tests/MisaConnect.ESign.UnitTests/TestSupport/StubWireClient.cs](tests/MisaConnect.ESign.UnitTests/TestSupport/StubWireClient.cs)
- [ ] T047 [P] [US1] Create `BeginAndWebhookHappyPathFakeServerTests` integration test that per-format (4 rows) drives the sample API: invokes `BeginSign{Format}Async` + `PostSyntheticWebhookAsync` against the fake server + asserts `WebhookHandleResultDto.Ack.ErrorCode == "0"`, delivery hook fires once with signed bytes, `/documents/attachment` counter == 1 — SC-025, SC-028, SC-029 in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/BeginAndWebhookHappyPathFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/BeginAndWebhookHappyPathFakeServerTests.cs)
- [ ] T048 [P] [US1] Create `WebhookStatusFailedFakeServerTests` integration test asserting FR-083: webhook with `status = FAILED` → no `/documents/attachment` call, delivery hook fires with `TerminalWithoutFinalize` outcome, ACK is success in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookStatusFailedFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookStatusFailedFakeServerTests.cs)

### Application — Begin use cases for webhook mode

- [X] T049 [P] [US1] Create `BeginSignPdfWorkRequest` Application record carrying the existing `SignPdfRequest` field set per [data-model.md §2.2.1](./data-model.md) in [src/MisaConnect.ESign.Application/UseCases/BeginSignPdfWorkRequest.cs](src/MisaConnect.ESign.Application/UseCases/BeginSignPdfWorkRequest.cs)
- [X] T050 [P] [US1] Create `BeginSignXmlWorkRequest` Application record carrying the slice-3 `SignXmlRequest` field set in [src/MisaConnect.ESign.Application/UseCases/BeginSignXmlWorkRequest.cs](src/MisaConnect.ESign.Application/UseCases/BeginSignXmlWorkRequest.cs)
- [X] T051 [P] [US1] Create `BeginSignWordWorkRequest` Application record in [src/MisaConnect.ESign.Application/UseCases/BeginSignWordWorkRequest.cs](src/MisaConnect.ESign.Application/UseCases/BeginSignWordWorkRequest.cs)
- [X] T052 [P] [US1] Create `BeginSignExcelWorkRequest` Application record in [src/MisaConnect.ESign.Application/UseCases/BeginSignExcelWorkRequest.cs](src/MisaConnect.ESign.Application/UseCases/BeginSignExcelWorkRequest.cs)
- [X] T053 [US1] Create `BeginSignPdf` use case running login → cert select → `HashPdfDocument` → `SubmitSignHash` → `ISigningSessionStore.RegisterAsync` (AFTER `/Signing/hash` returns per FR-076 + [research.md R-6](./research.md)) → return `BeginResult { TransactionId, Pdf }`; guards `Mode == Polling` at the top per FR-093 in [src/MisaConnect.ESign.Application/UseCases/BeginSignPdf.cs](src/MisaConnect.ESign.Application/UseCases/BeginSignPdf.cs) — depends on T015, T017, T022, T034, T049
- [X] T054 [US1] Create `BeginSignXml` symmetric with T053, format = `Xml`, hash via `HashXmlDocument`, payload `PerFormatHashPayload.Xml` in [src/MisaConnect.ESign.Application/UseCases/BeginSignXml.cs](src/MisaConnect.ESign.Application/UseCases/BeginSignXml.cs) — depends on T015, T017, T023, T034, T050
- [X] T055 [US1] Create `BeginSignWord` symmetric, format = `Word` in [src/MisaConnect.ESign.Application/UseCases/BeginSignWord.cs](src/MisaConnect.ESign.Application/UseCases/BeginSignWord.cs) — depends on T015, T017, T024, T034, T051
- [X] T056 [US1] Create `BeginSignExcel` symmetric, format = `Excel` in [src/MisaConnect.ESign.Application/UseCases/BeginSignExcel.cs](src/MisaConnect.ESign.Application/UseCases/BeginSignExcel.cs) — depends on T015, T017, T025, T034, T052

### Application — webhook handler + finalize

- [X] T057 [US1] Create `FinalizeFromWebhook` use case: acquire single-flight lock via `IFinalizeLockOwner.AcquireFinalizeLockAsync`, re-check `session.CachedSuccess` inside the lock, switch on `session.Format` dispatching to `AttachSignaturePdf` / `AttachSignatureToXml` / `AttachSignatureToWordExcel`; on success → `CacheSuccessAsync` + invoke `IWebhookDeliveryHook` with `SuccessWithSignedBytes` + return success ACK; on `ESignException` → return failure ACK without caching, without invoking the delivery hook per FR-081, FR-080, FR-078 in [src/MisaConnect.ESign.Application/UseCases/FinalizeFromWebhook.cs](src/MisaConnect.ESign.Application/UseCases/FinalizeFromWebhook.cs) — depends on T015, T017, T018, T019, T021, T030
- [X] T058 [US1] Create `HandleWebhook` orchestrator: assign correlation ID via `ICorrelationIdAccessor`, run four-step validation per FR-082 (shape was validated by deserialization → assert non-null), call `validator.AssertClientIdMatch`, look up session via `ISigningSessionStore.TryGetByTransactionIdAsync`, throw `UnknownTransactionException` if null, record observed messageId via `RecordObservedMessageIdAsync` (always — including on FAILED/CANCELLED), branch on `WebhookStatus`: SUCCESS → `validator.AssertSuccessShapeIsValid` then if `session.CachedSuccess is { } cached` return cached ACK + outcome without invoking hook (FR-080), else delegate to `FinalizeFromWebhook.RunAsync`; FAILED/CANCELLED → invoke `IWebhookDeliveryHook` with `TerminalWithoutFinalize` + return success ACK (FR-083); emit structured INFO logs at every step per FR-088 in [src/MisaConnect.ESign.Application/UseCases/HandleWebhook.cs](src/MisaConnect.ESign.Application/UseCases/HandleWebhook.cs) — depends on T017, T018, T020, T057

### Client — DTOs

- [X] T059 [P] [US1] Create per-format `BeginSignPdfRequestDto` record mirroring `SignPdfRequestDto` field-for-field in [src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignPdfRequestDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignPdfRequestDto.cs)
- [X] T060 [P] [US1] Create `BeginSignXmlRequestDto` mirroring slice-3 `SignXmlRequestDto` (incl. both `Xml` string and `XmlUtf8Bytes` overloads — Begin facade decides via which is non-null) in [src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignXmlRequestDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignXmlRequestDto.cs)
- [ ] T061 [P] [US1] Create `BeginSignWordRequestDto` mirroring slice-3 `SignWordRequestDto` in [src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignWordRequestDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignWordRequestDto.cs)
- [X] T062 [P] [US1] Create `BeginSignExcelRequestDto` mirroring slice-3 `SignExcelRequestDto` in [src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignExcelRequestDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignExcelRequestDto.cs)
- [X] T063 [P] [US1] Create `BeginSignPdfResultDto` record `(string TransactionId, DocumentFormat Format)` in [src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignPdfResultDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignPdfResultDto.cs)
- [X] T064 [P] [US1] Create `BeginSignXmlResultDto` (Format = Xml) in [src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignXmlResultDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignXmlResultDto.cs)
- [X] T065 [P] [US1] Create `BeginSignWordResultDto` (Format = Word) in [src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignWordResultDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignWordResultDto.cs)
- [X] T066 [P] [US1] Create `BeginSignExcelResultDto` (Format = Excel) in [src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignExcelResultDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/BeginSignExcelResultDto.cs)
- [X] T067 [P] [US1] Create `WebhookStatusDto` enum (mirror of `Domain.Webhook.WebhookStatus`) in [src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookStatusDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookStatusDto.cs)
- [X] T068 [P] [US1] Create `WebhookSignatureDto` public record `(string DocumentId, string Signature)` in [src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookSignatureDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookSignatureDto.cs)
- [X] T069 [P] [US1] Create public `WebhookEnvelopeDto` record mirroring MISA §3.8 / §4.9 verbatim (camelCase via `[JsonPropertyName]`) — this is the consumer-deserializable variant of the internal Infrastructure DTO per [data-model.md §6](./data-model.md) in [src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookEnvelopeDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookEnvelopeDto.cs) — depends on T067, T068
- [X] T070 [P] [US1] Create public `WebhookAckDto` record `(string ErrorCode, string DevMsg, string UserMsg)` mirroring MISA §4.9 in [src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookAckDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookAckDto.cs)
- [X] T071 [P] [US1] Create public `WebhookOutcomeDto` discriminated-union shape (mirror of `Domain.Webhook.WebhookOutcome` — abstract record base + three sealed nested subclasses) in [src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookOutcomeDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookOutcomeDto.cs)
- [X] T072 [P] [US1] Create public `WebhookHandleResultDto` record `(WebhookAckDto Ack, WebhookOutcomeDto Outcome)` returned by `IMisaESignClient.HandleWebhookAsync` in [src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookHandleResultDto.cs](src/MisaConnect.ESign.Client/Dtos/Webhook/WebhookHandleResultDto.cs) — depends on T070, T071

### Client — delivery hook interface + null default

- [X] T073 [P] [US1] Re-export `IWebhookDeliveryHook` into `Client.Webhook` namespace (consumer-facing) — typedef-style alias OR a thin pass-through interface that the SDK's Application-layer `IWebhookDeliveryHook` is wired to per [research.md R-9](./research.md); the consumer-facing interface accepts `WebhookOutcomeDto` (not the Domain `WebhookOutcome` type, to keep `Domain` out of consumer using directives) — adapter pattern: an internal `WebhookDeliveryHookAdapter : Application.IWebhookDeliveryHook` translates Domain `WebhookOutcome` to `Client.WebhookOutcomeDto` and forwards to the consumer-supplied `Client.IWebhookDeliveryHook` in [src/MisaConnect.ESign.Client/Webhook/IWebhookDeliveryHook.cs](src/MisaConnect.ESign.Client/Webhook/IWebhookDeliveryHook.cs)
- [X] T074 [P] [US1] Create internal `NullWebhookDeliveryHook : IWebhookDeliveryHook` default that logs the outcome at INFO via `ILogger<NullWebhookDeliveryHook>` and returns; registered via `TryAddSingleton` per [research.md R-9](./research.md) in [src/MisaConnect.ESign.Client/Webhook/NullWebhookDeliveryHook.cs](src/MisaConnect.ESign.Client/Webhook/NullWebhookDeliveryHook.cs)
- [X] T075 [US1] Create internal `WebhookDeliveryHookAdapter : Application.IWebhookDeliveryHook` that wraps a `Client.IWebhookDeliveryHook` and translates Domain `WebhookOutcome` → `WebhookOutcomeDto` (closed-pattern-match over the three subclasses) in [src/MisaConnect.ESign.Client/Webhook/WebhookDeliveryHookAdapter.cs](src/MisaConnect.ESign.Client/Webhook/WebhookDeliveryHookAdapter.cs) — depends on T012, T018, T071, T073

### Client — facade methods

- [X] T076 Edit `IMisaESignClient` interface to add five new methods: `Task<BeginSignPdfResultDto> BeginSignPdfAsync(BeginSignPdfRequestDto, CancellationToken)`, `Task<BeginSignXmlResultDto> BeginSignXmlAsync(BeginSignXmlRequestDto, CancellationToken)`, `Task<BeginSignWordResultDto> BeginSignWordAsync(BeginSignWordRequestDto, CancellationToken)`, `Task<BeginSignExcelResultDto> BeginSignExcelAsync(BeginSignExcelRequestDto, CancellationToken)`, `Task<WebhookHandleResultDto> HandleWebhookAsync(WebhookEnvelopeDto, CancellationToken)` per FR-075 + [contracts/public-surface.md](./contracts/public-surface.md) in [src/MisaConnect.ESign.Client/IMisaESignClient.cs](src/MisaConnect.ESign.Client/IMisaESignClient.cs)
- [X] T077 Edit `MisaESignClient` implementation to wire the five new methods through the corresponding Application use cases (`BeginSignPdf` / `BeginSignXml` / `BeginSignWord` / `BeginSignExcel` / `HandleWebhook`), mapping `Begin{Format}RequestDto` ↔ work-request records and `WebhookEnvelopeDto` ↔ `WebhookEnvelope` Domain record, surfacing the typed result via `WebhookHandleResultDto` in [src/MisaConnect.ESign.Client/MisaESignClient.cs](src/MisaConnect.ESign.Client/MisaESignClient.cs) — depends on T053, T054, T055, T056, T058, T076

### Infrastructure — DI wiring (US1 additions)

- [X] T078 Edit `ServiceCollectionExtensions.AddMisaConnectESign` to register: `TryAddScoped<BeginSignPdf>()`, `TryAddScoped<BeginSignXml>()`, `TryAddScoped<BeginSignWord>()`, `TryAddScoped<BeginSignExcel>()`, `TryAddScoped<HandleWebhook>()`, `TryAddScoped<FinalizeFromWebhook>()`, `TryAddSingleton<Client.IWebhookDeliveryHook, NullWebhookDeliveryHook>()`, register the `WebhookDeliveryHookAdapter` as the active `Application.IWebhookDeliveryHook` in [src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs](src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs) — depends on T053-T058, T074, T075

### Sample API — webhook endpoint wiring

- [X] T079 [P] [US1] Create `ESignWebhookEndpoint.MapESignWebhookEndpoint(IEndpointRouteBuilder, IConfiguration)` extension that reads `Misa:ESign:Webhook` options, mounts `MapPost(basePath, HandleAsync)` (or `MapPost(basePath + "/{secret}", HandleAsync)` when secret is configured), handler invokes `IMisaESignClient.HandleWebhookAsync` and returns `Results.Json(result.Ack)` with HTTP 200 per FR-085 / FR-094 in [samples/MisaConnect.Samples.Api/Endpoints/ESignWebhookEndpoint.cs](samples/MisaConnect.Samples.Api/Endpoints/ESignWebhookEndpoint.cs)
- [X] T080 [US1] Edit `samples/MisaConnect.Samples.Api/Program.cs` to call `builder.Services.AddMisaConnectESign(builder.Configuration)` (if not already wired) AND invoke `app.MapESignWebhookEndpoint(builder.Configuration)` after the existing `MapInvoiceEndpoints` / `MapTemplateEndpoints` calls in [samples/MisaConnect.Samples.Api/Program.cs](samples/MisaConnect.Samples.Api/Program.cs) — depends on T079

### EsignFake — webhook driver helper

- [ ] T081 [US1] Edit `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs` to add `PostSyntheticWebhookAsync(string transactionId, string clientId, DocumentFormat format, WebhookStatus status = Success, string? messageId = null, ...)` that constructs a MISA-shaped envelope (auto-generates messageId if null), POSTs it to the sample API's configured webhook URL via `HttpClient`, and returns the resulting `WebhookHandleResultDto`; also add a per-route request counter on `/documents/attachment` for SC-026 / SC-026a / SC-031 assertions in [tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs](tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs)

**Checkpoint**: User Story 1 is fully functional. A consumer can `BeginSign{Format}Async` + receive the synthetic webhook + finalize via `/documents/attachment` + receive signed bytes through the delivery hook + return the success ACK to MISA. T039-T048 must pass green. T047 covers SC-025/SC-028/SC-029.

---

## Phase 4: User Story 2 — Duplicate webhook delivery is idempotent (Priority: P2)

**Goal**: Across multiple webhook deliveries for the same `(clientId, transactionId)` — whether MISA reuses or rotates `messageId` — the SDK MUST call `/documents/attachment` exactly once on the first success and short-circuit subsequent deliveries to the cached ACK without re-invoking the delivery hook. Failure ACKs are NEVER cached — a subsequent delivery after a failed finalize triggers a fresh attempt. Single-flight under concurrent deliveries (FR-078). Covers FR-080, FR-081, FR-081a, FR-088 (duplicate-delivery short-circuit log event), SC-026, SC-026a, SC-031.

**Independent Test**: With the in-repo fake server: run the slice-4 happy path once (Begin + first webhook → signed bytes). Then POST 4 more identical webhook envelopes (whether reusing `messageId` or assigning fresh ones); assert the `/documents/attachment` counter stays at 1 and the delivery hook is invoked once total (SC-026). Then configure the fake to 5xx on first attachment call + 200 on second; POST the same envelope twice; assert counter == 2, delivery hook invoked once on second, ACKs are failure-then-success (SC-026a). Then fire 8 parallel POSTs against the same `transactionId`; assert counter == 1 and delivery hook invoked once (SC-031).

### Tests for User Story 2

- [ ] T082 [P] [US2] Create `DedupeAndIdempotencyTests` unit covering: 5 identical deliveries → exactly 1 `/documents/attachment` call + 1 delivery hook invocation (SC-026); 8 parallel deliveries → exactly 1 call (SC-031); 5xx on first + success on second → exactly 2 calls + 1 hook invocation on the second + failure ACK then success ACK (SC-026a); cached success short-circuits even when inbound messageId is NEW; failure-not-cached invariant: a failed finalize leaves `CachedSuccess = null` and `ObservedMessageIds` updated in [tests/MisaConnect.ESign.UnitTests/Webhook/DedupeAndIdempotencyTests.cs](tests/MisaConnect.ESign.UnitTests/Webhook/DedupeAndIdempotencyTests.cs)
- [ ] T083 [P] [US2] Create `InMemorySigningSessionStoreTests` covering: concurrency contract under parallel `RegisterAsync` + `TryGetByTransactionIdAsync` + `CacheSuccessAsync`; TTL eviction on read after advancing `ISystemClock` past `Ttl`; single-flight finalize semaphore behavior (concurrent `AcquireFinalizeLockAsync` calls for the same key serialize; lock released even on exception in the using-block); `RecordObservedMessageIdAsync` mutates `ObservedMessageIds` but does NOT set `CachedSuccess`; `RegisterAsync` then immediate eviction via TTL eviction; `CacheSuccessAsync` on an evicted session is a no-op in [tests/MisaConnect.ESign.UnitTests/Sessions/InMemorySigningSessionStoreTests.cs](tests/MisaConnect.ESign.UnitTests/Sessions/InMemorySigningSessionStoreTests.cs)
- [ ] T084 [P] [US2] Create `SigningSessionEquivalenceTests` ensuring the Domain `SigningSession` record's structural equality semantics work for the dedupe-by-`(clientId, transactionId)` lookup key, and that `record with` copies preserve immutability per [data-model.md §1.1](./data-model.md) in [tests/MisaConnect.ESign.UnitTests/Sessions/SigningSessionEquivalenceTests.cs](tests/MisaConnect.ESign.UnitTests/Sessions/SigningSessionEquivalenceTests.cs)
- [ ] T085 [P] [US2] Create `WebhookDedupeFakeServerTests` integration test: drives the round-trip once, then POSTs 4 more identical webhook envelopes (mix of same and rotated messageIds); asserts `/documents/attachment` counter stays at 1 and delivery hook fires exactly once across all 5 deliveries — SC-026 in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookDedupeFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookDedupeFakeServerTests.cs)
- [ ] T086 [P] [US2] Create `WebhookFailureThenSuccessFakeServerTests` integration test: configures fake to 5xx on first `/documents/attachment` + 200 on second; POSTs same envelope twice; asserts counter == 2, delivery hook fires once on second, ACKs are failure-then-success — SC-026a in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookFailureThenSuccessFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookFailureThenSuccessFakeServerTests.cs)
- [ ] T087 [P] [US2] Create `ConcurrentWebhookDeliveriesFakeServerTests` integration test: fires 8 parallel `HttpClient` POSTs against the sample API's webhook URL referencing the same `transactionId`; asserts counter == 1 and delivery hook invoked once — SC-031 in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/ConcurrentWebhookDeliveriesFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/ConcurrentWebhookDeliveriesFakeServerTests.cs)
- [ ] T088 [P] [US2] Create `WebhookSessionExpiredFakeServerTests` integration test asserting FR-081a: advance `ISystemClock` past `Webhook.Session.Ttl` after a Begin call, then POST a webhook → typed `UnknownTransaction` ACK (session was evicted on read) in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookSessionExpiredFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookSessionExpiredFakeServerTests.cs)

### Implementation (verifies the foundational locking + caching code paths are correct under load)

- [ ] T089 [US2] Audit `HandleWebhook` + `FinalizeFromWebhook` (T057, T058) to ensure the cache-hit short-circuit happens BOTH before lock acquisition (fast path) AND inside the lock (race-safe path) per [research.md R-5](./research.md); ensure the delivery hook is NOT invoked on the cache-hit short-circuit (FR-080); ensure failure paths set NO state mutation on the session (FR-081). Verify by running T082, T083, T085, T086, T087 — all green. No new files; this task may require small edits to T057 / T058
- [ ] T090 [US2] Audit `InMemorySigningSessionStore.AcquireFinalizeLockAsync` (T030) to ensure the lock is released on every exit path (success, exception, cancellation) by wrapping in `using` and disposing the lock via the returned `IAsyncDisposable`; ensure that a session evicted by TTL while the lock was held does NOT leak the semaphore. Verify by running T083 — all green. No new files

**Checkpoint**: User Story 2 is fully functional. Duplicate deliveries are exactly-once-on-success / at-least-once-on-failure. T082-T088 must pass green.

---

## Phase 5: User Story 3 — Webhook envelope validation and typed failure ACKs (Priority: P3)

**Goal**: Every inbound webhook envelope passes through the four-step typed validator before any side effect runs. Each of the five validation failure modes produces a distinct typed exception class and a distinct ACK envelope `errorCode`. No log line contains `signatures[].signature`, `documentBytes`, certificate private material, `AuthorizationRM`, `extraData`, or the configured `Webhook.Secret`. The sample API's transport-layer auth layers (secret URL segment + CIDR allowlist + startup WARN) enforce defense-in-depth. Covers FR-082 (full four-step validation including all failure modes), FR-087, FR-099, FR-100, FR-101, SC-027, SC-030, SC-035, SC-036, SC-037.

**Independent Test**: Drive the sample API's webhook endpoint with five rejection fixtures (malformed body, wrong `clientId`, no matching session, `SUCCESS` + empty `signatures[]`, `signatures[].documentId` not in session); for each assert the endpoint returns the correct typed ACK `errorCode` and no `/documents/attachment` call is made (SC-027). Then audit log captures over a Begin + webhook + finalize exchange contain zero occurrences of any secret-bearing string (SC-030). Then test sample-API secret routing (404 on bare path, 404 on wrong-secret, 200 on correct-secret — SC-035), CIDR allowlist (403 from outside, 200 from inside — SC-036), and the startup WARN log (one WARN line when no auth configured, zero when configured — SC-037).

### Tests for User Story 3

- [ ] T091 [P] [US3] Create `WebhookEnvelopeValidatorTests` covering one row per validation failure mode (`MalformedEnvelope` via deserialization rejection, `ClientIdMismatch`, `UnknownTransaction`, `IncompleteSuccessEnvelope`, `DocumentIdMismatch`) and a success row asserting the four-step ordering (early failures short-circuit later checks) per FR-082 / SC-027 in [tests/MisaConnect.ESign.UnitTests/Webhook/WebhookEnvelopeValidatorTests.cs](tests/MisaConnect.ESign.UnitTests/Webhook/WebhookEnvelopeValidatorTests.cs)
- [ ] T092 [P] [US3] Create `WebhookValidationExceptionTests` ensuring every typed subclass carries `CorrelationId`, `Category`, optional `MatchedTransactionId`, and `Format`; the slice-3 `Format` contract (FR-062) holds for the post-session-resolution failures (`IncompleteSuccessEnvelope`, `DocumentIdMismatch` carry resolved Format; the pre-session-resolution failures carry `DocumentFormat.Unknown`) in [tests/MisaConnect.ESign.UnitTests/Errors/WebhookValidationExceptionTests.cs](tests/MisaConnect.ESign.UnitTests/Errors/WebhookValidationExceptionTests.cs)
- [ ] T093 [P] [US3] Edit `ESignErrorMapperTests` to add rows asserting `MapWebhookValidationToAck` returns the right `errorCode` for each of the five typed failure subclasses per [contracts/error-mapping.md §A.11](./contracts/error-mapping.md) in [tests/MisaConnect.ESign.UnitTests/Errors/ESignErrorMapperTests.cs](tests/MisaConnect.ESign.UnitTests/Errors/ESignErrorMapperTests.cs)
- [ ] T094 [P] [US3] Edit `ESignLogScrubberTests` to add rows asserting: `$.signatures[*].signature` redacted on inbound captures; `$.extraData` content redacted; `IPAddress` structured-log values reduced to class label; the configured `Misa:ESign:Webhook:Secret` value never appears in any captured log entry across a Begin + webhook + finalize exchange per FR-087 / FR-100 / SC-030 in [tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs](tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs)
- [ ] T095 [P] [US3] Create `ExtraDataNotLoggedTests` unit test: runs a `HandleWebhookAsync` call with a populated `extraData` object containing distinctive sentinel values, captures the structured-log output, asserts zero occurrences of any sentinel value across all log entries — SC-030 / FR-087 in [tests/MisaConnect.ESign.UnitTests/Webhook/ExtraDataNotLoggedTests.cs](tests/MisaConnect.ESign.UnitTests/Webhook/ExtraDataNotLoggedTests.cs)
- [ ] T096 [P] [US3] Create `WebhookUnknownTransactionFakeServerTests` integration test: webhook arrives for a `transactionId` with no recorded session → typed `UnknownTransaction` ACK, no `/documents/attachment` call, delivery hook fires with `FailureWithError` outcome in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookUnknownTransactionFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookUnknownTransactionFakeServerTests.cs)
- [ ] T097 [P] [US3] Create `WebhookSecretRoutingTests` integration test asserting SC-035: with `Webhook.Secret` configured, POST to `{Path}` returns 404; POST to `{Path}/{wrong-secret}` returns 404; POST to `{Path}/{correct-secret}` reaches the handler in [tests/MisaConnect.ESign.IntegrationTests/SampleApi/WebhookSecretRoutingTests.cs](tests/MisaConnect.ESign.IntegrationTests/SampleApi/WebhookSecretRoutingTests.cs)
- [ ] T098 [P] [US3] Create `WebhookCidrAllowlistTests` integration test asserting SC-036: with `AllowedIps = ["10.0.0.0/8"]`, POST from simulated `192.168.x.x` IP returns 403; POST from simulated `10.x.x.x` IP reaches the handler; POST with empty `AllowedIps` reaches handler regardless of source IP in [tests/MisaConnect.ESign.IntegrationTests/SampleApi/WebhookCidrAllowlistTests.cs](tests/MisaConnect.ESign.IntegrationTests/SampleApi/WebhookCidrAllowlistTests.cs)
- [ ] T099 [P] [US3] Create `WebhookStartupWarnTests` integration test asserting SC-037: run the sample API in `Mode = Both` with no Secret + no AllowedIps, capture startup log, assert exactly one FR-101 WARN line; re-run with Secret configured, assert zero such WARN lines in [tests/MisaConnect.ESign.IntegrationTests/SampleApi/WebhookStartupWarnTests.cs](tests/MisaConnect.ESign.IntegrationTests/SampleApi/WebhookStartupWarnTests.cs)

### Implementation — sample-API auth layers + startup WARN

- [X] T100 [US3] Edit `ESignWebhookEndpoint.MapESignWebhookEndpoint` (from T079) to: (a) when `Webhook.Secret` is configured, mount ONLY the `{Path}/{secret}` route (no bare-path route, so the routing layer returns 404 on enumeration attempts per FR-099); (b) validate the route segment with `CryptographicOperations.FixedTimeEquals` against the configured secret; (c) when `Webhook.AllowedIps` is configured, match `HttpContext.Connection.RemoteIpAddress` against the parsed `System.Net.IPNetwork` ranges, return `Results.Forbid()` on mismatch with a structured WARN log carrying correlation ID + IP class label (NOT full IP at INFO/DEBUG) per FR-100; (d) on rejection, do NOT invoke the webhook handler in [samples/MisaConnect.Samples.Api/Endpoints/ESignWebhookEndpoint.cs](samples/MisaConnect.Samples.Api/Endpoints/ESignWebhookEndpoint.cs)
- [X] T101 [P] [US3] Create `WebhookStartupValidator` static helper with `EmitWarnIfPubliclyReachable(IServiceProvider, IConfiguration)` that reads `Misa:ESign:Webhook` and emits the FR-101 WARN log line `"Webhook endpoint is publicly reachable with no transport-layer auth; configure Misa:ESign:Webhook:Secret and/or :AllowedIps for production"` exactly once when `Mode != Polling` AND neither `Secret` nor `AllowedIps` is configured; never fails-start (informational only) per FR-101 in [samples/MisaConnect.Samples.Api/Middleware/WebhookStartupValidator.cs](samples/MisaConnect.Samples.Api/Middleware/WebhookStartupValidator.cs)
- [X] T102 [US3] Edit `samples/MisaConnect.Samples.Api/Program.cs` to invoke `WebhookStartupValidator.EmitWarnIfPubliclyReachable(app.Services, builder.Configuration)` after the `app.MapESignWebhookEndpoint(...)` call wired in T080 in [samples/MisaConnect.Samples.Api/Program.cs](samples/MisaConnect.Samples.Api/Program.cs)

### Polish for US3 — diagnostic option dump redaction

- [ ] T103 [P] [US3] Edit the existing `MisaESignOptionsDebugView` (or equivalent startup-time `IOptions<MisaESignOptions>` dump) to redact `Webhook.Secret` from any captured snapshot or log emission per FR-099 / Constitution Principle VIII; if no such view exists today, create a minimal one in [samples/MisaConnect.Samples.Api/Diagnostics/MisaESignOptionsDebugView.cs](samples/MisaConnect.Samples.Api/Diagnostics/MisaESignOptionsDebugView.cs)

**Checkpoint**: User Story 3 is fully functional. All five typed validation failures surface with distinct ACK error codes. Sample-API auth layers are enforced and tested. Logs contain zero secret-bearing strings. T091-T099 must pass green.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Layer audit, regression coverage, sandbox `[SandboxFact]`, EsignFake helpers not already added, public-surface catalogue updates, quickstart validation, CHANGELOG / README updates, final-build verification.

### Layer audit

- [ ] T104 [P] Edit `MisaConnectESignLayerAuditTests` to extend the audit over slice-4 namespaces: assert `Domain.Sessions.*`, `Domain.Webhook.*`, `Domain.Errors.WebhookValidationException.*` carry zero external deps; assert `Application.UseCases.{BeginSign*,HandleWebhook,FinalizeFromWebhook}` depend only on existing Application abstractions + `ILogger<>`; assert `Application.Abstractions.{ISigningSessionStore,IFinalizeLockOwner,IWebhookDeliveryHook}` reference only Domain types; assert `Infrastructure.Sessions.InMemorySigningSessionStore` and `Infrastructure.ESign.Webhook.*` stay `internal` per Constitution Principle I in [tests/MisaConnect.ESign.UnitTests/Layering/MisaConnectESignLayerAuditTests.cs](tests/MisaConnect.ESign.UnitTests/Layering/MisaConnectESignLayerAuditTests.cs)

### Regression coverage (FR-089 / FR-091 / SC-032)

- [ ] T105 [P] Create `PdfPollingRegressionFakeServerTests` re-running slice-1's `SignPdfHappyPathFakeServerTests.cs` end-to-end after the slice-4 refactor (T022) and additionally asserting that `ISigningSessionStore.RegisterAsync` is NOT called on the polling path per [research.md R-1](./research.md) in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/PdfPollingRegressionFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/PdfPollingRegressionFakeServerTests.cs)
- [ ] T106 [P] Create `MultiFormatRegressionFakeServerTests` re-running slice-3's XML/Word/Excel happy paths end-to-end after the slice-4 refactor (T023-T025); asserts byte-identical request shapes + observable behavior (FR-091 / SC-032) in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/MultiFormatRegressionFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/MultiFormatRegressionFakeServerTests.cs)

### Sandbox round-trip ([SandboxFact])

- [ ] T107 Create `SignViaWebhookSandboxTests` with one `[SandboxFact]` gated on `MISACONNECT_ESIGN_SANDBOX_USERNAME` / `_PASSWORD` / `_CLIENT_ID` / `_CLIENT_KEY` / `_WEBHOOK_URL` env vars; with a webhook URL registered with MISA out-of-band, exercises `BeginSignPdfAsync` → waits for MISA's push up to `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT` (default 5 min per [research.md R-10](./research.md)) → asserts the consumer-supplied delivery hook receives the signed bytes; skips cleanly when any required env var is absent OR sandbox is unreachable; fails loudly when credentials are explicitly rejected — FR-097, SC-034 in [tests/MisaConnect.ESign.IntegrationTests/Sandbox/SignViaWebhookSandboxTests.cs](tests/MisaConnect.ESign.IntegrationTests/Sandbox/SignViaWebhookSandboxTests.cs)

### Documentation + CHANGELOG

- [X] T108 [P] Update `README.md` supported-operations table to add the four `BeginSign{Format}Async` facades + `HandleWebhookAsync`; add a brief webhook-mode section pointing at [specs/004-misa-esign-webhook/quickstart.md](./quickstart.md) in [README.md](README.md)
- [X] T109 [P] Update `CHANGELOG.md` `[Unreleased]` section to enumerate the slice-4 additions per [contracts/public-surface.md §8](./contracts/public-surface.md); set version to `2.0.0-preview.4` in [CHANGELOG.md](CHANGELOG.md)
- [ ] T110 [P] Update `docs/sandbox-setup.md` to document the new `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL` + `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT` env vars and the ngrok / tunnel prerequisite for local webhook delivery per [research.md R-10](./research.md) in [docs/sandbox-setup.md](docs/sandbox-setup.md)

### Final verification

- [X] T111 Run `dotnet format MisaConnect.slnx` from repo root and commit any formatting deltas
- [X] T112 Run `dotnet build MisaConnect.slnx` from repo root and confirm zero warnings under `TreatWarningsAsErrors=true`
- [X] T113 Run `dotnet test tests/MisaConnect.ESign.UnitTests/MisaConnect.ESign.UnitTests.csproj` from repo root and confirm all unit tests pass + cumulative suite (slices 1+2+3+4) stays under 30s — SC-033
- [X] T114 Run `dotnet test tests/MisaConnect.ESign.IntegrationTests/MisaConnect.ESign.IntegrationTests.csproj` from repo root without sandbox env vars; confirm all `EndToEnd/*`, `SampleApi/*`, `MultiFormatRegression*`, `PdfPollingRegression*` tests pass and `Sandbox/*` tests skip cleanly — SC-034
- [ ] T115 Walk through [quickstart.md](./quickstart.md) end-to-end manually: configure `appsettings.json`, register a `IWebhookDeliveryHook`, run the sample API with the fake-server profile, drive a Begin + synthetic-webhook round-trip via `BeginAndWebhookHappyPathFakeServerTests`, confirm the delivery hook receives the signed bytes. Document any deviations as task additions or quickstart corrections

**Checkpoint**: Slice 4 is implementation-complete. All FRs covered, all SCs pass, cumulative unit suite stays under 30s, slice-1 + slice-3 regressions all pass byte-identically, sandbox `[SandboxFact]` skips cleanly without env vars, sample-API auth layers + startup WARN are tested, and the public-surface delta is catalogued in CHANGELOG + README.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately. Includes the tasks-time Postman tie-breaker check (T005)
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories. Internal sub-ordering: Domain types (T006-T016) → Application port/abstractions/validator (T017-T020) → Application error mapper + internal refactor (T021-T026) → Infrastructure wire DTOs + adapter + configuration + scrubber + DI (T027-T037) → Foundational regression gate (T038)
- **User Story 1 (Phase 3 — P1 MVP)**: Depends on Foundational. Can be implemented + tested independently of US2/US3. Critical path: tests T039-T048 written first → Application use cases T049-T058 → Client DTOs + facade T059-T077 → Infrastructure DI T078 → Sample API endpoint T079-T080 → EsignFake helper T081. T047 covers the SC-025/SC-028/SC-029 MVP demo. After this phase ships, the slice already delivers the headline P1 value — single first-delivery happy path works end-to-end
- **User Story 2 (Phase 4 — P2)**: Depends on Foundational + US1. The single-flight + caching code paths physically live in `HandleWebhook` (T058), `FinalizeFromWebhook` (T057), and `InMemorySigningSessionStore` (T030) — all introduced in Foundational/US1. US2 verifies them under load and audits the implementation. Audits T089/T090 may force small edits back to T030/T057/T058
- **User Story 3 (Phase 5 — P3)**: Depends on Foundational + US1. US3 finishes the validator coverage (T091/T092), the error-mapper coverage (T093), and the log-scrubber coverage (T094/T095). US3's sample-API auth layers (T100-T103) ship the FR-099/FR-100/FR-101 contract
- **Polish (Phase 6)**: Depends on US1 + US2 + US3 all green. The regression tests (T105/T106) re-verify FR-089/FR-091/SC-032 over the whole slice; the sandbox test (T107) closes out FR-097/SC-034; the docs (T108-T110) close out the user-facing surface

### User Story Dependencies

- **User Story 1 (P1)**: Independent of US2/US3. Ships the headline value alone — a single first-delivery happy path works end-to-end
- **User Story 2 (P2)**: Depends on US1 (it tests the code paths US1 introduced). Adds load-testing + idempotency contract verification. The implementation lives in US1's components; US2 is primarily a test phase
- **User Story 3 (P3)**: Depends on US1 (it extends US1's validator + scrubber + sample API). Adds typed-failure coverage + transport-layer auth. The five typed validation failures partially fire in US1's happy-path code (e.g. `UnknownTransaction` when the session doesn't exist) but US3 covers them explicitly

### Within Each User Story

- Tests (T039-T048 for US1; T082-T088 for US2; T091-T099 for US3) written and FAIL before implementation per Constitution Principle VI
- Domain types before Application use cases (Phase 2 already ordered)
- Application use cases before Client facades
- Client facades before Sample API wiring
- Sample API wiring before integration tests
- Story complete before moving to next priority

### Parallel Opportunities

- **Setup (Phase 1)**: T002, T003, T004, T005 can run in parallel
- **Foundational Domain types (T006-T014)**: All `[P]` — different files, no dependencies among them. T015 (SigningSession) depends on T013+T014; T016 (exception family) is independent
- **Foundational Application port/abstractions (T017-T019)**: All `[P]` after Phase 2 setup
- **Foundational slice-1/3 refactor (T022-T025)**: T023, T024, T025 `[P]` after T022 lands the refactor pattern
- **Foundational Infrastructure DTOs (T027-T029)**: All `[P]` — different files
- **Foundational configuration (T031-T033)**: All `[P]` — different files. T034 depends on T033
- **US1 tests (T039-T048)**: All `[P]` — different files; all should be written first and FAIL
- **US1 Client DTOs (T059-T072)**: All `[P]` — different files; T069 depends on T067/T068; T072 depends on T070/T071; T075 depends on T012/T018/T071/T073
- **US1 Application use cases (T053-T056)**: All depend on foundational refactor but are parallel to each other after dependencies are satisfied
- **US2 tests (T082-T088)**: All `[P]` — different files
- **US3 tests (T091-T099)**: All `[P]` — different files
- **Polish (T104-T110)**: All `[P]` — different files
- Once Foundational completes, US1/US2/US3 implementation tasks within each can proceed in parallel by different developers

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together (must FAIL before implementation):
Task: "Create BeginSignPdfTests in tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignPdfTests.cs"        # T039
Task: "Create BeginSignXmlTests in tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignXmlTests.cs"        # T040
Task: "Create BeginSignWordTests in tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignWordTests.cs"      # T041
Task: "Create BeginSignExcelTests in tests/MisaConnect.ESign.UnitTests/Webhook/BeginSignExcelTests.cs"    # T042
Task: "Create HandleWebhookOrchestratorTests"                                                              # T043
Task: "Create FinalizeFromWebhookTests"                                                                    # T044
Task: "Create ModeGuardTests"                                                                              # T045

# Then launch the four Begin work-request DTOs together:
Task: "Create BeginSignPdfWorkRequest"                                                                     # T049
Task: "Create BeginSignXmlWorkRequest"                                                                     # T050
Task: "Create BeginSignWordWorkRequest"                                                                    # T051
Task: "Create BeginSignExcelWorkRequest"                                                                   # T052

# Then launch the four Begin use cases together (each depends on its work-request + matching slice-1/3 refactor):
Task: "Create BeginSignPdf use case"                                                                       # T053
Task: "Create BeginSignXml use case"                                                                       # T054
Task: "Create BeginSignWord use case"                                                                      # T055
Task: "Create BeginSignExcel use case"                                                                     # T056

# Then launch all twelve Client.Dtos.Webhook records together:
Task: "Create BeginSignPdfRequestDto / XmlRequestDto / WordRequestDto / ExcelRequestDto"                   # T059-T062
Task: "Create BeginSignPdfResultDto / XmlResultDto / WordResultDto / ExcelResultDto"                       # T063-T066
Task: "Create WebhookStatusDto / WebhookSignatureDto / WebhookEnvelopeDto / WebhookAckDto / WebhookOutcomeDto / WebhookHandleResultDto"  # T067-T072
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T005)
2. Complete Phase 2: Foundational (T006-T038) — CRITICAL — blocks all stories
3. Complete Phase 3: User Story 1 (T039-T081)
4. **STOP and VALIDATE**: Run `BeginAndWebhookHappyPathFakeServerTests` (T047) — assert the four formats Begin → synthetic webhook → finalize → signed bytes round-trip is green for all four formats
5. Demo: the consumer can `BeginSignPdfAsync` + receive the webhook + get signed bytes through the delivery hook + return the success ACK to MISA. This is the headline P1 value

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready (~38 tasks; ~30% of slice)
2. Add User Story 1 → Test independently → MVP demo (~43 tasks; ~60% of slice)
3. Add User Story 2 → Test independently → Idempotency contract verified (~9 tasks; ~67% of slice)
4. Add User Story 3 → Test independently → Validation + transport-layer auth shipped (~13 tasks; ~78% of slice)
5. Polish → Layer audit + regression + sandbox + docs (~12 tasks; 100%)

Each story adds value without breaking previous stories. Slice 1's PDF blocking happy path (`SC-001`) and slice-3's multi-format paths (`SC-015`) MUST pass byte-identically throughout — verified at the foundational gate (T038), at the polish regression tests (T105/T106), and at the final integration-test run (T114).

### Parallel Team Strategy

With multiple developers after Foundational completes:

1. **Phase 1+2 together**: One developer drives the slice-1/3 refactor (T022-T026); another drives the new Domain + Application + Infrastructure + configuration types (T006-T021, T027-T037). Sync on T038 (foundational regression gate)
2. **Once Foundational is done**:
   - Developer A: User Story 1 (Phase 3 — MVP) — owns the four Begin use cases + HandleWebhook + sample API endpoint
   - Developer B: User Story 2 (Phase 4) starts in parallel after T030/T057/T058 land, owns the idempotency + concurrency tests
   - Developer C: User Story 3 (Phase 5) starts in parallel after T020/T036 land, owns the validation + transport-layer auth tests
3. **Phase 6**: All hands on regression + sandbox + docs

---

## Notes

- `[P]` tasks = different files, no dependencies on incomplete tasks
- `[Story]` label maps task to specific user story for traceability
- Each user story should be independently completable and testable per Constitution Principle V
- Verify tests FAIL before implementing (Constitution Principle VI / TDD)
- Commit after each task or logical group; auto-commit hook configured in `.specify/extensions.yml` will offer to commit after the slice-implement command finishes
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same-file conflicts on parallel tasks, cross-story dependencies that break independence
- File paths use repo-relative URLs in `[label](path)` markdown form so VSCode renders them as clickable links
- Slice 4 ships as `2.0.0-preview.4`; the stable `2.0.0` cut gates on slices 1+2+3+4 all being implemented and merged per [plan.md Principle VII gate](./plan.md)

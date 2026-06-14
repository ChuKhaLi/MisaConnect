---
description: "Task list for slice 008 — per-user credentials seam (MisaConnect.ESign)"
---

# Tasks: Per-User Credentials Seam for MisaConnect.ESign

**Input**: Design documents from `specs/008-esign-per-user-credentials/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/public-surface.md, quickstart.md

**Tests**: REQUIRED. Constitution Principle VI ("tests are the spec") + spec acceptance criteria mandate the new unit/integration tests and TDD ordering (write failing test → implement).

**Organization**: Grouped by user story (US1–US4 from spec.md). The new shared types every story needs are in Phase 2 (Foundational). Note: this is one cohesive seam, so several new test files lock more than one story — each file is created exactly once, in its primary story, and its cross-story coverage is noted.

## Path Conventions

Layered library: `src/MisaConnect.ESign.{Application,Infrastructure,Client}/`, tests in `tests/MisaConnect.ESign.{UnitTests,IntegrationTests}/`. All paths below are repository-relative.

---

## Phase 1: Setup

**Purpose**: Establish a green baseline before any change.

- [x] T001 Confirm baseline green: `dotnet build MisaConnect.slnx`, `dotnet test tests/MisaConnect.ESign.UnitTests`, `dotnet test tests/MisaConnect.ESign.IntegrationTests` all pass on branch `008-esign-per-user-credentials` before edits.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The new public types + default adapter + registration that ALL user stories depend on.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete.

- [x] T002 [P] Create `MisaCredentials` public sealed record (ClientId, ClientKey, UserName, Password) with the redacting `ToString()` override in `src/MisaConnect.ESign.Application/Abstractions/MisaCredentials.cs` (per contracts/public-surface.md — emit UserName/ClientId, `<redacted>` for ClientKey/Password).
- [x] T003 [P] Create `IMisaCredentialsAccessor` public port (`MisaCredentials Get();`) with the per-call/singleton-safe/never-scoped XML-doc contract in `src/MisaConnect.ESign.Application/Abstractions/IMisaCredentialsAccessor.cs`.
- [x] T004 [P] Create `CredentialsMode` public enum (`Static = 0`, `Dynamic = 1`) in `src/MisaConnect.ESign.Infrastructure/Configuration/CredentialsMode.cs` (next to `ESignEnvironment`).
- [x] T005 Add `public CredentialsMode CredentialsMode { get; set; } = CredentialsMode.Static;` (with XML doc) to `src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptions.cs` (depends on T004).
- [x] T006 Create `OptionsMisaCredentialsAccessor` (`internal sealed`, ctor-injects `IOptions<MisaESignOptions>`, returns the four option values, does NOT inspect `CredentialsMode`) in `src/MisaConnect.ESign.Infrastructure/Credentials/OptionsMisaCredentialsAccessor.cs` (depends on T002, T003).
- [x] T007 Register `services.TryAddSingleton<IMisaCredentialsAccessor, OptionsMisaCredentialsAccessor>();` in `AddCoreServices` (the TryAdd block) in `src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` (depends on T006).

**Checkpoint**: Solution compiles; the default accessor is registered and (with no consumer override) returns the static options — byte-identical behavior.

---

## Phase 3: User Story 1 - Supply MISA credentials per signing call (Priority: P1) 🎯 MVP

**Goal**: In Dynamic mode the SDK uses consumer-supplied per-call credentials for the login body, the `x-clientId`/`x-clientKey` headers, and the token-cache key, resolved per-call inside the awaited pipeline.

**Independent Test**: Register a custom `IMisaCredentialsAccessor` (Dynamic mode) against `FakeMisaESignServer`; supply two different credential sets across two calls and assert two distinct logins, two distinct header sets, and two distinct cache keys.

### Tests for User Story 1 (write first; must FAIL before implementation)

- [x] T008 [P] [US1] Create `tests/MisaConnect.ESign.UnitTests/Http/ClientHeadersHandlerTests.cs` with a Static regression-lock (emits `options.ClientId`/`ClientKey` — locks US2) AND a Dynamic fact (emits the accessor's `ClientId`/`ClientKey`). Use a stub `IMisaCredentialsAccessor`.
- [x] T009 [P] [US1] Add Dynamic-mode facts to `tests/MisaConnect.ESign.UnitTests/Configuration/MisaESignOptionsValidatorTests.cs`: empty ClientId/ClientKey/UserName/Password **succeeds** when `CredentialsMode = Dynamic`, while BaseUrl/Environment/Polling violations still **fail** in Dynamic mode. (Existing 15 facts unchanged.)
- [x] T010 [P] [US1] Create a Dynamic-mode end-to-end test under `tests/MisaConnect.ESign.IntegrationTests/EndToEnd/` (via `TestServiceProvider` + `FakeMisaESignServer`): register a stub accessor returning two different `MisaCredentials` across two ambient scopes; assert two distinct `/login-api` bodies, two distinct token-cache keys, and two distinct `x-clientId`/`x-clientKey` header sets.

### Implementation for User Story 1

- [x] T011 [P] [US1] `ClientHeadersHandler`: add `IMisaCredentialsAccessor` ctor dependency; in `SendAsync` read `ClientId`/`ClientKey` from `Get()` per-call (keep the `Contains` guards and the `X-Correlation-Id` injection) in `src/MisaConnect.ESign.Infrastructure/Http/ClientHeadersHandler.cs`.
- [x] T012 [P] [US1] `MisaESignWireClient`: add `IMisaCredentialsAccessor` ctor dependency; in `NewRequest` stamp `x-clientId`/`x-clientKey` from `Get()` per-call instead of `_options.Value` in `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs`.
- [x] T013 [US1] In `ServiceCollectionExtensions` change the `EnsureAccessToken` DI factory closure from `() => (optionsAccessor.Value.UserName, optionsAccessor.Value.Password)` to resolve `IMisaCredentialsAccessor` and return `(c.UserName, c.Password)` from `Get()` at call time. **Keep `EnsureAccessToken`'s `Func<(string,string)>` ctor parameter unchanged.** File: `src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` (same file as T007 → sequential).
- [x] T014 [P] [US1] `DefaultTokenCacheKeySelector`: add `IMisaCredentialsAccessor` ctor dependency; derive `UserName`/`ClientId` for `{UserName}|{ClientId}|{host}` from `Get()` per-call in `src/MisaConnect.ESign.Infrastructure/Caching/DefaultTokenCacheKeySelector.cs` (fail-fast guard added in US3/T018).
- [x] T015 [P] [US1] `MisaESignOptionsValidator`: wrap ONLY the four credential checks in `if (options.CredentialsMode == CredentialsMode.Static) { … }`; all other checks stay outside the guard in `src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptionsValidator.cs`.

**Checkpoint**: US1 tests (T008–T010) pass; Dynamic-mode per-call credentials work end-to-end.

---

## Phase 4: User Story 2 - Existing single-account consumers are unaffected (Priority: P1)

**Goal**: The default (Static, no accessor) path is byte-identical to 2.1.1.

**Independent Test**: Run the full existing ESign unit + integration suites with zero fixture changes; all pass.

- [x] T016 [US2] Verify byte-identical default-Static path: `EnsureAccessTokenTests` (×4), `MisaESignClientUsernameContextTests` (×2 facts), `MisaESignOptionsValidatorTests` (existing ×15), `MisaESignOptionsDebugViewTests`, and the `FakeMisaESignServer` E2E suite all pass **without modification**. (Regression also locked by the Static facts in T008 + T017.) Run `dotnet test tests/MisaConnect.ESign.UnitTests` and `dotnet test tests/MisaConnect.ESign.IntegrationTests`.

**Checkpoint**: Existing consumers provably unaffected.

---

## Phase 5: User Story 3 - Per-user token isolation with loud failure (Priority: P2)

**Goal**: Two signers never share a token-cache slot; an empty resolved credential fails loudly instead of collapsing the key.

**Independent Test**: Compose keys for two distinct credential sets (assert different); resolve empty UserName / empty ClientId (assert `InvalidOperationException`).

### Tests for User Story 3 (write first; must FAIL before implementation)

- [x] T017 [P] [US3] Create `tests/MisaConnect.ESign.UnitTests/Caching/DefaultTokenCacheKeySelectorTests.cs`: (a) Static regression-lock `{UserName}|{ClientId}|{host}` from options (locks US2); (b) Dynamic isolation — a stub accessor returning two different `(UserName, ClientId)` yields two different keys (locks US1 isolation); (c) empty fail-fast — empty `UserName` (and, separately, empty `ClientId`) makes `Compose()` throw `InvalidOperationException`.

### Implementation for User Story 3

- [x] T018 [US3] `DefaultTokenCacheKeySelector.Compose`: throw `InvalidOperationException` when the resolved `UserName` or `ClientId` is null/empty (before composing the key), in `src/MisaConnect.ESign.Infrastructure/Caching/DefaultTokenCacheKeySelector.cs` (extends T014; same file → sequential after T014).

**Checkpoint**: No cross-user token bleed; missing-ambient bugs surface loudly.

---

## Phase 6: User Story 4 - Credentials never leak into logs or strings (Priority: P3)

**Goal**: `MisaCredentials` string form redacts both secrets; the SDK never logs credential values or credential header values.

**Independent Test**: `new MisaCredentials("cid","SECRET_KEY","user","SECRET_PWD").ToString()` contains neither secret; logging audit shows no credential/ header-value emission.

### Tests for User Story 4 (write first; must FAIL before implementation)

- [x] T019 [P] [US4] Create `tests/MisaConnect.ESign.UnitTests/Abstractions/MisaCredentialsTests.cs`: assert `ToString()` contains neither `"SECRET_KEY"` nor `"SECRET_PWD"` (both `<redacted>`) and still surfaces `UserName`/`ClientId`.

### Implementation for User Story 4

- [x] T020 [US4] Confirm the `MisaCredentials.ToString()` redaction (implemented in T002) satisfies T019; adjust the override if the test reveals a gap. File: `src/MisaConnect.ESign.Application/Abstractions/MisaCredentials.cs`.
- [x] T021 [US4] Audit credential logging (FR-010): verify `ESignCallLogger`/correlation logging does not serialize `x-clientId`/`x-clientKey` header values or any credential value; document the finding. Search `src/MisaConnect.ESign.Infrastructure/` logging call sites.

**Checkpoint**: Constitution Principle VIII satisfied for the new seam.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Docs, version, format, release-readiness.

- [x] T022 [P] Add a `MisaConnect.ESign` `[Unreleased]` entry to `CHANGELOG.md` (Added: port + record + redacting ToString + default adapter + CredentialsMode; Changed: per-call resolution; Notes: deferred webhook clientId) per contracts/public-surface.md.
- [x] T023 [P] Update `docs/esign/configuration.md`: add the `CredentialsMode` keys-table row; note the four-credential requirement is Static-mode only; add `IMisaCredentialsAccessor` to the custom-DI override example with **register-before** ordering (correct the "or just after" wording) and the singleton/ambient (never scoped) lifetime contract.
- [x] T024 [P] Update `README.md` (root) and `src/MisaConnect.ESign.Client/README.md`: mention the new credentials accessor port in the configuration prose/links.
- [x] T025 Bump `<Version>` `2.1.1` → `2.2.0` in `src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj`.
- [x] T026 Run `dotnet format MisaConnect.slnx` and a full `dotnet build MisaConnect.slnx` under `TreatWarningsAsErrors`; resolve any warnings.
- [x] T027 Run both ESign suites green and validate quickstart.md scenarios (Static no-op + Dynamic two-signer); update docs if any drift.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)**: none.
- **Foundational (P2)**: after Setup. BLOCKS all user stories. Internal order: (T002, T003, T004) parallel → T005 (needs T004) → T006 (needs T002, T003) → T007 (needs T006).
- **US1 (P3 phase)**: after Foundational. MVP.
- **US2 (P4 phase)**: after US1 implementation (verifies the changed default path). Its regression locks also rely on T008/T017.
- **US3 (P5 phase)**: after US1 (T018 extends T014 in the same file).
- **US4 (P6 phase)**: after Foundational (T020 confirms T002); independent of US1–US3.
- **Polish (P7)**: after all desired stories.

### Within each user story

- Tests before implementation (TDD): T008–T010 before T011–T015; T017 before T018; T019 before T020.
- Same-file edits are sequential: T007 → T013 (ServiceCollectionExtensions.cs); T014 → T018 (DefaultTokenCacheKeySelector.cs).

### Parallel opportunities

- **Foundational**: T002, T003, T004 together (3 new files, no interdeps).
- **US1 tests**: T008, T009, T010 together (different files).
- **US1 impl**: T011, T012, T014, T015 together (different files); T013 is sequential after T007 (same file).
- **US4**: T019 runs independently of US1–US3 (only needs T002).
- **Polish**: T022, T023, T024 together (different files); T025/T026/T027 sequential.

---

## Parallel Example: Foundational + US1

```text
# Foundational new types (parallel):
Task T002: MisaCredentials record (Application/Abstractions)
Task T003: IMisaCredentialsAccessor port (Application/Abstractions)
Task T004: CredentialsMode enum (Infrastructure/Configuration)

# US1 tests (parallel, after foundation):
Task T008: ClientHeadersHandlerTests.cs
Task T009: MisaESignOptionsValidatorTests Dynamic facts
Task T010: Dynamic-mode E2E test

# US1 implementation (parallel, different files):
Task T011: ClientHeadersHandler per-call read
Task T012: MisaESignWireClient.NewRequest per-call read
Task T014: DefaultTokenCacheKeySelector per-call key
Task T015: MisaESignOptionsValidator mode gate
# (T013 ServiceCollectionExtensions runs after T007 — same file)
```

---

## Implementation Strategy

### MVP First (US1)

1. Phase 1 Setup → Phase 2 Foundational → Phase 3 US1.
2. STOP and VALIDATE: Dynamic-mode per-call credentials work (T010 E2E green).

### Incremental delivery

1. Foundation ready (default accessor = static options, byte-identical).
2. US1 → per-call dynamic credentials (MVP).
3. US2 → prove existing consumers unaffected (regression locks + full suites).
4. US3 → token isolation + empty fail-fast.
5. US4 → secret redaction + logging audit.
6. Polish → CHANGELOG, docs, 2.2.0 bump, format, quickstart validation.

---

## Notes

- [P] = different files, no incomplete-task dependency.
- This seam is cohesive: US2 is largely satisfied by the Foundational default adapter + the US1 per-call changes producing identical values; its tasks are verification/regression-lock.
- Keep `EnsureAccessToken`'s `Func<(string,string)>` ctor parameter unchanged (the port is adapted at the DI seam) — this is what keeps existing tests green.
- Commit after each phase (per the goal): foundational+stories together at implement, then docs/release.
- Verify each new test fails before implementing its production change.

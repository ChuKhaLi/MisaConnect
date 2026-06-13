---
description: "Task list for slice 006 — fix ESRM routing & silent-failure hardening"
---

# Tasks: Fix ESRM Routing & Silent-Failure Hardening

**Input**: Design documents from `specs/006-fix-esrm-routing/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: REQUIRED — per Constitution Principle VI ("Tests are the spec") and spec §Testing Requirements / SC-005. TDD: each story's tests are written first and MUST fail before its implementation.

**Organization**: by user story (US1 P1, US2 P2, US3 P2). Note the shared routing seam is foundational; see Dependencies for why US1 + US3 ship together for real-world correctness.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (setup, foundational, polish carry no story label)

## Path Conventions

- Source: `src/MisaConnect.ESign.Infrastructure/…`, public option in `…/Configuration/`
- Unit tests: `tests/MisaConnect.ESign.UnitTests/…`
- Integration/fake: `tests/MisaConnect.ESign.IntegrationTests/…`
- Test file paths below follow the existing project layout; confirm exact folders in T002.

---

## Phase 1: Setup (Shared)

**Purpose**: Establish a green baseline and confirm test layout before any change.

- [ ] T001 Build the solution and run the ESign suites to confirm a green baseline: `dotnet build MisaConnect.slnx`, `dotnet test tests/MisaConnect.ESign.UnitTests`, `dotnet test tests/MisaConnect.ESign.IntegrationTests` (sandbox facts skip without creds).
- [ ] T002 Confirm test-project folder conventions and existing fixture entry points in `tests/MisaConnect.ESign.UnitTests/` and `tests/MisaConnect.ESign.IntegrationTests/EndToEnd/TestServiceProvider.cs` + `EsignFake/FakeMisaESignServer.cs`; note the namespaces/paths the new test files in later phases will use.

---

## Phase 2: Foundational (Blocking — behavior-preserving seam)

**Purpose**: Introduce the shared routing seam and the public option WITHOUT changing behavior, so existing tests stay green and later phases can do red→green.

**⚠️ CRITICAL**: No user-story work begins until this phase is complete.

- [ ] T003 [P] Add `public bool? AuthUnderWebdev { get; set; }` to `src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptions.cs` with XML doc per [contracts/public-surface.md](./contracts/public-surface.md). No consumer of it yet (no behavior change).
- [ ] T004 Create `src/MisaConnect.ESign.Infrastructure/ESign/ESignRouteResolver.cs` (Infrastructure-internal): expose (a) an origin-derivation helper `Origin(string baseUrl)` → `scheme://host/` and (b) `ResolveRequestPath(string canonicalRoute)` returning the request path. Initial implementation returns the canonical route unchanged (identity) for all routes — behavior-preserving. Include the `EffectiveAuthUnderWebdev` computation (`AuthUnderWebdev ?? Environment == Sandbox`) but DO NOT yet apply it (US3 wires it in).
- [ ] T005 Inject `ESignRouteResolver` (and existing `IOptions<MisaESignOptions>`) into `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs`; route request-path construction in `NewRequest`/call sites through `ResolveRequestPath(canonicalRoute)`, while continuing to pass the **canonical** `ESignHttpRoutes.*` constant as the `endpoint` argument to the error mappers (keeps `ESignErrorMapper`/`OtpErrorMapper` exact-match dispatch intact). Register the resolver in `src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`. Behavior unchanged (resolver is identity, BaseAddress still = BaseUrl).
- [ ] T006 In `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs`, stop discarding the `AuthorizationRM` header — capture the last-seen bearer per endpoint so tests can assert it (no SDK behavior change). Keep existing route maps for now.

**Checkpoint**: Solution builds, all existing tests still pass (`dotnet test` green), seam in place.

---

## Phase 3: User Story 1 - ESRM at host root, base-path tolerant (Priority: P1) 🎯 MVP

**Goal**: ESRM (certs + signing) and refresh/resend resolve correctly regardless of any path in `BaseUrl` (fixes Defects A + C). Bearer remains the remote-signing token (Defect B guard).

**Independent Test**: With `BaseUrl` ending in `/webdev/`, cert-list returns the ACTIVE cert and the ESRM request hits `…/external/esrm/…` at the host root; identical to a bare-host base.

### Tests for User Story 1 (write first — MUST fail) ⚠️

- [ ] T007 [P] [US1] Unit test `tests/MisaConnect.ESign.UnitTests/ESign/Routing/BaseAddressNormalizationTests.cs`: assert resolved **absolute** URLs for ESRM (cert-list, hash, signing, status, attachment) and refresh/resend against `BaseUrl = https://host/webdev/` AND `https://host/` are byte-identical and land at host root (ESRM) / single `/webdev/` (refresh/resend). Contracts C1, C2.
- [ ] T008 [P] [US1] Integration test in `tests/MisaConnect.ESign.IntegrationTests/EndToEnd/` (new file, e.g. `EsrmRootRoutingTests.cs`): configure the SDK with a `/webdev/`-suffixed base against the fake; assert cert-list returns the seeded ACTIVE certificate (not zero) and the ESRM request path observed by the fake has no `/webdev/` segment.
- [ ] T009 [P] [US1] Integration test asserting the `AuthorizationRM` bearer the fake received on an ESRM call equals the login response's `remoteSigningAccessToken` (Defect B guard / Contract C5).
- [ ] T010 [P] [US1] Integration test for the `401` refresh-and-retry path against the corrected ESRM routes (Contract C6) — extend/relocate the existing 401 test to run with a `/webdev/` base.

### Implementation for User Story 1

- [ ] T011 [US1] In `src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`, change the HttpClient registration so `http.BaseAddress = ESignRouteResolver.Origin(opts.BaseUrl)` (scheme+host, trailing slash), replacing the current `EndsWith('/')` logic. (Makes T007/T008 ESRM + refresh/resend pass.)
- [ ] T012 [US1] In `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs`, ensure ESRM endpoints are served **only at the host root** and remove any `/webdev/`-prefixed ESRM mapping, so a mis-routed ESRM call cannot accidentally succeed. (Locks T008.)
- [ ] T013 [US1] Run US1 tests; confirm red→green and no regression in the existing suite.

**Checkpoint**: ESRM + refresh/resend correct and path-tolerant against the fake. (Login still resolves at root here — completed in US3 for real-sandbox correctness.)

---

## Phase 4: User Story 2 - Non-JSON 2xx fails loudly (Priority: P2)

**Goal**: A `2xx` non-JSON ESRM response raises a clear, secret-free error instead of "no active certificate" (Defect D). Independent of routing.

**Independent Test**: Fake returns `200 text/html` for the cert-list endpoint → SDK throws a clear exception naming the endpoint + content type; `200 []` still yields `NoActiveCertificateException`.

### Tests for User Story 2 (write first — MUST fail) ⚠️

- [ ] T014 [P] [US2] Unit test `tests/MisaConnect.ESign.UnitTests/ESign/ContentTypeGuardTests.cs` (or integration if HttpClient plumbing is needed): `200 text/html` body on the cert-list path → `ESignGeneralException` whose message contains the endpoint and content type and a body snippet, and contains NO bearer/credential. Contract C4 + FR-007.
- [ ] T015 [P] [US2] Test: `200 application/json` body `[]` → `NoActiveCertificateException` (legitimate empty); and a valid JSON array parses normally. Contract C4 / FR-008.

### Implementation for User Story 2

- [ ] T016 [US2] In `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs`, add a content-type/JSON guard at the ESRM JSON-response boundary (after the existing `IsSuccessStatusCode` check, before deserialize): if media type is not JSON (`application/json`/`*+json`) or a non-empty body fails to parse, throw `ESignGeneralException` (category `MisaUnknown`, code e.g. `UnexpectedContentType`) with endpoint + content type + truncated (~256 char) body snippet. Do NOT alter the shared `Deserialize<T>` swallow behavior. Apply to the cert-list path (and the other ESRM JSON reads where low-risk).
- [ ] T017 [US2] In `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs`, add a way to make an ESRM endpoint return `200 text/html` (e.g. a toggle/seam) so T014 exercises the real HTTP path.
- [ ] T018 [US2] Run US2 tests; confirm red→green and no regression.

**Checkpoint**: A routing/gateway anomaly surfaces as a clear exception, never a silent empty result.

---

## Phase 5: User Story 3 - Auth topology by environment + override (Priority: P2)

**Goal**: login/two-factor resolve at host root for Production and under `/webdev/` for Sandbox, with `AuthUnderWebdev` override (option b). Completes real-sandbox correctness on top of US1.

**Independent Test**: Resolved login/two-factor URLs follow the C3 matrix; ESRM/refresh/resend URLs are unchanged across the matrix.

### Tests for User Story 3 (write first — MUST fail) ⚠️

- [ ] T019 [P] [US3] Unit test `tests/MisaConnect.ESign.UnitTests/ESign/Routing/AuthLocationTests.cs`: across the C3 matrix (Production/Sandbox × override null/true/false), assert login + two-factor resolved paths, and assert ESRM/refresh/resend paths are invariant. Contract C3.
- [ ] T020 [P] [US3] Unit test for `EffectiveAuthUnderWebdev` derivation: `null` ⇒ `Environment == Sandbox`; `true`/`false` force the value. data-model §1.
- [ ] T021 [P] [US3] Integration test: with `Environment = Sandbox` (default), login is served under `/webdev/` by the fake and login succeeds; with `Environment = Production`, login is served at root.

### Implementation for User Story 3

- [ ] T022 [US3] In `src/MisaConnect.ESign.Infrastructure/ESign/ESignRouteResolver.cs`, apply `EffectiveAuthUnderWebdev` so `ResolveRequestPath` prepends `webdev/` to the login and two-factor canonical routes when effective; identity for all other routes. (Endpoint ids passed to error mappers remain canonical.)
- [ ] T023 [US3] In `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs` (and `EndToEnd/TestServiceProvider.cs` if needed), serve login/two-factor under `/webdev/` for the Sandbox topology (and at root for a Production-configured test), matching the real sandbox. Update any existing login/two-factor E2E tests that assumed root-only.
- [ ] T024 [US3] Run US3 tests + the full ESign suite; confirm red→green and that US1/US2 still pass.

**Checkpoint**: All three stories pass; SDK is correct against both topologies (fake-verified).

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T025 [P] Add a `CHANGELOG.md` `[Unreleased]` entry under `MisaConnect.ESign` (routing fix + `AuthUnderWebdev`; Defect D hardening; note Defect B was a non-issue).
- [ ] T026 [P] Update README config/supported-ops docs (repo root `README.md` and `src/MisaConnect.ESign.Client/README.md`): `BaseUrl` = host root (path tolerated), document `AuthUnderWebdev` + the Production/Sandbox default.
- [ ] T027 Bump the `MisaConnect.ESign` package version to `2.1.0` (version property in the Client csproj / version props).
- [ ] T028 Run `dotnet format MisaConnect.slnx` and the full ESign unit + integration suites; confirm green with `TreatWarningsAsErrors=true`.
- [ ] T029 Validate [quickstart.md](./quickstart.md) end to end; record the **production login-path pre-release check** (research D7) as a release-gate item (do not tag 2.1.0 until confirmed, or ship guidance to set `AuthUnderWebdev=true` for Production if prod serves login under `/webdev/`).

---

## Dependencies & Execution Order

### Phase order
- Setup (P1) → Foundational (P2) → US1 (P3) → US3 (P5) → US2 (P4 — independent, may run any time after Foundational) → Polish (P6).
- **Within a story**: write tests first (red), then implement (green).

### Story dependencies
- **US1 (P1)**: depends only on Foundational. The actual blocking bug (A+C) fix. **MVP.**
- **US3 (P2)**: depends on US1 (its login-under-`/webdev/` resolution assumes the origin-normalized base from T011) — and is **required alongside US1 for real-sandbox correctness** (US1 alone leaves login at root, which 405s on the real sandbox). Independently *testable* against the fake, but ship US1+US3 together.
- **US2 (P2)**: independent of US1/US3 (response-handling only); touches the same `MisaESignWireClient.cs` as T005/T011, so sequence its edit after Foundational to avoid conflicts.

### File-conflict notes (not [P] across these)
- `MisaESignWireClient.cs`: T005, T016.
- `ESignRouteResolver.cs`: T004, T022.
- `ServiceCollectionExtensions.cs`: T005, T011.
- `FakeMisaESignServer.cs`: T006, T012, T017, T023.

## Parallel Opportunities

- T007–T010 (US1 tests, distinct files) can be written in parallel.
- T014–T015 (US2 tests) in parallel; T019–T021 (US3 tests) in parallel.
- Polish T025, T026 in parallel.

## Implementation Strategy

- **MVP**: Setup → Foundational → US1 → (US3 to complete real-sandbox auth) → validate cert-list/signing against the fake with a `/webdev/` base.
- **Hardening**: US2 (fail-loud) can land independently and improves diagnosability immediately.
- Commit after each task or logical group; keep the suite green at every checkpoint.

## Notes

- Defect B requires **no** implementation — only the T009 assertion that locks the already-correct bearer.
- The error-mapper endpoint constants are intentionally **untouched**; the resolver separates request path from endpoint identity (research D2), so error categorization is unaffected.
- No secrets in the new exception/logs (FR-007): body snippet only, never request headers.

---
description: "Task list — MISA eSign Two-Factor Authentication (OTP) slice"
---

# Tasks: MISA eSign — Two-Factor Authentication (OTP)

**Input**: Design documents from `/specs/002-misa-esign-2fa-otp/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Tests are included — Constitution Principle VI ("Tests are the spec") is binding for this slice, and FR-039 / FR-044 / SC-011 / SC-012 / SC-013 / SC-014 can only be verified by the new test files enumerated in [plan.md §Project Structure](./plan.md). The unit-suite latency budget is `< 30s` (SC-013).

**Organization**: Tasks are grouped by user story (US1 = P1 / MVP, US2 = P2, US3 = P3) so each story can be implemented and validated independently against its acceptance scenarios.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- All paths are relative to the repo root `f:\Projects\MyProjects\MisaConnect\`

## Path Conventions

Single layered solution (Domain → Application → Infrastructure → Client) — see [docs/architecture.md](../../docs/architecture.md). Production code lives under `src/MisaConnect.ESign.{Domain,Application,Infrastructure,Client}/`, tests under `tests/MisaConnect.ESign.{UnitTests,IntegrationTests}/`. No new csproj is introduced by this slice.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Package versioning and changelog touch-ups that should land before any code task.

- [X] T001 [P] Bump the `MisaConnect.ESign` NuGet package version to `2.0.0-preview.2` in [src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj](../../src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj) (no other csproj versions change — slice 2 is additive)
- [X] T002 [P] Add `[Unreleased]` entries to [CHANGELOG.md](../../CHANGELOG.md) enumerating every slice-2 addition (`SignInWithOtpAsync`, `ResendOtpAsync`, `IOtpProvider`, `OtpDeliveryChannel`, four typed OTP exceptions, `AuthenticationFailedException.Username`, `OtpResendResultDto`, `MisaESignOptions.Otp`, two new `IMisaESignWireClient` methods)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain types, Application records/ports, Infrastructure options/routes/handlers, and the log-scrubber rule extensions that ALL three user stories depend on.

**⚠️ CRITICAL**: No user-story phase may begin until every task in this phase is complete.

- [X] T003 [P] Create the `OtpDeliveryChannel` byte-backed enum (`SmsOrEmail = 0`, `Authenticator = 1`) in [src/MisaConnect.ESign.Domain/Authentication/OtpDeliveryChannel.cs](../../src/MisaConnect.ESign.Domain/Authentication/OtpDeliveryChannel.cs) per [data-model.md §1.1](./data-model.md)
- [X] T004 Edit `AuthenticationFailedException` to add the non-null `Username` property and a 5th optional constructor parameter (default `""`) in [src/MisaConnect.ESign.Domain/Errors/AuthenticationFailedException.cs](../../src/MisaConnect.ESign.Domain/Errors/AuthenticationFailedException.cs) per [data-model.md §1.3](./data-model.md) and [public-surface.md §4](./contracts/public-surface.md)
- [X] T005 [P] Create `InvalidOtpException` as a `sealed` subclass of `AuthenticationFailedException` (defaults `RawCode = "InvalidOtp"`, `Requires2FA = false`) in [src/MisaConnect.ESign.Domain/Errors/InvalidOtpException.cs](../../src/MisaConnect.ESign.Domain/Errors/InvalidOtpException.cs) (depends on T004)
- [X] T006 [P] Create `ExpiredOtpException` as a `sealed` subclass of `AuthenticationFailedException` (defaults `RawCode = "ExpiredOtp"`, `Requires2FA = false`) in [src/MisaConnect.ESign.Domain/Errors/ExpiredOtpException.cs](../../src/MisaConnect.ESign.Domain/Errors/ExpiredOtpException.cs) (depends on T004)
- [X] T007 [P] Create `ExhaustedOtpAttemptsException` as a `sealed` subclass of `AuthenticationFailedException` (defaults `RawCode = "ExhaustedOtpAttempts"`, `Requires2FA = false`) in [src/MisaConnect.ESign.Domain/Errors/ExhaustedOtpAttemptsException.cs](../../src/MisaConnect.ESign.Domain/Errors/ExhaustedOtpAttemptsException.cs) (depends on T004)
- [X] T008 [P] Create `OtpRejectedException` (residual bucket; preserves MISA's raw `errorCode` when provided) in [src/MisaConnect.ESign.Domain/Errors/OtpRejectedException.cs](../../src/MisaConnect.ESign.Domain/Errors/OtpRejectedException.cs) (depends on T004)
- [X] T009 [P] Create the `OtpChallenge` sealed record `(string UserName, string CorrelationId)` in [src/MisaConnect.ESign.Application/Abstractions/OtpChallenge.cs](../../src/MisaConnect.ESign.Application/Abstractions/OtpChallenge.cs)
- [X] T010 [P] Create the `OtpSubmission` sealed record `(string Code, OtpDeliveryChannel OtpType, bool Remember)` in [src/MisaConnect.ESign.Application/Abstractions/OtpSubmission.cs](../../src/MisaConnect.ESign.Application/Abstractions/OtpSubmission.cs) (depends on T003)
- [X] T011 [P] Create the `OtpResendResult` sealed record `(bool Success, string? RawCode, string? UserMsg, string? DevMsg, string CorrelationId)` in [src/MisaConnect.ESign.Application/Abstractions/OtpResendResult.cs](../../src/MisaConnect.ESign.Application/Abstractions/OtpResendResult.cs)
- [X] T012 Create the `IOtpProvider` port (`ProvideAsync(OtpChallenge, CancellationToken)`, `RequestResendAsync(OtpChallenge, string?, CancellationToken)`) in [src/MisaConnect.ESign.Application/Abstractions/IOtpProvider.cs](../../src/MisaConnect.ESign.Application/Abstractions/IOtpProvider.cs) (depends on T009, T010, T011)
- [X] T013 Edit `MisaESignOptions` to add the nested `MisaESignOtpOptions` (`DefaultResendLanguage = "en-US"`) and the `Otp` property in [src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptions.cs](../../src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptions.cs) per [public-surface.md §6](./contracts/public-surface.md)
- [X] T014 Edit `MisaESignOptionsValidator` to assert `Otp.DefaultResendLanguage` is non-empty after binding in [src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptionsValidator.cs](../../src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptionsValidator.cs) (depends on T013)
- [X] T015 Edit `ESignHttpRoutes` to add `AuthTwoFactor = "api/auth/api/v1/auth/two-factor-auth"` and `AuthResendOtp = "webdev/api/auth/api/v1/auth/resend-otp-auth"` constants in [src/MisaConnect.ESign.Infrastructure/ESign/ESignHttpRoutes.cs](../../src/MisaConnect.ESign.Infrastructure/ESign/ESignHttpRoutes.cs) per [data-model.md §3.6](./data-model.md)
- [X] T016 Edit `RemoteSigningAuthHandler.IsAuthEndpoint(...)` to also return `true` for paths ending in `/two-factor-auth` or `/resend-otp-auth` (case-insensitive) in [src/MisaConnect.ESign.Infrastructure/Http/RemoteSigningAuthHandler.cs](../../src/MisaConnect.ESign.Infrastructure/Http/RemoteSigningAuthHandler.cs) per [wire-envelopes.md "Header convention reminder"](./contracts/wire-envelopes.md)
- [X] T017 Edit `ESignLogScrubber` to add three rules — drop `$.code` on request bodies to `*/two-factor-auth`, redact `$..device*` defensively on every captured body, ensure the existing `$.data.accessToken`/`remoteSigningAccessToken`/`refreshToken` rules apply to `/two-factor-auth` responses (same envelope as `/login-api`) — in [src/MisaConnect.ESign.Infrastructure/Logging/ESignLogScrubber.cs](../../src/MisaConnect.ESign.Infrastructure/Logging/ESignLogScrubber.cs) per [research.md R-7](./research.md)
- [X] T018 Edit `LayerAuditTests` to extend the namespace audit to cover `Domain.Authentication.OtpDeliveryChannel`, the four new `Domain.Errors.*Otp*Exception` types, `Application.Abstractions.{IOtpProvider, OtpChallenge, OtpSubmission, OtpResendResult}`, `Application.UseCases.{ExchangeOtp, ResendOtp}`, `Application.Errors.OtpErrorMapper`, and `Application.Validation.OtpSubmissionValidator` in [tests/MisaConnect.ESign.UnitTests/Layering/LayerAuditTests.cs](../../tests/MisaConnect.ESign.UnitTests/Layering/LayerAuditTests.cs)

**Checkpoint**: Foundation ready — user story implementation can now begin in parallel.

---

## Phase 3: User Story 1 — Complete sign-in when MISA requires a second factor (Priority: P1) 🎯 MVP

**Goal**: A consumer whose MISA account is enrolled in 2FA can complete a signing operation by catching the typed `Requires2FA = true` signal, supplying an OTP via either the explicit `SignInWithOtpAsync` pair or a DI-registered `IOtpProvider`, and resuming signing with cached tokens — within one round-trip to `/two-factor-auth` per cache key.

**Independent Test**: Drive the in-repo `EsignFake/FakeMisaESignServer.cs` with a login fixture returning `errorCode 122`. Assert: (a) the SDK POSTs `{userName, code, otpType, remember}` to `/two-factor-auth`, (b) cached tokens after success have the slice-1 `AuthSession` shape, (c) a second `SignPdfAsync` call performs zero additional `/login-api` or `/two-factor-auth` requests (SC-009, SC-010). Demonstrable end-to-end against the MISA sandbox with `MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER` configured.

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation tasks T030–T041.**

- [X] T019 [P] [US1] Add a `/two-factor-auth` handler to `FakeMisaESignServer` — tracks OTP submissions per cache key, accepts a configurable response envelope per test (200 + token bundle OR 4xx + `ResponseError`), and asserts no `AuthorizationRM` header — in [tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs](../../tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs)
- [X] T020 [P] [US1] Create `TwoFactorAuthExchangeTests` covering the happy path: wire body equals `{userName, code, otpType, remember}` verbatim, `otpType` round-trips `0` and `1`, `remember` round-trips `true` and `false`, the resulting `AuthSession` is written to `ITokenCache` under the same key as `/login-api` would have used (FR-035), in [tests/MisaConnect.ESign.UnitTests/Authentication/TwoFactorAuthExchangeTests.cs](../../tests/MisaConnect.ESign.UnitTests/Authentication/TwoFactorAuthExchangeTests.cs)
- [X] T021 [P] [US1] Create `TwoFactorAuthSingleFlightTests` asserting that N concurrent `SignInWithOtpAsync` invocations against the same cache key produce exactly ONE outbound `/two-factor-auth` POST and that all waiters return the same cached `AccessToken` (FR-042) in [tests/MisaConnect.ESign.UnitTests/Authentication/TwoFactorAuthSingleFlightTests.cs](../../tests/MisaConnect.ESign.UnitTests/Authentication/TwoFactorAuthSingleFlightTests.cs)
- [X] T021a [P] [US1] Create `TwoFactorAuthTransportRetryTests` asserting that (a) transient 5xx and 429 responses from `/two-factor-auth` engage `TransientFailureRetryHandler` and ultimately surface `ESignTransportException` after the retry budget is exhausted, (b) a 401 from `/two-factor-auth` does NOT trigger `RemoteSigningAuthHandler`'s refresh path (per FR-043 + T016) — it surfaces as `AuthenticationFailedException` directly, (c) the cache is NOT written on either failure mode (FR-034/FR-042) — in [tests/MisaConnect.ESign.UnitTests/Authentication/TwoFactorAuthTransportRetryTests.cs](../../tests/MisaConnect.ESign.UnitTests/Authentication/TwoFactorAuthTransportRetryTests.cs)
- [X] T022 [P] [US1] Create `OtpProviderTransparentFlowTests` asserting that when an `IOtpProvider` is registered, `SignPdfAsync` catches the 122 signal internally, invokes `ProvideAsync`, runs `ExchangeOtp`, and recurses exactly once (R-10 — no internal retry) in [tests/MisaConnect.ESign.UnitTests/Authentication/OtpProviderTransparentFlowTests.cs](../../tests/MisaConnect.ESign.UnitTests/Authentication/OtpProviderTransparentFlowTests.cs)
- [X] T023 [P] [US1] Create `MisaESignClientUsernameContextTests` asserting (a) the `AsyncLocal<string?>` is set when the facade rethrows `AuthenticationFailedException(Requires2FA = true)`, (b) it is cleared on a successful `SignInWithOtpAsync`, (c) it is NOT shared across unrelated async flows in the same process (the audit point in [data-model.md §4.1](./data-model.md)), (d) `SignInWithOtpAsync` throws `InvalidOperationException` when invoked without a captured context — in [tests/MisaConnect.ESign.UnitTests/Authentication/MisaESignClientUsernameContextTests.cs](../../tests/MisaConnect.ESign.UnitTests/Authentication/MisaESignClientUsernameContextTests.cs)
- [X] T024 [P] [US1] Create `OtpSubmissionValidatorTests` asserting that `Code` must be non-null and non-whitespace; `OtpType` (closed enum) and `Remember` (required bool) require no runtime check, in [tests/MisaConnect.ESign.UnitTests/Validation/OtpSubmissionValidatorTests.cs](../../tests/MisaConnect.ESign.UnitTests/Validation/OtpSubmissionValidatorTests.cs)
- [X] T025 [P] [US1] Edit `ESignErrorMapperTests` to add a row asserting that a `/two-factor-auth` 4xx with `errorCode = "122"` surfaces `AuthenticationFailedException(Requires2FA = true)` and that the orchestrator does NOT auto-loop (edge case 7 in [spec.md](./spec.md)) in [tests/MisaConnect.ESign.UnitTests/Errors/ESignErrorMapperTests.cs](../../tests/MisaConnect.ESign.UnitTests/Errors/ESignErrorMapperTests.cs)
- [X] T026 [P] [US1] Edit `EnsureAccessTokenTests` to add an assertion that the consumer's `password` from `MisaESignOptions.UserName/Password` is NOT propagated past `EnsureAccessToken` when surfacing the 122 signal (FR-031) in [tests/MisaConnect.ESign.UnitTests/Authentication/EnsureAccessTokenTests.cs](../../tests/MisaConnect.ESign.UnitTests/Authentication/EnsureAccessTokenTests.cs)
- [X] T027 [P] [US1] Create `TwoFactorAuthHappyPathFakeServerTests` driving the fake server's `/login-api` to return 122 → `/two-factor-auth` returns the token bundle → first `SignPdfAsync` completes → second `SignPdfAsync` reuses the cached token (zero additional auth requests — SC-009, SC-010), in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/TwoFactorAuthHappyPathFakeServerTests.cs](../../tests/MisaConnect.ESign.IntegrationTests/EndToEnd/TwoFactorAuthHappyPathFakeServerTests.cs)
- [X] T028 [P] [US1] Create `OtpProviderTransparentFlowFakeServerTests` registering a fake `IOtpProvider` via DI and asserting `SignPdfAsync` completes end-to-end without surfacing any exception to the consumer in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/OtpProviderTransparentFlowFakeServerTests.cs](../../tests/MisaConnect.ESign.IntegrationTests/EndToEnd/OtpProviderTransparentFlowFakeServerTests.cs)
- [X] T029 [P] [US1] Create `TwoFactorAuthSandboxTests` with a `[SandboxFact(Requires = SandboxRequirement.TwoFactorAuth)]` attribute that skips cleanly when any of `MISACONNECT_ESIGN_SANDBOX_*`, `MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER`, or `MISACONNECT_ESIGN_SANDBOX_USER_2FA_ENABLED` is absent; fails loudly when MISA explicitly rejects the credentials (SC-014, R-8), in [tests/MisaConnect.ESign.IntegrationTests/Sandbox/TwoFactorAuthSandboxTests.cs](../../tests/MisaConnect.ESign.IntegrationTests/Sandbox/TwoFactorAuthSandboxTests.cs)

### Implementation for User Story 1

- [X] T030 [P] [US1] Create `TwoFactorAuthRequestDto` (camelCase wire fields `userName`, `code`, `otpType`, `remember`) and declare the `TwoFactorAuthResponseDto = LoginResponseDto` `using` alias in [src/MisaConnect.ESign.Infrastructure/ESign/Wire/TwoFactorAuthDtos.cs](../../src/MisaConnect.ESign.Infrastructure/ESign/Wire/TwoFactorAuthDtos.cs) per [wire-envelopes.md §E8](./contracts/wire-envelopes.md) and [data-model.md §3.1–§3.2](./data-model.md)
- [X] T031 [US1] Edit `AuthSessionMapper` to add `FromTwoFactorAuthResponse(LoginResponseDto, DateTimeOffset)` as a one-line forwarder to `FromLoginResponse(...)` in [src/MisaConnect.ESign.Infrastructure/ESign/Mapping/AuthSessionMapper.cs](../../src/MisaConnect.ESign.Infrastructure/ESign/Mapping/AuthSessionMapper.cs)
- [X] T032 [US1] Edit `ESignErrorMapper.MapLogin` to populate the new `AuthenticationFailedException.Username` (from the request body's `userName`) on the `errorCode = 122` path in [src/MisaConnect.ESign.Application/Errors/ESignErrorMapper.cs](../../src/MisaConnect.ESign.Application/Errors/ESignErrorMapper.cs)
- [X] T033 [US1] Create `OtpErrorMapper` with `MapTwoFactor(...)` — canonical errorCode dispatch for `122 → AuthenticationFailedException(Requires2FA=true)`, `1001 → InvalidOtpException`, `1002 → ExpiredOtpException`, `1003 → ExhaustedOtpAttemptsException`; placeholder substring fallback that always returns `OtpRejectedException` (US3 finalizes the keyword tables); a `BuildDetail(...)` helper gated on `MisaESignOptions.Errors.IncludeRawErrorMessage`; and a static `EndpointTwoFactorAuth` / `EndpointResendOtp` constants pair — in [src/MisaConnect.ESign.Application/Errors/OtpErrorMapper.cs](../../src/MisaConnect.ESign.Application/Errors/OtpErrorMapper.cs) per [error-mapping.md §A.8](./contracts/error-mapping.md)
- [X] T034 [US1] Edit `IMisaESignWireClient` to add `Task<AuthSession> TwoFactorAuthAsync(string userName, string code, OtpDeliveryChannel otpType, bool remember, CancellationToken ct)` in [src/MisaConnect.ESign.Application/Abstractions/IMisaESignWireClient.cs](../../src/MisaConnect.ESign.Application/Abstractions/IMisaESignWireClient.cs)
- [X] T035 [US1] Edit `MisaESignWireClient` to implement `TwoFactorAuthAsync` — POST with `attachAuthorization: false`, deserialize the envelope, throw via `OtpErrorMapper.MapTwoFactor(...)` on non-success, return `AuthSessionMapper.FromTwoFactorAuthResponse(dto, _clock.UtcNow)` on success — in [src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs](../../src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs)
- [X] T036 [P] [US1] Create `OtpSubmissionValidator` validating `Code` non-null/non-whitespace in [src/MisaConnect.ESign.Application/Validation/OtpSubmissionValidator.cs](../../src/MisaConnect.ESign.Application/Validation/OtpSubmissionValidator.cs)
- [X] T037 [US1] Create the `ExchangeOtp` use case — compose cache key via `ITokenCacheKeySelector`, acquire a single-flight slot via the `SingleFlightRefresh` delegate (R-9), call `IMisaESignWireClient.TwoFactorAuthAsync`, map `AuthSession → AccessToken`, `ITokenCache.SetAsync`, return the `AccessToken`; on failure the cache is NOT written — in [src/MisaConnect.ESign.Application/UseCases/ExchangeOtp.cs](../../src/MisaConnect.ESign.Application/UseCases/ExchangeOtp.cs) per [data-model.md §2.5](./data-model.md)
- [X] T038 [US1] Edit the `SignPdf` use case to wrap its body in `try { ... } catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is not null) { ... }` that builds an `OtpChallenge`, invokes `IOtpProvider.ProvideAsync(...)`, runs `ExchangeOtp.ExecuteAsync(...)`, and **recursively invokes `this.ExecuteAsync(request, ct)` exactly once** (R-10) — in [src/MisaConnect.ESign.Application/UseCases/SignPdf.cs](../../src/MisaConnect.ESign.Application/UseCases/SignPdf.cs) per [data-model.md §2.7](./data-model.md)
- [X] T039 [US1] Edit `IMisaESignClient` to add `Task SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken ct = default)` with the xmldoc enumerating typed exceptions per [public-surface.md §1](./contracts/public-surface.md) in [src/MisaConnect.ESign.Client/IMisaESignClient.cs](../../src/MisaConnect.ESign.Client/IMisaESignClient.cs)
- [X] T040 [US1] Edit `MisaESignClient` — implement `SignInWithOtpAsync` reading the captured `userName` from a private `AsyncLocal<string?>`, throwing `InvalidOperationException` when unset, invoking `OtpSubmissionValidator` + `ExchangeOtp`, clearing the `AsyncLocal` on success; in `SignPdfAsync`'s catch path set the `AsyncLocal` to `ex.Username` before rethrowing (ONLY when no `IOtpProvider` is registered — when one is, the catch is internal to `SignPdf` and the facade never rethrows) — in [src/MisaConnect.ESign.Client/MisaESignClient.cs](../../src/MisaConnect.ESign.Client/MisaESignClient.cs) per [data-model.md §4.1–§4.2](./data-model.md)
- [X] T041 [US1] Edit `ServiceCollectionExtensions.AddCoreServices(...)` to `services.AddScoped<ExchangeOtp>()` and update the `SignPdf` factory lambda to resolve `IOtpProvider?` via `sp.GetService<IOtpProvider>()` and pass it through the constructor — DO NOT `TryAdd` a default `IOtpProvider`, leave consumer-registered providers as the only source — in [src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs](../../src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs) per [data-model.md §3.11](./data-model.md)

**Checkpoint**: User Story 1 is fully functional and testable independently. T020–T029 should all pass. The MVP — a 2FA-enrolled MISA account completing PDF signing through both consumer surfaces — is shippable here.

---

## Phase 4: User Story 2 — Re-deliver an OTP that was lost or never arrived (Priority: P2)

**Goal**: Consumers can request OTP re-delivery via `IMisaESignClient.ResendOtpAsync(language?, ct)`. The SDK POSTs `{userName, language}` to `/resend-otp-auth`, defaults `language` to `"en-US"` from `MisaESignOptions.Otp.DefaultResendLanguage`, accepts per-call overrides, and surfaces MISA failure envelopes as a typed `OtpResendResultDto { Success = false, ... }` rather than throwing.

**Independent Test**: Drive the fake server's `/resend-otp-auth` handler through each documented success/failure envelope. Assert: (a) the request body is exactly `{userName, language}`, (b) default `language` is `"en-US"` when the consumer does not override, (c) the surfaced result distinguishes success from MISA-returned failure without the consumer parsing raw envelopes (FR-038).

### Tests for User Story 2 ⚠️

- [X] T042 [P] [US2] Add a `/resend-otp-auth` handler to `FakeMisaESignServer` with configurable success and typed-failure envelopes (200 + `status.error=false`, 200 + `status.error=true`, 4xx + envelope, 4xx without envelope) in [tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs](../../tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs)
- [X] T043 [P] [US2] Create `ResendOtpTests` covering: default `"en-US"` is sent when the consumer does not override, per-call override `"vi-VN"` round-trips verbatim, 200+`status.error=true` surfaces `OtpResendResult{Success=false, RawCode/UserMsg/DevMsg populated}`, 4xx surfaces the same typed-failure shape, the captured correlation ID is non-empty — in [tests/MisaConnect.ESign.UnitTests/Authentication/ResendOtpTests.cs](../../tests/MisaConnect.ESign.UnitTests/Authentication/ResendOtpTests.cs)
- [X] T044 [P] [US2] Edit `MisaESignOptionsValidatorTests` to assert (a) `Otp.DefaultResendLanguage` defaults to `"en-US"` after a no-op bind and (b) the validator rejects an explicitly-empty `DefaultResendLanguage` in [tests/MisaConnect.ESign.UnitTests/Configuration/MisaESignOptionsValidatorTests.cs](../../tests/MisaConnect.ESign.UnitTests/Configuration/MisaESignOptionsValidatorTests.cs)
- [X] T045 [P] [US2] Create `ResendOtpFakeServerTests` driving the fake server's `/resend-otp-auth` through one acceptance scenario per envelope variant — happy path with default language, happy path with override, typed-failure (200+error), typed-failure (4xx), transport-retry-exhausted throws `ESignTransportException` — in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/ResendOtpFakeServerTests.cs](../../tests/MisaConnect.ESign.IntegrationTests/EndToEnd/ResendOtpFakeServerTests.cs)

### Implementation for User Story 2

- [X] T046 [P] [US2] Create `ResendOtpRequestDto`, `ResendOtpResponseDto` (reusing `LoginStatusBlockDto?` for `status`), nested `ResendOtpDataDto` and `ResendOtpUserDto` in [src/MisaConnect.ESign.Infrastructure/ESign/Wire/ResendOtpDtos.cs](../../src/MisaConnect.ESign.Infrastructure/ESign/Wire/ResendOtpDtos.cs) per [wire-envelopes.md §E9](./contracts/wire-envelopes.md) and [data-model.md §3.3–§3.4](./data-model.md)
- [X] T047 [US2] Extend `OtpErrorMapper` with `MapResendResult(int statusCode, ResponseError? envelope, string correlationId, bool includeRawErrorMessage = false)` returning `OtpResendResult` for the four documented response permutations per [error-mapping.md §A.9](./contracts/error-mapping.md) in [src/MisaConnect.ESign.Application/Errors/OtpErrorMapper.cs](../../src/MisaConnect.ESign.Application/Errors/OtpErrorMapper.cs)
- [X] T048 [US2] Edit `IMisaESignWireClient` to add `Task<OtpResendResult> ResendOtpAsync(string userName, string language, CancellationToken ct)` in [src/MisaConnect.ESign.Application/Abstractions/IMisaESignWireClient.cs](../../src/MisaConnect.ESign.Application/Abstractions/IMisaESignWireClient.cs)
- [X] T049 [US2] Edit `MisaESignWireClient` to implement `ResendOtpAsync` — POST `{userName, language}` with `attachAuthorization: false`, deserialize the response, call `OtpErrorMapper.MapResendResult(...)`, return the typed result; transport failures propagate as `ESignTransportException` via the existing handlers — in [src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs](../../src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs)
- [X] T050 [US2] Create the `ResendOtp` use case — `language ??= options.CurrentValue.Otp.DefaultResendLanguage`, call `IMisaESignWireClient.ResendOtpAsync(...)`, no single-flight, no cache write — in [src/MisaConnect.ESign.Application/UseCases/ResendOtp.cs](../../src/MisaConnect.ESign.Application/UseCases/ResendOtp.cs) per [data-model.md §2.6](./data-model.md)
- [X] T051 [P] [US2] Create the public `OtpResendResultDto` (1:1 wrapper over `OtpResendResult`) in [src/MisaConnect.ESign.Client/Dtos/OtpResendResultDto.cs](../../src/MisaConnect.ESign.Client/Dtos/OtpResendResultDto.cs)
- [X] T052 [US2] Edit `IMisaESignClient` to add `Task<OtpResendResultDto> ResendOtpAsync(string? language = null, CancellationToken ct = default)` with the xmldoc per [public-surface.md §1](./contracts/public-surface.md) in [src/MisaConnect.ESign.Client/IMisaESignClient.cs](../../src/MisaConnect.ESign.Client/IMisaESignClient.cs)
- [X] T053 [US2] Edit `MisaESignClient` to implement `ResendOtpAsync` — read the captured `userName` from the `AsyncLocal<string?>` (throw `InvalidOperationException` when unset), invoke `ResendOtp`, wrap the result as `OtpResendResultDto` — in [src/MisaConnect.ESign.Client/MisaESignClient.cs](../../src/MisaConnect.ESign.Client/MisaESignClient.cs)
- [X] T054 [US2] Edit `ServiceCollectionExtensions.AddCoreServices(...)` to `services.AddScoped<ResendOtp>()` in [src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs](../../src/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs)

**Checkpoint**: User Stories 1 AND 2 both work independently. T042–T045 pass alongside the US1 suite.

---

## Phase 5: User Story 3 — Typed errors for OTP rejection, exhaustion, and expiry (Priority: P3)

**Goal**: `/two-factor-auth` rejections surface as four distinct typed exceptions (`InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`, `OtpRejectedException`) that consumers can branch on by type — never by string-matching `devMsg`. No log line contains the OTP `code`, the consumer's `password`, or any "remembered device" identifier.

**Independent Test**: Drive the fake server through one OTP submission per rejection category, each emitting the canonical `errorCode` (1001 / 1002 / 1003) per [error-mapping.md §A.8.1](./contracts/error-mapping.md). Assert: each surfaced exception's runtime type is the documented one (NOT inspecting any string field); each carries `errorCode` + correlation ID; a capture of the full log stream contains zero occurrences of the OTP `code` value (SC-011, SC-012).

### Tests for User Story 3 ⚠️

- [X] T055 [US3] Create `OtpErrorMapperTests` with `[Theory]` inline data covering EVERY row from [error-mapping.md](./contracts/error-mapping.md) — 4 A.8.1 canonical rows (122, 1001, 1002, 1003), the A.8.2 keyword rows for `InvalidOtp`/`ExpiredOtp`/`ExhaustedOtpAttempts` × English + Vietnamese (≥9 rows including the residual bucket and the "errorCode populated but neither canonical nor keyword-matched → residual" case), 4 A.8.4 cross-cutting rows (401, 429-exhausted, malformed JSON, operation-timeout), 6 A.9.1 resend permutations (200-ok / 200-error / 4xx-with-envelope / 4xx-no-envelope / 429-exhausted-throws / timeout-throws), plus 2 `IncludeRawErrorMessage`-toggle rows asserting that `Detail`/`Message` includes vendor `userMsg`/`devMsg` ONLY when the flag is `true` (FR-040) — total ≈25 rows in [tests/MisaConnect.ESign.UnitTests/Errors/OtpErrorMapperTests.cs](../../tests/MisaConnect.ESign.UnitTests/Errors/OtpErrorMapperTests.cs)
- [X] T056 [P] [US3] Edit `ESignLogScrubberTests` to add captured-log assertions per SC-012: (a) the `code` value supplied to `SignInWithOtpAsync` appears nowhere in the captured log, (b) the consumer's `password` is never propagated past the auth layer's log surface, (c) the defensive `$..device*` matcher fires on a fixture body containing a device key — in [tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs](../../tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs)
- [X] T057 [P] [US3] Extend `FakeMisaESignServer` to return configurable canonical-code rejection envelopes per fixture (1001/1002/1003 mapped through `state.NextTwoFactorErrorCode`) so `OtpRejectionFakeServerTests` can drive each category independently in [tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs](../../tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs)
- [X] T058 [P] [US3] Create `OtpRejectionFakeServerTests` exercising one fake-server rejection per category and asserting `Assert.IsType<InvalidOtpException>`, `Assert.IsType<ExpiredOtpException>`, `Assert.IsType<ExhaustedOtpAttemptsException>` respectively (SC-011 — non-equal exception types verifiable WITHOUT string inspection), plus a captured-log scan for zero occurrences of the OTP `code` (SC-012), in [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/OtpRejectionFakeServerTests.cs](../../tests/MisaConnect.ESign.IntegrationTests/EndToEnd/OtpRejectionFakeServerTests.cs)

### Implementation for User Story 3

- [X] T059 [US3] Finalize `OtpErrorMapper.MapTwoFactor` — replace the US1 placeholder substring fallback with the full keyword tables from [error-mapping.md §A.8.2](./contracts/error-mapping.md) (English + Vietnamese tokens for `invalid`, `expired`, `exhausted` buckets; residual → `OtpRejectedException`), confirm canonical-table-first ordering, populate `Username` on the re-challenge (`errorCode = 122`) branch, and ensure `IncludeRawErrorMessage` toggles `devMsg`/`userMsg` inclusion in `Detail` exactly as `ESignErrorMapper.BuildDetail` does — in [src/MisaConnect.ESign.Application/Errors/OtpErrorMapper.cs](../../src/MisaConnect.ESign.Application/Errors/OtpErrorMapper.cs)

**Checkpoint**: All three user stories are independently functional. SC-011 and SC-012 are verifiable end-to-end.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation refresh, format gate, and validation runs covering the constitution's invariants (SC-013 unit-test latency, SC-014 sandbox-skip-cleanliness, the format-before-PR rule).

- [X] T060 [P] Update the supported-operations table in [README.md](../../README.md) to list `SignInWithOtpAsync`, `ResendOtpAsync`, and the optional `IOtpProvider` port (under the `MisaConnect.ESign` family section)
- [X] T061 [P] Update [docs/sandbox-setup.md](../../docs/sandbox-setup.md) to document the new `MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER` env var (binary path OR literal OTP value) and the optional `MISACONNECT_ESIGN_SANDBOX_USER_2FA_ENABLED` skip-gate per [research.md R-8](./research.md)
- [X] T062 Run `dotnet format MisaConnect.slnx` from the repo root to satisfy the pre-PR format gate (zero diffs expected after a clean run)
- [X] T063 Run `dotnet test tests/MisaConnect.ESign.UnitTests` and verify the wall-clock total is `< 30s` (SC-013)
- [X] T064 Run `dotnet test tests/MisaConnect.ESign.IntegrationTests` and verify (a) every `*FakeServerTests.cs` class passes without network, (b) every `[SandboxFact]` skips cleanly when sandbox env vars are absent (SC-014)
- [X] T065 Walk through the [quickstart.md](./quickstart.md) Path A (explicit pair) and Path B (transparent `IOtpProvider`) scenarios against the in-repo fake server and confirm the documented consumer code compiles and runs to success

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup, T001–T002)**: No dependencies — can start immediately.
- **Phase 2 (Foundational, T003–T018)**: Depends on Setup completion. Within Phase 2, T004 blocks T005–T008; T013 blocks T014; T009/T010/T011 block T012; everything else is independent.
- **Phase 3 (US1, T019–T041)**: All tasks depend on Phase 2 completion. Tests T019–T029 can be authored once foundational types compile; implementation T030–T041 follows the test-first discipline.
- **Phase 4 (US2, T042–T054)**: Depends on Phase 2 completion. US2 implementation can run in parallel with US1 if developer capacity allows; the shared-file edits (T047 on `OtpErrorMapper.cs`, T049 on `MisaESignWireClient.cs`, T053 on `MisaESignClient.cs`, T054 on `ServiceCollectionExtensions.cs`) must be sequenced after the corresponding US1 edits if the same developer is working both stories serially.
- **Phase 5 (US3, T055–T059)**: Depends on Phase 2 completion. US3 finalizes `OtpErrorMapper` (T059) — if US3 is implemented in parallel with US1, the developer of US1 stops at the placeholder substring fallback in T033 and US3's developer fills in the canonical/substring tables in T059.
- **Phase 6 (Polish, T060–T065)**: Depends on all desired user stories being complete (typically US1+US2+US3 for the full slice).

### User Story Dependencies

- **US1 (P1)** can start after Phase 2 — no dependency on US2 or US3. US1 is the MVP and the only mandatory deliverable.
- **US2 (P2)** can start after Phase 2 — no dependency on US1. The transparent-provider resend hook (`IOtpProvider.RequestResendAsync`) defined in Phase 2 is exercised by either US1 (transparent flow) or US2 (explicit `ResendOtpAsync` facade); they remain independent.
- **US3 (P3)** can start after Phase 2 — no dependency on US1/US2 BEYOND the `OtpErrorMapper` skeleton. If US1 has not yet been implemented when US3 starts, T033 (US1) can be inlined into US3's work and T059 becomes a single creation task.

### Within Each User Story

- Tests (T019–T029, T042–T045, T055–T058) MUST be authored and FAIL before the corresponding implementation tasks complete.
- Wire DTOs (T030, T046) precede the wire-client methods that use them.
- Wire-client methods (T035, T049) precede the use cases (T037, T050) that call them.
- Use cases (T037, T050) precede the facade methods (T040, T053) that orchestrate them.
- DI registration (T041, T054) follows the use case existing in code.

### Parallel Opportunities

- T001 / T002: two unrelated files — fully parallel.
- T003 / T005–T008 / T009–T011 / T013 / T015 / T017: distinct files within Phase 2, all parallelizable after their respective dependencies (T004 for the exceptions, T009–T011 for the IOtpProvider port).
- Within US1: T020–T029 (tests) are all in distinct files — fully parallelizable. T030 + T036 are in distinct files — parallelizable.
- Within US2: T042 / T043 / T044 / T045 (tests) are distinct files. T046 + T051 are distinct files — parallelizable.
- Within US3: T055 is the single mapper-tests authoring task; T056 / T057 / T058 are distinct files.
- Different stories: a three-developer team can work US1, US2, US3 simultaneously after Phase 2 finishes; the shared-file edits (`OtpErrorMapper.cs`, `MisaESignWireClient.cs`, `MisaESignClient.cs`, `ServiceCollectionExtensions.cs`) need a quick merge-coordination but do NOT block independent test runs.

---

## Parallel Example: User Story 1

```text
# After Phase 2 completes, run the US1 test-authoring tasks together:
Task: "T019 Add /two-factor-auth handler to FakeMisaESignServer.cs"
Task: "T020 Create TwoFactorAuthExchangeTests.cs"
Task: "T021 Create TwoFactorAuthSingleFlightTests.cs"
Task: "T022 Create OtpProviderTransparentFlowTests.cs"
Task: "T023 Create MisaESignClientUsernameContextTests.cs"
Task: "T024 Create OtpSubmissionValidatorTests.cs"
Task: "T025 Edit ESignErrorMapperTests.cs (122-do-not-loop case)"
Task: "T026 Edit EnsureAccessTokenTests.cs (no-password-propagation)"
Task: "T027 Create TwoFactorAuthHappyPathFakeServerTests.cs"
Task: "T028 Create OtpProviderTransparentFlowFakeServerTests.cs"
Task: "T029 Create TwoFactorAuthSandboxTests.cs"

# Then in implementation, the leaf tasks can fan out:
Task: "T030 Create TwoFactorAuthDtos.cs"
Task: "T036 Create OtpSubmissionValidator.cs"
```

---

## Parallel Example: User Story 2

```text
# Once Phase 2 is done (and ideally US1's wire-layer edits have landed):
Task: "T042 Add /resend-otp-auth handler to FakeMisaESignServer.cs"
Task: "T043 Create ResendOtpTests.cs"
Task: "T044 Edit MisaESignOptionsValidatorTests.cs (Otp.DefaultResendLanguage)"
Task: "T045 Create ResendOtpFakeServerTests.cs"

# In implementation:
Task: "T046 Create ResendOtpDtos.cs"
Task: "T051 Create OtpResendResultDto.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T002).
2. Complete Phase 2: Foundational (T003–T018) — every task in Phase 2 is a hard prerequisite.
3. Complete Phase 3: US1 (T019–T041) — author tests first, then implementation.
4. **STOP and VALIDATE**: Run `dotnet test tests/MisaConnect.ESign.UnitTests` (must pass `< 30s`) and `dotnet test tests/MisaConnect.ESign.IntegrationTests` (fake-server tests must pass; `[SandboxFact]` tests skip cleanly without env vars). Walk through [quickstart.md §2](./quickstart.md) Path A end-to-end against the fake server.
5. Ship `2.0.0-preview.2-rc.1` with US1 only if a partial release is wanted.

### Incremental Delivery

1. Phases 1 + 2 → Foundation ready.
2. Phase 3 (US1) → Test independently → Ship MVP if desired (the headline value of the slice).
3. Phase 4 (US2) → Test independently → Ship (adds the OTP-resend UX).
4. Phase 5 (US3) → Test independently → Ship (locks the typed-error contract).
5. Phase 6 (Polish) → Final `2.0.0-preview.2` cut.

### Parallel Team Strategy

With three developers:

1. The team completes Phases 1 + 2 together (foundational types touch every story).
2. Developer A → US1 (T019–T041) — the MVP path.
3. Developer B → US2 (T042–T054) — coordinates with Developer A on the shared-file edits to `OtpErrorMapper.cs`, `MisaESignWireClient.cs`, `IMisaESignWireClient.cs`, `MisaESignClient.cs`, `IMisaESignClient.cs`, and `ServiceCollectionExtensions.cs`.
4. Developer C → US3 (T055–T059) — uses Developer A's `OtpErrorMapper` skeleton (T033) as the starting point; their changes land as a refinement.
5. All three converge on Phase 6 (Polish) once their respective phases pass.

---

## Notes

- `[P]` tasks operate on different files with no incomplete dependencies — see the format rules in the speckit-tasks skill description.
- Every task path uses the repo-relative form as a clickable markdown link; the linker resolves them within VS Code.
- Per Constitution Principle V, slice 2 is a strict extension of slice 1 — no new csproj, no namespace reshuffles. All edits are additive.
- Per Constitution Principle VIII and SC-012, every test that captures a log stream MUST scan for zero occurrences of the OTP `code` value and the consumer's `password`.
- Per CLAUDE.md, `dotnet format MisaConnect.slnx` (T062) is REQUIRED before opening a PR.
- The integration-test split rule (CLAUDE.md): unit tests under `tests/MisaConnect.ESign.UnitTests` must stay `< 30s` total; integration tests under `tests/MisaConnect.ESign.IntegrationTests` may reach the sandbox or the in-repo fake server; `[SandboxFact]` skips cleanly without sandbox env vars (T064 confirms this).
- Verify each acceptance scenario in [spec.md §User Scenarios](./spec.md) maps to at least one test row before marking the corresponding implementation task complete.
- Commit after each task or logical group (auto-commit hooks are wired via `.specify/extensions.yml`).

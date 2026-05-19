# Implementation Plan: MISA eSign — Two-Factor Authentication (OTP)

**Branch**: `002-misa-esign-2fa-otp` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/002-misa-esign-2fa-otp/spec.md`

## Summary

Slice 2 of the `MisaConnect.ESign` product family. It unblocks MISA accounts where `/login-api` answers `errorCode = 122` ("2FA required") by adding two new MISA endpoints to the wire client and the orchestrator: `POST /api/auth/api/v1/auth/two-factor-auth` (OTP exchange — returns the same token bundle as `/login-api`) and `POST /webdev/api/auth/api/v1/auth/resend-otp-auth` (request re-delivery of the OTP). The slice ships **two consumer surfaces side-by-side** (per the 2026-05-19 clarification): (1) an explicit pair `IMisaESignClient.SignInWithOtpAsync(otpCode, otpType, remember, ct)` + `IMisaESignClient.ResendOtpAsync(language?, ct)` that consumers reach by catching the `Requires2FA = true` `AuthenticationFailedException` already raised by slice 1's `SignPdfAsync`, and (2) an optional `IOtpProvider` port registered at `AddMisaConnectESign(...)` time that lets `SignPdfAsync` invoke a consumer-supplied callback to obtain `{ otpCode, otpType, remember }` (and optionally request a resend) so the 2FA challenge never surfaces to the caller. Both surfaces converge on the same Application-layer `ExchangeOtp` use case, populate the existing `ITokenCache` in the slice-1 `AccessToken` shape, and reuse slice 1's `ClientHeadersHandler`, `TransientFailureRetryHandler`, `ICorrelationIdAccessor`, `ISystemClock`, `SingleFlightRefresh`, and error-mapping plumbing. Error mapping for OTP rejection follows the clarified hybrid strategy — canonical `errorCode` table from the Postman collection first, then a `SynthesizeOtpCode` substring fallback over `errorCode + devMsg` — producing three typed exceptions (`InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`) plus a residual `OtpRejectedException` for the unenumerated tail. The slice extends the unit and integration suites: new unit tests cover the 122-detection branch, both `otpType` paths, resend default + override, the three rejection categories, the OTP-redaction log scrubber, and the two consumer-surface entry points; new integration tests drive the `EsignFake` server through every wire variant and add `[SandboxFact]` end-to-end tests that skip cleanly unless the sandbox account is 2FA-enrolled and `MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER` is configured. Public-surface additions are catalogued in [contracts/public-surface.md](./contracts/public-surface.md) so slice 2 is a clean minor-version bump on the still-preview `MisaConnect.ESign` package.

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (unchanged from slice 1 — `Directory.Build.props` pins `TargetFramework=net8.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`).
**Primary Dependencies**: No new packages. The slice reuses slice 1's `System.Net.Http` typed `HttpClient` + `IHttpClientFactory`, the existing `Microsoft.Extensions.{Configuration,DependencyInjection,Http,Options,Logging.Abstractions}` set, and `System.Text.Json` for the two new request/response shapes via `ESignJsonOptions.Wire`.
**Storage**: None. The clarified outcome of `remember: true` is a pure pass-through to MISA — the SDK does **not** persist a device-trust identifier client-side (FR-035 / Q3 clarification). The cached tokens continue to live in `ITokenCache` (default `InMemoryTokenCache`) under the same `ITokenCacheKeySelector` key as slice 1; no new persistence port and no new domain entity.
**Testing**: xUnit. Two new files in `tests/MisaConnect.ESign.UnitTests/Authentication/TwoFactor*` and `tests/MisaConnect.ESign.UnitTests/Errors/OtpErrorMapperTests.cs`; the existing in-repo `EsignFake/FakeMisaESignServer.cs` gets two new endpoint handlers (`/api/auth/api/v1/auth/two-factor-auth`, `/webdev/api/auth/api/v1/auth/resend-otp-auth`) and a `[SandboxFact]`-gated integration class. Sandbox env vars: `MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER` (path to a console binary or static OTP file) per the sandbox-setup convention. Unit-suite budget per the constitution: `< 30s` total — slice 2 inherits this.
**Target Platform**: NuGet class library consumed by .NET 8 hosts (Web API, console, worker, MAUI). No platform-specific code paths.
**Project Type**: Layered class-library product family slice 2. No new csproj. The four production projects (`MisaConnect.ESign.{Domain,Application,Infrastructure,Client}`) and two test projects already exist; slice 2 adds files to them.
**Performance Goals**: No throughput target. Per-call latency for the new path is dominated by MISA's OTP-exchange round-trip (no polling involved). Concurrent in-flight OTP exchanges for the same cache key are bounded to one by reusing slice 1's `SingleFlightRefresh` keyed on the cache key (FR-042 extension).
**Constraints**:
- Constitution-bound: `Domain` zero external deps, `Application` Domain + `Microsoft.Extensions.Logging.Abstractions` only, Infrastructure `internal` except DI/options/port-interfaces, Client public. Slice 2 introduces no new layer-crossing.
- Logs MUST NOT contain the OTP `code`, the consumer `password`, or any future remember-device identifier (FR-044). The `ESignLogScrubber` from slice 1 gains an OTP-redaction rule.
- Wire DTOs match the MISA doc verbatim (Constitution Principle IV). New DTOs: `TwoFactorAuthRequestDto`, `TwoFactorAuthResponseDto` (same envelope as login), `ResendOtpRequestDto`, `ResendOtpResponseDto`.
- The 2FA endpoint inherits slice 1's `ClientHeadersHandler` for `x-clientId` / `x-clientKey` injection (FR-033). The MISA doc shows `clientId` / `clientKey` without the `x-` prefix on the 2FA row but `x-clientId` / `x-clientKey` everywhere else — slice 1 already chose `x-` everywhere; slice 2 inherits that decision and flags the doc-vs-implementation discrepancy in the wire-envelopes contract.
- The 2FA and resend endpoints have no `AuthorizationRM` header (no cached access token exists at this point in the flow), so `RemoteSigningAuthHandler` MUST skip them (slice 1 already skips `/login-api` and `/refreshtoken` via `IsAuthEndpoint`; slice 2 extends the check to cover the two new paths). The transport-retry handler (`TransientFailureRetryHandler`) DOES apply (FR-043).
- Correlation IDs are mandatory and structured on every new log entry and every surfaced typed error (FR-045).
**Scale/Scope**: Two new MISA endpoints, two new public methods on `IMisaESignClient`, one new public port `IOtpProvider`, three new typed exception subclasses (`InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`) + one residual `OtpRejectedException`, two new use cases (`ExchangeOtp`, `ResendOtp`), two new wire DTO pairs, and OTP-redaction rules on the log scrubber. Expected new code: ~900–1200 LoC including tests and fake-server endpoints. No new csproj.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Gates derived from [.specify/memory/constitution.md](../../.specify/memory/constitution.md):

| # | Principle | Status | How this slice satisfies it |
|---|-----------|--------|-----------------------------|
| I | Layered architecture is non-negotiable | PASS | New types land at the existing layers without crossing them. `Domain.Errors.{InvalidOtpException, ExpiredOtpException, ExhaustedOtpAttemptsException, OtpRejectedException}` are zero-dep Domain types. `Application.Abstractions.{IOtpProvider, OtpSubmission, OtpResendResult}` are pure interfaces / immutable records in Application — no new external dep. `Application.UseCases.{ExchangeOtp, ResendOtp}` depend only on `IMisaESignWireClient`, `ITokenCache`, `ITokenCacheKeySelector`, `ISystemClock`, `ICorrelationIdAccessor`, `SingleFlightRefresh` (already Application-visible via the abstraction `Application.Abstractions.ISingleFlightCoordinator` extracted in slice 2 — see Complexity Tracking below). `Application.Errors.OtpErrorMapper` (new) is a sibling of the existing `ESignErrorMapper`. Infrastructure additions (`MisaESignWireClient.TwoFactorAuthAsync`, `MisaESignWireClient.ResendOtpAsync`, the four wire DTOs, the new `ESignLogScrubber` OTP rule) remain `internal`. Client additions (`SignInWithOtpAsync`/`ResendOtpAsync` on `IMisaESignClient`, the `IOtpProvider` DI registration hook on `ServiceCollectionExtensions`) are the only public-surface deltas. The layer-audit unit test that already exists for the eInvoice family and was extended to ESign in slice 1 is extended again to cover slice 2's namespaces. |
| II | Public surface is small and stable | PASS | New public types: `IMisaESignClient.SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken)`, `IMisaESignClient.ResendOtpAsync(string? language, CancellationToken)`, `Application.Abstractions.IOtpProvider`, `Application.Abstractions.OtpSubmission` (record), `Application.Abstractions.OtpResendResult` (record), `Domain.Authentication.OtpDeliveryChannel` (enum: `SmsOrEmail = 0`, `Authenticator = 1`), four new exception classes in `Domain.Errors`. Existing public types unchanged in shape; `AuthenticationFailedException.Requires2FA` continues to be the signal carrier (slice 1 already exposes it). `MisaESignOptions` gains a nested `Otp` class with `DefaultResendLanguage = "en-US"`. All additions are non-breaking → slice 2 ships as a minor pre-release bump (`2.0.0-preview.2`). New `CHANGELOG.md` entry under `[Unreleased]` enumerates each addition. |
| III | Port-and-adapter for extensibility | PASS | The clarified "explicit primary + optional transparent" stance translates cleanly to ports. `IOtpProvider` is a new optional Application-layer port — consumers register an implementation at `AddMisaConnectESign(...)` time and `SignPdfAsync` consults it when it catches a 2FA-required signal. If no provider is registered, the 2FA-required signal surfaces unchanged so the explicit `SignInWithOtpAsync` path remains the supported route (FR-032b). The provider has two methods (`ProvideAsync(...)` for the OTP, optional `RequestResendAsync(...)` for re-delivery during a transparent flow) so consumers can satisfy FR-036 without dropping back to the explicit surface. No new persistence port. No swap-required collaborator (per Principle III, only collaborators consumers may legitimately want to swap get ports). |
| IV | Wire format mirrors MISA's documentation verbatim | PASS | Wire DTOs added in `Infrastructure/ESign/Wire/TwoFactorAuthDtos.cs` and `Infrastructure/ESign/Wire/ResendOtpDtos.cs` mirror the MISA doc rows on lines 305–306 and 318–319 verbatim. `TwoFactorAuthRequestDto = { userName, code, otpType, remember }` (camelCase). `TwoFactorAuthResponseDto` is **identical** to `LoginResponseDto` per the doc note "Lưu lại các thông tin tương tự mục 4.3" — slice 2 alias-types it via `using TwoFactorAuthResponseDto = LoginResponseDto;` to avoid duplicating the envelope. `ResendOtpRequestDto = { userName, language }`. `ResendOtpResponseDto = { status, data: { user: { username } } }` per the doc row. Field casing matches MISA's wire shape. The header-name discrepancy noted in slice 1 ("MISA doc uses `clientId`/`clientKey` without the `x-` prefix on the 2FA row only") is documented and ignored — slice 1 chose `x-` everywhere and the live integration confirmed the prefix is accepted; slice 2 inherits that choice and records it in the wire-envelopes contract. |
| V | Slice-driven development | PASS | This is slice 2 of [docs/misa-esign-spec-plan.md](../../docs/misa-esign-spec-plan.md). All artifacts live under `specs/002-misa-esign-2fa-otp/`: spec.md (present, clarified 2026-05-19), this plan.md, research.md (Phase 0 below), data-model.md, contracts/, quickstart.md, and a forthcoming tasks.md generated by `/speckit-tasks`. Slices 3 (multi-format) and 4 (webhook) remain explicitly out of scope. |
| VI | Tests are the spec | PASS | New unit tests under `tests/MisaConnect.ESign.UnitTests/Authentication/` (`TwoFactorAuthExchangeTests.cs`, `ResendOtpTests.cs`, `OtpProviderTransparentFlowTests.cs`, `TwoFactorAuthSingleFlightTests.cs`) and `tests/MisaConnect.ESign.UnitTests/Errors/OtpErrorMapperTests.cs` cover FR-030 through FR-045, executable in `< 30s` total without network. New integration tests under `tests/MisaConnect.ESign.IntegrationTests/EndToEnd/` (`TwoFactorAuthHappyPathFakeServerTests.cs`, `OtpRejectionFakeServerTests.cs`, `ResendOtpFakeServerTests.cs`, `OtpProviderTransparentFlowFakeServerTests.cs`) drive the existing `EsignFake/FakeMisaESignServer.cs` augmented with the two new MISA endpoints. New `[SandboxFact]` test `tests/MisaConnect.ESign.IntegrationTests/Sandbox/TwoFactorAuthSandboxTests.cs` exercises the live path when `MISACONNECT_ESIGN_SANDBOX_*` plus the new `MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER` env var are configured; it skips cleanly otherwise (SC-014). No MISA-HTTP-boundary mocks in integration tests — the in-repo fake is the only deterministic harness. |
| VII | Semver discipline | PASS | The `MisaConnect.ESign` package is still on the `2.0.0-preview.N` train (slice 1 introduced it). Slice 2 adds members (new methods on `IMisaESignClient`, new types in `Application.Abstractions` + `Domain.Errors`) without removing or breaking any. By the spirit of semver this is a minor bump; on the preview line it ships as `2.0.0-preview.2`. `CHANGELOG.md` entry under `[Unreleased]` lists each addition. The stable `2.0.0` cut continues to gate on slices 1+2 both being implemented and merged. |
| VIII | Logs never leak secrets or PII | PASS | The slice-1 `ESignLogScrubber` gains explicit OTP-redaction rules: the `code` field in `TwoFactorAuthRequestDto` is dropped before serialization-for-logging; any string field whose JSON path resolves to `$..code` on a request to `/two-factor-auth` is replaced with `"<redacted-otp>"`; any value of `remember` is logged as a boolean (the value itself is non-secret); the `accessToken`/`remoteSigningAccessToken`/`refreshToken` redactions inherited from slice 1 continue to apply to the 2FA success envelope (it is identical to the login envelope). The `password` field carried in the 2FA-required signal is **not** propagated past `EnsureAccessToken` — the consumer only sees the `userName` per FR-031. The captured-log unit test extends to OTP exchanges: zero occurrences of the OTP `code` in any captured log line per SC-012. The opt-in `MisaESignOptions.Errors.IncludeRawErrorMessage` flag continues to be the only way to surface MISA's raw `devMsg`/`userMsg` on a typed exception; slice 2 inherits that behavior — when the flag is OFF the typed exception carries only `errorCode` + correlation ID; when ON the synthesized exception message includes MISA's `userMsg`/`devMsg`. |

**Result**: All eight principles PASS. No violations to justify. The Complexity Tracking section below remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/002-misa-esign-2fa-otp/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── README.md
│   ├── public-surface.md     # Adds SignInWithOtpAsync, ResendOtpAsync, IOtpProvider, OTP enum + exceptions
│   ├── wire-envelopes.md     # Adds E8 (/two-factor-auth) and E9 (/resend-otp-auth) per MISA doc
│   └── error-mapping.md      # Adds A.8 (/two-factor-auth) and A.9 (/resend-otp-auth) tables
└── tasks.md             # Phase 2 output (/speckit-tasks command — NOT created by /speckit-plan)
```

### Source Code (repository root)

Slice 2 adds files to the four production projects and two test projects established by slice 1. No new csproj. Layered dependency rules from Constitution Principle I apply (Domain → Application → Infrastructure → Client).

```text
src/
├── MisaConnect.ESign.Domain/                                  # NEW files only
│   ├── Authentication/OtpDeliveryChannel.cs                   # enum { SmsOrEmail = 0, Authenticator = 1 }
│   └── Errors/                                                # NEW exceptions (zero-dep)
│       ├── InvalidOtpException.cs                             # category=Authentication, rawCode=InvalidOtp
│       ├── ExpiredOtpException.cs                             # category=Authentication, rawCode=ExpiredOtp
│       ├── ExhaustedOtpAttemptsException.cs                   # category=Authentication, rawCode=ExhaustedOtpAttempts
│       └── OtpRejectedException.cs                            # category=Authentication, residual rawCode preserved
├── MisaConnect.ESign.Application/                             # NEW files + small edits
│   ├── Abstractions/
│   │   ├── IOtpProvider.cs                                    # NEW port — ProvideAsync(challenge, ct), RequestResendAsync(challenge, ct)
│   │   ├── OtpChallenge.cs                                    # NEW record { string UserName, string CorrelationId }
│   │   ├── OtpSubmission.cs                                   # NEW record { string Code, OtpDeliveryChannel OtpType, bool Remember }
│   │   └── OtpResendResult.cs                                 # NEW record { bool Success, string? RawCode, string? UserMsg, string? DevMsg, string CorrelationId }
│   ├── UseCases/
│   │   ├── ExchangeOtp.cs                                     # NEW — calls IMisaESignWireClient.TwoFactorAuthAsync, writes AccessToken to cache (single-flight per cache key per FR-042)
│   │   ├── ResendOtp.cs                                       # NEW — calls IMisaESignWireClient.ResendOtpAsync, returns OtpResendResult
│   │   └── SignPdf.cs                                         # EDITED — catches AuthenticationFailedException.Requires2FA, invokes IOtpProvider if registered, calls ExchangeOtp + (optionally) ResendOtp, retries the operation; otherwise rethrows so the explicit surface works
│   └── Errors/
│       └── OtpErrorMapper.cs                                  # NEW — hybrid mapper (canonical errorCode table first, then SynthesizeOtpCode substring fallback)
├── MisaConnect.ESign.Infrastructure/                          # NEW files + small edits
│   ├── ESign/
│   │   ├── MisaESignWireClient.cs                             # EDITED — adds TwoFactorAuthAsync and ResendOtpAsync
│   │   ├── ESignHttpRoutes.cs                                 # EDITED — adds AuthTwoFactor + AuthResendOtp route constants
│   │   ├── Wire/
│   │   │   ├── TwoFactorAuthDtos.cs                           # NEW — TwoFactorAuthRequestDto, TwoFactorAuthResponseDto (alias to LoginResponseDto)
│   │   │   └── ResendOtpDtos.cs                               # NEW — ResendOtpRequestDto, ResendOtpResponseDto
│   │   └── Mapping/AuthSessionMapper.cs                       # EDITED — gains FromTwoFactorAuthResponse(...) that reuses FromLoginResponse internally
│   ├── Http/
│   │   └── RemoteSigningAuthHandler.cs                        # EDITED — IsAuthEndpoint() also matches /two-factor-auth and /resend-otp-auth so AuthorizationRM is never injected on those calls
│   ├── Logging/
│   │   └── ESignLogScrubber.cs                                # EDITED — drop $.code on requests to /two-factor-auth; alias the existing token/refresh-token redactions to apply to the 2FA success envelope (identical shape)
│   ├── Configuration/MisaESignOptions.cs                      # EDITED — adds Otp { string DefaultResendLanguage = "en-US"; }
│   └── DependencyInjection/ServiceCollectionExtensions.cs     # EDITED — TryAddScoped<ExchangeOtp>, TryAddScoped<ResendOtp>; consumer-registered IOtpProvider wins via TryAdd
└── MisaConnect.ESign.Client/                                  # NEW files + small edits
    ├── IMisaESignClient.cs                                    # EDITED — adds SignInWithOtpAsync(...) and ResendOtpAsync(...) (xmldoc captures both routes per FR-032a/FR-032b)
    ├── MisaESignClient.cs                                     # EDITED — wires the new methods through ExchangeOtp / ResendOtp
    └── Dtos/
        └── OtpResendResultDto.cs                              # NEW — consumer-facing shape mirroring OtpResendResult

tests/
├── MisaConnect.ESign.UnitTests/                               # NEW files + small edits
│   ├── Authentication/
│   │   ├── TwoFactorAuthExchangeTests.cs                      # NEW — happy path, otpType=0 and =1, remember=true|false, cache write
│   │   ├── ResendOtpTests.cs                                  # NEW — default "en-US", override, typed failure result
│   │   ├── OtpProviderTransparentFlowTests.cs                 # NEW — IOtpProvider end-to-end (sign attempt → 122 → provider → exchange → resume sign)
│   │   ├── TwoFactorAuthSingleFlightTests.cs                  # NEW — N concurrent 122-responses → exactly 1 outbound /two-factor-auth per cache key (FR-042)
│   │   └── EnsureAccessTokenTests.cs                          # EDITED — already covers 122 surfacing; add an assertion that no password is propagated past EnsureAccessToken
│   ├── Errors/
│   │   ├── OtpErrorMapperTests.cs                             # NEW — one row per error-mapping contract entry (canonical + synthesized)
│   │   └── ESignErrorMapperTests.cs                           # EDITED — assert /two-factor-auth response with errorCode=122 surfaces AuthenticationFailedException(Requires2FA=true) "do not loop" path (edge case 7 in spec)
│   ├── Logging/
│   │   └── ESignLogScrubberTests.cs                           # EDITED — adds "OTP code is never logged" cases (SC-012)
│   ├── Configuration/
│   │   └── MisaESignOptionsValidatorTests.cs                  # EDITED — confirms default Otp.DefaultResendLanguage = "en-US"
│   ├── Layering/
│   │   └── LayerAuditTests.cs                                 # EDITED — extends the audit to cover slice-2 namespaces
│   └── Validation/
│       └── OtpSubmissionValidatorTests.cs                     # NEW — code non-empty, otpType ∈ {0,1}, remember required (no nullable)
└── MisaConnect.ESign.IntegrationTests/                        # NEW files + small edits
    ├── EsignFake/
    │   └── FakeMisaESignServer.cs                             # EDITED — adds /two-factor-auth and /resend-otp-auth handlers with state per cache key (track OTP submissions, returnable error envelopes per test)
    ├── EndToEnd/
    │   ├── TwoFactorAuthHappyPathFakeServerTests.cs           # NEW — fake login returns 122 → fake exchange returns the token bundle → SignPdf completes
    │   ├── OtpRejectionFakeServerTests.cs                     # NEW — three rejection categories, one assertion per typed exception
    │   ├── ResendOtpFakeServerTests.cs                        # NEW — default + override; failure surfaces typed OtpResendResultDto
    │   └── OtpProviderTransparentFlowFakeServerTests.cs       # NEW — DI-registered fake IOtpProvider → SignPdf completes without the consumer touching IMisaESignClient.SignInWithOtpAsync
    └── Sandbox/
        └── TwoFactorAuthSandboxTests.cs                       # NEW — [SandboxFact] gated on MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER; skips when sandbox account is not 2FA-enrolled (SC-014)
```

**Structure Decision**: Slice 2 is a strict **extension** of the slice-1 layout — no new csproj, no namespace reshuffles, no copying of HTTP plumbing. The two new MISA endpoints become two new methods on the existing `IMisaESignWireClient` interface. The two new consumer surfaces (`SignInWithOtpAsync`/`ResendOtpAsync` on `IMisaESignClient`, and the optional `IOtpProvider` port) are both wired through the same two Application use cases (`ExchangeOtp`, `ResendOtp`), preventing drift between the explicit and transparent routes. The `SignPdf` orchestrator is edited (not rewritten) to consult `IOtpProvider` only when it catches a `Requires2FA = true` exception, so consumers who don't register a provider see the slice-1 behavior unchanged. The `ITokenCache` shape, `ITokenCacheKeySelector` default, `ClientHeadersHandler`, `TransientFailureRetryHandler`, `SingleFlightRefresh`, and `ESignErrorMapper` are reused verbatim — the `OtpErrorMapper` is a sibling, not a replacement.

## Complexity Tracking

> No violations recorded — all eight constitution gates pass without justified exceptions. Section intentionally left empty.

# Implementation Plan: MISA eSign — PDF Signing Flow (foundational)

**Branch**: `001-misa-esign-pdf-sign-flow` | **Date**: 2026-05-18 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-misa-esign-pdf-sign-flow/spec.md`

## Summary

Slice 1 of the new `MisaConnect.ESign` product family. It delivers a single SDK facade call that signs a PDF end-to-end via MISA eSign RemoteSigning: login → cache tokens → list certificates and pick the first ACTIVE → hash the PDF (server-side via MISA's `documents/hash`) → submit the digest to `Signing/hash` → poll `Signing/status/{transactionId}` until `SUCCESS`/`FAILED`/`CANCELLED` → attach the returned signature via `documents/attachment` → return signed PDF bytes. Wires up `x-clientId` / `x-clientKey` / `AuthorizationRM` header injection, sandbox-vs-production base-URL validation, `services.AddMisaConnectESign(IConfiguration)`, refresh-on-401-then-retry-once with single-flight per cache key, and a transport-level bounded retry policy for transient 5xx/429/timeout failures. Ships with a no-network unit suite and a `[SandboxFact]` integration suite that exercises the live happy path plus an in-repo `EsignFake` server for deterministic offline runs. Mirrors the layering, port-and-adapter shape, wire-format-fidelity discipline, and log-scrubbing rules established by `MisaConnect.EInvoice`.

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (matches `Directory.Build.props`'s `TargetFramework=net8.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`)
**Primary Dependencies**: `System.Net.Http` typed `HttpClient` via `IHttpClientFactory`; `Microsoft.Extensions.{Configuration,DependencyInjection,Http,Options,Logging.Abstractions}`; `System.Text.Json` for wire serialization. No Polly — retries are hand-rolled (matches the existing `ThrottleRetryHandler` precedent).
**Storage**: None persistent. In-process `ITokenCache` (default `InMemoryTokenCache`, swappable) keyed by `ITokenCacheKeySelector` (default = `userName + clientId + base-URL host`). PDF bytes flow through memory; nothing is written to disk.
**Testing**: xUnit + the existing `MisaConnect.EInvoice.TestSupport` patterns for `[SandboxFact]` and HTTP fakes. New projects: `tests/MisaConnect.ESign.UnitTests/` (no network, <30 s) and `tests/MisaConnect.ESign.IntegrationTests/` (sandbox + in-repo `EsignFake/` deterministic harness).
**Target Platform**: NuGet class library consumed by .NET 8 / .NET Standard 2.1+-compatible host applications (Web API, console, worker, MAUI). No platform-specific code; PDF bytes are opaque `byte[]`.
**Project Type**: Layered class-library product family, parallel to `MisaConnect.EInvoice` (Domain → Application → Infrastructure → Client). v2.0 slot reserved in [docs/architecture.md](../../docs/architecture.md#future-products-v20).
**Performance Goals**: Slice-1 has no throughput target. Per-call latency dominated by MISA's polling window — defaults: poll interval ~2 s, total polling timeout ~60 s, both configurable on `MisaESignOptions`. Bounded transport retry adds ≤ ~4 s in worst case (3 attempts × ~2 s cap). Singleton `IMisaESignClient` must serve concurrent sign calls without serialization across cache keys.
**Constraints**:
- Constitution-bound: Domain has zero external deps; Application depends only on Domain + `Microsoft.Extensions.Logging.Abstractions`; Infrastructure types are `internal` except DI/options/port-interfaces; Client is the public surface.
- No tokens / refresh tokens / `AuthorizationRM` header values / certificate private bytes / raw document bytes / end-user PII in logs at any level by default.
- Wire DTOs match MISA's published shapes verbatim (field casing, envelope shape, `ResponseError { error, errorCode, devMsg, userMsg }`).
- All MISA calls carry a structured correlation ID; it propagates onto every log entry and every surfaced error.
- `IMisaESignClient` is thread-safe; refresh is single-flight per cache key.
**Scale/Scope**: One slice, seven MISA endpoints, one consumer-facing facade method (`SignPdfAsync`). ~4 new csproj projects (Domain/Application/Infrastructure/Client) + 2 test csproj. Expected new code: ~2500–3500 LoC including tests and the in-repo fake server.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Gates derived from [.specify/memory/constitution.md](../../.specify/memory/constitution.md):

| # | Principle | Status | How this slice satisfies it |
|---|-----------|--------|-----------------------------|
| I | Layered architecture is non-negotiable | PASS | New csprojs: `MisaConnect.ESign.{Domain,Application,Infrastructure,Client}`. Domain has zero `<PackageReference>`/`<ProjectReference>` outside Domain itself. Application references only Domain + `Microsoft.Extensions.Logging.Abstractions`. Infrastructure references Application + Domain + `Microsoft.Extensions.*`. Client references all three. Enforced by `<ProjectReference>` graph + the namespace-audit test that already exists for the eInvoice family (extended to cover the ESign family). |
| II | Public surface is small and stable | PASS | Public types limited to `MisaConnect.ESign.Client.IMisaESignClient`, `MisaConnect.ESign.Client.DependencyInjection.ServiceCollectionExtensions.AddMisaConnectESign`, `MisaConnect.ESign.Infrastructure.Configuration.MisaESignOptions` (and its nested options classes + `SectionName` constant), the swappable port interfaces (`ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`, `ISystemClock`, `ICorrelationIdAccessor`), and the Domain types they expose (`Certificate`, `SignTransaction`, `SignStatus`, `ResponseError`, the typed exception hierarchy). Everything else `internal`. New `CHANGELOG.md` entry under `[Unreleased]` introduces the slice; first public release of `MisaConnect.ESign` is the v2.0 cut. |
| III | Port-and-adapter for extensibility | PASS | Application-layer ports: `ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`, `ISystemClock`, `ICorrelationIdAccessor`, `IMisaESignWireClient` (the typed-HTTP boundary). Infrastructure ships defaults (`InMemoryTokenCache`, `DefaultTokenCacheKeySelector`, `FirstActiveCertificateSelector`, `SystemClock`, and a typed `MisaESignWireClient` HTTP adapter). Consumers register their own before `AddMisaConnectESign` to swap. Same shape as the eInvoice ports. |
| IV | Wire format mirrors MISA's documentation verbatim | PASS | Wire DTOs in `Infrastructure/Wire/` (`LoginRequestDto`, `LoginResponseDto`, `RefreshTokenRequestDto`/`ResponseDto`, `CertificateDto`, `HashRequestDto`/`PdfHashOutputDto`, `SignHashRequestDto`/`SignHashResponseDto`, `SignStatusResponseDto`, `AttachmentRequestDto`/`AttachmentedResponseDto`, `ResponseErrorDto`) match the field names, casing, and envelope shapes documented in [docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md](../../docs/misa-api-reference/T%C3%A0i%20li%E1%BB%87u%20t%C3%ADch%20h%E1%BB%A3p%20API%20eSign%20RemoteSigning%20-%20V2.md). MISA's `certiticateChain` typo is preserved verbatim per the doc. Mapping between wire DTOs and Domain types lives in `Infrastructure/Mapping/`. |
| V | Slice-driven development | PASS | This is slice 1 of [docs/misa-esign-spec-plan.md](../../docs/misa-esign-spec-plan.md), under `specs/001-misa-esign-pdf-sign-flow/`, with spec.md (present), this plan.md, contracts/, data-model.md, quickstart.md, and a forthcoming tasks.md. Slices 2–4 (2FA, multi-format, webhook) are explicitly out of scope. |
| VI | Tests are the spec | PASS | `tests/MisaConnect.ESign.UnitTests/` is mandatory and stays under 30 s with no network — covers FR-001 through FR-019 with mock HTTP fixtures. `tests/MisaConnect.ESign.IntegrationTests/` provides `[SandboxFact]` end-to-end against the MISA eSign sandbox (`MISACONNECT_ESIGN_SANDBOX_*` env vars per [docs/sandbox-setup.md](../../docs/sandbox-setup.md)), skipping cleanly when the host is unreachable. The integration project also hosts an in-repo `EsignFake/` server, modeled on `tests/MisaConnect.EInvoice.IntegrationTests/MisaFake/`, for deterministic offline scenarios (refresh-stampede counting, polling state machine, terminal-state mapping). No mocks at the MISA HTTP boundary in integration tests. |
| VII | Semver discipline | PASS | First publication of `MisaConnect.ESign` ships as `2.0.0-preview.N` (pre-release identifier permitted by the constitution). Pre-1.0 of `MisaConnect.ESign` is a v2.0 release of the `MisaConnect` family — `MisaConnect.EInvoice` v1.x is unaffected. Slice-1 introduces only new types in a new package, so it is purely additive. |
| VIII | Logs never leak secrets or PII | PASS | The `ESignLogScrubber` (Infrastructure) mirrors the eInvoice `MeInvoiceLogScrubber` pattern: structured fields only, no token/refresh-token/`AuthorizationRM`/cert-private-byte/raw-PDF-byte/end-user-name/email/phone values. Raw MISA error envelopes only surface when `MisaESignOptions.Errors.IncludeRawErrorMessage = true` (opt-in flag matching FR-019 / the eInvoice precedent). Correlation IDs are mandatory and structured on every log entry. SC-006 enforces this with a captured-log scan over the full unit+integration suites. |

**Result**: All eight principles PASS. No violations to justify. Complexity Tracking section below remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-misa-esign-pdf-sign-flow/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── README.md
│   ├── public-surface.md     # IMisaESignClient + AddMisaConnectESign + MisaESignOptions
│   ├── wire-envelopes.md     # MISA request/response shapes per endpoint
│   └── error-mapping.md      # ResponseError errorCode → typed SDK error
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

The slice adds four production csprojs and two test csprojs, parallel to the existing `MisaConnect.EInvoice` family (see [docs/architecture.md](../../docs/architecture.md)). Layered dependency rules from Constitution Principle I apply.

```text
src/
├── MisaConnect.EInvoice.Domain/                # (existing v1.0 — untouched by this slice)
├── MisaConnect.EInvoice.Application/           # (existing)
├── MisaConnect.EInvoice.Infrastructure/        # (existing)
├── MisaConnect.EInvoice.Client/                # (existing)
├── MisaConnect.ESign.Domain/                   # NEW — zero external deps
│   ├── Authentication/AuthSession.cs           # accessToken + remoteSigningAccessToken + refreshToken + expiresAtUtc
│   ├── Certificates/Certificate.cs             # keyAlias, userId, keyStatus, certificate, certificateChain
│   ├── Certificates/CertificateChain.cs
│   ├── Certificates/KeyStatus.cs               # ACTIVE / INACTIVE
│   ├── Documents/PdfDocument.cs                # opaque byte[] wrapper (parallels EInvoice.Domain.Pdf.PdfDocument)
│   ├── Signing/HashAlgorithm.cs                # SHA256 (only value supported in slice 1)
│   ├── Signing/SignatureInfo.cs                # display/position metadata for the signature
│   ├── Signing/SignatureDescription.cs
│   ├── Signing/SignTransaction.cs              # transactionId
│   ├── Signing/SignStatus.cs                   # PENDING / SUCCESS / FAILED / CANCELLED
│   ├── Signing/SignedDocument.cs               # output byte[]
│   └── Errors/                                 # ESignErrorCategory, ESignErrorCode, ESignException + subclasses
│       ├── ESignErrorCategory.cs
│       ├── ESignException.cs
│       ├── AuthenticationFailedException.cs
│       ├── NoActiveCertificateException.cs
│       ├── SignRejectedException.cs
│       ├── SignTerminalStateException.cs
│       ├── SignTimeoutException.cs
│       └── ESignTransportException.cs
├── MisaConnect.ESign.Application/              # NEW — Domain + Microsoft.Extensions.Logging.Abstractions
│   ├── Abstractions/
│   │   ├── IMisaESignWireClient.cs             # typed HTTP boundary port
│   │   ├── ITokenCache.cs                      # AccessToken cache
│   │   ├── ITokenCacheKeySelector.cs           # FR-026 — composes the cache key
│   │   ├── ICertificateSelector.cs             # FR-007 — picks the ACTIVE cert
│   │   ├── ISystemClock.cs
│   │   ├── ICorrelationIdAccessor.cs
│   │   └── AccessToken.cs                      # (string Value, string RemoteSigningValue, string RefreshToken, DateTimeOffset ExpiresAtUtc)
│   ├── UseCases/
│   │   ├── EnsureAccessToken.cs                # login-or-refresh with cache
│   │   ├── RefreshAccessToken.cs               # single-flight per cache key (FR-028)
│   │   ├── ListActiveCertificates.cs           # FR-006, FR-008
│   │   ├── HashPdfDocument.cs                  # FR-009
│   │   ├── SubmitSignHash.cs                   # FR-010
│   │   ├── PollSignStatus.cs                   # FR-011 + state machine
│   │   ├── AttachSignature.cs                  # FR-012
│   │   └── SignPdf.cs                          # orchestrator — single facade-facing use case
│   ├── Errors/ESignErrorMapper.cs              # ResponseError → typed exception (FR-018)
│   └── Validation/SignPdfRequestValidator.cs
├── MisaConnect.ESign.Infrastructure/           # NEW — internal except ServiceCollectionExtensions, MisaESignOptions, port adapters
│   ├── Configuration/
│   │   ├── MisaESignOptions.cs                 # SectionName = "Misa:ESign"
│   │   ├── MisaESignOptionsValidator.cs        # asserts env ↔ base-URL host consistency (FR-015)
│   │   └── ESignEnvironment.cs                 # Sandbox / Production
│   ├── DependencyInjection/
│   │   └── ServiceCollectionExtensions.cs      # AddMisaConnectESign(IConfiguration|Action<MisaESignOptions>)
│   ├── Caching/InMemoryTokenCache.cs
│   ├── Caching/DefaultTokenCacheKeySelector.cs
│   ├── Certificates/FirstActiveCertificateSelector.cs
│   ├── Time/SystemClock.cs
│   ├── Logging/ESignLogScrubber.cs
│   ├── Logging/ESignCallLogger.cs              # decorator over IMisaESignWireClient (mirrors MeInvoiceCallLogger)
│   ├── Http/
│   │   ├── ClientHeadersHandler.cs             # injects x-clientId + x-clientKey on every call
│   │   ├── RemoteSigningAuthHandler.cs         # injects AuthorizationRM + 401-refresh-then-retry-once + single-flight (FR-004, FR-028)
│   │   ├── TransientFailureRetryHandler.cs     # FR-023/024/025 bounded exponential backoff + Retry-After (separate budget from auth refresh)
│   │   └── SingleFlightRefresh.cs              # in-process synchronization primitive keyed by cache key
│   └── ESign/
│       ├── MisaESignWireClient.cs              # sole concrete implementation of IMisaESignWireClient
│       ├── ESignHttpRoutes.cs                  # the seven route constants
│       ├── ESignJsonOptions.cs                 # System.Text.Json options matching MISA's casing
│       ├── Wire/                               # DTOs match MISA doc verbatim (incl. `certiticateChain` typo)
│       │   ├── LoginDtos.cs
│       │   ├── RefreshTokenDtos.cs
│       │   ├── CertificateDtos.cs
│       │   ├── HashDtos.cs
│       │   ├── SignHashDtos.cs
│       │   ├── SignStatusDtos.cs
│       │   ├── AttachmentDtos.cs
│       │   └── ResponseErrorDto.cs
│       └── Mapping/                            # wire ↔ Domain mappers
│           ├── AuthSessionMapper.cs
│           ├── CertificateMapper.cs
│           ├── SignStatusMapper.cs
│           └── ResponseErrorMapper.cs
└── MisaConnect.ESign.Client/                   # NEW — consumer-facing NuGet surface
    ├── IMisaESignClient.cs                     # SignPdfAsync(byte[] pdf, SignPdfRequest req, ct) facade
    ├── MisaESignClient.cs                      # facade → orchestrating use case
    ├── DependencyInjection/ServiceCollectionExtensions.cs   # AddMisaConnectESign(IConfiguration)
    ├── Dtos/                                   # consumer-facing surface DTOs (kebab/camel case; not wire DTOs)
    │   ├── SignPdfRequestDto.cs                # input pdfBytes, signing position, reason, signer name, contact, etc.
    │   ├── SignPdfResultDto.cs                 # signed PDF bytes + transaction id + cert thumbprint
    │   └── CertificateDto.cs                   # subset surfaced to consumers
    └── Mapping/SignPdfRequestMapper.cs

tests/
├── MisaConnect.EInvoice.TestSupport/           # (existing — may add a shared SandboxFact-like helper if not already cross-product)
├── MisaConnect.EInvoice.UnitTests/             # (existing — untouched)
├── MisaConnect.EInvoice.IntegrationTests/      # (existing — untouched)
├── MisaConnect.ESign.UnitTests/                # NEW — under 30 s, no network
│   ├── Authentication/
│   │   ├── EnsureAccessTokenTests.cs           # cache hit, cache miss, proactive refresh (FR-001 → FR-005)
│   │   └── RefreshTokenSingleFlightTests.cs    # FR-028 — SC-007 with concurrent waiters
│   ├── Certificates/CertificateSelectorTests.cs # FR-006 → FR-008
│   ├── Hashing/HashPdfDocumentTests.cs         # FR-009 request shape
│   ├── Signing/SubmitSignHashTests.cs          # FR-010
│   ├── Signing/PollSignStatusTests.cs          # FR-011 state machine — PENDING→SUCCESS, →FAILED, →CANCELLED, →timeout
│   ├── Signing/AttachSignatureTests.cs         # FR-012 happy path
│   ├── Errors/ESignErrorMapperTests.cs         # FR-018 per-errorCode mapping (SC-005)
│   ├── Http/RemoteSigningAuthHandlerTests.cs   # 401→refresh→retry-once → success | refresh-fail → typed auth error (FR-004)
│   ├── Http/TransientFailureRetryHandlerTests.cs # FR-023/024/025 — bounded backoff + jitter + Retry-After + cancel
│   ├── Logging/ESignLogScrubberTests.cs        # FR-019 — captured-log scan (SC-006)
│   └── Configuration/MisaESignOptionsValidatorTests.cs # FR-015 — env↔base-URL host consistency
└── MisaConnect.ESign.IntegrationTests/         # NEW — [SandboxFact] live + EsignFake offline
    ├── EsignFake/
    │   ├── FakeMisaESignServer.cs              # in-repo deterministic HTTP host (mirrors FakeMisaServer.cs)
    │   ├── FakeAuthEndpoints.cs                # login + refreshtoken handlers
    │   ├── FakeCertificateEndpoints.cs
    │   ├── FakeDocumentEndpoints.cs            # hash + attachment
    │   ├── FakeSigningEndpoints.cs             # Signing/hash + Signing/status state machine
    │   └── FakeMisaESignServerTests.cs         # sanity checks on the fake itself
    ├── EndToEnd/
    │   ├── SignPdfHappyPathSandboxTests.cs     # [SandboxFact] — SC-001
    │   ├── SignPdfHappyPathFakeServerTests.cs  # deterministic E2E against EsignFake
    │   ├── TokenCacheReuseFakeServerTests.cs   # SC-004
    │   ├── RefreshStampedeFakeServerTests.cs   # SC-007 — 8 concurrent 401s → exactly 1 refreshtoken request
    │   └── TerminalStateFakeServerTests.cs     # FAILED / CANCELLED / poll-timeout
    └── Configuration/AddMisaConnectESignTests.cs  # DI registration + options binding from MISACONNECT_ESIGN_SANDBOX_*

samples/                                         # (existing)
├── MisaConnect.Samples.Api/                    # (existing — may receive an /esign/sign-pdf demo route in a later slice; out-of-scope here)
└── MisaConnect.Samples.Console/                # (existing)
```

**Structure Decision**: Layered four-csproj product family (`MisaConnect.ESign.{Domain,Application,Infrastructure,Client}`) parallel to `MisaConnect.EInvoice`, with two test csprojs alongside the existing eInvoice test pair. This honors Constitution Principles I (layer rules), II (small public surface — only Client + DI + options + ports are public), III (port-and-adapter shape consistent with eInvoice), and V (slice-driven; one slice per spec). No shared `MisaConnect.Common` is extracted yet — per [docs/architecture.md](../../docs/architecture.md#future-products-v20), commonization is deferred until duplication actually appears across products. The slice copies — does not generalize — the patterns proven by `MisaConnect.EInvoice` (HTTP handler pipeline, log scrubber, in-memory token cache, in-repo fake server). The `IMisaESignWireClient` boundary is the swap point for the in-repo `EsignFake/` deterministic harness, mirroring how `IMeInvoiceClient` works for the eInvoice suite.

## Complexity Tracking

> No violations recorded — all eight constitution gates pass without justified exceptions. Section intentionally left empty.

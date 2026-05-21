# Changelog

All notable changes to MisaConnect will be documented here. This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `MisaConnect.ESign` slice 2 — Two-factor authentication (OTP) flow. Shipping as `2.0.0-preview.2`.
  - `IMisaESignClient.SignInWithOtpAsync(otpCode, otpType, remember, ct)` — completes an in-progress 2FA challenge captured from a preceding `SignPdfAsync`.
  - `IMisaESignClient.ResendOtpAsync(language?, ct)` — requests OTP re-delivery; returns a typed `OtpResendResultDto` (does not throw on documented MISA rejections).
  - Optional `IOtpProvider` port (`ProvideAsync`, `RequestResendAsync`) — when registered, `SignPdfAsync` transparently consults it on the 2FA-required signal so the challenge never surfaces to the consumer.
  - `OtpDeliveryChannel` enum (`SmsOrEmail = 0`, `Authenticator = 1`) in `Domain.Authentication`.
  - Typed OTP exceptions in `Domain.Errors`: `InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`, `OtpRejectedException` — all sealed subclasses of `AuthenticationFailedException` (existing catches continue to pick them up).
  - `AuthenticationFailedException.Username` property (non-null; populated only on the `errorCode = 122` re-challenge branch). Constructor gains an optional 5th `username` parameter, default `""` — fully backwards compatible.
  - `OtpResendResultDto` (Client.Dtos) — 1:1 wrapper over `Application.Abstractions.OtpResendResult`.
  - `MisaESignOptions.Otp` block with `DefaultResendLanguage = "en-US"`.
  - Two new methods on `IMisaESignWireClient`: `TwoFactorAuthAsync(...)` and `ResendOtpAsync(...)`.
- New product family `MisaConnect.ESign` (slice 1 — PDF signing flow). Shipping initially as `2.0.0-preview.1`.
  - Public DI entry point `services.AddMisaConnectESign(IConfiguration)` (and `Action<MisaESignOptions>` overload) binding the `Misa:ESign` configuration section.
  - Public facade `IMisaESignClient.SignPdfAsync(SignPdfRequestDto, CancellationToken)` — end-to-end PDF signing via MISA eSign RemoteSigning (login → list certs → hash → sign → poll → attach).
  - Public port interfaces: `ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`, `ISystemClock`, `ICorrelationIdAccessor`.
  - Public options type `MisaESignOptions` (section name `Misa:ESign`) with nested `Polling`, `TransportRetry`, and `Errors` blocks.
  - Public typed exception hierarchy: `ESignException` (base), `AuthenticationFailedException` (with `Requires2FA`), `NoActiveCertificateException`, `SignRejectedException` (with `RequiresUserCertSetup`), `SignTerminalStateException` (with `TerminalStatus`/`TransactionId`), `SignTimeoutException` (with `TransactionId`/`ElapsedTime`), `ESignTransportException` (with `LastStatusCode`/`AttemptCount`).
  - HTTP handler pipeline: bounded exponential-backoff retry with full jitter (transient 5xx/429/timeouts), `AuthorizationRM` injection with 401-refresh-then-retry-once, single-flight refresh per cache key, `x-clientId`/`x-clientKey` headers on every call.
  - In-memory token cache and call-logging decorator with structured log shape — never logs tokens, refresh tokens, `AuthorizationRM`, cert private bytes, raw PDF bytes, or end-user PII by default.
  - In-repo `FakeMisaESignServer` (under `tests/MisaConnect.ESign.IntegrationTests/EsignFake/`) for deterministic offline E2E coverage.

## [1.1.0] - 2026-05-15

### Added
- Package icon (`icon.png`, 256x256) embedded in the NuGet listing.

### Changed
- Build SDK pinned to .NET 10 LTS via `global.json` and `setup-dotnet` workflows. Target framework remains `net8.0` for broad consumer reach (still under LTS through Nov 2026).

### Breaking
- `AmendmentDtoMapper` demoted from `public` to `internal`. Only intended for Client-internal use; external callers should use `IMisaEInvoiceClient.IssueReplacementAsync` / `IssueAdjustmentAsync`. Caught immediately after v1.0.0 — no known external consumers.

## [1.0.0] - 2026-05-14

### Added
- Initial public release of `MisaConnect.EInvoice` targeting .NET 8.
- Use cases: `EnsureAccessToken`, `ListActiveTemplates`, `PreviewInvoice`, `SaveDraftInvoices`, `GetDraftPdfByRefId`, `DeleteDraftInvoice`, `LookupByRefIds`, `LookupStandard`, `LookupCalculating`, `IssueReplacementInvoice`, `IssueAdjustmentInvoice`.
- Port-and-adapter interfaces: `IMeInvoiceClient`, `ITokenCache`, `IInvoiceValidator`, `IRefIdGenerator`, `ISystemClock`, `ICorrelationIdAccessor`, `ITemplateResolver`, `IDeleteOptionsAccessor`.
- DI entry point `AddMisaConnectEInvoice(IConfiguration)` binding the `Misa:EInvoice` configuration section.
- Throttle retry, bearer-token auth, in-memory token cache, call-logging decorator.
- Sample console (`samples/MisaConnect.Samples.Console`) and minimal-API host (`samples/MisaConnect.Samples.Api`).
- 315 unit tests; integration tests for MISA sandbox and a fake server.

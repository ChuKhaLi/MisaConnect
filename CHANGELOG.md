# Changelog

All notable changes to MisaConnect will be documented here. This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [2.0.0] - 2026-05-23

Public API is the union of slices 1–4 of the `MisaConnect.ESign` product family, previewed through `2.0.0-preview.1`..`preview.4`. This is the first stable release of `MisaConnect.ESign`.

### Added

#### Slice 1 — PDF signing flow (`MisaConnect.ESign`)

- Public DI entry point `services.AddMisaConnectESign(IConfiguration)` (and `Action<MisaESignOptions>` overload) binding the `Misa:ESign` configuration section.
- Public facade `IMisaESignClient.SignPdfAsync(SignPdfRequestDto, CancellationToken)` — end-to-end PDF signing via MISA eSign RemoteSigning (login → list certs → hash → sign → poll → attach).
- Public port interfaces: `ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`, `ISystemClock`, `ICorrelationIdAccessor`.
- Public options type `MisaESignOptions` (section name `Misa:ESign`) with nested `Polling`, `TransportRetry`, and `Errors` blocks.
- Public typed exception hierarchy: `ESignException` (base), `AuthenticationFailedException` (with `Requires2FA`), `NoActiveCertificateException`, `SignRejectedException` (with `RequiresUserCertSetup`), `SignTerminalStateException` (with `TerminalStatus`/`TransactionId`), `SignTimeoutException` (with `TransactionId`/`ElapsedTime`), `ESignTransportException` (with `LastStatusCode`/`AttemptCount`).
- HTTP handler pipeline: bounded exponential-backoff retry with full jitter (transient 5xx/429/timeouts), `AuthorizationRM` injection with 401-refresh-then-retry-once, single-flight refresh per cache key, `x-clientId`/`x-clientKey` headers on every call.
- In-memory token cache and call-logging decorator with structured log shape — never logs tokens, refresh tokens, `AuthorizationRM`, cert private bytes, raw PDF bytes, or end-user PII by default.
- In-repo `FakeMisaESignServer` (under `tests/MisaConnect.ESign.IntegrationTests/EsignFake/`) for deterministic offline E2E coverage.

#### Slice 2 — Two-factor authentication (OTP)

- `IMisaESignClient.SignInWithOtpAsync(otpCode, otpType, remember, ct)` — completes an in-progress 2FA challenge captured from a preceding `SignPdfAsync`.
- `IMisaESignClient.ResendOtpAsync(language?, ct)` — requests OTP re-delivery; returns a typed `OtpResendResultDto` (does not throw on documented MISA rejections).
- Optional `IOtpProvider` port (`ProvideAsync`, `RequestResendAsync`) — when registered, `SignPdfAsync` transparently consults it on the 2FA-required signal so the challenge never surfaces to the consumer.
- `OtpDeliveryChannel` enum (`SmsOrEmail = 0`, `Authenticator = 1`) in `Domain.Authentication`.
- Typed OTP exceptions in `Domain.Errors`: `InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`, `OtpRejectedException` — all sealed subclasses of `AuthenticationFailedException` (existing catches continue to pick them up).
- `AuthenticationFailedException.Username` property (non-null; populated only on the `errorCode = 122` re-challenge branch). Constructor gains an optional 5th `username` parameter, default `""` — fully backwards compatible.
- `OtpResendResultDto` (Client.Dtos) — 1:1 wrapper over `Application.Abstractions.OtpResendResult`.
- `MisaESignOptions.Otp` block with `DefaultResendLanguage = "en-US"`.
- Two new methods on `IMisaESignWireClient`: `TwoFactorAuthAsync(...)` and `ResendOtpAsync(...)`.

#### Slice 3 — Multi-format document signing (XML, Word, Excel)

- `IMisaESignClient.SignXmlAsync(SignXmlRequestDto, CancellationToken)` — end-to-end XML (XAdES) signing. Accepts either a `string Xml` (primary; matches MISA's "text content" semantics on `FileToSign` per §4.1.2) or `byte[] XmlUtf8Bytes` (secondary; UTF-8-decoded by the SDK). Construct via `SignXmlRequest.FromString(...)` / `FromUtf8Bytes(...)` factories.
- `IMisaESignClient.SignWordAsync(SignWordRequestDto, CancellationToken)` — end-to-end Word (OOXML `.docx`) signing using MISA's `wordDocs` per-format array on `/documents/hash` and `/documents/attachment` per §4.1.1 / §4.6 / §4.15.
- `IMisaESignClient.SignExcelAsync(SignExcelRequestDto, CancellationToken)` — end-to-end Excel (OOXML `.xlsx`) signing using MISA's `excelDocs` per-format array.
- `DocumentFormat` byte-backed closed enum (`Unknown = 0, Pdf = 1, Xml = 2, Word = 3, Excel = 4`) in `Domain.Documents`. Surfaced as the `Format` property on every typed exception and on every result DTO so consumers can branch on `(ExceptionType, Format)` without string-matching MISA's `devMsg`.
- `XmlSignatureContext` slim record (`SignatureName`, `HashAlgorithm`, `SignatureDescription`) in `Domain.Signing` — exposes only XAdES-meaningful fields. Visual-positioning fields are intentionally absent so the type system rejects consumer mistakes at compile time.
- Public client DTOs: `SignXmlRequestDto`, `SignWordRequestDto`, `SignExcelRequestDto`, `SignXmlResultDto`, `SignWordResultDto`, `SignExcelResultDto`, `XmlSignatureContextDto`, `SignatureDescriptionDto`.
- `SignPdfResultDto` gains a `Format = DocumentFormat.Pdf` property (additive; existing call sites unaffected).
- Every existing typed exception (`ESignException` base + every concrete subclass) gains an optional `format` constructor parameter at the end and a `Format` property. Slice-1 PDF call sites pass `DocumentFormat.Pdf`; slice-2 paths reached during a facade call inherit the called facade's format.
- Five new methods on `IMisaESignWireClient` (additive): `HashXmlAsync`, `HashWordAsync`, `HashExcelAsync`, `AttachSignatureToXmlAsync`, `AttachSignatureToWordExcelAsync`.
- Two new Application records: `XmlHashOutput`, `WordExcelHashOutput`. New `SignHashInput(string DocumentId, string Digest)` record — `SubmitSignHash.ExecuteAsync(...)` now takes this in place of the slice-1 `PdfHashOutput hash` parameter (each per-format hash output exposes a `ToSignHashInput()` projection). Wire shape on `/Signing/hash` stays byte-identical (FR-065).
- `ESignErrorMapper.Map(...)` gains a `DocumentFormat requestedFormat = DocumentFormat.Pdf` parameter and new per-format synthesized codes (`InvalidXmlInput`, `MissingMainDom`, `MissingSignatureId`, `UnsupportedDocumentVariant`).
- `ESignLogScrubber` redacts the new sensitive fields on `/documents/*` payloads: `mainDom`, `signatureId`, `document`.

#### Slice 4 — Webhook-mode signing

- `IMisaESignClient.BeginSignPdfAsync` / `BeginSignXmlAsync` / `BeginSignWordAsync` / `BeginSignExcelAsync` — initiate a sign without polling; the SDK runs login → cert → `/documents/hash` → `/Signing/hash`, registers a `SigningSession`, and returns a `BeginResultDto { TransactionId, Format }`. Throws `InvalidOperationException("Webhook-mode signing is disabled when Mode == Polling")` per FR-093.
- `IMisaESignClient.HandleWebhookAsync(WebhookEnvelopeDto, CancellationToken)` — entry point for inbound MISA webhook deliveries. Runs the four-step typed validation pipeline (FR-082), single-flight finalize via `/documents/attachment`, idempotent dedupe on the success path (FR-080), failure-not-cached invariant (FR-081), and short-circuit on terminal `FAILED`/`CANCELLED` envelopes (FR-083). Returns `WebhookHandleResultDto { Ack, Outcome }` for the consumer's HTTP layer to echo back to MISA.
- `IWebhookDeliveryHook` (`Client.Webhook` namespace) — consumer-implementable callback receiving the typed `WebhookOutcomeDto`. Default `NullClientWebhookDeliveryHook` logs at INFO; consumers register their own via `services.AddSingleton<IWebhookDeliveryHook, MyImpl>()` before `AddMisaConnectESign(...)`.
- `Application.Abstractions.ISigningSessionStore` — single new port for session bookkeeping. Default `InMemorySigningSessionStore` uses `ConcurrentDictionary` keyed by `(clientId, transactionId)` with lazy TTL eviction on read; it also implements `IFinalizeLockOwner` for the single-flight finalize lock.
- `Domain.Webhook.*` — new namespace with `WebhookEnvelope`, `WebhookSignature`, `WebhookStatus`, `WebhookAck`, `BeginResult`, `WebhookOutcome` + 3 variants (`SuccessWithSignedBytes`, `FailureWithError`, `TerminalWithoutFinalize`), `WebhookValidationCategory`.
- `Application.Sessions.*` — `SigningSession`, `PerFormatHashPayload` + 4 variants (Pdf / Xml / Word / Excel), `SigningSessionCachedSuccess`. Located in Application (not Domain) because they wrap Application-layer hash output types — staying compatible with Constitution Principle I (Domain has zero external deps).
- `Domain.Errors.WebhookValidationException` (abstract) + 5 sealed subclasses (`MalformedEnvelopeException`, `ClientIdMismatchException`, `UnknownTransactionException`, `IncompleteSuccessEnvelopeException`, `DocumentIdMismatchException`) — extends `ESignException` with `WebhookCategory` / `MatchedTransactionId` properties.
- `MisaESignOptions.Webhook` sub-section: `Mode` (`Polling`/`Webhook`/`Both`, default `Both`), `Session.Ttl` (default 24h), `Path` (default `/esign/webhook`), `Secret` (≥32 chars when set), `AllowedIps` (CIDR strings). Validated at startup by `MisaESignOptionsValidator`.
- `ESignErrorMapper.MapWebhookValidationToAck(WebhookValidationException)` — returns the namespaced ACK `errorCode` (`webhook.malformed`, `webhook.client_id_mismatch`, `webhook.unknown_transaction`, `webhook.incomplete_success`, `webhook.document_id_mismatch`).
- Sample API endpoint `MapESignWebhookEndpoint(IEndpointRouteBuilder, IConfiguration)` — paralleling `MapInvoiceEndpoints` / `MapTemplateEndpoints`. Mounts on the configured `Webhook.Path`, with optional shared-secret URL segment (constant-time compared via `CryptographicOperations.FixedTimeEquals`) and CIDR allowlist enforcement. Emits the FR-101 startup WARN when `Mode != Polling` and neither `Secret` nor `AllowedIps` is configured.
- `ESignLogScrubber` extended: redacts `signature`, `signatureId`, `mainDom`, `extraData`, `secret` JSON field values on captured envelopes (`extraData` object body redacted entirely).

### Changed

- Constitution amended (`.specify/memory/constitution.md` 1.0.0 → 1.1.0): Principle II and Principle VII widened to name `MisaConnect.<Product>.{Client, Domain}` as the public-surface pattern across `EInvoice` and `ESign` families; governance footer now requires major version bumps on every affected product-family package. Amendment is additive — no existing artifact (EInvoice 1.1.0 or any ESign preview) became non-compliant.

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

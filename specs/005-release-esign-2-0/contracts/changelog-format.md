# Contract — `CHANGELOG.md` `[2.0.0]` Section

Specifies the exact shape of the `[2.0.0]` section so the `awk` extractor in `release.yml` produces clean release notes and so consumers reading nuget.org get a coherent narrative.

## Required structure

```markdown
# Changelog

All notable changes to MisaConnect will be documented here. This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

<!-- empty; post-2.0 work accumulates here -->

## [2.0.0] - 2026-MM-DD

Public API is the union of slices 1–4, previewed through `2.0.0-preview.1`..`preview.4`. This is the first stable release of the `MisaConnect.ESign` product family.

### Added

#### Slice 1 — PDF signing flow (`MisaConnect.ESign`)

- `services.AddMisaConnectESign(IConfiguration)` and `AddMisaConnectESign(Action<MisaESignOptions>)` overload — public DI entry point binding the `Misa:ESign` configuration section.
- `IMisaESignClient.SignPdfAsync(SignPdfRequestDto, CancellationToken)` — end-to-end PDF signing via MISA eSign RemoteSigning (login → list certs → hash → sign → poll → attach).
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
- Typed OTP exceptions in `Domain.Errors`: `InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`, `OtpRejectedException` — sealed subclasses of `AuthenticationFailedException`.
- `AuthenticationFailedException.Username` property (non-null; populated only on the `errorCode = 122` re-challenge branch).
- `OtpResendResultDto` (`Client.Dtos`) — 1:1 wrapper over `Application.Abstractions.OtpResendResult`.
- `MisaESignOptions.Otp` block with `DefaultResendLanguage = "en-US"`.
- Two new methods on `IMisaESignWireClient`: `TwoFactorAuthAsync(...)`, `ResendOtpAsync(...)`.

#### Slice 3 — Multi-format document signing (XML, Word, Excel)

- `IMisaESignClient.SignXmlAsync(SignXmlRequestDto, CancellationToken)` — XAdES XML signing; accepts string or `byte[]` via factories `SignXmlRequest.FromString(...)` / `FromUtf8Bytes(...)`.
- `IMisaESignClient.SignWordAsync(SignWordRequestDto, CancellationToken)` — OOXML `.docx` signing.
- `IMisaESignClient.SignExcelAsync(SignExcelRequestDto, CancellationToken)` — OOXML `.xlsx` signing.
- `DocumentFormat` byte-backed closed enum (`Unknown = 0, Pdf = 1, Xml = 2, Word = 3, Excel = 4`) in `Domain.Documents`. Surfaced as the `Format` property on every typed exception and on every result DTO.
- `XmlSignatureContext` slim record (`SignatureName`, `HashAlgorithm`, `SignatureDescription`) in `Domain.Signing` — exposes only XAdES-meaningful fields.
- Public client DTOs: `SignXmlRequestDto`, `SignWordRequestDto`, `SignExcelRequestDto`, `SignXmlResultDto`, `SignWordResultDto`, `SignExcelResultDto`, `XmlSignatureContextDto`, `SignatureDescriptionDto`.
- `SignPdfResultDto` gains a `Format = DocumentFormat.Pdf` property (additive).
- Every existing typed exception gains an optional `format` constructor parameter and a `Format` property (additive; slice-1 PDF call sites pass `DocumentFormat.Pdf`).
- Five new methods on `IMisaESignWireClient`: `HashXmlAsync`, `HashWordAsync`, `HashExcelAsync`, `AttachSignatureToXmlAsync`, `AttachSignatureToWordExcelAsync`.
- New Application records: `XmlHashOutput`, `WordExcelHashOutput`, `SignHashInput`.
- `ESignErrorMapper.Map(...)` gains a `DocumentFormat requestedFormat = DocumentFormat.Pdf` parameter and new per-format synthesized codes (`InvalidXmlInput`, `MissingMainDom`, `MissingSignatureId`, `UnsupportedDocumentVariant`).
- `ESignLogScrubber` redacts new sensitive fields on `/documents/*` payloads: `mainDom`, `signatureId`, `document`.

#### Slice 4 — Webhook-mode signing

- `IMisaESignClient.BeginSignPdfAsync` / `BeginSignXmlAsync` / `BeginSignWordAsync` / `BeginSignExcelAsync` — initiate a sign without polling; returns `BeginResultDto { TransactionId, Format }`. Throws `InvalidOperationException("Webhook-mode signing is disabled when Mode == Polling")` per FR-093.
- `IMisaESignClient.HandleWebhookAsync(WebhookEnvelopeDto, CancellationToken)` — entry point for inbound MISA webhook deliveries. Runs the four-step typed validation pipeline, single-flight finalize via `/documents/attachment`, idempotent dedupe on success, failure-not-cached invariant, and short-circuit on terminal `FAILED`/`CANCELLED` envelopes. Returns `WebhookHandleResultDto { Ack, Outcome }`.
- `IWebhookDeliveryHook` (`Client.Webhook` namespace) — consumer-implementable callback receiving typed `WebhookOutcomeDto`. Default `NullClientWebhookDeliveryHook` logs at INFO.
- `Application.Abstractions.ISigningSessionStore` — single new port. Default `InMemorySigningSessionStore` uses `ConcurrentDictionary` keyed by `(clientId, transactionId)` with lazy TTL eviction; also implements `IFinalizeLockOwner`.
- `Domain.Webhook.*` namespace: `WebhookEnvelope`, `WebhookSignature`, `WebhookStatus`, `WebhookAck`, `BeginResult`, `WebhookOutcome` + variants (`SuccessWithSignedBytes`, `FailureWithError`, `TerminalWithoutFinalize`), `WebhookValidationCategory`.
- `Domain.Errors.WebhookValidationException` (abstract) + 5 sealed subclasses (`MalformedEnvelopeException`, `ClientIdMismatchException`, `UnknownTransactionException`, `IncompleteSuccessEnvelopeException`, `DocumentIdMismatchException`).
- `MisaESignOptions.Webhook` sub-section: `Mode` (`Polling`/`Webhook`/`Both`, default `Both`), `Session.Ttl` (default 24h), `Path` (default `/esign/webhook`), `Secret` (≥32 chars when set), `AllowedIps` (CIDR strings).
- `ESignErrorMapper.MapWebhookValidationToAck(WebhookValidationException)` — returns the namespaced ACK `errorCode`.
- Sample API endpoint `MapESignWebhookEndpoint(IEndpointRouteBuilder, IConfiguration)` in `samples/MisaConnect.Samples.Api/`. Mounts on the configured `Webhook.Path` with optional shared-secret URL segment (constant-time compared) and CIDR allowlist.
- `ESignLogScrubber` extended: redacts `signature`, `signatureId`, `mainDom`, `extraData`, `secret` JSON field values.

### Changed

- Constitution amended (1.0.0 → 1.1.0): Principle II and Principle VII now name `MisaConnect.<Product>.{Client, Domain}` as the public-surface pattern, with `EInvoice` and `ESign` enumerated; governance footer requires major bumps on every affected product-family package.

## [1.1.0] - 2026-05-15
…
```

## Required properties

1. **Heading exactness**: `## [2.0.0] - <date>` — bracket-quoted version, space, dash, space, ISO date. The `release.yml` awk command relies on `/^## \[2.0.0\]/` so any deviation (`v2.0.0`, missing brackets, missing date) breaks extraction.
2. **One provenance sentence at the top** — not scattered "Shipping as preview.N" markers inside slice bullets.
3. **Empty `## [Unreleased]` above** — present so post-2.0 work has somewhere to land on day 1.
4. **Bullets are present-tense or past-tense, consistent within a sub-section** — the inherited slice content uses bullet-list-of-additions style; preserve verbatim where possible.
5. **No mention of unreleased / withheld features** — anything not on the published surface must not appear in `[2.0.0]`.

## Extractor behavior (informative, do not duplicate)

```sh
awk "/^## \[2.0.0\]/{flag=1; next} /^## \[/{flag=0} flag" CHANGELOG.md
```

Returns every line between the `## [2.0.0]` heading (exclusive) and the next `## [` heading (exclusive). The output is used **verbatim** as the GitHub Release body. This means:

- The provenance sentence becomes the first line of the release notes — keep it pithy.
- The `### Added` / `### Changed` sub-headings render as markdown headers in the GitHub Release UI.
- Trailing blank lines are preserved harmlessly.

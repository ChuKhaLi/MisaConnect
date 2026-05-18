# Feature Specification: MISA eSign — PDF Signing Flow (foundational)

**Feature Branch**: `001-misa-esign-pdf-sign-flow`
**Created**: 2026-05-18
**Status**: Draft
**Input**: User description: "Integrate with MISA eSign RemoteSigning so an authenticated caller of our service can sign a PDF end-to-end via MISA's remote certificate service. The slice covers: logging in with userName/password against /api/auth/api/v1/auth/login-api to obtain an accessToken, a remoteSigningAccessToken, a refreshToken, and an expiresIn lifetime (cached and refreshed on HTTP 401 via /auth/refreshtoken); listing the user's digital certificates via /external/esrm/service/general/api/v1/Certificates/by-userId and selecting one whose keyStatus = ACTIVE; hashing a PDF document via /external/esrm/service/document/api/v1/documents/hash using SHA256 with the selected certificate and certificate chain; submitting the resulting digest to /external/esrm/service/signing/api/v1/Signing/hash to obtain a transactionId; polling /external/esrm/service/signing/api/v1/Signing/status/{transactionId} until status is SUCCESS, FAILED, or CANCELLED; and attaching the returned signature to the original PDF via /external/esrm/service/document/api/v1/documents/attachment to produce the final signed PDF bytes. The slice wires up x-clientId / x-clientKey / AuthorizationRM header handling, sandbox vs production base URL via the Misa:ESign configuration section, and the consumer-facing services.AddMisaConnectESign(IConfiguration) DI entry point. The deliverable also includes a unit test suite using mock fixtures (no network) covering validation, ResponseError mapping, refresh-on-401, ACTIVE-cert filtering, and the polling state machine; plus an end-to-end integration test suite that exercises the full happy path against the MISA eSign sandbox using credentials from MISACONNECT_ESIGN_SANDBOX_* env vars (see docs/sandbox-setup.md). Out of scope: 2FA / OTP flow (slice 2), non-PDF document types (slice 3), and the webhook receiver as an alternative to polling (slice 4). Source doc: docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md."

## Clarifications

### Session 2026-05-18

- Q: Transport-level retry policy for transient failures (5xx, network errors, 429)? → A: Bounded exponential backoff with jitter (default ~3 attempts), configurable + disable-able via `MisaESignOptions`. Auth-401 refresh-then-retry-once (FR-004) is governed separately.
- Q: Token-cache key identity? → A: Swappable `ITokenCacheKeySelector` port; default selector composes the key from `userName + clientId + base-URL host`.
- Q: Concurrency / thread-safety contract? → A: `IMisaESignClient` is thread-safe by contract; concurrent sign calls supported per cache key; refresh is single-flight (one in-flight refresh per cache key, other waiters reuse its result).

## User Scenarios & Testing *(mandatory)*

This feature is the foundational slice of a new product family (`MisaConnect.ESign`) parallel to the existing `MisaConnect.EInvoice`. The actor is a **consumer developer** integrating the SDK into their .NET service so that an end user of that service can have a PDF signed by their own MISA-issued remote certificate. The "user" in each story below is that consumer developer.

### User Story 1 - Sign a PDF end-to-end with a single SDK call (Priority: P1)

A consumer developer wires the SDK into their service. Given valid MISA credentials and a user who owns at least one ACTIVE remote certificate, the developer invokes a single facade method passing the original PDF bytes and any signing context (e.g. signing position, reason). The SDK orchestrates the full MISA workflow — authenticate, list and select a certificate, hash the document, submit the digest, poll until terminal status, attach the signature — and returns the signed PDF bytes.

**Why this priority**: This is the MVP for the entire ESign product family. Without this, no other slice (2FA, multi-format, webhook) has anything to build on. If shipped alone it already delivers the headline business value: a consumer can sign a PDF.

**Independent Test**: Configure the sandbox credentials in `Misa:ESign`, call the facade method with a one-page test PDF, and verify that the returned bytes (a) are a valid PDF, (b) carry an attached digital signature, and (c) were produced without any consumer code touching MISA endpoints directly. Demonstrable end-to-end against the MISA eSign sandbox.

**Acceptance Scenarios**:

1. **Given** valid sandbox credentials and exactly one ACTIVE certificate on the configured user, **When** the developer invokes the SDK facade with a one-page PDF, **Then** the SDK returns signed PDF bytes within the configured polling timeout and no additional consumer code is required beyond the facade call.
2. **Given** valid credentials and the same configuration, **When** the developer makes two facade calls back-to-back within the cached token lifetime, **Then** only the first call performs a login; the second reuses cached tokens.
3. **Given** valid credentials but a user whose certificates are all expired or revoked (none ACTIVE), **When** the developer invokes the facade, **Then** the SDK surfaces a typed "no active certificate" error with the MISA correlation ID — without attempting to hash or sign.

---

### User Story 2 - Transparent token lifecycle and refresh on 401 (Priority: P2)

After the first successful login, the SDK transparently reuses the cached access tokens until they expire. If any signing-pipeline request returns HTTP 401 (token rejected mid-session), the SDK exchanges the cached refresh token for a fresh access token and retries the original request exactly once. The consumer never sees the 401 unless the refresh itself is rejected.

**Why this priority**: Critical for production reliability — without this, every consumer hits the login endpoint repeatedly (rate-limit risk) and any token expiry during a long sign session surfaces as an opaque failure. P2 because the P1 happy path technically works without it for short sessions.

**Independent Test**: With the in-repo fake server (no MISA needed), force a 401 on a downstream call after a successful login. Verify the SDK calls the refresh endpoint and retries the original call exactly once before surfacing any error to the consumer.

**Acceptance Scenarios**:

1. **Given** a session with cached tokens and an `expiresIn` window still open, **When** the consumer makes a second facade call, **Then** no `login-api` request is made.
2. **Given** the SDK has cached tokens, **When** a downstream MISA call returns HTTP 401 once, **Then** the SDK calls the refresh endpoint, retries the original call once, and surfaces success to the consumer if the retry succeeds.
3. **Given** the SDK's refresh attempt itself is rejected, **When** the original 401 occurs, **Then** the SDK surfaces a typed auth error to the consumer and does not enter a refresh loop.

---

### User Story 3 - Typed, observable error surface (Priority: P3)

When MISA returns a `ResponseError` envelope (`error`, `errorCode`, `devMsg`, `userMsg`), the SDK maps it to a typed exception (or equivalent Result variant) that the consumer can branch on. Every outbound request carries a correlation ID; every error and log entry references that correlation ID. No log entry contains tokens, refresh tokens, certificate private material, raw document bytes, or end-user PII.

**Why this priority**: Required for operational debuggability and for the existing Constitution Principle VIII (logs never leak secrets/PII). P3 because the happy path works without it, but no production team will ship without observable errors.

**Independent Test**: Drive the SDK against the fake server with fixtures that emit each documented `errorCode` for the seven slice-1 endpoints. Assert: the surfaced exception type matches the documented mapping; the exception carries the `errorCode` and a correlation ID; the captured log output is scanned and contains no token, no refresh token, no certificate private bytes, no raw PDF bytes.

**Acceptance Scenarios**:

1. **Given** MISA returns a 4xx with a populated `ResponseError`, **When** the SDK surfaces the failure, **Then** the resulting error is typed (auth failure / cert missing / hash invalid / sign rejected / transaction terminal-failure as applicable) and carries `errorCode` + correlation ID.
2. **Given** any successful or failed sign flow, **When** logs are inspected, **Then** no log line contains a token, refresh token, certificate private key material, raw document bytes, or end-user PII.

---

### Edge Cases

- **No ACTIVE certificates** on the user account → surfaced as a typed `NoActiveCertificate`-class error before any hash request is made.
- **Multiple ACTIVE certificates** → the default certificate selector picks the **first ACTIVE certificate in the order returned by MISA's API**. Consumers needing different behavior register their own `ICertificateSelector` adapter before calling `AddMisaConnectESign`.
- **Sign transaction terminates in FAILED or CANCELLED** → the SDK does not retry; it surfaces a typed terminal-state error carrying MISA's `errorCode` and `devMsg`.
- **Polling exceeds the configured total timeout** → typed `SignTimeout`-class error; the partially-completed transaction is left as-is on MISA's side (no implicit cancellation).
- **Refresh token itself is rejected** → typed auth error; no refresh-loop; consumer must re-authenticate explicitly.
- **MISA sandbox unreachable** at test time → `[SandboxFact]` integration tests skip cleanly; runtime SDK calls exhaust the configured transport-retry budget (FR-023) and then surface a typed transport error — never silently swallowed.
- **Transient transport failure** mid-pipeline (5xx, network, 429) → SDK retries within the configured budget; if a sign transaction has already produced a `transactionId` before the failure, retries resume polling that same `transactionId` (no duplicate sign submission).
- **Refresh stampede** — multiple parallel sign calls observe a 401 for the same cached token simultaneously → exactly one outbound `refreshtoken` request is issued for that cache key; the rest await its result and proceed with the refreshed token (FR-028).
- **Cached token deemed expired by the system clock** but not yet rejected by MISA → SDK refreshes proactively rather than waiting for the 401.

## Requirements *(mandatory)*

### Functional Requirements

**Authentication and token lifecycle**

- **FR-001**: System MUST authenticate via the MISA `login-api` endpoint using a configured `userName` and `password`, and capture the returned `accessToken`, `remoteSigningAccessToken`, `refreshToken`, and `expiresIn`.
- **FR-002**: System MUST cache the captured tokens in the registered `ITokenCache` (default: in-memory) using a key produced by the registered `ITokenCacheKeySelector` (FR-026), with a TTL derived from `expiresIn`.
- **FR-003**: System MUST reuse cached tokens for subsequent operations until the cached entry expires.
- **FR-004**: On HTTP 401 from any signing-pipeline endpoint, System MUST attempt a single refresh via the MISA `refreshtoken` endpoint using the cached `refreshToken`, then retry the original request exactly once. If the refresh itself fails, System MUST NOT loop and MUST surface a typed auth error.
- **FR-005**: System MUST proactively refresh when the cached token's clock-based remaining lifetime is exhausted, without waiting for a 401.
- **FR-026**: System MUST expose a swappable `ITokenCacheKeySelector` port. The default adapter MUST compose the cache key from the configured `userName`, `clientId`, and base-URL host (in that order). Consumers register their own selector before `AddMisaConnectESign` to alter the identity scheme (e.g. add a tenant discriminator).

**Transport reliability**

- **FR-023**: System MUST retry transient HTTP failures — connection errors, request timeouts, 5xx status codes, and 429 — using bounded exponential backoff with jitter, applied to every MISA call in the slice-1 pipeline. The retry budget MUST be a configurable policy in `MisaESignOptions` (default: at most 3 attempts total, starting delay ~200 ms with full jitter, doubled per retry, capped at ~2 s). On 429 with a `Retry-After` header, System MUST honor the header value (clamped to the configured max delay) instead of the computed backoff.
- **FR-024**: The transport-retry policy MUST be disable-able via configuration (e.g. `MaxAttempts = 1`). The auth-refresh retry defined in FR-004 is a separate budget — it is not affected by the transport-retry policy and is not consumed by it.
- **FR-025**: When the configured retry budget is exhausted, System MUST surface a typed transport error carrying the last response's status (if any), the correlation ID, and the attempt count, without ever entering an unbounded retry loop.

**Concurrency and thread safety**

- **FR-027**: The `IMisaESignClient` facade and the SDK's default port adapters (`ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`) MUST be safe for use as a DI singleton with multiple in-flight sign calls per cache key. Consumer-supplied adapters are expected to honor the same contract; the SDK does not introduce extra serialization for them.
- **FR-028**: Concurrent calls that observe a 401 for the same cached token MUST result in exactly one in-flight refresh per cache key. Other waiters MUST await that refresh and reuse its result rather than issuing duplicate `refreshtoken` requests. If the in-flight refresh fails, all waiters MUST surface the same typed auth error.

**Certificate selection**

- **FR-006**: System MUST list a user's certificates via the MISA `Certificates/by-userId` endpoint and filter to entries with `keyStatus = ACTIVE`.
- **FR-007**: System MUST expose a swappable `ICertificateSelector` port. The default adapter MUST return the first ACTIVE certificate in MISA's API response order.
- **FR-008**: If no certificate has `keyStatus = ACTIVE`, System MUST surface a typed `NoActiveCertificate`-class error before any hash or sign call is made.

**Signing pipeline**

- **FR-009**: System MUST submit the input PDF to MISA's `documents/hash` endpoint using SHA256 together with the selected certificate and its chain, and capture the returned hash payload.
- **FR-010**: System MUST submit the hash payload to MISA's `Signing/hash` endpoint and capture the returned `transactionId`.
- **FR-011**: System MUST poll MISA's `Signing/status/{transactionId}` until the status is one of `SUCCESS`, `FAILED`, or `CANCELLED`, with a configurable poll interval and total timeout.
- **FR-012**: On `SUCCESS`, System MUST submit the original PDF and the returned signature to MISA's `documents/attachment` endpoint and return the resulting signed PDF bytes to the consumer.
- **FR-013**: On `FAILED` or `CANCELLED`, System MUST surface a typed terminal-state error carrying the MISA `errorCode` and `devMsg`. On poll-timeout, System MUST surface a typed `SignTimeout`-class error.

**Transport, configuration, and DI**

- **FR-014**: System MUST inject the `x-clientId`, `x-clientKey`, and `AuthorizationRM` headers on every MISA request per the MISA RemoteSigning v2 contract.
- **FR-015**: System MUST bind its options from the `Misa:ESign` configuration section, expose `MisaESignOptions` (with the section-name constant), and validate that the configured environment (Sandbox vs Production) matches the configured base URL host.
- **FR-016**: System MUST expose the consumer-facing entry point `services.AddMisaConnectESign(IConfiguration)` and the facade interface `IMisaESignClient`. These types form the supported public surface and changes to them are semver events.

**Observability and safety**

- **FR-017**: System MUST attach a correlation ID to every outbound MISA request and propagate it onto every log entry and surfaced error for that request.
- **FR-018**: System MUST map MISA's `ResponseError { error, errorCode, devMsg, userMsg }` envelope to typed errors with the `errorCode` and correlation ID retained on the surfaced exception.
- **FR-019**: System MUST NOT log access tokens, refresh tokens, the `AuthorizationRM` header, certificate private-key material, raw document bytes, or end-user PII at any log level by default.

**Test deliverables**

- **FR-020**: A unit test project at `tests/MisaConnect.ESign.UnitTests/` MUST run in under 30 seconds with no network access, and MUST cover request-shape validation, `ResponseError` mapping, the refresh-on-401-then-retry-once behavior, ACTIVE-cert filtering, the certificate-selector default, and the polling state machine.
- **FR-021**: An integration test project at `tests/MisaConnect.ESign.IntegrationTests/` MUST provide `[SandboxFact]`-style tests against the MISA eSign sandbox using `MISACONNECT_ESIGN_SANDBOX_*` env vars (see [docs/sandbox-setup.md](../../docs/sandbox-setup.md)), and MUST skip cleanly when the sandbox is unreachable or the env vars are absent, but MUST fail loudly when credentials are explicitly rejected by the sandbox.
- **FR-022**: The integration project MUST include an in-repo fake server at `tests/MisaConnect.ESign.IntegrationTests/EsignFake/`, modelled on the existing `MisaFake/` used by the eInvoice integration tests, for deterministic offline runs.

### Key Entities

- **Auth Session** — captures `accessToken`, `remoteSigningAccessToken`, `refreshToken`, and `expiresIn` for a given credential identity; lives in `ITokenCache`.
- **Certificate** — represents a user's remote signing certificate (`id`, `keyStatus`, owner info, certificate chain).
- **Hash Request** — the input to MISA's hash endpoint (input document bytes + selected certificate + hash algorithm + signing position metadata).
- **Sign Transaction** — identified by `transactionId`; has a status in `{ PENDING, SUCCESS, FAILED, CANCELLED }` and, on success, an attached signature payload.
- **Signed Document** — the final output: signed PDF bytes returned to the consumer.
- **Error Envelope** — the MISA `ResponseError { error, errorCode, devMsg, userMsg }` shape, mapped to typed SDK errors.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A consumer with valid sandbox credentials can sign a one-page PDF end-to-end with a single SDK facade call.
- **SC-002**: The unit test suite completes in under 30 seconds on a developer laptop, with no network access.
- **SC-003**: The integration test suite passes against a configured sandbox account, and skips cleanly with no failures when the `MISACONNECT_ESIGN_SANDBOX_*` env vars are absent or the sandbox host is unreachable.
- **SC-004**: After the first sign of a given credential identity, subsequent signs within the cached `expiresIn` window perform zero additional login requests.
- **SC-005**: Every MISA `errorCode` documented for the seven slice-1 endpoints maps to a typed SDK error whose surfaced payload carries that `errorCode` and a non-empty correlation ID — verified by unit-test coverage of each documented code.
- **SC-006**: A captured-log scan over the full unit and integration suites yields zero hits for any cached token value, refresh token value, certificate private-key material, raw document byte sequence, or end-user PII string.
- **SC-007**: Under at least 8 parallel sign calls that all observe a 401 for the same cached token, exactly 1 outbound `refreshtoken` request is issued — verified by request counting against the in-repo fake server.

## Assumptions

- The actor calling the SDK is a consumer developer integrating `MisaConnect.ESign` into their own service; the end user whose document is signed is one MISA-account hop away and is not directly involved in this slice.
- The consumer holds and securely stores MISA `userName`/`password`/`clientId`/`clientKey`, e.g. via .NET user secrets or a secret manager — matching the existing eInvoice convention in [docs/sandbox-setup.md](../../docs/sandbox-setup.md).
- The MISA production base URL is `esignapp.misa.vn` (per the slice-1 input); the sandbox base URL is the value MISA issues with sandbox credentials and is supplied via the `Misa:ESign` configuration section.
- Default poll interval and total timeout for `Signing/status` are reasonable web-service defaults (e.g. interval ~2 s, total ~60 s), both configurable via `MisaESignOptions`.
- Default transport-retry settings (FR-023): up to 3 attempts, starting delay ~200 ms with full jitter, doubled per retry, capped at ~2 s. Consumers tighten or disable via `MisaESignOptions`.
- Default `ITokenCache` is in-memory; consumers register a distributed adapter (e.g. Redis) by binding their own implementation before `AddMisaConnectESign`. The default `ITokenCacheKeySelector` (FR-026) composes the cache key from `userName + clientId + base-URL host`, so the same process can hold sandbox and production sessions without collision; consumers register their own selector to add further discriminators (e.g. a tenant ID).
- 2FA / OTP (slice 2), non-PDF document types (slice 3), and the webhook receiver (slice 4) are explicitly out of scope for this slice and will be specified separately per [docs/misa-esign-spec-plan.md](../../docs/misa-esign-spec-plan.md).
- The constitution principles in [.specify/memory/constitution.md](../../.specify/memory/constitution.md) — especially zero-dep Domain, port-and-adapter extensibility, wire-format fidelity, and no-PII-in-logs — apply to every requirement above.

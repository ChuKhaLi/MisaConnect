# Feature Specification: MISA eSign — Two-Factor Authentication (OTP)

**Feature Branch**: `002-misa-esign-2fa-otp`
**Created**: 2026-05-19
**Status**: Draft
**Input**: User description: "Add support for MISA eSign two-factor authentication. When /auth/login-api returns error code 122 (2FA required), the slice routes through /api/auth/api/v1/auth/two-factor-auth with the user's OTP code, an otpType discriminator (0 = received via SMS/email, 1 = received from the authenticator app), and a remember-device flag, to obtain the accessToken / remoteSigningAccessToken / refreshToken. The slice also supports OTP re-delivery via /webdev/api/auth/api/v1/auth/resend-otp-auth with the userName and a language code (e.g. en-US). Surfaces clear errors for invalid OTP, expired OTP, and exhausted attempts. Reuses slice 1's HTTP plumbing, header handling, token cache, and refresh-token flow. The deliverable extends slice 1's test suites: unit tests using mock fixtures for the 122-code branch, both otpType paths, the resend flow, and the rejection paths; plus end-to-end integration tests that, when the sandbox account is configured with 2FA enabled (a documented sandbox precondition), drive the OTP exchange to completion. Reuses the token cache and HTTP plumbing built in slice 1 (misa-esign-pdf-sign-flow). Out of scope: alternative MFA providers, OTP delivery channel selection beyond the documented otpType enum, and account-recovery flows. Source doc: docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md."

## Clarifications

### Session 2026-05-19

- Q: Should the 2FA completion API surface be an explicit two-step pair on `IMisaESignClient`, a DI-registered `IOtpProvider` callback, or both? → A: Ship both surfaces from day one. The explicit pair (`SignInWithOtpAsync` + `ResendOtpAsync` on `IMisaESignClient`) is the primary API; `IOtpProvider` is an optional convenience layer built on top of it, registered at `AddMisaConnectESign(...)` time.
- Q: How should OTP rejection variants (invalid / expired / exhausted attempts) be mapped to typed errors when MISA's doc enumerates only code 122? → A: Hybrid — use canonical `errorCode` values from the MISA Postman collection when present; otherwise fall back to substring synthesis on `errorCode + devMsg` matching slice-1's `SynthesizeHashCode` pattern. The error-mapping contract publishes both layers and the keyword table that drives the fallback.
- Q: When the consumer sets `remember: true` and OTP exchange succeeds, what does the SDK persist client-side? → A: Nothing. `remember` is a pure pass-through hint to MISA. No new persistence port, no new entity. The MISA `/two-factor-auth` success body returns no device-trust identifier today; if a future MISA response variant adds one, persistence is a follow-up slice.

## User Scenarios & Testing *(mandatory)*

This feature is slice 2 of the `MisaConnect.ESign` product family. It builds directly on slice 1 (`001-misa-esign-pdf-sign-flow`), which already establishes login, token caching, refresh-on-401, header handling, certificate selection, and the end-to-end PDF signing pipeline. The actor in each story is a **consumer developer** integrating the SDK into their .NET service; the end user of that service supplies the one-time password (OTP) out-of-band (from SMS, email, or an authenticator app).

### User Story 1 - Complete sign-in when MISA requires a second factor (Priority: P1)

A consumer developer's service attempts to authenticate against MISA on behalf of a user whose account is enrolled in 2FA. MISA's `/login-api` returns `errorCode 122`. The SDK exposes a typed "2FA required" signal carrying the `userName` (no password, no tokens). The consumer's service prompts its end user, collects the OTP code and `otpType` (whether the user received the code via SMS/email or from an authenticator app), and submits both back to the SDK along with the optional remember-device flag. The SDK exchanges the OTP via `/auth/two-factor-auth`, caches the resulting tokens in the same shape produced by slice 1, and from that point forward signing operations proceed without further 2FA prompts within the cached token lifetime.

**Why this priority**: This is the headline value of the slice. Without it, any MISA account with 2FA enabled is unreachable from the SDK — slice 1's `Requires2FA = true` exception is currently a dead end. P1 because it's the only path that unblocks signing for 2FA-enrolled users; P2 and P3 enhance the UX but do not unblock the baseline.

**Independent Test**: Drive against the in-repo fake server (no MISA needed) with a login fixture that returns `errorCode 122`. Catch the typed 2FA-required signal, supply a fixture OTP, and verify (a) the SDK POSTs to `/auth/two-factor-auth` with the body `{ userName, code, otpType, remember }`, (b) on success the cached tokens match the slice-1 shape, and (c) a subsequent signing call performs zero additional `/login-api` or `/two-factor-auth` requests. Demonstrable end-to-end against the MISA eSign sandbox when the sandbox account is configured for 2FA.

**Acceptance Scenarios**:

1. **Given** valid credentials on a 2FA-enrolled MISA account and an end user who can supply the correct OTP, **When** the consumer attempts a signing operation, **Then** the SDK surfaces a typed "2FA required" signal carrying the `userName` and (after the consumer submits the OTP) the SDK completes the OTP exchange, caches the tokens, and signing proceeds to success.
2. **Given** a successful 2FA exchange has just populated the cache, **When** the consumer makes a second signing call within the cached token lifetime, **Then** no further `/login-api` or `/two-factor-auth` request is made.
3. **Given** the consumer supplies `otpType = 0` (SMS/email) but the end user pasted a code from the authenticator app (or vice versa), **When** the SDK calls `/auth/two-factor-auth`, **Then** the SDK forwards exactly what the consumer supplied — it does not silently translate the `otpType` — and surfaces MISA's rejection as a typed error.

---

### User Story 2 - Re-deliver an OTP that was lost or never arrived (Priority: P2)

The end user's OTP delivery failed or was lost (delayed SMS, deleted email, locked-out authenticator). The consumer exposes a "resend code" affordance and invokes the SDK's resend operation, supplying the `userName` and an optional language code (defaults to `"en-US"`). The SDK POSTs to `/webdev/api/auth/api/v1/auth/resend-otp-auth` and returns a typed result that the consumer can branch on regardless of MISA's response shape.

**Why this priority**: A real-world deployment will hit OTP-delivery failures regularly. Without an explicit resend path consumers either give up or restart `/login-api`, which is wasteful and confusing. P2 because P1 still works for end users whose first OTP arrives correctly; resend is an enhancement, not the critical path.

**Independent Test**: Drive against the in-repo fake server with a resend endpoint returning each documented success/failure envelope shape. Assert (a) the request body is exactly `{ userName, language }`, (b) the default `language` is `"en-US"` when the consumer does not override, and (c) the SDK's surfaced result distinguishes success from MISA-returned failure without the consumer parsing raw envelopes.

**Acceptance Scenarios**:

1. **Given** a 2FA-required signal is in hand and the end user did not receive the OTP, **When** the consumer invokes the SDK's resend operation with no overrides, **Then** the SDK POSTs `{ userName: <captured>, language: "en-US" }` to MISA's resend endpoint and returns a typed success result.
2. **Given** the consumer overrides the language (e.g. `"vi-VN"`), **When** the resend operation is invoked, **Then** the SDK forwards the override verbatim; the SDK does not validate the value client-side.
3. **Given** MISA's resend endpoint returns a failure envelope (any documented `errorCode`), **When** the SDK surfaces the result, **Then** the surfaced result is typed-failure carrying `errorCode`, `devMsg`, `userMsg`, and the correlation ID — the consumer never has to parse raw MISA JSON.

---

### User Story 3 - Typed errors for OTP rejection, exhaustion, and expiry (Priority: P3)

When `/auth/two-factor-auth` rejects an OTP submission, the SDK distinguishes — at minimum — three categories: **invalid OTP** (wrong code), **expired OTP** (stale code), and **exhausted attempts** (too many failures). Each category surfaces as a distinct typed error that the consumer can branch on by type, not by string-matching `devMsg`. No log line, exception message, or telemetry attribute contains the OTP code itself.

**Why this priority**: Operationally important for end-user UX (the consumer can show "wrong code" vs "code expired, please request a new one" vs "too many attempts, please contact support") and for the existing Constitution Principle VIII (logs never leak secrets). P3 because the P1 happy path technically works without the typed distinction — every rejection could surface as a single generic "OTP rejected" — but no production team would ship that.

**Independent Test**: Drive the fake server through one OTP submission per rejection category, each emitting the documented `errorCode` (or, where MISA's doc is silent, the fallback `errorCode` chosen at clarification time). Assert: each surfaced exception has the documented typed shape; each exception carries `errorCode`, `devMsg`, `userMsg`, and the correlation ID; a log capture of the full exchange contains zero occurrences of the OTP code or the resulting remember-device token.

**Acceptance Scenarios**:

1. **Given** MISA returns the documented "invalid OTP" envelope, **When** the SDK surfaces the failure, **Then** the resulting exception's type indicates "invalid OTP" and carries the MISA `errorCode` + correlation ID.
2. **Given** MISA returns the documented "expired OTP" envelope, **When** the SDK surfaces the failure, **Then** the resulting exception's type indicates "expired OTP" — distinct from invalid-OTP — and the consumer can branch on type alone.
3. **Given** any 2FA exchange or resend operation, **When** logs are captured, **Then** no log line contains the OTP `code` value, the consumer's `password`, or any "remembered device" token that may result from `remember: true`.

---

### Edge Cases

- **2FA required but the consumer abandons the flow** (no OTP supplied within the consumer's UX window) → the SDK does not auto-retry or hold state across processes; the consumer's next signing attempt repeats from `/login-api` and surfaces a fresh 2FA challenge.
- **Resend invoked while a previous OTP is still valid** → the SDK does not enforce client-side throttling; MISA's response is authoritative (the doc is silent on resend cooldowns). Documented in Assumptions.
- **Wrong `otpType` supplied by the consumer** (e.g. claims `1` for app when the user got it via SMS) → MISA rejects; the SDK surfaces the rejection typed; no client-side translation.
- **`remember: true` and a successful 2FA exchange** → cached tokens behave like any other slice-1 cached tokens within the process. The `remember` flag is forwarded to MISA verbatim; the SDK does not persist a "remembered device" identifier client-side. If a future process restart cannot reuse remember-device semantics, that is by design (FR-035) and the consumer's next sign attempt will surface a fresh 2FA challenge.
- **Concurrent OTP exchanges for the same token-cache key** (rare — two consumer threads both observe the 2FA-required signal and race to submit) → at most one in-flight `/auth/two-factor-auth` request per cache key; the other waiter reuses its result. This extends slice 1's `FR-028` single-flight contract.
- **2FA exchange itself rejected mid-pipeline by a 5xx or 401** → 5xx engages slice 1's transport retry policy (`FR-023`); 401 surfaces as a typed auth error (the 2FA endpoint has no `AuthorizationRM` header to refresh — see Assumption 5).
- **MISA returns code 122 again on the `/auth/two-factor-auth` response** (re-challenge) → treated as a typed auth error; the SDK does not loop. The consumer must restart from `/login-api`.
- **OTP exchange success but the consumer never calls a signing operation afterward** → cached tokens are still valid for the lifetime returned in `expiresIn`; the next consumer call (signing or otherwise) reuses them.

## Requirements *(mandatory)*

### Functional Requirements

Slice 1's last `FR-NNN` is `FR-028`; this slice picks up at `FR-030` with room to grow.

**2FA challenge detection**

- **FR-030**: System MUST recognize MISA `errorCode == "122"` on `/login-api` as a "2FA required" signal and surface it to the consumer as a typed value (or typed exception) distinguishable from generic auth failures.
- **FR-031**: The 2FA-required signal MUST carry the `userName` so the consumer can correlate the challenge with the user account. It MUST NOT carry the consumer's `password`, any partial token, or any value that could be used in place of completing the OTP exchange.

**OTP exchange**

- **FR-032**: System MUST POST to `/api/auth/api/v1/auth/two-factor-auth` with a request body of exactly `{ userName, code, otpType, remember }`, where `code` is the end-user-supplied OTP value, `otpType ∈ {0, 1}` per the MISA doc (`0` = SMS/email, `1` = authenticator app), and `remember` is a boolean device-trust flag. Field names and casing MUST match MISA's published shape verbatim (Constitution Principle IV).
- **FR-032a (consumer API — primary explicit surface)**: `IMisaESignClient` MUST expose an explicit `SignInWithOtpAsync(otpCode, otpType, remember, ct)` method that performs the OTP exchange (FR-032) and populates the token cache (FR-034). Consumers reach it by catching the typed 2FA-required signal raised from `SignPdfAsync` (FR-030), collecting the OTP from their end user out-of-band, then invoking `SignInWithOtpAsync`. The next call to `SignPdfAsync` MUST succeed without further 2FA prompts while the cached tokens are valid.
- **FR-032b (consumer API — optional transparent provider)**: System MUST also expose an optional `IOtpProvider` port. When a consumer registers an implementation at `AddMisaConnectESign(IConfiguration)` time, `SignPdfAsync` MUST, on encountering a 2FA-required signal, invoke the provider to obtain `{ otpCode, otpType, remember }`, complete the OTP exchange via the same code path as FR-032a, and continue the signing operation transparently. If no `IOtpProvider` is registered, the typed 2FA-required signal MUST surface to the consumer unchanged (so FR-032a remains the supported path). The provider MUST also expose a resend hook (matching FR-036) so transparent flows can request OTP re-delivery without dropping back to the explicit surface.
- **FR-033**: System MUST send the `/two-factor-auth` request with the `clientId` and `clientKey` headers per the existing slice-1 header injection. No `AuthorizationRM` header is required on this endpoint (no cached access token exists at this point in the flow).
- **FR-034**: On successful OTP exchange, System MUST cache the returned `accessToken`, `remoteSigningAccessToken`, `refreshToken`, and `expiresIn` using the same `ITokenCache` and `ITokenCacheKeySelector` infrastructure as slice 1 (FR-002, FR-026). Slice 2 MUST NOT introduce a new cache port.
- **FR-035**: The shape of cached tokens after a 2FA exchange MUST be indistinguishable from the shape produced by `/login-api`, so downstream signing operations (slice 1's FR-001 through FR-028) require no awareness of how the cache was populated. The `remember` flag is a pure pass-through to MISA: System MUST forward the consumer-supplied value verbatim on the `/two-factor-auth` request and MUST NOT persist any "remembered device" identifier client-side. No new persistence port and no new domain entity are introduced by this slice.

**OTP resend**

- **FR-036**: System MUST expose an explicit "resend OTP" operation that POSTs to `/webdev/api/auth/api/v1/auth/resend-otp-auth` with a request body of exactly `{ userName, language }`. The `userName` MUST be the same value carried by the originating 2FA-required signal.
- **FR-037**: When the consumer does not specify a language, the resend operation MUST default to `language = "en-US"` (the only example value the MISA doc lists). Consumers MUST be able to override the language per call. The SDK MUST NOT validate the value client-side; MISA is the authority on supported values.
- **FR-038**: The resend operation MUST return (or throw) a typed value that distinguishes success from failure. On failure, the typed value MUST carry MISA's `errorCode`, `devMsg`, `userMsg`, and the correlation ID. The SDK MUST NOT silently swallow MISA's response.

**Error mapping**

- **FR-039**: System MUST surface typed exceptions for at least three OTP-rejection categories from `/auth/two-factor-auth`: **invalid OTP**, **expired OTP**, and **exhausted attempts**. The categories MUST be distinguishable by exception type (or a typed enum), not by string-matching `devMsg` or `userMsg` at the consumer's call site. Mapping strategy is hybrid: when the MISA Postman collection (the tie-breaker per the parent plan doc) yields a canonical `errorCode` for a category, the SDK MUST map by that `errorCode` directly. When the Postman collection is also silent, the SDK MUST fall back to substring synthesis on `errorCode + devMsg` following slice-1's `SynthesizeHashCode` pattern, producing stable internal codes (e.g. `"InvalidOtp"`, `"ExpiredOtp"`, `"ExhaustedOtpAttempts"`). Both layers — the canonical-code table and the substring keyword table that drives the fallback — MUST be published in the slice's error-mapping contract so consumers can audit how their MISA responses are bucketed.
- **FR-040**: Every OTP-related exception (challenge surfaced, exchange rejected, resend rejected) MUST carry MISA's `errorCode`, `devMsg`, `userMsg` (when present in the response envelope), and the request correlation ID.

**Reuse contracts — no regression**

- **FR-041**: The slice-1 refresh-on-401 contract (`FR-004`) MUST continue to apply to tokens obtained via 2FA exchange. Downstream signing-pipeline endpoints MUST NOT need to know whether the cached token came from `/login-api` or `/two-factor-auth`.
- **FR-042**: The slice-1 single-flight refresh contract (`FR-028`) MUST extend to the 2FA endpoints: at most one in-flight `/auth/two-factor-auth` request per token-cache key; concurrent waiters reuse its result.
- **FR-043**: The slice-1 transport-retry policy (`FR-023`, `FR-024`, `FR-025`) MUST apply to both `/auth/two-factor-auth` and `/auth/resend-otp-auth` for transient transport failures (5xx, 429, connection errors). The auth-refresh retry (`FR-004`) MUST NOT apply to these endpoints — they have no `AuthorizationRM` header to refresh, so a 401 from them surfaces as a typed auth error directly.

**Observability and secret hygiene**

- **FR-044**: No log line, exception message, telemetry attribute, or metric label produced by the 2FA exchange or resend operations MUST contain (a) the OTP `code` value, (b) any "remembered device" identifier that may result from `remember: true`, (c) the consumer's `password`, or (d) end-user PII beyond what slice 1 already permits. Adding these to logs is a regression and MUST fail review.
- **FR-045**: Every outbound `/auth/two-factor-auth` and `/auth/resend-otp-auth` request MUST carry a correlation ID injected by slice 1's `ICorrelationIdAccessor`. The correlation ID MUST appear on every related log entry and on every surfaced typed error so a single failed challenge is traceable end-to-end.

### Key Entities

- **2FA Challenge** — surfaced to the consumer when `/login-api` returns `errorCode 122`. Carries `userName` and (if MISA returns it in a future envelope variant) a channel hint. Carries no tokens, no password, no OTP. Consumer-visible by design.
- **OTP Submission** — the consumer-supplied payload for `/auth/two-factor-auth`. Fields: `userName`, `code` (the end-user OTP value), `otpType` (closed `{0, 1}` enum per MISA), `remember` (boolean device-trust flag).
- **OTP Resend Request** — the payload for `/auth/resend-otp-auth`. Fields: `userName`, `language` (default `"en-US"`, consumer-overridable).
- **Auth Session (reused)** — same shape as slice 1's `AuthSession`, populated either from `/login-api` (non-2FA path) or `/auth/two-factor-auth` (2FA path). No new entity introduced.

## Success Criteria *(mandatory)*

### Measurable Outcomes

Slice 1's last `SC-NNN` is `SC-007`; this slice picks up at `SC-008`.

- **SC-008**: A consumer with valid sandbox credentials for a 2FA-enrolled account can complete a one-page PDF signing operation end-to-end with exactly one OTP exchange, within the sandbox's session timeout.
- **SC-009**: When the consumer submits an OTP, the SDK reaches MISA's `/auth/two-factor-auth` in a single round-trip — no exploratory retry, no silent fallback to `/login-api`, no probe call before the exchange.
- **SC-010**: After a successful 2FA exchange, the next signing operation within the cached token lifetime performs zero additional `/login-api` or `/auth/two-factor-auth` requests.
- **SC-011**: Each of the three OTP-rejection categories (invalid, expired, exhausted) maps to a distinct typed SDK error that consumers can branch on by type — verifiable by a unit test that asserts non-equal exception types for the three categories without inspecting any string field.
- **SC-012**: A log capture covering the full 2FA exchange — challenge surfaced, OTP supplied, exchange success, cache write — contains zero occurrences of the OTP `code` value, the consumer's `password`, and any "remembered device" identifier resulting from `remember: true`.
- **SC-013**: The unit test suite extended for slice 2 still completes in under 30 seconds total — the constitution's unit-test latency invariant is preserved.
- **SC-014**: The integration test suite skips cleanly when `MISACONNECT_ESIGN_SANDBOX_*` env vars are absent or when the configured sandbox account is not 2FA-enrolled, and fails loudly when credentials are explicitly rejected by MISA.

## Assumptions

1. The OTP `code` field is documented by MISA as `code`, not `otpCode`. The phrase "OTP code" in the slice prompt is plain-language paraphrase; the spec mirrors MISA's wire shape verbatim per Constitution Principle IV.
2. `otpType` is a closed `{0, 1}` enum per the MISA doc (line ~305): `0` = SMS/email delivery, `1` = authenticator-app delivery. Other values are surfaced as MISA rejections; the SDK does not pre-validate client-side.
3. Default `language` for resend is `"en-US"` (the doc's only example). Consumers may override per-call. The SDK does not validate values client-side; other values surface as MISA responses.
4. The SDK does not enforce client-side OTP TTL, resend throttling, or attempt-count limits — MISA is the authority on all three. Doc gaps; consumers receive MISA's typed rejections directly via FR-039.
5. The 2FA exchange and resend endpoints reuse slice-1's transport retry policy for transient failures (FR-043). Auth-refresh retry (slice-1 `FR-004`) does NOT apply — these endpoints carry no `AuthorizationRM` header and have no refreshable cached token at the point of call.
6. The `clientId` / `clientKey` header injection already implemented by slice-1's `ClientHeadersHandler` is correct for `/auth/two-factor-auth` and `/auth/resend-otp-auth`. If MISA's actual wire shape requires different casing or different header names on these specific endpoints, that reconciliation happens in `/speckit-plan` and is captured in the wire-envelopes contract.
7. Slice 2 does not change `IMisaESignClient`'s thread-safety contract from slice 1. Concurrent in-flight 2FA exchanges per cache key are bounded by FR-042's single-flight contract.
8. Sandbox preconditions: the MISA doc is silent on how to enable 2FA on a sandbox account. Integration tests rely on a sandbox account configured for 2FA out-of-band (documented in `docs/sandbox-setup.md` updates that ride with this slice's implementation). If the sandbox account is not 2FA-enrolled, the 2FA-specific integration facts skip cleanly per SC-014.
9. The MISA `/two-factor-auth` success body, as documented, returns no device-trust identifier or remember-me cookie — only the standard token bundle plus a `default` channel-preference block. FR-035's pass-through stance assumes this remains MISA's behavior; if a future MISA response variant adds a device identifier, a follow-up slice introduces the persistence port without breaking this one.
10. **Out of scope** (verbatim from the slice prompt): alternative MFA providers, OTP delivery-channel selection beyond the documented `otpType` enum, and account-recovery flows.

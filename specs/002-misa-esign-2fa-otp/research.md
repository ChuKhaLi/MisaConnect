# Phase 0 — Research: MISA eSign 2FA / OTP

This document resolves the open questions left by the Technical Context above. Spec-level clarifications (the dual surface, the hybrid error mapping, the no-persistence stance on `remember`) already landed in [spec.md §Clarifications](./spec.md#clarifications) on 2026-05-19; the entries below pick up where the spec stopped.

Each entry follows: **Decision** → **Rationale** → **Alternatives considered**.

---

## R-1. Consumer-surface shape — explicit pair plus optional transparent provider

**Decision**: Ship both surfaces from day one (the clarified outcome of the 2026-05-19 session). The **primary, supported** surface is the explicit pair on `IMisaESignClient`:

```csharp
Task SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken ct = default);
Task<OtpResendResultDto> ResendOtpAsync(string? language = null, CancellationToken ct = default);
```

Consumers reach the explicit pair by catching the `Requires2FA = true` `AuthenticationFailedException` already raised by slice 1's `SignPdfAsync`. The exception's `Username` property (added in this slice — see R-2) carries the `userName` that `SignInWithOtpAsync` and `ResendOtpAsync` thread into the request body. The next `SignPdfAsync` call after a successful `SignInWithOtpAsync` reuses the cached tokens (slice-1 FR-002 / FR-026 behavior — unchanged).

The **optional** surface is a DI-registered `IOtpProvider`:

```csharp
public interface IOtpProvider
{
    Task<OtpSubmission> ProvideAsync(OtpChallenge challenge, CancellationToken ct);
    Task<OtpResendResult> RequestResendAsync(OtpChallenge challenge, string? language, CancellationToken ct);
}
```

When a consumer registers an implementation **before** calling `AddMisaConnectESign(...)`, slice 2's edited `SignPdf` orchestrator catches `AuthenticationFailedException.Requires2FA = true`, invokes `IOtpProvider.ProvideAsync(...)` to obtain `{ Code, OtpType, Remember }`, runs `ExchangeOtp` against MISA, and **continues** the sign operation transparently. If no provider is registered, the original 2FA-required exception is rethrown unchanged — the explicit pair is the only path.

**Rationale**:
- The clarification was explicit: ship both, with the explicit pair as primary. The provider is a thin convenience layer on top of the same Application use case (`ExchangeOtp`), so there is no path-divergence risk between the two surfaces.
- A pure callback (`IOtpProvider`) is the right shape for an optional convenience because: (a) it lives in Application as a port (Constitution Principle III), (b) the consumer never has to thread `userName` themselves (`OtpChallenge` carries it), (c) a transparent flow that still needs a resend can do so without dropping to the explicit surface (the provider exposes `RequestResendAsync`), and (d) consumer test code can implement it without spinning up DI.
- The explicit pair is necessary because the transparent flow assumes the consumer has a synchronous-enough way to obtain an OTP at the moment of signing. Many real-world deployments do not — they need to prompt an end user via a UI affordance, which is asynchronous and may take minutes. The explicit pair lets the consumer abandon and resume on their own timeline.

**Alternatives considered**:
- *Explicit pair only*: rejected per the 2026-05-19 clarification. Many consumers will want the transparent flow because their service has a way to fetch OTPs (e.g. an authenticator-app integration). Forcing them through the catch-and-retry dance is unnecessary friction.
- *Provider only*: rejected per the clarification. A provider implies the consumer can produce an OTP at orchestration time, which is false for most interactive UX flows.
- *`SignPdfAsync` overload that takes an `Func<...>` OTP callback*: rejected — it would require the consumer to thread the callback through every signing call site and would not compose with DI (no way to register a provider once and have all signing paths use it).

---

## R-2. Carrying the 2FA-required signal — extend the existing exception, not a new typed value

**Decision**: Reuse slice 1's `AuthenticationFailedException` as the 2FA-required signal. Add a non-nullable `Username` property to it (slice 1's exception already has `Requires2FA`). The `Username` is populated only on the 122-code path; on all other authentication failures it surfaces as the empty string (because no `userName` is meaningful to consumers post-failure).

**Rationale**:
- Slice 1 already documents the contract (per [specs/001-misa-esign-pdf-sign-flow/contracts/public-surface.md](../001-misa-esign-pdf-sign-flow/contracts/public-surface.md) §2): "Inspect `Requires2FA` to detect 'MISA wants 2FA'". Extending the existing exception is non-breaking — adding a property is additive — and avoids inventing a parallel type.
- The alternative (a new `TwoFactorRequiredException` extending `AuthenticationFailedException`) would force consumers who already catch `AuthenticationFailedException` to also catch the new subclass to stay correct. A property-based discriminator is more ergonomic.
- The `Username` field carries no secret material — it is the same value the consumer supplied in `MisaESignOptions.UserName` and is necessary for both `SignInWithOtpAsync` and `ResendOtpAsync` request bodies.

**Alternatives considered**:
- *New `TwoFactorRequiredException` type*: rejected for the breakage reason above.
- *Side-channel `IOtpChallengeAccessor`-like port*: rejected — exceptions are the established pattern for "signing failed, here is why" in slice 1 (`SignTerminalStateException`, `SignTimeoutException`, etc.). Side-channels add asymmetry and racing.

---

## R-3. Wire shape of `/two-factor-auth` and `/resend-otp-auth`

**Decision**: Mirror the MISA doc rows 305–306 (`/two-factor-auth`) and 318–319 (`/resend-otp-auth`) verbatim per Constitution Principle IV.

`POST /api/auth/api/v1/auth/two-factor-auth` request body:

```json
{
  "userName": "string",
  "code": "string",
  "otpType": 0,
  "remember": true
}
```

`/two-factor-auth` response body is **identical** to `/login-api` (the doc note says "Lưu lại các thông tin tương tự mục 4.3"). Implementation re-uses `LoginResponseDto` for read, exposes `TwoFactorAuthResponseDto` as a `using`-alias in the wire layer so the wire DTO file lists it for discoverability without duplicating shape.

`POST /webdev/api/auth/api/v1/auth/resend-otp-auth` request body:

```json
{
  "userName": "string",
  "language": "en-US"
}
```

`/resend-otp-auth` response body shape:

```json
{
  "status": {
    "type": "string",
    "code": 200,
    "message": "string",
    "error": false,
    "errorCode": "0"
  },
  "data": {
    "user": { "username": "string" }
  }
}
```

`status.errorCode` is the canonical failure indicator. On HTTP success with `status.error = true`, the SDK treats the response as a typed failure (see R-4).

**Rationale**:
- Direct transcription of the MISA doc table rows. The 2FA endpoint is documented as returning the same shape as login, which is precisely why the slice can reuse the existing `LoginResponseDto` parser path (and inherit the FLAG noted in slice 1 about the envelope-vs-flat shape variance — though for `/two-factor-auth` the doc unambiguously shows the envelope shape).
- The header note ("clientId / clientKey" without `x-` prefix on `/two-factor-auth`, but `x-clientId` / `x-clientKey` everywhere else) is documented in the wire-envelopes contract as a deliberate inconsistency in the MISA doc that slice 1 already chose to normalize toward `x-`. Slice 2 inherits that choice. Integration tests against the sandbox in slice 2 confirm both prefixes work — flagged in the contract.

**Alternatives considered**:
- *Separate `TwoFactorAuthResponseDto` with copy-pasted fields*: rejected — the doc says they are the same, so copying is rot-prone (any future divergence in MISA's `/two-factor-auth` envelope must be fixed in one place, not two).
- *Strip the `x-` prefix on the 2FA endpoint specifically*: rejected — would require per-endpoint header logic that the existing `ClientHeadersHandler` does not have. The flag stays; the integration test confirms the deviation is acceptable to MISA.

---

## R-4. Error mapping — hybrid (canonical errorCode table first, substring fallback second)

**Decision**: Follow the 2026-05-19 clarification. `OtpErrorMapper` (new, in `Application.Errors`) is a sibling of `ESignErrorMapper`; it owns the `/two-factor-auth` and `/resend-otp-auth` paths. The mapper has two layers:

1. **Canonical errorCode table** — exact-match (case-insensitive) on `errorCode` for the codes the MISA Postman collection enumerates. Initial seed values (sourced from the Postman collection on 2026-05-19; will be expanded as the integration suite hits unenumerated codes):
   - `122` (or any `/login-api` 4xx with the same code) → `AuthenticationFailedException(Requires2FA = true)`. This is the slice-1 path, called out here for traceability — slice 2 does not change it.
   - `1001` (OTP wrong) → `InvalidOtpException`.
   - `1002` (OTP expired) → `ExpiredOtpException`.
   - `1003` (max attempts) → `ExhaustedOtpAttemptsException`.
   - `122` returned on `/two-factor-auth` itself (re-challenge) → `AuthenticationFailedException(Requires2FA = true)`. The orchestrator does NOT loop; the consumer must restart from `/login-api` (edge case 7 in spec).
2. **Substring fallback** — when `errorCode` is not in the canonical table (or is empty), match case-insensitively on `errorCode + devMsg + userMsg` against a published keyword table:
   - `"invalid"`, `"wrong"`, `"sai mã"`, `"không đúng"` → `InvalidOtpException`.
   - `"expire"`, `"hết hạn"`, `"quá hạn"` → `ExpiredOtpException`.
   - `"max attempts"`, `"exceeded"`, `"vượt quá số lần"` → `ExhaustedOtpAttemptsException`.
   - Anything else → `OtpRejectedException` (a new residual subclass of `AuthenticationFailedException` carrying `errorCode` + `correlationId` + `Detail`).

Both tables are published in [contracts/error-mapping.md](./contracts/error-mapping.md) so consumers can audit how their MISA responses are bucketed.

**Rationale**:
- Mirrors slice 1's hybrid strategy on `/Signing/hash` and `/documents/hash` (where the doc enumerates one numbered code and a few keyword categories). Consumers who built a `catch (SignRejectedException)` block in slice 1 will recognize the shape.
- The canonical-table-first approach guarantees the three documented categories (invalid / expired / exhausted) surface as distinct types without depending on string heuristics whenever the Postman collection nails a code.
- The substring fallback covers the long tail. The keyword set includes both English and Vietnamese terms because MISA's `userMsg` is Vietnamese in production traffic — slice-1 integration data confirms this.
- Publishing both tables in the contract makes the mapping testable and auditable (one unit test row per table entry) and exposes the synthesized rawCodes (`InvalidOtp`, `ExpiredOtp`, `ExhaustedOtpAttempts`) so consumers see stable internal identifiers in their logs even when MISA's wire-side code is unstable.

**Alternatives considered**:
- *Canonical-only*: rejected — the Postman collection is silent on several edge cases (e.g. OTP expired after one wrong attempt). Substring fallback is necessary to surface the right typed exception on those paths.
- *Substring-only*: rejected — fragile to MISA changing wording, hides the canonical codes from the consumer's logs, fails an audit of "how was this 4xx categorized?".
- *Single `OtpRejectedException` with an enum `Reason`*: defensible but loses the type-driven `catch (InvalidOtpException) { ... }` ergonomics the clarification explicitly wants (FR-039).

---

## R-5. Cache & single-flight semantics for OTP exchange

**Decision**: The OTP exchange writes to the same `ITokenCache` under the same `ITokenCacheKeySelector` key as the login path. The cache value is an `AccessToken` constructed the same way `EnsureAccessToken.ToAccessToken(AuthSession)` builds it from a `LoginResponseDto`, so cache reads are shape-indistinguishable from the login path (FR-035).

`ExchangeOtp` uses `SingleFlightRefresh.RefreshAsync(cacheKey, factory)` (slice 1's primitive) to ensure at most one in-flight `/two-factor-auth` per cache key — extending FR-028 to cover this endpoint (FR-042). Concurrent waiters share the result. On failure the entry is **not** written to the cache; subsequent waiters see the same typed exception.

**Rationale**:
- The clarification on `remember` (no client-side persistence) is the lynchpin: the cache write after a successful OTP exchange is the **only** state the SDK persists across the 2FA flow. Reusing the slice-1 cache shape keeps downstream signing endpoints oblivious to how the cache was populated (FR-041).
- `SingleFlightRefresh` already keys by `string cacheKey` and is per-key, not global — its FR-027 isolation guarantee carries over to the 2FA path for free.

**Alternatives considered**:
- *Separate `IOtpExchangeCache` port*: rejected per FR-034 ("Slice 2 MUST NOT introduce a new cache port") and by the clarification (no new persistence port).
- *Bypass single-flight (let two threads both POST `/two-factor-auth`)*: rejected — MISA's 2FA endpoint has an attempt counter; double-submitting the same OTP eats two attempts and risks an `ExhaustedOtpAttemptsException` on the second waiter.

---

## R-6. Resend OTP — request shape, default language, failure surfacing

**Decision**: `IMisaESignClient.ResendOtpAsync(string? language = null, CancellationToken ct = default)` calls the new Application-layer `ResendOtp` use case which POSTs `{ userName, language }` to `/resend-otp-auth`. Default `language = "en-US"` is read from `MisaESignOptions.Otp.DefaultResendLanguage` (consumer-overridable at config-bind time **and** per-call). The SDK does **not** validate `language` client-side — MISA is the authority on supported values.

Failures (HTTP non-2xx OR HTTP 200 with `status.error = true`) are surfaced as a typed `OtpResendResultDto { Success = false, RawCode, DevMsg, UserMsg, CorrelationId }`. Success surfaces as `OtpResendResultDto { Success = true, CorrelationId }`. Consumers branch on `Success`. The SDK does NOT throw on a documented MISA failure envelope — resend is a "fire and inspect" operation, and consumers usually want to surface the failure in their UX rather than catch an exception. Transport failures (5xx, 429, connection errors) DO throw `ESignTransportException` per FR-043.

Where the transparent path needs a resend (`IOtpProvider.RequestResendAsync(...)` returns an `OtpResendResult` rather than the wire DTO), the use case returns the same typed shape; the provider can decide whether to retry by re-calling its own provider hook.

**Rationale**:
- The dual-shape choice (typed result vs. typed exception) follows the consumer-UX intuition: a resend failure is usually shown to the end user ("we tried to resend, but MISA said X"). Throwing an exception forces the consumer to wrap every `ResendOtpAsync` call in `try/catch`, which is awkward when the failure is expected behavior on the happy path of "no, the original OTP is still valid".
- The default-from-config-with-per-call-override pattern matches slice 1's options usage (e.g. `Polling.Interval` is a default that callers can in principle override; FR-037 makes the per-call override mandatory for `language`).
- Transport failures **do** throw because they are not a "MISA response said no" — they are "we couldn't reach MISA". Consumers handle them via the same retry-budget budget as slice 1 (FR-043).

**Alternatives considered**:
- *Always throw on resend failure*: rejected — bad UX for the most common consumer pattern.
- *Always return a result on resend failure including transport*: rejected — masks transport conditions that consumers handle differently from MISA-side rejections.
- *Validate `language` against an allowlist*: rejected per FR-037 ("MUST NOT validate the value client-side").

---

## R-7. Logging and OTP redaction

**Decision**: Extend slice 1's `ESignLogScrubber` with a `/two-factor-auth`-specific rule:

1. On any captured request body to a path ending in `/two-factor-auth`, replace `$.code` with `"<redacted-otp>"` before serialization-for-logging.
2. On any captured exception detail or `userMsg`/`devMsg` surfaced via `IncludeRawErrorMessage = true`, do NOT log the inbound request body even if it contained `code`. The scrubber is request-stage; exception-stage logging never receives the request body.
3. The `remember` flag is logged as a boolean (it is not secret).
4. The token redactions for `accessToken`/`remoteSigningAccessToken`/`refreshToken` already applied to `/login-api` responses are extended to apply to `/two-factor-auth` responses (since the envelope shape is identical, the same JSON-path matcher catches both).
5. No future "remembered device" identifier (placeholder per FR-035 in case MISA's response variant evolves) is permitted in logs; this is encoded as a JSON-path matcher on `$..device*` that emits a `<redacted-device>` placeholder if any such field ever appears.

**Rationale**:
- The constitution (Principle VIII) and FR-044 are both explicit: OTP `code`, `password`, and any remember-device value must never appear in logs. The scrubber is the existing enforcement point — slice 2 extends it with new rules rather than introducing a parallel scrubber.
- The captured-log unit test (`ESignLogScrubberTests.cs`) gets new assertions per SC-012 to lock the behavior. The test reads the actual log output produced by a full happy-path 2FA exchange against the in-repo fake server and grep-asserts zero occurrences of the OTP `code` value.

**Alternatives considered**:
- *Per-endpoint scrubber*: rejected — the scrubber already handles per-endpoint rules via its JSON-path matchers; adding `/two-factor-auth` is one new rule, not a new scrubber.
- *Drop the entire `/two-factor-auth` request body from logs*: defensible but loses the `userName` + `otpType` + `remember` context that operators want during incident response. The targeted `$.code`-redaction is the right granularity.

---

## R-8. Sandbox preconditions & integration-test gating

**Decision**: Add a new sandbox env var `MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER` pointing to either (a) a path to a console binary that prints an OTP on stdout when invoked, or (b) a literal OTP value for static-fixture sandbox accounts. Document the contract in `docs/sandbox-setup.md` (changes ride with the slice-2 implementation; out of scope for the plan).

`[SandboxFact]` evolves: tests under `tests/MisaConnect.ESign.IntegrationTests/Sandbox/TwoFactorAuthSandboxTests.cs` use a new `[SandboxFact(Requires = SandboxRequirement.TwoFactorAuth)]` overload that skips cleanly when **any** of the following is absent: the existing `MISACONNECT_ESIGN_SANDBOX_*` set, the new `MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER`, or the sandbox account's 2FA-enabled flag (probed by attempting a login and inspecting whether `errorCode == 122` returns). When the sandbox account is not 2FA-enrolled, the test logs an explicit skip reason ("sandbox account is not 2FA-enrolled — set MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER and enable 2FA on the account"). When the credentials are present but MISA rejects them, the test fails loudly per SC-014.

**Rationale**:
- MISA's doc is silent on how to enable 2FA on a sandbox account. The sandbox-setup pattern (env vars + skip-on-absence) is the established convention from the eInvoice suite; extending it with a new gate is a small, additive change.
- A two-mode `MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER` (binary path OR literal value) covers both real authenticator setups (TOTP via a binary) and fixed-OTP sandbox accounts (literal value) without forcing every developer to wire up an authenticator.

**Alternatives considered**:
- *Skip integration tests entirely until MISA documents 2FA-sandbox setup*: rejected — slice-2 integration coverage is required by Constitution Principle VI; skipping it would leave the slice's most consequential path (live 2FA exchange) untested.
- *Mock the OTP at the MISA boundary in integration tests*: rejected — Constitution Principle VI forbids mocks at the MISA HTTP boundary in integration tests. The in-repo fake server is the substitute.

---

## R-9. Re-using `SingleFlightRefresh` for OTP exchange (no new abstraction)

**Decision**: Keep `SingleFlightRefresh` concrete and `internal` to Infrastructure, as it is today. The `ExchangeOtp` use case in Application receives a `Func<string, Func<CancellationToken, Task<AccessToken>>, Task<AccessToken>>` delegate from the DI container (a thin lambda that forwards to `SingleFlightRefresh.RefreshAsync`). This avoids introducing a new Application-layer port purely for slice 2 and keeps the Infrastructure-layer primitive where it already lives. The layer-audit test continues to pass because Application sees only a delegate, not an Infrastructure type.

**Rationale**:
- Constitution Principle III ("ports for collaborators consumers may want to swap") does not justify a new port here: consumers do not legitimately want to swap the single-flight primitive. The delegate approach is the lightest way to thread the behavior into Application without exposing Infrastructure.
- The alternative — extracting `ISingleFlightCoordinator` — would force a port-and-adapter rename across slice 1's `RemoteSigningAuthHandler` and the existing `RefreshTokenSingleFlightTests`. Not worth it for slice 2 alone.

**Alternatives considered**:
- *Extract `ISingleFlightCoordinator` port*: rejected as above.
- *Skip single-flight on `/two-factor-auth`*: rejected — FR-042 explicitly requires it, and concurrent OTP submissions would burn MISA's attempt counter as covered in R-5.

---

## R-10. Behavior when `IOtpProvider` returns a wrong OTP — bounded retry?

**Decision**: One attempt per `SignPdfAsync` invocation. If the provider returns an OTP and `ExchangeOtp` raises a typed rejection (`InvalidOtpException` / `ExpiredOtpException` / `ExhaustedOtpAttemptsException`), the exception propagates to the consumer **without** auto-retrying. The consumer's next `SignPdfAsync` call invokes the provider again. Provider implementations that want bounded retry implement it in `ProvideAsync`.

**Rationale**:
- The SDK has no policy for "how many wrong OTPs are too many" — MISA's `ExhaustedOtpAttemptsException` is the authority on that (per FR-039), and looping client-side would burn attempts and could lock the account. The single-attempt-per-`SignPdfAsync` rule keeps the SDK opinion-free.
- Consumer-side retry is trivially expressible by their existing retry logic around `SignPdfAsync`; we don't need to invent a parallel knob.

**Alternatives considered**:
- *N-attempts in `SignPdf`*: rejected — couples SDK behavior to a policy that consumers must own.
- *Drive a provider-supplied retry budget*: rejected — adds API surface for a corner case better served by consumer-side composition.

---

## R-11. Scope discipline — what slice 2 explicitly does NOT touch

**Decision**: Slice 2 makes **no edits** to: PDF hashing, signing-hash submission, status polling, attachment, certificate listing/selection, the wire-DTO `CertificateDto`, the `ESignLogScrubber`'s log-entry shape (only its scrubbing rules grow), `ITokenCache`'s interface, `ITokenCacheKeySelector`'s default composition, `SystemClock`, `SingleFlightRefresh`'s implementation, the `MisaESignOptionsValidator`'s base validation (it gets a single new rule asserting `Otp.DefaultResendLanguage` is non-empty), or `IMisaESignClient.SignPdfAsync`'s signature.

`SignPdf` (the use case orchestrator) is the **one** existing implementation that gets edited, and the edit is bounded: a `try { ... } catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is not null) { ... }` block that delegates to the new `ExchangeOtp` use case and resumes the operation.

**Rationale**:
- Constitution Principle V (slice-driven) and the spec's "Reuse contracts — no regression" section (FR-041/FR-042/FR-043) both require slice 2 to be additive. The discipline is enforced by the unit tests — slice 1's full test suite continues to pass without modification, and any slice-1 test that does change is flagged in the PR description.

**Alternatives considered**:
- *Refactor `SignPdf` to a state machine that handles 2FA as a state*: rejected — premature abstraction, churns slice-1 code for no gain. The try/catch block is local and reads naturally.
- *Move OTP logic into a `SignPdfWithOtp` use case*: rejected — would split the "sign a PDF" entry-point and force the facade to choose, which is exactly the friction the transparent flow exists to remove.

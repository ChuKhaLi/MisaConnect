# MisaConnect.ESign — back-port of the TypeScript eSign fixes — Design

Date: 2026-11-05 · Status: draft, awaiting owner review · Target: `MisaConnect.ESign` 2.5.0 (minor; behaviour changes)

Sources: [TS esign design §9](2026-11-04-ts-esign-design.md) and the `src/typescript/esign/README.md` section "Differences from the .NET SDK". Scope: items 1–9, the webhook and OTP security rulings, and the eSign docs errors.

Every defect below was re-verified against `main` (`680c0d7`) on 2026-11-05 by reading the code; file:line evidence is in [the verification notes](2026-11-05-esign-dotnet-ts-parity-verification.md). Verification refined several TS-side claims and found five defects the TS list did not name; those are marked **(new)**.

## User scenarios

### User Story 1 - A signing flow survives a token refresh (Priority: P1)

An integrator signs a document. Mid-flow the access token is rejected (401). They expect the SDK to refresh once and carry on. Today the SDK refreshes and retries only the one rejected request; the rest of the flow (certificates, hash, every status poll, attachment) keeps sending the old token, so each later call is rejected and refreshes again. If the refresh itself fails, the call fails with `Authentication` even though logging in again would work, as MISA's documentation instructs.

**Why this priority**: A token expiring during a long signing flow (status polling can last minutes) turns one refresh into a refresh per call, or a hard failure. It affects every integrator.

**Independent Test**: Against the in-repo fake server, expire the token after the certificate call; the flow completes with exactly one refresh and every later request carries the refreshed token. Make the refresh fail; the flow completes after exactly one fresh login.

**Acceptance Scenarios**:

1. **Given** a signing flow in progress, **When** one request gets a 401 and the refresh succeeds, **Then** that request and every later request in the flow use the refreshed token, and no second refresh happens.
2. **Given** a 401 and a refresh that fails (error response, missing refresh token, or an unparseable refresh body **(new)**), **When** the SDK handles it, **Then** it removes the cached token, logs in once more, and retries the request with the new token.
3. **Given** the fresh login also fails or the retried request gets a 401 again, **When** the SDK handles it, **Then** the call fails with `Authentication` and the cached token is removed.
4. **Given** a fresh login that MISA answers with the two-factor code, **When** the fallback runs, **Then** the caller gets the same two-factor signal as on a first login (no new behaviour for OTP).
5. **Given** a cached token with less than 30 seconds left, **When** a call starts, **Then** the SDK refreshes before sending, instead of waiting for expiry or a 401.

---

### User Story 2 - A signer is never asked to approve the same document twice (Priority: P1)

A signer approves a signing request on their phone. If MISA answers the "start signing" request with a server error or the request times out, the SDK today retries it (up to 3 attempts, the same as every other call). MISA may already have started the first transaction, so the retry can start a second one and the signer gets a second approval request.

**Why this priority**: Visible to end users, confusing, and can produce two signed artefacts for one intent.

**Independent Test**: Make the fake server answer "start signing" with 500, then with a connection reset after the request was sent, then with a timeout; each is sent exactly once and surfaces as a transport error. Make it answer 429, then refuse the connection; each is retried.

**Acceptance Scenarios**:

1. **Given** "start signing" gets a 5xx, a timeout, or a connection dropped after the request was sent, **When** the SDK handles it, **Then** it does not retry and fails with `ESignTransportException`.
2. **Given** "start signing" gets a 429, or the connection could not be established at all (refused, host not found), **When** the SDK handles it, **Then** it retries under the existing retry limits.
3. **Given** any other eSign call, **When** it fails transiently, **Then** retry behaviour is unchanged.

---

### User Story 3 - Webhooks cannot forge or crash a signing result (Priority: P1)

An integrator exposes a webhook endpoint that calls `HandleWebhookAsync`. They expect every inbound request, well-formed or not, to get an ACK. They also expect only results MISA actually confirms to reach their delivery hook. Today:
- an unknown status or a null signature entry **(new)** throws out of `HandleWebhookAsync`;
- an auth or certificate error during finalize also throws out of it **(new)**;
- the webhook's own status and signature are trusted without asking MISA;
- FAILED/CANCELLED webhooks are re-delivered to the hook on every repeat **(new)**.

**Why this priority**: Security and robustness. The endpoint is public. A forged or replayed body must not decide the outcome, and a malformed body must not become a 500 that MISA keeps retrying.

**Independent Test**: Feed `HandleWebhookAsync` a matrix of bodies: valid, each field missing or malformed, unknown status, duplicate, and forged SUCCESS for a transaction MISA reports as pending. Each returns an ACK. Only confirmed outcomes reach the hook, each once.

**Acceptance Scenarios**:

1. **Given** a body with a missing or malformed `messageId`, `clientId`, `transactionId`, `status` (unknown value) or `signatures` (wrong shape, null entry), **When** it is handled, **Then** the SDK answers ACK `webhook.malformed` with a `FailureWithError` outcome and throws nothing.
2. **Given** a status in any letter case (`success`, `Success`), **When** it is handled, **Then** it is treated as the canonical status.
3. **Given** a well-formed webhook for a known transaction, **When** it is handled, **Then** the SDK confirms the transaction's state with MISA's status call before acting. The confirmed state decides the outcome, not the webhook body.
4. **Given** MISA's status call says the transaction is still pending, or disagrees with the webhook, **When** it is handled, **Then** nothing is delivered to the hook as success, and the ACK tells MISA the webhook was not accepted.
5. **Given** a transaction whose terminal state (success, failed or cancelled) was already recorded, **When** further webhooks for it arrive, **Then** the hook is not called again, and the SDK returns the ACK it returned the first time.
6. **Given** a validation failure (unknown transaction, clientId mismatch, malformed body), **When** it is handled with default options, **Then** it is delivered to the hook as `FailureWithError`, as today, and also returned to the caller. It is never delivered as a success.
7. **Given** `Webhook.DeliverValidationFailuresToHook = false`, **When** a validation failure is handled, **Then** the hook is not called, and the failure is returned to the caller only (TS ruling R12).
8. **Given** finalize needs a token and login would require two-factor, or any auth or certificate error occurs, **When** the webhook is handled, **Then** the SDK returns a failure ACK and outcome. It never prompts for or waits on an OTP, and it throws nothing.

---

### User Story 4 - Per-user credentials work in webhook mode (Priority: P2)

An integrator serving many signers with per-user credentials (slice 008, Dynamic mode) wants webhook mode too. Today the signing session records only the static `ClientId`. When the webhook arrives there is no per-call credential context, so finalize fails ("ensure the per-call credentials ambient is set"). In Dynamic mode with no static `ClientId`, every webhook fails the clientId check.

**Why this priority**: Slice 008 deferred this explicitly (FR-015). It blocks one supported combination but does not affect static-credential users.

**Independent Test**: In Dynamic mode, begin a webhook signing flow for user A, then deliver the webhook with no ambient credentials. Finalize obtains A's credentials through the integrator-supplied resolver, and the clientId check uses A's clientId.

**Acceptance Scenarios**:

1. **Given** Dynamic credentials, **When** a webhook signing flow begins, **Then** the session records the `clientId` and `userName` it was started with (never the password or client key).
2. **Given** a webhook for that session, **When** finalize runs, **Then** the SDK asks the integrator for the credentials of that recorded `clientId` + `userName`, and validates the webhook's `clientId` against the recorded one.
3. **Given** the integrator cannot supply credentials for the recorded principal, **When** finalize runs, **Then** the webhook gets a failure ACK and outcome, and no exception escapes.
4. **Given** static credentials, **When** webhooks are handled, **Then** behaviour is unchanged.

---

### User Story 5 - Odd MISA responses produce the right error (Priority: P2)

An integrator handling errors by category expects the category to describe what happened. Today:
- an HTML error page (e.g. from a proxy) on any eSign call except the certificate list surfaces as `EmptyResponse`;
- an `error` field sent as the string `"true"` drops the whole error envelope and its code;
- `"error": null` in a login response drops the login response entirely **(new)**;
- the two-factor code `122` is missed when a 2xx login carries a flat error body, and when a non-2xx login carries the nested `{status:{…}}` body.

**Why this priority**: Wrong categories send integrators down the wrong diagnosis path, and a missed `122` means a two-factor user is told their credentials are wrong.

**Independent Test**: Against the fake server, return non-JSON bodies and string or null `error` values on each endpoint, and `122` in both body shapes on 2xx and non-2xx. Check the resulting category or two-factor signal.

**Acceptance Scenarios**:

1. **Given** any eSign (ESRM) JSON endpoint returns a non-JSON content type, **When** the SDK reads it, **Then** it fails with `UnexpectedContentType`. **Given** a JSON content type with an unparseable body, **Then** it fails with `UnparseableResponse`. Either error reports the content type and body length, never the body.
2. **Given** an auth call (login, two-factor, refresh, resend OTP), **When** it returns JSON with an unexpected content type, **Then** it is still parsed (unchanged leniency).
3. **Given** an `error` field of `true`, `false`, `"true"`, `"false"` or `null`, in either the flat or the nested shape, **When** the SDK reads it, **Then** the envelope is kept and its error code is used.
4. **Given** a login response carrying code `122`, **When** it arrives with any HTTP status and in either body shape, **Then** the caller gets the two-factor signal.

---

### User Story 6 - Two-factor challenges stay with the right user (Priority: P3)

An integrator using per-user credentials within one client scope starts sign-ins for two users. Both get a two-factor challenge. Today the client holds one pending username, so the second challenge overwrites the first, and `SignInWithOtpAsync` / `ResendOtpAsync` act on whichever user challenged last.

**Why this priority**: Only hits integrators who mix several users in one scope; static-credential users have one user.

**Independent Test**: In one scope, trigger challenges for users A and B, then complete A's OTP with A's credentials current; A is signed in, and B's challenge is untouched.

**Acceptance Scenarios**:

1. **Given** pending challenges for two principals (clientId + userName), **When** `SignInWithOtpAsync` or `ResendOtpAsync` runs with one principal's credentials current, **Then** it acts on that principal's challenge only.
2. **Given** no pending challenge for the current principal, **When** either method runs, **Then** it fails with the same error as today when nothing is pending.

---

### User Story 7 - eSign docs compile and match the code (Priority: P3)

A new integrator copies the getting-started sample. Today it fails in three ways:
- it imports an internal namespace (`MisaConnect.ESign.Infrastructure.DependencyInjection`), so it does not compile;
- it passes `LogoImageBase64: null`, which the validator rejects;
- it advertises `SignXmlAsync` string/bytes overloads that do not exist.

The configuration page says URLs must be https-only, but loopback http is allowed. It says adapters can be registered "before or after" in general, but that holds only for some adapters. The sandbox page claims a reachability probe that does not exist.

**Why this priority**: Docs only, but they are the first thing an integrator runs.

**Independent Test**: The getting-started sample compiles and runs against the fake server; every claim on the three pages matches the code.

**Acceptance Scenarios**:

1. **Given** `docs/esign/getting-started.md`, **When** its sample is compiled, **Then** it uses only public namespaces, passes a valid logo, and lists only facade methods that exist.
2. **Given** `docs/esign/configuration.md`, **When** read, **Then** it states the real URL rule (https, plus http for loopback hosts) and which adapters may be registered after `AddMisaConnectESign`.
3. **Given** `docs/esign/sandbox-setup.md`, **When** read, **Then** it states that sandbox tests skip on missing env vars and on NoActiveCertificate (PR #6), with no reachability-probe claim.

### Edge Cases

- A 401 on a status poll late in a long flow → one refresh; later polls use the new token.
- Two concurrent flows in one scope both hit a 401 → still one refresh (single-flight refresh exists today; must keep working with the shared current token).
- A refresh succeeds but returns a token that already expired → treated as a failed refresh, so fall back to login once.
- "Start signing" fails with a connection reset: is it ambiguous whether MISA received it? Yes, so it is treated as "may have reached MISA" and not retried.
- A webhook arrives before the SDK has stored the session (race with begin) → unknown transaction ACK, unchanged; MISA's retry will find it.
- A SUCCESS webhook arrives after a FAILED one was recorded → no delivery; the first recorded terminal state wins.
- A webhook whose `signatures` is present but empty for a SUCCESS → `webhook.incomplete_success` (existing ACK), not malformed.
- A token with exactly 30 s left → refreshed proactively (the boundary is inclusive).
- Status confirmation call itself fails transiently → normal retry rules for status; if it still fails, failure ACK so MISA retries the webhook later; nothing delivered.

## Requirements


**Token lifecycle**

- **FR-001**: After a successful refresh triggered by a 401, every later request in the same flow MUST use the refreshed token, and MUST NOT trigger another refresh because of the old token.
- **FR-002**: When a refresh after a 401 fails for any reason (error response, no refresh token, unparseable or already-expired result), the SDK MUST remove the cached token, log in once more, and retry the request once. Only if that fails MUST it fail with `Authentication`.
- **FR-003**: The SDK MUST refresh a cached token proactively when 30 seconds or less remain before expiry. The unused skew declaration MUST either drive this or be removed.
- **FR-004**: Any refresh-path failure MUST leave no stale entry in the token cache, including non-SDK exceptions.

**Retry safety**

- **FR-005**: The "start signing" request (`POST Signing/hash`) MUST NOT be retried after a 5xx, a timeout, or a failure after the request may have been sent. It MUST be retried only on 429 or when the connection was never established. All other calls MUST keep today's retry rules.

**Response handling**

- **FR-006**: Every ESRM JSON response MUST go through the content-type guard: `UnexpectedContentType` for a non-JSON content type, `UnparseableResponse` for an unparseable JSON body. Errors MUST report content type and body length, never body content. Auth calls MUST keep parsing JSON whatever the content type.
- **FR-007**: The `error` field MUST accept boolean, `"true"`/`"false"` strings (any case) and null in both the flat and the nested login shape, without losing the envelope.
- **FR-008**: The two-factor code `122` MUST be recognised on any HTTP status that reaches login error mapping, in both the nested and the flat body shape.

**Webhooks**

- **FR-009**: `HandleWebhookAsync` MUST NOT throw for any inbound body or any finalize failure; it MUST always return an ACK and an outcome. (Caller cancellation is the only exception.)
- **FR-010**: Malformed input MUST produce ACK `webhook.malformed` with a `FailureWithError` outcome. That covers a missing or empty `messageId`, `clientId` or `transactionId`, an unknown `status`, and a `signatures` value of the wrong shape or with a null entry. Status matching MUST be case-insensitive.
- **FR-011**: Before delivering any outcome to the hook, the SDK MUST confirm the transaction state with MISA's status call; the confirmed state, not the webhook body, decides the outcome.
- **FR-012**: The first recorded terminal state (success, failed or cancelled) MUST be stored once per transaction. Later webhooks for that transaction MUST NOT call the hook again, and MUST return the originally returned ACK.
- **FR-013**: Validation failures (malformed body, unknown transaction, clientId or document mismatch) MUST NOT be delivered to the hook as success. A new option `MisaESignOptions.Webhook.DeliverValidationFailuresToHook` MUST control whether they reach the hook as `FailureWithError`. It defaults to `true` (today's behaviour) in 2.x; `false` gives TS ruling R12 (caller only). The CHANGELOG and `docs/esign/configuration.md` MUST say the default becomes `false` in 3.0, and why (a public endpoint lets anyone trigger hook calls). Outcomes delivered as success MUST be MISA-confirmed (FR-011).
- **FR-014**: Finalize MUST NOT prompt for or wait on an OTP. A two-factor requirement or any auth or certificate error during finalize MUST become a failure ACK and outcome.

**Per-user credentials in webhook mode**

- **FR-015**: In Dynamic credentials mode, a webhook signing session MUST record the `clientId` and `userName` it was started with, and MUST NOT record secrets.
- **FR-016**: When a webhook for such a session arrives, the SDK MUST obtain that principal's credentials through an integrator-supplied resolver and MUST validate the webhook's `clientId` against the recorded one. Resolver failure MUST become a failure ACK and outcome. Static mode MUST be unchanged.

**Two-factor challenges**

- **FR-017**: Pending two-factor challenges MUST be tracked per principal (`clientId` + `userName`). `SignInWithOtpAsync` and `ResendOtpAsync` MUST act on the current principal's challenge only.

**Docs, release and compatibility**

- **FR-018**: `docs/esign/getting-started.md`, `configuration.md` and `sandbox-setup.md` MUST be corrected as in User Story 7.
- **FR-019**: The changes MUST ship as a minor release of `MisaConnect.ESign` with a CHANGELOG entry. It MUST list each behaviour change (retry, categories, webhook ACKs and delivery, OTP scoping) under a "Behaviour changes" heading. Public-surface additions (e.g. the resolver) MUST be additive; no existing public member is removed or changes signature.
- **FR-020**: The stale `<Version>` in the ESign Client project file MUST be removed or aligned, so the tag-derived version is the single source.
- **FR-021**: Logging rules MUST hold: no tokens, secrets, OTPs, passwords or document content in logs or exception messages, including the new ACK and resolver paths.
- **FR-022**: Each FR MUST be covered by unit tests and, where it involves HTTP, by fake-server integration tests. The TS package's behaviour MUST remain the reference: where both SDKs implement a rule, they MUST agree.

### Key Entities

- **Signing session**: a webhook-mode pending transaction. It gains the principal (`clientId`, `userName`) and the recorded terminal state with its ACK; it still never holds secrets.
- **Pending two-factor challenge**: keyed by principal instead of one slot per client.
- **Credentials resolver**: integrator-supplied lookup from a recorded principal to credentials, used only on the webhook path in Dynamic mode.

## Success criteria

- **SC-001**: A signing flow whose token expires mid-flow completes with exactly 1 refresh (or exactly 1 fallback login), versus 1 refresh per remaining call today.
- **SC-002**: In fault-injection tests, "start signing" is sent at most once for every 5xx, timeout or post-send failure, so 0 duplicate signer approvals.
- **SC-003**: 100% of the malformed and forged webhook test matrix returns an ACK without an exception escaping. 0 unconfirmed successes and 0 duplicate deliveries reach the hook.
- **SC-004**: A Dynamic-mode webhook flow completes end to end against the fake server with no ambient credentials at webhook time.
- **SC-005**: Every non-JSON or odd-envelope fault-injection case yields the documented category (no `EmptyResponse` where `UnexpectedContentType`, `UnparseableResponse` or a MISA code applies). Code `122` is detected in 4 of 4 status/shape combinations.
- **SC-006**: The getting-started sample compiles unchanged against the released package.
- **SC-007**: Unit tests stay under 30 s total; existing ESign unit and integration suites stay green, except tests updated for a behaviour change listed in the CHANGELOG.

## Decisions and assumptions

- **Owner decision (2026-11-05), FR-013:** no silent change in a minor release. Validation failures keep reaching the hook by default; `Webhook.DeliverValidationFailuresToHook = false` opts in to TS ruling R12, and the default flips in 3.0.

- The TS rulings (design §9, README "Differences from the .NET SDK", R10–R12) are accepted decisions; this slice ports them rather than re-deciding them.
- The release is `MisaConnect.ESign` 2.5.0. `MisaConnect.EInvoice` has no code changes, and the shared tag/release flow is unchanged.
- "Connection never established" means the client could not open the connection (refused, DNS failure). A reset or timeout after connect counts as "may have reached MISA".
- MISA's status call is the authoritative source for transaction state; one extra status call per webhook is acceptable.
- Token fingerprint binding (TS `tokenFingerprintSecret`) is out of scope. .NET token cache keys already include the principal, and multi-host cache sharing is not a .NET scenario today.
- Live verification against the eSign sandbox still needs an ACTIVE certificate (owner action, deferred). This slice is verified against the in-repo fake server.

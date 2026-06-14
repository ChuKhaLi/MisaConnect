# Feature Specification: Per-User Credentials Seam for MisaConnect.ESign

**Feature Branch**: `008-esign-per-user-credentials`
**Created**: 2026-06-15
**Status**: Draft
**Input**: Change request [`REQUEST.md`](./REQUEST.md) (in this slice), from the `elims-backend` consumer of `MisaConnect.ESign.Client`.

## Summary

`MisaConnect.ESign` today signs through a single shared organisation service account: the four MISA credentials (`ClientId`, `ClientKey`, `UserName`, `Password`) are read from static options at startup and used for every call. A consumer (ELIMS) is moving to a **per-person** model where each signer carries their own MISA credential set, stored encrypted and decrypted only for the duration of one signing call — fully unattended, with no per-sign password or OTP prompt.

This slice adds a **per-call credentials seam** so a consumer can supply MISA credentials for the current call instead of from static options, without changing any existing signing method. It introduces a consumer-swappable credentials accessor port, a record carrying one signer's credential set, and a `CredentialsMode` switch that relaxes the startup validator when credentials are supplied dynamically. The default path — no accessor registered, mode unset — is **byte-identical** to 2.1.1.

The change mirrors the SDK's existing, proven ambient-input pattern (the certificate selector + cert-selection `AsyncLocal`) so a consumer's per-call ambient value flows through the awaited signing pipeline into the login request, the outbound request headers, and the token-cache key consistently.

### Scope at a glance

| Area | In scope (v1) | Deferred |
|---|---|---|
| Synchronous-poll sign path (sign + list certs + token acquisition/refresh) | ✅ | |
| Per-user token-cache isolation (no cross-user token bleed) | ✅ | |
| OTP relink path consistency (rides the same seam) | ✅ | |
| Webhook / begin-sign async path (`clientId` correlation + anti-spoof match) | | ⏸ Deferred — consumer v1 is sync-poll only |
| Splitting credentials into separate app vs. user modes | | ⏸ Out of scope — all four treated as per-user |
| Consumer-side credential storage / encryption | | ⏸ Out of scope — consumer concern |

## Clarifications

### Session 2026-06-15

- Q: Per-call credential resolution contract in Dynamic mode (multiple reads vs. resolve-once-and-cache)? → A: Per-call reads, no SDK-side caching — the SDK reads the accessor at each point (login, headers, cache key) and relies on the consumer's ambient value being stable for the call's duration.
- Q: Wire the per-user seam through the OTP relink path (exchange/resend) in this slice, or defer with the webhook path? → A: In scope for consistency — relink rides the same header + token-cache-key resolution with no new public surface, and the slice adds coverage for it.
- Q: How to handle a consumer registering a second credentials accessor *after* `AddMisaConnectESign` (the unsupported order)? → A: Document register-before as the only supported path (correct the misleading "or just after" wording); no runtime guard is added.
- Q: Add any Dynamic-mode diagnostics or sanity validation beyond skipping the four credential checks? → A: No — keep the change minimal; the options debug view and the rest of the validator are unchanged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Supply MISA credentials per signing call (Priority: P1)

A consumer with per-person MISA credentials configures the SDK in "dynamic credentials" mode and supplies the current signer's credential set around each awaited SDK call. The SDK uses those credentials — not the static options — for the login request that mints the token, for the `x-clientId`/`x-clientKey` headers on every outbound MISA request, and for the token-cache key, all resolved per-call inside the signing pipeline. Signing is unattended: no per-call secret entry.

**Why this priority**: This is the entire purpose of the feature. Without it the consumer cannot move from a single shared org account to per-person credentials, which is the blocking requirement.

**Independent Test**: Register a custom credentials accessor (dynamic mode) against the in-repo fake MISA server, drive a signing call while supplying credentials for the current call, and assert the login body, the outbound credential headers, and the token-cache key all reflect the supplied credentials rather than the static options.

**Acceptance Scenarios**:

1. **Given** dynamic mode and a consumer-supplied credential set for the current call, **When** the consumer signs a document, **Then** the login request, the outbound `x-clientId`/`x-clientKey` headers, and the token-cache key all use the supplied credentials.
2. **Given** two different signers' credential sets supplied across two separate calls, **When** each signs, **Then** two distinct logins are issued, two distinct credential-header sets are sent, and two distinct token-cache keys are composed — with no collision.
3. **Given** dynamic mode but the four static credential options left empty, **When** the host starts, **Then** startup validation succeeds (the four credential checks are skipped) while every other validation still runs.

---

### User Story 2 - Existing single-account consumers are unaffected (Priority: P1)

A consumer using the current single shared-account configuration changes nothing — no new option set, no accessor registered. The SDK behaves exactly as it did in 2.1.1: the four credentials are read from static options, the startup validator still requires them, and the header values, login body, and token-cache key are identical.

**Why this priority**: This is a MINOR, additive release. A behaviour change for existing consumers would be a breaking regression and is non-negotiable to avoid.

**Independent Test**: Run the existing single-account end-to-end suite against the fake MISA server with no source changes to the test fixtures; assert it passes unmodified, and that the default credentials path produces the same header values, login body, and cache-key string as before.

**Acceptance Scenarios**:

1. **Given** no credentials accessor registered and `CredentialsMode` unset, **When** any sign/list/token operation runs, **Then** the credentials, headers, login body, and token-cache key are exactly as in 2.1.1.
2. **Given** existing app settings that do not mention `CredentialsMode`, **When** options are bound, **Then** the mode resolves to `Static` and the four credentials are still required at startup.
3. **Given** the existing unit and integration test suites, **When** they run against this change, **Then** they all pass without modification to their fixtures or assertions.

---

### User Story 3 - Per-user token isolation with loud failure on missing credentials (Priority: P2)

When multiple signers share a process and a shared (e.g. distributed) token cache, each signer's token must be stored and retrieved under a key unique to that signer, so one signer can never receive another's token. If the credentials for the current call are missing or empty, the SDK fails loudly at the point of use rather than silently collapsing identities into one cache slot.

**Why this priority**: Cross-user token bleed is the exact failure this feature exists to prevent; a silent collapse would hand one staff member another's signing authority. The guarantee must be defensive by construction, not dependent on the consumer getting it right.

**Independent Test**: Compose the token-cache key for two distinct credential sets and assert the keys differ; then resolve an empty username (and, separately, an empty client id) and assert the key composition raises a clear failure instead of producing a collapsed key.

**Acceptance Scenarios**:

1. **Given** two signers with different usernames/client ids, **When** their token-cache keys are composed, **Then** the two keys are different (no collision).
2. **Given** the resolved username or client id is null/empty, **When** the token-cache key is composed, **Then** the SDK raises a clear error at the call site and composes no key.
3. **Given** a single supplied credential set for one logical call, **When** the login, headers, and cache key are resolved, **Then** all three reflect the same credential snapshot (the token is written under a key matching the credentials it was minted from).

---

### User Story 4 - Credentials never leak into logs or strings (Priority: P3)

The credential set carries two secret-bearing values (`ClientKey`, `Password`). Whenever a credential set is rendered as text — structured logging, an exception context capture, or string interpolation — the two secrets are redacted, and the SDK never logs the resolved credential values or the credential headers.

**Why this priority**: A leaked secret undermines the whole per-person trust model and violates the project's no-secrets-in-logs principle. It is a guardrail rather than the headline capability, hence P3, but it is a hard requirement.

**Independent Test**: Render a credential set containing known secret values to its string form and assert neither secret value appears (both show as redacted) while the non-secret identifiers remain visible; inspect the SDK's logging on the sign path and assert no credential value or credential header value is emitted.

**Acceptance Scenarios**:

1. **Given** a credential set with secret `ClientKey` and `Password` values, **When** it is converted to a string, **Then** the output contains neither secret value (both redacted) but still shows the username and client id.
2. **Given** any sign/login/refresh call, **When** the SDK logs, **Then** no credential value and no credential header value appears in the output.

---

### Edge Cases

- **Missing ambient in dynamic mode** (consumer forgot to supply credentials for the call): surfaces as a loud failure at the point the token-cache key or credentials are needed — never a collapsed cache key or a silent wrong-identity token.
- **Empty credentials in static mode under a misconfiguration**: the token-cache key composition still fails loudly rather than collapsing.
- **Multiple credential reads per logical call** (the SDK reads the current credentials in the login, the headers, and the cache key): all reads within one call must return the same snapshot; a torn read that mixes two signers' values must not occur.
- **Override registration ordering**: registering a consumer accessor *after* the SDK's own registration is unsupported and leaves the options-default accessor constructible (a second descriptor). The SDK does not detect or reject this at runtime; register-before is documented as the only supported path so exactly one accessor is in effect.
- **Relink (OTP) path**: when a consumer supplies credentials around the OTP exchange/resend, the resulting token lands under the correct per-user key and the outbound credential headers match — consistent with the sign path.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The SDK MUST expose a consumer-swappable credentials accessor port that resolves the MISA credential set for the current call, and a public credential record carrying one signer's `ClientId`, `ClientKey`, `UserName`, and `Password`.
- **FR-002**: The SDK MUST provide a default credentials accessor that reads the four credentials from the existing options, registered so that a consumer pre-registering its own accessor wins and a consumer that registers nothing gets the default.
- **FR-003**: The credentials accessor MUST be consulted **per-call inside the awaited signing pipeline** at every credential consumer on the synchronous-poll path — the login request, the outbound request headers (`x-clientId`/`x-clientKey`), and the token-cache key — so a consumer's per-call ambient value flows in. No credential value may be captured eagerly into a singleton snapshot.
- **FR-004**: The SDK MUST read credentials per-call at each consumer (login, headers, token-cache key) and MUST NOT introduce per-call or scoped caching of the resolved value. Mutual consistency across the login body, headers, and token-cache key (no torn read across signers) relies on the consumer returning a **stable** credential value for the duration of one awaited call; the SDK does not resolve-once-and-cache.
- **FR-005**: The SDK MUST expose a `CredentialsMode` option with values `Static` (default) and `Dynamic`, bound automatically from the existing configuration section, where an unset/absent value resolves to `Static`.
- **FR-006**: In `Static` mode the startup validator MUST continue to require all four credentials. In `Dynamic` mode the validator MUST skip **only** the four credential checks; every other validation (base URL, environment/host coupling, polling, transport retry, OTP, webhook) MUST still run in both modes.
- **FR-007**: The token-cache key MUST incorporate the per-call username and client id (alongside the host) so two signers never share a cache slot, and the same per-call snapshot used to mint the token MUST be used to compose the key it is stored under.
- **FR-008**: The token-cache key composition MUST fail loudly (raise a clear error at the call site) when the resolved username or client id is null/empty, rather than composing a collapsed key — making the SDK defensive by construction regardless of consumer correctness.
- **FR-009**: The credential record's string representation MUST redact the two secret-bearing values (`ClientKey`, `Password`) while still surfacing the non-secret identifiers, so structured logging, exception capture, or interpolation cannot leak the secrets.
- **FR-010**: The SDK MUST NOT log the resolved credential values or the credential header values on any call path.
- **FR-011**: Adopting the feature MUST require no change to the public signing/list/OTP method signatures; the seam is the accessor port plus the mode option only. Credentials MUST NOT be threaded as new parameters on existing entry points.
- **FR-012**: The default path — no accessor registered and `CredentialsMode` unset — MUST be byte-identical to 2.1.1 for the credential headers, the login body, the token-cache key string, and the validator errors; all existing unit and integration tests MUST pass without fixture changes.
- **FR-013**: A consumer accessor override MUST be safe to register as a singleton resolving from ambient/options state read per-call; the documented and supported override is register-before the SDK's own registration. The accessor MUST NOT require scoped registration (which would be a captive-dependency failure). The SDK MUST NOT add a runtime guard against a mis-registered (register-after) accessor; the register-before requirement is enforced by documentation only, and the documentation MUST correct the misleading "or just after" wording for this secret-bearing port.
- **FR-014**: The OTP relink path (exchange/resend) MUST stay consistent with the sign path — when a consumer supplies credentials around those calls, the per-user token-cache key and the outbound credential headers reflect them — without adding new public surface to those operations.
- **FR-015**: The webhook / begin-sign `clientId` usages (session-correlation tag and inbound anti-spoof match value, not outbound credentials) are **deferred**; they continue to read the static option. This deferral, and the caveat that per-user webhook sessions would later need per-session handling, MUST be documented.
- **FR-016**: This is a public-surface addition for the `MisaConnect.ESign` 2.x family and MUST ship as a MINOR version bump (target `2.2.0`) with a `CHANGELOG` `[Unreleased]` entry, a slice public-surface contract, and updated configuration/README documentation — including the register-before override-ordering guidance and the accessor lifetime contract.

### Key Entities *(include if feature involves data)*

- **Credential set (`MisaCredentials`)**: One signer's full MISA credential set — `ClientId`, `ClientKey` (secret), `UserName`, `Password` (secret). A consumer-extension contract type whose string form redacts the two secrets. Drives header injection, the login body, and the token-cache key from a single per-call resolution.
- **Credentials accessor port (`IMisaCredentialsAccessor`)**: Resolves the credential set for the current call. Default reads the static options; a consumer override reads its own per-call ambient value. Read per-call deep in the pipeline.
- **`CredentialsMode`**: `Static` (read the four credentials from options; validator requires them) or `Dynamic` (credentials supplied per-call; validator does not require the four static values). Default `Static`. Governs only the startup validator — dynamic behaviour is achieved purely by the consumer's accessor, never by any use-case branching on the mode.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With dynamic credentials supplied for two different signers, the SDK issues two distinct logins, sends two distinct credential-header sets, and composes two distinct token-cache keys — zero collisions across signers, verified by tests.
- **SC-002**: The default (static, no accessor) path is byte-identical to 2.1.1: 100% of existing `MisaConnect.ESign` unit and integration tests pass without modification, and the credential headers, login body, cache-key string, and validator errors are unchanged.
- **SC-003**: A missing/empty credential produces a clear, loud failure at the point of use in 100% of cases, with zero occurrences of a collapsed token-cache key or a wrong-identity token.
- **SC-004**: No credential value and no credential header value appears in any log output or string representation across the sign/login/refresh paths — the credential record's string form shows the two secrets as redacted in 100% of renderings.
- **SC-005**: In dynamic mode the host starts successfully with the four static credentials empty, while every non-credential validation still rejects an invalid configuration.
- **SC-006**: Adopting the feature requires zero change to existing public signing/list/OTP method signatures, and the change ships as `MisaConnect.ESign 2.2.0` with the changelog, public-surface contract, and documentation deliverables in place.

## Assumptions

- The consumer supplies a **stable** credential value for the duration of one awaited call (an ambient value that does not change mid-call). The SDK may read the current credentials more than once per logical call (login, headers, cache key) and relies on each read returning the same snapshot; resolving once and caching within a singleton is explicitly not done.
- Credential storage, encryption, and per-sign decryption are entirely a consumer-side concern. The SDK only consumes plaintext credentials via the accessor per-call and must not cache them.
- The OTP relink path is in scope for **consistency only** (it rides the same per-call header/cache-key resolution); it is a lower-priority path for the requesting consumer and adds no new public surface.
- The credential set keeps all four values on a single record and `CredentialsMode` is intentionally coarse (dynamic relaxes all four together). A split app-dimension (`ClientId`/`ClientKey`) vs. user-dimension (`UserName`/`Password`) mode is out of scope; the requesting consumer treats all four as per-user.
- Optional diagnostics (e.g. surfacing `CredentialsMode` in the options debug view) and any extra Dynamic-mode sanity validation are **not added in this slice**; the options debug view and the rest of the validator are unchanged apart from gating the four credential checks. No new secret-bearing configuration key is introduced, so the redaction map is unchanged.
- The existing certificate-selector ambient pattern proves an `AsyncLocal` set by the consumer flows through the SDK's internal DI child scopes into a singleton collaborator read deep in the pipeline; the credentials seam relies on the same mechanism.

## Out of Scope

- Per-user `clientId` on the webhook / begin-sign async path (session-correlation tag and anti-spoof match value) — deferred; these continue to read the static option.
- Changing the public `Sign*` / list-certificates / OTP entry-point signatures to take credentials — rejected in favour of the accessor + ambient seam.
- A new secret store or encryption mechanism — a consumer concern; the SDK only consumes plaintext per-call.
- A split app-vs-user credentials mode — the mode is coarse by design (all four credentials together).
- New document formats or operations — the existing signing surface is unchanged.

## Dependencies

- Change request: [`REQUEST.md`](./REQUEST.md) (this slice).
- Existing consumer-extension precedent in the SDK: the certificate selector port + cert-selection ambient, the token-cache-key selector port + its options-reading default, and the correlation-id accessor port — the new credentials seam follows the same shape.
- Project constitution principles: II (small/stable public surface, semver-appropriate bump), III (port-and-adapter extensibility), and VIII (logs never leak secrets).

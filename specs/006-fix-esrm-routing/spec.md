# Feature Specification: Fix ESRM Routing & Silent-Failure Hardening

**Feature Branch**: `006-fix-esrm-routing`
**Created**: 2026-06-13
**Status**: Draft
**Input**: Verified bug report [`bug-report.md`](./bug-report.md) (in this slice), cross-checked against source and `docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md`.

## Summary

`MisaConnect.ESign` cannot see certificates or sign documents when the consumer's configured base URL carries a path segment (e.g. `…/webdev/`). The SDK assumes a single base URL serves both the MISA auth app (hosted under `/webdev/`) and the ESRM signing microservices (hosted at the host root). It cannot: ESRM requests are sent under `/webdev/external/esrm/…`, where MISA returns the single-page-app HTML (`200 text/html`) instead of JSON. That malformed-but-successful response is silently swallowed into an empty certificate list, so a valid **ACTIVE** organisation certificate is reported as *"no active certificate."*

This slice makes the SDK compose endpoint URLs correctly per MISA's actual topology, and makes a non-JSON success response fail loudly instead of masquerading as a business state.

### Verified defect status (from the bug report)

| # | Defect | Status | In scope |
|---|--------|--------|----------|
| A | ESRM calls resolve under `/webdev/` instead of the host root → SPA HTML | **Confirmed** | ✅ |
| C | `refreshtoken` / `resend-otp` double-prefix to `/webdev/webdev/…` under a `/webdev/` base | **Confirmed (latent)** | ✅ |
| D | A `2xx` non-JSON body is swallowed into an empty result → false "no active certificate" | **Confirmed** | ✅ |
| B | Wrong bearer token (`accessToken` vs `remoteSigningAccessToken`) | **Refuted** — SDK already sends the remote-signing token | ❌ (no change) |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Certificates and signing work regardless of base-URL path (Priority: P1)

A consumer of `MisaConnect.ESign` has configured the SDK with a MISA-issued base URL and holds a valid ACTIVE organisation certificate. They list certificates and sign a document. Today this returns zero certificates and signing never starts. After the fix, the SDK reaches the ESRM service at the host root and returns the certificate, regardless of whether the configured base URL is the bare host or includes a `/webdev/` (or other) path.

**Why this priority**: This is the defect that blocks the entire remote-signing flow. Without it the package is unusable against the affected environment.

**Independent Test**: Configure the SDK against a fake/sandbox server that serves ESRM only at the host root; assert the certificate list is non-empty and signing proceeds, using a base URL value that includes a `/webdev/` path segment.

**Acceptance Scenarios**:

1. **Given** a base URL of `https://host/webdev/` and an ACTIVE certificate, **When** the consumer lists certificates, **Then** the ESRM request is issued to `https://host/external/esrm/…` and the certificate is returned.
2. **Given** a base URL of the bare host `https://host/` and the same account, **When** the consumer lists certificates, **Then** the result is identical to scenario 1 (path-tolerant, zero config change).
3. **Given** an expired remote-signing token, **When** an ESRM call returns `401`, **Then** the existing refresh-and-retry flow still succeeds against the corrected routes.

---

### User Story 2 - A non-JSON success fails loudly, not silently (Priority: P2)

When MISA returns a successful HTTP status but a non-JSON body (e.g. the SPA `text/html` from a mis-routed request), the consumer must receive a clear, diagnosable error rather than a misleading "no active certificate."

**Why this priority**: This is the failure mode that turned a routing bug into hours of misdiagnosis. It is hardening that protects against this and future routing/gateway regressions.

**Independent Test**: Point the SDK at a fake server that returns `200 text/html` for an ESRM endpoint; assert a clear SDK exception naming the endpoint and content type is raised — not an empty list / "no active certificate."

**Acceptance Scenarios**:

1. **Given** an ESRM endpoint that responds `200 text/html`, **When** the consumer lists certificates, **Then** the SDK raises a clear error identifying the endpoint and the response content type.
2. **Given** an ESRM endpoint that legitimately responds `200` with an empty JSON array `[]`, **When** the consumer lists certificates, **Then** the SDK reports "no active certificate" (the existing business outcome), distinct from the error in scenario 1.
3. **Given** any such error, **When** it is produced, **Then** it contains no secret material (no bearer token, no credentials).

---

### User Story 3 - Target production or sandbox auth topology by configuration (Priority: P2)

The MISA auth endpoints differ between environments: production serves login/two-factor at the host root (per the official doc) while the sandbox serves them under `/webdev/`. A consumer can select the correct behaviour without code changes, defaulting from the environment they already declare, with an explicit override available.

**Why this priority**: Required so a single package build works against both production and sandbox, and so the residual production-topology risk can be corrected by configuration rather than a code change.

**Independent Test**: Resolve login/two-factor URLs for `Environment = Production` (root) and `Environment = Sandbox` (`/webdev/`), and with the override set both ways; assert only login/two-factor location changes while all other endpoints are unchanged.

**Acceptance Scenarios**:

1. **Given** `Environment = Production` and no override, **When** the SDK logs in, **Then** login/two-factor resolve at the host root (`/api/auth/…`).
2. **Given** `Environment = Sandbox` and no override, **When** the SDK logs in, **Then** login/two-factor resolve under `/webdev/api/auth/…`.
3. **Given** the override set to "login under webdev", **When** the SDK logs in against a Production environment, **Then** login/two-factor resolve under `/webdev/` (override wins), and refresh/resend/ESRM are unaffected.

---

### Edge Cases

- Base URL ends with `/webdev/`, the bare host, or an unrelated extra path → all collapse to the same origin for request composition.
- ESRM success response with `Content-Type: application/json` but an empty array `[]` → legitimate empty result, not an error.
- ESRM success response with a JSON content type but an unparseable body → treated as a content/transport error, not an empty result.
- `refreshtoken` / `resend-otp` under any accepted base value → resolve to exactly one `/webdev/` segment.
- Override conflicts with environment default → override always wins.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: ESRM requests (certificate list, document hash, signing hash, signing status, attachment) MUST be issued to the host root, independent of any path contained in the configured base URL.
- **FR-002**: The configured base URL MUST be normalized to its origin (scheme + host) for request composition; values with or without a `/webdev/` (or other) path MUST produce identical behaviour, requiring no consumer configuration change.
- **FR-003**: `refreshtoken` and `resend-otp` requests MUST resolve with exactly one `/webdev/` segment (no doubling) for every accepted base-URL value.
- **FR-004**: `login` and `two-factor` requests MUST resolve at the host root by default when the declared environment is Production, and under `/webdev/` when the declared environment is Sandbox.
- **FR-005**: A new optional public configuration setting (`Misa:ESign:AuthUnderWebdev`, a nullable boolean) MUST override the environment-derived login/two-factor location — `true` forces `/webdev/`, `false` forces the root, and unset derives from the environment.
- **FR-006**: On an HTTP success (`2xx`) whose body is not valid JSON for an ESRM JSON endpoint (non-JSON content type or unparseable body), the SDK MUST raise a clear error identifying the endpoint, the response content type, and a short body snippet, and MUST NOT report the condition as an empty result or "no active certificate."
- **FR-007**: Any error message or log produced by this feature MUST NOT contain secrets or tokens (in particular the `AuthorizationRM` bearer value or credentials).
- **FR-008**: A legitimately empty certificate result (empty JSON array) MUST continue to surface as the existing "no active certificate" outcome, distinct from the content/transport error in FR-006.
- **FR-009**: The authenticated bearer for ESRM calls MUST remain the remote-signing access token; no token-selection change is in scope (verified already correct).
- **FR-010**: Existing HTTP error-status handling and the `401` refresh-and-retry flow MUST continue to work unchanged against the corrected routes.
- **FR-011**: The new configuration option is a public-surface addition for the `MisaConnect.ESign` 2.x family and MUST be released as a minor version bump (target `2.1.0`), with a `CHANGELOG` `[Unreleased]` entry and updated README configuration/supported-operations documentation (base URL = host root, path tolerated; document `AuthUnderWebdev`).

### Key Concepts

- **Auth-app endpoints**: `login`, `two-factor`, `refreshtoken`, `resend-otp` — hosted by the MISA web app. `refreshtoken`/`resend-otp` always under `/webdev/`; `login`/`two-factor` location is environment-dependent (see FR-004/FR-005).
- **ESRM endpoints**: certificate list, hash, signing, status, attachment — hosted at the host root under `external/esrm/…`.
- **`AuthUnderWebdev` option**: nullable boolean overriding the login/two-factor location; default unset derives from `Environment`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a valid ACTIVE organisation certificate, the SDK returns at least one certificate where it previously returned zero, in both the production and sandbox topologies.
- **SC-002**: Two SDK configurations — one whose base URL includes a `/webdev/` path and one with the bare host — produce identical successful certificate-list and signing results; adopting the fix requires zero consumer configuration change.
- **SC-003**: A `2xx` non-JSON response on an ESRM endpoint produces a clear, endpoint-named error in 100% of cases, with zero occurrences of a silent empty result for that condition.
- **SC-004**: Switching the declared environment between Production and Sandbox (or setting the override) changes only the login/two-factor location; all other endpoints' resolved URLs are unchanged — verified by tests asserting resolved absolute URLs.
- **SC-005**: All pre-existing `MisaConnect.ESign` unit and integration tests pass, and new tests cover each of the four previously-masked defects (A, C, D, and the bearer-token assertion for B).

## Assumptions

- The official API reference doc is the source of truth for the production topology; the sandbox differs only in that login/two-factor are served under `/webdev/`.
- Production login/two-factor are served at the host root per the doc. **Residual risk**: the doc is internally inconsistent and reproduction evidence exists only for the sandbox; production login may also live under `/webdev/`. Mitigated by the `AuthUnderWebdev` override and a pre-release verification against the production endpoint.
- The remote-signing bearer token is already selected correctly (Defect B verified non-issue); no token change is performed.
- The consumer's existing base-URL value remains valid input (path tolerated); no breaking configuration change is introduced.

## Out of Scope

- Token-selection changes (Defect B) — verified already correct.
- Removing the unused raw-access-token field — optional cleanup, deferred.
- Independent live re-verification of the production topology within this slice (tracked as a pre-release check; see Assumptions).

## Dependencies

- Verified bug report: [`bug-report.md`](./bug-report.md) (this slice).
- Official API reference: `docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md`.

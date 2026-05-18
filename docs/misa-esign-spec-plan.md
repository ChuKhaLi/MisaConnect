# MISA eSign API — Spec Decomposition Plan

This document tracks how the MISA eSign RemoteSigning v2 API integration is split into independent feature specs for `MisaConnect.ESign` (v2.0 product family, sibling to the v1.0 `MisaConnect.EInvoice` package). Each slice below is implemented via its own `/speckit-specify` invocation, in order. After approval, run `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement` for that slice before moving to the next.

## Authoritative MISA reference material

1. **Main API doc** — [Tài liệu tích hợp API eSign RemoteSigning - V2.md](./misa-api-reference/T%C3%A0i%20li%E1%BB%87u%20t%C3%ADch%20h%E1%BB%A3p%20API%20eSign%20RemoteSigning%20-%20V2.md). Endpoint catalogue, request/response envelopes, header conventions (`x-clientId`, `x-clientKey`, `AuthorizationRM`), `ResponseError` shape, signing/refresh/cert workflows, and §4 object descriptions for PDF/XML/Word/Excel hash + attach payloads.
2. **MISA Postman collection** — linked from the API doc §6 step "Postman tham khảo" (`https://drive.usercontent.google.com/u/0/uc?id=1E4GNoFrsU5UDModXx10lzm8c4jegggHn&export=download`). When the doc and the wire format disagree, the Postman collection is the tie-breaker. The spec records discrepancies as flagged items rather than silently picking one.
3. **Sandbox conventions** — [sandbox-setup.md](./sandbox-setup.md). eSign sandbox env vars use the `MISACONNECT_ESIGN_SANDBOX_*` prefix, parallel to the existing `MISACONNECT_SANDBOX_*` used by eInvoice tests.

## Architectural target

This work introduces a new product family parallel to `MisaConnect.EInvoice` (the v2.0 slot reserved in [architecture.md](./architecture.md#future-products-v20)):

- `src/MisaConnect.ESign.Domain` — entities (Certificate, Document, SignSession, Signature), value objects, errors, enums. Zero external dependencies.
- `src/MisaConnect.ESign.Application` — use cases (one per operation), port interfaces.
- `src/MisaConnect.ESign.Infrastructure` — typed HTTP client for `esignapp.misa.vn`, header injection, refresh-token handler, in-memory token cache adapter, DI extensions.
- `src/MisaConnect.ESign.Client` — facade `IMisaESignClient` and `services.AddMisaConnectESign(IConfiguration)` binding the `Misa:ESign` configuration section.

The constitution invariants from [.specify/memory/constitution.md](../.specify/memory/constitution.md) apply: zero-dep Domain, layer rules, swappable ports (`ITokenCache`, `ISystemClock`, `ICorrelationIdAccessor`, plus eSign-specific ones like `ICertificateSelector` and `IDocumentHasher`), wire-format fidelity to MISA's published shapes, and no PII/secrets/tokens in logs.

## Slice overview

| # | Slug | Endpoints | Status |
|---|---|---|---|
| 1 | `misa-esign-pdf-sign-flow` | `auth/login-api`, `auth/refreshtoken`, `Certificates/by-userId`, `documents/hash`, `Signing/hash`, `Signing/status`, `documents/attachment` | Pending |
| 2 | `misa-esign-2fa-otp` | `auth/two-factor-auth`, `auth/resend-otp-auth` | Pending |
| 3 | `misa-esign-multi-format` | `documents/hash`, `documents/attachment` extended for `XmlDocs` / `WordDocs` / `ExcelDocs` | Pending |
| 4 | `misa-esign-webhook` | partner-hosted webhook receiver (alternative to polling `Signing/status`) | Pending |

Dependencies: slice 1 is foundational (auth, token cache, cert selection, end-to-end PDF signing). Slices 2, 3, and 4 each depend on slice 1 only and can be done in any order.

## Test coverage standard (applies to every slice)

Every slice must ship with **both** of the following test layers, set up in slice 1 and extended in subsequent slices:

- **Unit tests** — fast, no network, in `tests/MisaConnect.ESign.UnitTests/`. Exercise validation, hash composition, error-envelope mapping (`ResponseError { error, errorCode, devMsg, userMsg }`), refresh-on-401 logic, certificate filtering by `keyStatus = ACTIVE`, and the sign-status polling state machine. Use mock fixtures only (no live MISA calls).
- **Integration / end-to-end tests** — in `tests/MisaConnect.ESign.IntegrationTests/`. `[SandboxFact]` tests exercise the MISA eSign sandbox using `MISACONNECT_ESIGN_SANDBOX_*` env vars (registered per [sandbox-setup.md](./sandbox-setup.md)); they must skip cleanly when the sandbox is unreachable or credentials are absent, but fail loudly when credentials are explicitly rejected. An in-repo fake server under `tests/MisaConnect.ESign.IntegrationTests/EsignFake/` mirrors the `MisaFake/` pattern used by the eInvoice integration tests, for deterministic offline runs.

Each slice's `/speckit-specify` prompt below carries this expectation explicitly so the spec captures it as a deliverable, not an afterthought.

---

## Slice 1 — `misa-esign-pdf-sign-flow`

**Run first. Foundational — establishes auth, token cache, cert selection, and the end-to-end PDF signing workflow.**

```
/speckit-specify Integrate with MISA eSign RemoteSigning so an authenticated caller of our service can sign a PDF end-to-end via MISA's remote certificate service. The slice covers: logging in with userName/password against /api/auth/api/v1/auth/login-api to obtain an accessToken, a remoteSigningAccessToken, a refreshToken, and an expiresIn lifetime (cached and refreshed on HTTP 401 via /auth/refreshtoken); listing the user's digital certificates via /external/esrm/service/general/api/v1/Certificates/by-userId and selecting one whose keyStatus = ACTIVE; hashing a PDF document via /external/esrm/service/document/api/v1/documents/hash using SHA256 with the selected certificate and certificate chain; submitting the resulting digest to /external/esrm/service/signing/api/v1/Signing/hash to obtain a transactionId; polling /external/esrm/service/signing/api/v1/Signing/status/{transactionId} until status is SUCCESS, FAILED, or CANCELLED; and attaching the returned signature to the original PDF via /external/esrm/service/document/api/v1/documents/attachment to produce the final signed PDF bytes. The slice wires up x-clientId / x-clientKey / AuthorizationRM header handling, sandbox vs production base URL via the Misa:ESign configuration section, and the consumer-facing services.AddMisaConnectESign(IConfiguration) DI entry point. The deliverable also includes a unit test suite using mock fixtures (no network) covering validation, ResponseError mapping, refresh-on-401, ACTIVE-cert filtering, and the polling state machine; plus an end-to-end integration test suite that exercises the full happy path against the MISA eSign sandbox using credentials from MISACONNECT_ESIGN_SANDBOX_* env vars (see docs/sandbox-setup.md). Out of scope: 2FA / OTP flow (slice 2), non-PDF document types (slice 3), and the webhook receiver as an alternative to polling (slice 4). Source doc: docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md.
```

---

## Slice 2 — `misa-esign-2fa-otp`

**Run when slice 1 is implemented and merged. Independent of slices 3 and 4.**

```
/speckit-specify Add support for MISA eSign two-factor authentication. When /auth/login-api returns error code 122 (2FA required), the slice routes through /api/auth/api/v1/auth/two-factor-auth with the user's OTP code, an otpType discriminator (0 = received via SMS/email, 1 = received from the authenticator app), and a remember-device flag, to obtain the accessToken / remoteSigningAccessToken / refreshToken. The slice also supports OTP re-delivery via /webdev/api/auth/api/v1/auth/resend-otp-auth with the userName and a language code (e.g. en-US). Surfaces clear errors for invalid OTP, expired OTP, and exhausted attempts. Reuses slice 1's HTTP plumbing, header handling, token cache, and refresh-token flow. The deliverable extends slice 1's test suites: unit tests using mock fixtures for the 122-code branch, both otpType paths, the resend flow, and the rejection paths; plus end-to-end integration tests that, when the sandbox account is configured with 2FA enabled (a documented sandbox precondition), drive the OTP exchange to completion. Reuses the token cache and HTTP plumbing built in slice 1 (misa-esign-pdf-sign-flow). Out of scope: alternative MFA providers, OTP delivery channel selection beyond the documented otpType enum, and account-recovery flows. Source doc: docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md.
```

---

## Slice 3 — `misa-esign-multi-format`

**Run when slice 1 is implemented and merged. Independent of slices 2 and 4.**

```
/speckit-specify Extend the document-signing flow from slice 1 to cover non-PDF document types supported by MISA eSign. The slice adds XML, Word, and Excel document support to /external/esrm/service/document/api/v1/documents/hash and /external/esrm/service/document/api/v1/documents/attachment, with their format-specific request shapes (xmlDocs, wordDocs, excelDocs) and signing-position semantics described in §4 (Mô tả đối tượng) of the API doc. The slice composes the appropriate hash request DTO per format, threads the resulting digest through the same /Signing/hash → /Signing/status → /documents/attachment chain built in slice 1, and returns the signed document bytes in the original format. The deliverable extends slice 1's test suites: unit tests using mock fixtures per format covering DTO shape, signing-position metadata, and per-format error mapping; plus end-to-end integration tests against the MISA eSign sandbox (using MISACONNECT_ESIGN_SANDBOX_* credentials per docs/sandbox-setup.md) that sign one fixture document of each format and verify the returned bytes carry an attached signature. Reuses slice 1's auth, token cache, certificate selection, and signing pipeline. Out of scope: client-side hashing fallback (the PHP/NodeJS path called out in the API doc — MisaConnect always uses MISA's hash endpoint), bulk multi-document signing within a single transaction, and document format conversion. Source doc: docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md.
```

---

## Slice 4 — `misa-esign-webhook`

**Run last, when slices 1–3 are merged. Optional — pollers can keep using `Signing/status` indefinitely.**

```
/speckit-specify Add support for receiving asynchronous signing-status notifications from MISA eSign via webhook, as an alternative to polling /Signing/status. MISA POSTs the documented webhook envelope { messageId, clientId, extraData, status, errorCode, transactionId, signatures: [{ documentId, signature }] } to a partner-hosted endpoint when all documents in a transaction are signed successfully. The slice ships: (a) a typed Domain model and Application-layer handler for the webhook envelope; (b) a verification helper that validates clientId and matches messageId / transactionId against in-flight signing sessions; (c) a wired-up minimal-API endpoint in samples/MisaConnect.Samples.Api/ that accepts the webhook, returns the documented { errorCode, devMsg, userMsg } acknowledgement, and finalizes signed documents by calling slice 1's /documents/attachment path. The deliverable extends slice 1's test suites: unit tests using mock fixtures for envelope parsing, transactionId reconciliation, duplicate-delivery handling, and the success/failure acknowledgement shapes; plus integration tests that drive the sample API's webhook endpoint with synthetic MISA-shaped payloads and verify the attached-signature finalization path. Reuses slice 1's signing-session bookkeeping, attach call, and HTTP plumbing. Out of scope: webhook signature / HMAC / replay protection beyond what MISA's doc specifies (none documented), durable webhook queueing, and webhook registration / discovery (registration is handled with MISA out-of-band per the doc). Source doc: docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md.
```

---

## How to use this file

1. Implement slice 1 to completion: `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement`, then merge.
2. When ready for slice 2, 3, or 4, copy its `/speckit-specify ...` block from above into Claude Code, run it, then run the rest of the speckit chain. Slices 2–4 are mutually independent and can be done in any order after slice 1.
3. Update the **Status** column in the slice overview table as each slice is created and as it ships.

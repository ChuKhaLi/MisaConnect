# Error-mapping contract — `ResponseError.errorCode` → typed SDK exception

This file pins how the SDK translates MISA's `ResponseError { error, errorCode, devMsg, userMsg }` envelope into the typed exception hierarchy from [data-model.md §1.10](../data-model.md#110-error-hierarchy-domainerrors). SC-005 requires every code documented for the seven slice-1 endpoints to map deterministically and to surface `errorCode` + correlation ID on the resulting exception — this file is the test plan for that.

Every row below is one unit-test case in `tests/MisaConnect.ESign.UnitTests/Errors/ESignErrorMapperTests.cs`.

---

## A. Mapping table

> **Code values**: MISA's published doc gives one explicitly numbered code (`122` for 2FA). The rest of the codes are named ("InvalidPassword", "AccountLocked", …) in the doc's prose or the Postman collection. Where the doc does not nail down an exact string, the implementation matches **case-insensitively on substring** against the documented English keyword and records both the raw code and the message on the exception. This is the same pragmatic strategy `MeInvoiceErrorMapper` uses for the eInvoice family.

### A.1. `login-api` (E1)

| Trigger | Maps to | Exception properties |
|---|---|---|
| `errorCode == "122"` | `AuthenticationFailedException(Requires2FA = true)` | Category=Authentication, RawCode=122 |
| `errorCode` contains `Password` or `InvalidUser` or `Credential` | `AuthenticationFailedException` | Requires2FA=false |
| `errorCode` contains `Lock` or `Disabled` | `AuthenticationFailedException` | — |
| `errorCode` contains `Expire` (account expired) | `AuthenticationFailedException` | — |
| Any other 4xx with non-empty errorCode | `AuthenticationFailedException` | RawCode preserved verbatim |
| 4xx with empty errorCode | `ESignException(category = MisaUnknown, rawCode = "EmptyErrorCode")` | — |

### A.2. `refreshtoken` (E2)

| Trigger | Maps to | Exception properties |
|---|---|---|
| **Any** 4xx | `AuthenticationFailedException` | Requires2FA=false. Constitution: do **not** loop to re-login automatically. RawCode = MISA's value. |
| 5xx / transport-exhausted | `ESignTransportException` | LastStatusCode + AttemptCount filled |

### A.3. `Certificates/by-userId` (E3)

| Trigger | Maps to | Exception properties |
|---|---|---|
| 401 | (handled by `RemoteSigningAuthHandler` — refresh + retry once) — | only surfaces as exception if refresh fails |
| 200 with empty array OR no `keyStatus == ACTIVE` | `NoActiveCertificateException` | RawCode = "NoActiveCertificate" (synthesized client-side) |
| Any other 4xx | `ESignException(category = CertificateLookupFailed)` | RawCode preserved |

### A.4. `documents/hash` (E4)

| Trigger | Maps to | Exception properties |
|---|---|---|
| `errorCode` contains `Cert` or `Certificate` | `ESignException(category = HashRejected, rawCode = "InvalidCertificate")` | — |
| `errorCode` contains `Hash` or `Digest` | `ESignException(category = HashRejected, rawCode = "InvalidHash")` | — |
| `errorCode` contains `File` or `Document` (request shape malformed) | `ESignException(category = HashRejected, rawCode = "InvalidDocument")` | — |
| `errorCode` contains `Signature` (signature-info shape) | `ESignException(category = HashRejected, rawCode = "InvalidSignatureInfo")` | — |
| Any other 4xx | `ESignException(category = HashRejected)` | RawCode preserved |

### A.5. `Signing/hash` (E5)

| Trigger | Maps to | Exception properties |
|---|---|---|
| `errorCode` / `devMsg` indicates "user not connected" or "remote signing account not set up" | `SignRejectedException(RequiresUserCertSetup = true)` | — |
| `errorCode` contains `Cert` (cert revoked/expired mid-flow) | `SignRejectedException` | — |
| `errorCode` contains `Doc` or `Hash` | `SignRejectedException` | — |
| `errorCode` contains `Quota` or `Limit` | `SignRejectedException` | — |
| Any other 4xx | `SignRejectedException` | RawCode preserved |

### A.6. `Signing/status/{tx}` (E6)

This endpoint returns 200 even for terminal failures — the differentiator is the body's `status` field.

| Trigger | Maps to | Exception properties |
|---|---|---|
| Body `status == "PENDING"` | (no exception — poll continues) | — |
| Body `status == "SUCCESS"` | (no exception — orchestrator advances to attach) | — |
| Body `status == "FAILED"` | `SignTerminalStateException(TerminalStatus = FAILED)` | TransactionId + body.errorCode + body.errorDescription preserved |
| Body `status == "CANCELLED"` | `SignTerminalStateException(TerminalStatus = CANCELLED)` | — |
| Body `status` is anything else | `SignTerminalStateException(TerminalStatus = Unknown)` | RawCode = body.errorCode or "UnknownStatus" |
| Polling exceeded `MisaESignOptions.Polling.TotalTimeout` | `SignTimeoutException` | TransactionId + ElapsedTime |
| 4xx (e.g. unknown transactionId) | `ESignException(category = StatusLookupFailed)` | RawCode preserved |

### A.7. `documents/attachment` (E7)

| Trigger | Maps to | Exception properties |
|---|---|---|
| `errorCode` contains `Signature` (mismatch / malformed) | `ESignException(category = AttachmentRejected, rawCode = "InvalidSignature")` | — |
| `errorCode` contains `Cert` | `ESignException(category = AttachmentRejected, rawCode = "InvalidCertificate")` | — |
| `errorCode` contains `Doc` or `Hash` | `ESignException(category = AttachmentRejected, rawCode = "InvalidHashInputs")` | — |
| Any other 4xx | `ESignException(category = AttachmentRejected)` | RawCode preserved |

### A.8. Cross-cutting (any endpoint)

| Trigger | Maps to | Exception properties |
|---|---|---|
| 401 (after refresh failed) | `AuthenticationFailedException` | — |
| 429 / 5xx / connection failure — retry budget exhausted | `ESignTransportException` | LastStatusCode (if any), AttemptCount = `MaxAttempts` |
| `TaskCanceledException` (caller cancel, `ct.IsCancellationRequested == true`) | (propagated unchanged) | — |
| `TaskCanceledException` (operation timeout) — retry budget exhausted | `ESignTransportException(LastStatusCode = null)` | AttemptCount filled |
| Malformed JSON / missing required field on a 200 | `ESignException(category = MisaUnknown, rawCode = "EmptyResponse"\|"MalformedResponse")` | — |
| Unrecognized errorCode but populated `userMsg`/`devMsg` | `ESignException(category = MisaUnknown)` | RawCode preserved verbatim |

---

## B. Property-population rules

Every exception thrown by the mapper:

1. `CorrelationId` — must be non-empty. Sourced from `ICorrelationIdAccessor.Current` at the layer that threw.
2. `RawCode` — MISA's `errorCode` value verbatim when populated, otherwise the synthesized identifier from the table above.
3. `Detail` — by default a scrubbed summary suitable for default logs (`"MISA returned errorCode={RawCode} on {Endpoint}"`). When `MisaESignOptions.Errors.IncludeRawErrorMessage = true`, the raw `userMsg` + `devMsg` are concatenated into `Detail`.
4. `Category` — the SDK's `ESignErrorCategory` enum value from the table.

---

## C. SC-005 verification plan

The SC-005 unit suite drives the mapper with one fixture per row above. Each test asserts:

- The right exception type was thrown (typed assertion, not message-substring).
- `Category`, `RawCode`, `CorrelationId` carry the expected values.
- For `AuthenticationFailedException`, `Requires2FA` matches the trigger.
- For `SignRejectedException`, `RequiresUserCertSetup` matches the trigger.
- For `SignTerminalStateException`, `TerminalStatus` + `TransactionId` are populated.
- For `SignTimeoutException`, `ElapsedTime > 0`.
- For `ESignTransportException`, `AttemptCount` equals the configured `MaxAttempts`.

The captured-log scan for SC-006 confirms none of the test cases leak token values, refresh-token values, cert private bytes, raw PDF bytes, or PII strings into the log output — even when `IncludeRawErrorMessage = true` (which surfaces the message on the exception, but log lines still scrub).

---

## D. Forward compatibility — slice 2 hooks

Slice 2 (2FA / OTP) reuses this table with two additions: a new `OtpInvalidException` and a new `OtpRateLimitedException` subclass. The current `AuthenticationFailedException.Requires2FA = true` hint is the seam — slice 2 replaces it with the OTP-specific path, but slice-1 consumers can already detect "MISA wants 2FA" today.

# Error-mapping contract — slice 2 additions (`/two-factor-auth`, `/resend-otp-auth`)

This file pins how the slice-2 `OtpErrorMapper` (Application.Errors) translates MISA's `ResponseError { error, errorCode, devMsg, userMsg }` envelope (or its absence) into the typed slice-2 outputs. Per the 2026-05-19 clarification, the mapping is **hybrid**: a canonical `errorCode` table first, a substring keyword table second. Both tables are published below so consumers can audit how their MISA responses are bucketed (FR-039).

The slice-1 mapping tables under [specs/001-misa-esign-pdf-sign-flow/contracts/error-mapping.md](../../001-misa-esign-pdf-sign-flow/contracts/error-mapping.md) remain authoritative for endpoints E1–E7. This file adds A.8 (E8 — `/two-factor-auth`) and A.9 (E9 — `/resend-otp-auth`).

---

## A.8. `/two-factor-auth` (E8)

The two-factor endpoint throws on rejection (typed exceptions — FR-039). Mapping is two-layered:

### A.8.1. Layer 1 — canonical `errorCode` table

When MISA returns a 4xx response with a `ResponseError` envelope whose `errorCode` matches one of the values below (case-insensitive exact match), the mapper produces the indicated typed exception. Initial seed values are sourced from the MISA Postman collection on 2026-05-19; new codes are added as the integration suite hits them.

| `errorCode` | Maps to | Notes |
|---|---|---|
| `122` | `AuthenticationFailedException(Requires2FA = true, Username = <captured>)` | Re-challenge from `/two-factor-auth` itself. Orchestrator does NOT loop — consumer must restart from `/login-api` (edge case 7). |
| `1001` | `InvalidOtpException` | OTP value is wrong. |
| `1002` | `ExpiredOtpException` | OTP value is stale. |
| `1003` | `ExhaustedOtpAttemptsException` | MISA refused further attempts. |
| Any other value | (fall through to Layer 2) | |

The table is encoded as a `static readonly Dictionary<string, Func<...>>` inside `OtpErrorMapper`. Adding a new canonical code is a single-line PR plus a unit test row.

### A.8.2. Layer 2 — substring keyword fallback on `errorCode + devMsg + userMsg`

When `errorCode` is empty or not in the canonical table, the mapper concatenates `errorCode ?? "" + " " + devMsg ?? "" + " " + userMsg ?? ""`, lower-cases it, and matches against the keyword table below. The first matching row wins. Both English and Vietnamese keywords are listed because MISA's `userMsg` is Vietnamese in production traffic.

| Keyword (case-insensitive, substring) | Maps to | Synthesized `RawCode` |
|---|---|---|
| `"invalid otp"`, `"wrong otp"`, `"otp incorrect"`, `"otp wrong"`, `"sai mã"`, `"không đúng"`, `"otp không hợp lệ"` | `InvalidOtpException` | `"InvalidOtp"` (if MISA's `errorCode` was empty); else MISA's value |
| `"expired"`, `"hết hạn"`, `"quá hạn"`, `"timed out"` | `ExpiredOtpException` | `"ExpiredOtp"` (or MISA's value) |
| `"max attempts"`, `"exceeded"`, `"vượt quá số lần"`, `"too many attempts"`, `"locked"` | `ExhaustedOtpAttemptsException` | `"ExhaustedOtpAttempts"` (or MISA's value) |
| (no match) | `OtpRejectedException` | MISA's `errorCode` verbatim; else `"OtpRejected"` |

The "(no match)" residual bucket guarantees every 4xx surfaces as some `AuthenticationFailedException` subclass — the consumer's `catch (AuthenticationFailedException)` block always picks it up.

### A.8.3. Property-population rules (per exception)

Every exception thrown by `OtpErrorMapper.MapTwoFactor(...)`:

1. `CorrelationId` — must be non-empty. Sourced from `ICorrelationIdAccessor.Current` at the layer that threw.
2. `RawCode` — MISA's `errorCode` value verbatim when populated; otherwise the synthesized identifier from the table.
3. `Detail` — built by `OtpErrorMapper.BuildDetail(endpoint, rawCode, envelope, includeRawErrorMessage)` — same shape as slice-1's `ESignErrorMapper.BuildDetail`. When `MisaESignOptions.Errors.IncludeRawErrorMessage = false` (default), the detail is a one-line summary; when `true`, MISA's `userMsg` and `devMsg` are appended.
4. `Username` (on `AuthenticationFailedException(Requires2FA = true)` only — i.e. the `errorCode = 122` re-challenge case) — populated from the captured `userName` of the originating challenge.
5. The constituent `AuthenticationFailedException.Requires2FA` is **false** on `InvalidOtpException` / `ExpiredOtpException` / `ExhaustedOtpAttemptsException` / `OtpRejectedException` (the consumer has supplied an OTP — the loop terminates).

### A.8.4. Cross-cutting cases on E8

| Trigger | Maps to | Notes |
|---|---|---|
| 401 from `/two-factor-auth` | `OtpRejectedException` | Per Assumption 5: the endpoint has no `AuthorizationRM` header to refresh; a 401 surfaces typed and the consumer must restart from `/login-api`. |
| 429 / 5xx with retry budget exhausted | `ESignTransportException` | FR-043 — transport retry applies; auth retry does not. |
| `TaskCanceledException` (caller cancel) | (propagated unchanged) | — |
| `TaskCanceledException` (operation timeout — retry exhausted) | `ESignTransportException(LastStatusCode = null)` | Same as slice-1 cross-cutting. |
| Malformed JSON on a 200 | `ESignGeneralException(category = MisaUnknown, rawCode = "EmptyResponse" or "MalformedResponse")` | Same shape as slice-1. |

---

## A.9. `/resend-otp-auth` (E9) — typed-result table

The resend endpoint returns a typed `OtpResendResult` instead of throwing (per [research.md R-6](../research.md#r-6-resend-otp--request-shape-default-language-failure-surfacing)). Only transport-level failures throw.

### A.9.1. Success / typed-failure table

| HTTP status | `status.error` (envelope) | Outcome |
|---|---|---|
| 200 | `false` (or absent) | `OtpResendResult { Success = true, RawCode = null, UserMsg = null, DevMsg = null, CorrelationId }` |
| 200 | `true` | `OtpResendResult { Success = false, RawCode = status.errorCode, UserMsg = status.userMsg, DevMsg = status.devMsg, CorrelationId }` |
| 4xx | (envelope present) | `OtpResendResult { Success = false, RawCode = envelope.errorCode, UserMsg = envelope.userMsg, DevMsg = envelope.devMsg, CorrelationId }` |
| 4xx | (envelope absent / malformed) | `OtpResendResult { Success = false, RawCode = "EmptyErrorCode", UserMsg = null, DevMsg = null, CorrelationId }` |
| 429 / 5xx — retry budget exhausted | (n/a) | `throw ESignTransportException(...)` |
| `TaskCanceledException` (caller cancel) | (n/a) | (propagated unchanged) |
| `TaskCanceledException` (operation timeout — retry exhausted) | (n/a) | `throw ESignTransportException(LastStatusCode = null)` |

### A.9.2. Property-population rules

`OtpResendResult.CorrelationId` is always populated from `ICorrelationIdAccessor.Current`. The consumer-visible `OtpResendResultDto` is a 1:1 wrapper. The opt-in `IncludeRawErrorMessage` flag has **no effect on `OtpResendResult`** — the result type intentionally exposes `RawCode`, `UserMsg`, and `DevMsg` directly, because the consumer's downstream display logic (typically "show this to the end user") needs all three. (This is distinct from the exception path on E8, where `UserMsg`/`DevMsg` only appear in the exception message when `IncludeRawErrorMessage = true`.)

The captured-log unit test (SC-012) confirms that `RawCode`/`UserMsg`/`DevMsg` flowing through `OtpResendResult` do NOT leak into the `ESignLogScrubber`-bounded logs — they leave the SDK only via the typed result returned to the consumer.

---

## B. Test-case manifest (one row per unit test)

The unit test `tests/MisaConnect.ESign.UnitTests/Errors/OtpErrorMapperTests.cs` covers exactly one row per table entry above. For maintainability, the test uses `[Theory]` with inline data so each row is a self-contained case:

- Layer 1 cases on A.8.1: 4 rows (122, 1001, 1002, 1003).
- Layer 2 cases on A.8.2: at least 1 row per keyword group × 2 languages = 8 rows; plus the residual bucket = 1 row. Plus 1 row for "errorCode populated but not canonical AND keyword does not match" → residual.
- Cross-cutting cases on A.8.4: 4 rows (401, 429-exhausted, malformed JSON, operation timeout).
- A.9.1 typed-result cases: 6 rows (the six HTTP/envelope permutations).

Total: ~24 inline rows, executable in `< 100ms` collectively. Within the constitution's `< 30s` unit-suite budget.

---

## C. Maintenance discipline

When a new MISA `errorCode` is observed in production traffic that doesn't match any current row:

1. Add a row to A.8.1 (or A.8.2 if it's a keyword match) and a `[Theory]` inline data row to `OtpErrorMapperTests.cs`.
2. Update `CHANGELOG.md` under `[Unreleased]` ("Mapper now recognizes errorCode XYZ as InvalidOtp").
3. The change is a patch-level update on the preview line (no breaking surface change).

The discipline is the same as slice 1's `ESignErrorMapper` — the test rows ARE the spec of the mapper's behavior.

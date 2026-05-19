# Wire-envelope contract — slice 2 additions (`/two-factor-auth`, `/resend-otp-auth`)

This file pins the two HTTP request/response shapes that `MisaConnect.ESign.Infrastructure.ESign.Wire/` must mirror verbatim per Constitution Principle IV.

Source of truth: [docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md](../../../docs/misa-api-reference/T%C3%A0i%20li%E1%BB%87u%20t%C3%ADch%20h%E1%BB%A3p%20API%20eSign%20RemoteSigning%20-%20V2.md) rows §3.2.1 (`/two-factor-auth`, lines 302–308) and §3.2.2 (`/resend-otp-auth`, lines 316–320). When the doc and the Postman collection disagree, the Postman collection wins per [docs/misa-esign-spec-plan.md](../../../docs/misa-esign-spec-plan.md) — divergences are flagged inline as **FLAG**.

The seven slice-1 endpoint shapes (E1–E7) remain authoritative under [specs/001-misa-esign-pdf-sign-flow/contracts/wire-envelopes.md](../../001-misa-esign-pdf-sign-flow/contracts/wire-envelopes.md). This file adds E8 and E9.

---

## Header convention reminder

| Header | Value | Applies to E8 / E9? |
|--------|-------|---|
| `Content-Type` | `application/json` | Both |
| `x-clientId` | `MisaESignOptions.ClientId` | Both |
| `x-clientKey` | `MisaESignOptions.ClientKey` | Both |
| `AuthorizationRM` | `"Bearer " + remoteSigningAccessToken` | **Neither** — the 2FA and resend endpoints carry no `AuthorizationRM` header (no cached access token exists at this point in the flow). `RemoteSigningAuthHandler.IsAuthEndpoint(...)` is extended in slice 2 to skip these two paths. |
| `X-Correlation-Id` | `ICorrelationIdAccessor.Current` | Both (FR-045) |

**FLAG** — the MISA doc row for `/two-factor-auth` (§3.2.1) writes the client-credentials headers as `clientId` / `clientKey` (no `x-` prefix), while all other endpoints in the doc use `x-clientId` / `x-clientKey`. Slice 1 chose `x-clientId` / `x-clientKey` everywhere, and slice 2 inherits that decision. The slice-2 sandbox integration test (`TwoFactorAuthSandboxTests.cs`) confirms MISA accepts the `x-`-prefixed header on `/two-factor-auth` in practice. If a future MISA breaking change rejects the prefix, the fix is a single-line condition in `ClientHeadersHandler` to drop the prefix on the 2FA endpoint specifically.

---

## E8. `POST /api/auth/api/v1/auth/two-factor-auth`

**Used by**: `MisaESignWireClient.TwoFactorAuthAsync(...)` — invoked by `ExchangeOtp` via the explicit `SignInWithOtpAsync` path or the transparent `IOtpProvider` path.

**Request body**

```json
{
  "userName": "string",
  "code": "string",
  "otpType": 0,
  "remember": true
}
```

Field semantics:

| Field | Type | Notes |
|---|---|---|
| `userName` | `string` | Same value as the originating `/login-api` call. |
| `code` | `string` | The end-user-supplied OTP value. Subject to `ESignLogScrubber` redaction (R-7 in [research.md](../research.md#r-7-logging-and-otp-redaction)). |
| `otpType` | `integer` (0 or 1) | Closed enum per the MISA doc: `0` = SMS/email, `1` = authenticator app. The SDK serializes the `OtpDeliveryChannel` enum's integer value verbatim. |
| `remember` | `boolean` | Pass-through to MISA. Per the 2026-05-19 clarification, the SDK does NOT persist a device-trust identifier client-side regardless of this value. |

**Response body** (200) — **identical shape** to `/login-api` (E1). The MISA doc (§3.2.1, "Mô tả đầu ra: Tham số đầu ra (eSign-login)") explicitly says the response object is the same. Slice-2 implementation re-uses `LoginResponseDto` for parsing.

```json
{
  "status": {
    "type": "string",
    "code": 200,
    "message": "string",
    "error": false,
    "errorCode": 0,
    "devMsg": "string",
    "userMsg": "string"
  },
  "data": {
    "accessToken": "string",
    "remoteSigningAccessToken": "string",
    "tokenType": "Bearer",
    "expiresIn": 3600,
    "refreshToken": "string",
    "user": {
      "id": "string",
      "email": "string",
      "phoneNumber": "string",
      "firstName": "string",
      "lastName": "string",
      "username": "string"
    },
    "default": { "email": false, "phoneNumber": false, "appAuthenticator": true }
  }
}
```

Mapping note: `AuthSessionMapper.FromTwoFactorAuthResponse(...)` is a one-line forwarder to `FromLoginResponse(...)`. `expiresIn` (seconds) is added to `_clock.UtcNow` to populate `AuthSession.ExpiresAtUtc` (same convention as login).

PII handling on response: the inner `user` object carries `email`, `phoneNumber`, `firstName`, `lastName` exactly as in `/login-api`. Per slice 1's mapper, these are discarded at the mapping boundary — only `id` and `username` flow into `AuthSession`. Slice 2 inherits that behavior; no new redaction needed (the existing `ESignLogScrubber` rules for `/login-api`'s `data.user.*` apply to `/two-factor-auth` automatically because the JSON-path matcher targets the shape, not the path).

**Error envelope** (4xx) — the `status` block is populated with `error: true`, `errorCode`, `devMsg`, `userMsg`. `data` may be omitted or `null`. The orchestrator's interpretation of `errorCode` is enumerated in [error-mapping.md §A.8](./error-mapping.md#a8-two-factor-auth-e8).

`errorCode == "122"` returned on `/two-factor-auth` itself indicates a re-challenge (MISA wants a fresh login). The orchestrator does NOT auto-loop; it surfaces `AuthenticationFailedException(Requires2FA = true)` and the consumer's next call restarts from `/login-api` (edge case 7 in [spec.md §Edge Cases](../spec.md#edge-cases)).

---

## E9. `POST /webdev/api/auth/api/v1/auth/resend-otp-auth`

**Used by**: `MisaESignWireClient.ResendOtpAsync(...)` — invoked by `ResendOtp` via the explicit `ResendOtpAsync` path or the transparent `IOtpProvider.RequestResendAsync(...)` path.

**Request body**

```json
{
  "userName": "string",
  "language": "en-US"
}
```

Field semantics:

| Field | Type | Notes |
|---|---|---|
| `userName` | `string` | Same value as the originating `/login-api` call. |
| `language` | `string` | The notification language. Defaults to `MisaESignOptions.Otp.DefaultResendLanguage` (`"en-US"`) when the consumer's call site does not override. The SDK does NOT validate the value client-side. |

**Response body** (200) — per the MISA doc row §3.2.2.

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

`status.error` is the canonical "did it work?" indicator. `status.errorCode` is a string (the doc shows `error_status_code` — the slice mirrors that as a `string?` to handle both `"0"` and absent). The inner `data.user.username` is informational and is NOT propagated to the consumer's `OtpResendResultDto` (only success/failure plus the typed envelope fields matter).

**FLAG** — the MISA doc does not enumerate any specific `errorCode` values for `/resend-otp-auth`. The Postman collection (per the slice's tie-breaker rule) is also silent on numbered codes. The error-mapping contract (§A.9) treats every documented failure as a typed `OtpResendResult { Success = false, ... }` — slice 2 does NOT throw a typed `OtpResendException` because the consumer pattern is "fire and inspect", not "fire and catch" (per [research.md R-6](../research.md#r-6-resend-otp--request-shape-default-language-failure-surfacing)).

**Error envelope** (4xx) — `status.error: true` with `status.errorCode`, `status.devMsg`, `status.userMsg`. The slice-2 mapper builds an `OtpResendResult` from these fields.

Transport failures (5xx, 429, connection errors) DO surface as `ESignTransportException` through the existing `TransientFailureRetryHandler` per FR-043.

---

## Wire DTO files (Infrastructure)

```
src/MisaConnect.ESign.Infrastructure/ESign/Wire/
├── (existing) LoginDtos.cs            # E1 — LoginRequestDto, LoginResponseDto, LoginStatusBlockDto
├── (existing) RefreshTokenDtos.cs     # E2
├── (existing) CertificateDtos.cs      # E3
├── (existing) HashDtos.cs             # E4
├── (existing) SignHashDtos.cs         # E5
├── (existing) SignStatusDtos.cs       # E6
├── (existing) AttachmentDtos.cs       # E7
├── (existing) ResponseErrorDto.cs     # cross-cutting
├── (new)      TwoFactorAuthDtos.cs    # E8 — TwoFactorAuthRequestDto + (type alias) TwoFactorAuthResponseDto = LoginResponseDto
└── (new)      ResendOtpDtos.cs        # E9 — ResendOtpRequestDto, ResendOtpResponseDto, ResendOtpDataDto, ResendOtpUserDto
```

JSON serialization uses the existing `ESignJsonOptions.Wire` (camelCase, no special converters). The four new DTOs declare `JsonPropertyName` attributes where the wire name diverges from the PascalCase property name (i.e. on every field — the SDK uses PascalCase properties throughout and explicit attribute names for wire fidelity).

---

## Sandbox confirmation matrix

The sandbox integration test (`TwoFactorAuthSandboxTests.cs`) confirms — when `MISACONNECT_ESIGN_SANDBOX_*` and `MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER` are configured:

| Behavior | Confirmation method |
|---|---|
| `/two-factor-auth` accepts `x-clientId` / `x-clientKey` prefixed headers | Live successful OTP exchange returns the documented envelope |
| `/two-factor-auth` envelope on success matches `LoginResponseDto` | Deserialization without `JsonException` |
| `/resend-otp-auth` accepts a `language` value of `"en-US"` | Live resend returns `status.error = false` |
| `/two-factor-auth` rejects a wrong OTP with a typed envelope | Live wrong-OTP submission returns a 4xx with `errorCode` |
| `RemoteSigningAuthHandler` does NOT inject `AuthorizationRM` on these endpoints | Captured request has no `AuthorizationRM` header (asserted by an integration test that uses a packet-capture-style `DelegatingHandler` wrapper) |

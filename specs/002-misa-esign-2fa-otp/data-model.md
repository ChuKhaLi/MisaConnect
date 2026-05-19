# Phase 1 — Data Model: MISA eSign 2FA / OTP

This document captures the Domain entities, Application-layer DTOs/ports/results, options edits, and state transitions added (or, where applicable, extended) by slice 2. Every type listed below lives at the layer indicated (Domain / Application / Infrastructure-Wire / Client-Dto) and respects Constitution Principle I.

Field names in the **Wire DTO** column reproduce MISA's published shape verbatim per Principle IV. Field names in Domain / Application / Client columns are SDK-idiomatic (PascalCase).

Cross-references to slice 1 use the form [slice-1 §X.Y](../001-misa-esign-pdf-sign-flow/data-model.md#xy).

---

## Layered overview (slice-2 additions only)

```
Client (public DTOs)         Client.Dtos.OtpResendResultDto
                                        │  Mapping
                                        ▼
Application (use cases,      Application.UseCases.ExchangeOtp           (NEW)
ports, error mapper)         Application.UseCases.ResendOtp             (NEW)
                             Application.UseCases.SignPdf                (EDITED — consults IOtpProvider)
                             Application.Abstractions.IOtpProvider      (NEW port — optional)
                             Application.Abstractions.OtpChallenge      (NEW record)
                             Application.Abstractions.OtpSubmission     (NEW record)
                             Application.Abstractions.OtpResendResult   (NEW record)
                             Application.Errors.OtpErrorMapper          (NEW sibling of ESignErrorMapper)
                                        │
                                        ▼
Domain (entities, errors,    Domain.Authentication.OtpDeliveryChannel   (NEW enum)
enums)                       Domain.Authentication.AuthSession           (REUSED — populated from 2FA response identically to login)
                             Domain.Errors.InvalidOtpException          (NEW)
                             Domain.Errors.ExpiredOtpException          (NEW)
                             Domain.Errors.ExhaustedOtpAttemptsException (NEW)
                             Domain.Errors.OtpRejectedException         (NEW — residual)
                             Domain.Errors.AuthenticationFailedException (EDITED — gains Username property)
                                        ▲
                                        │  Mapping (Infrastructure)
Infrastructure (wire DTOs)   Infrastructure.ESign.Wire.TwoFactorAuthDtos   (NEW)
                             Infrastructure.ESign.Wire.ResendOtpDtos       (NEW)
                             Infrastructure.ESign.Mapping.AuthSessionMapper (EDITED — FromTwoFactorAuthResponse)
```

---

## 1. Domain layer — entities & value objects

### 1.1. `OtpDeliveryChannel` (Domain.Authentication) — NEW

Closed enum mapping MISA's `otpType` discriminator on `/two-factor-auth`.

| Member | Wire value | Meaning per MISA doc §3.2.1 |
|---|---|---|
| `SmsOrEmail` | `0` | The end user received the OTP via SMS or email |
| `Authenticator` | `1` | The end user obtained the OTP from an authenticator app |

`OtpDeliveryChannel` is a `byte`-backed `enum`. No "Unknown" / "Default" member — the consumer always supplies one of the two. Wire serialization uses the integer value verbatim (System.Text.Json's default integer-enum behavior, no custom converter needed).

### 1.2. `AuthSession` (Domain.Authentication) — REUSED

Unchanged from [slice-1 §1.1](../001-misa-esign-pdf-sign-flow/data-model.md#11-authsession-domainauthentication). The `/two-factor-auth` success body is identical in shape to `/login-api`, so the same `AuthSessionMapper.FromLoginResponse(...)` populates the `AuthSession` for both paths (the slice-2 edit adds a one-line `FromTwoFactorAuthResponse(...)` that forwards to `FromLoginResponse(...)`). FR-035 guarantee: cache reads are indistinguishable.

### 1.3. `AuthenticationFailedException` (Domain.Errors) — EDITED

Slice 1's exception is extended with a new property:

| Property | Type | Set when | Notes |
|---|---|---|---|
| `Username` | `string` | Always non-null; empty string on non-122 paths | Populated when `Requires2FA = true` (122 branch on `/login-api`). Used by `SignInWithOtpAsync` / `ResendOtpAsync` to thread the value into the new request bodies. Carrying it on the exception is non-PII per the secret-hygiene rule (it is the same value the consumer supplied in `MisaESignOptions.UserName`). |

The constructor remains backwards-compatible (new parameter is optional with default `""`). Existing call sites in slice 1 don't need to change; only the `/login-api` 122-path call site in `MisaESignWireClient` is updated to pass the `userName` it just sent.

### 1.4. New typed exceptions (Domain.Errors) — NEW

All four are concrete subclasses of `AuthenticationFailedException` (so `catch (AuthenticationFailedException)` still picks them up — preserves slice-1 ergonomics) and follow slice 1's exception conventions (immutable, non-empty `CorrelationId`, `RawCode` preserved from MISA).

| Type | Category | RawCode default | Constructor extras |
|---|---|---|---|
| `InvalidOtpException` | `Authentication` | `"InvalidOtp"` (or MISA's canonical code if mapped) | — |
| `ExpiredOtpException` | `Authentication` | `"ExpiredOtp"` (or MISA's canonical code if mapped) | — |
| `ExhaustedOtpAttemptsException` | `Authentication` | `"ExhaustedOtpAttempts"` (or MISA's canonical code if mapped) | — |
| `OtpRejectedException` | `Authentication` | MISA's `errorCode` verbatim (preferred); else `"OtpRejected"` | The residual bucket for any 4xx from `/two-factor-auth` that the hybrid mapper cannot classify into the three documented categories. |

`Requires2FA` is `false` on all four (the consumer has supplied an OTP — the loop terminates). The orchestrator's catch-block in `SignPdf` does NOT consult the provider again for any of these four exceptions; the consumer's next call surfaces a fresh `/login-api` request and a fresh 122 → provider invocation.

### 1.5. `ESignErrorCategory` (Domain.Errors) — UNCHANGED

Slice 1's `Authentication` category is reused. No new enum value introduced — the OTP exceptions all live under `Authentication`. FR-039 distinguishes them by **type**, not by category.

---

## 2. Application layer — ports, records, use cases, mapper

### 2.1. `IOtpProvider` (Application.Abstractions) — NEW (optional port)

```csharp
public interface IOtpProvider
{
    Task<OtpSubmission> ProvideAsync(OtpChallenge challenge, CancellationToken ct);
    Task<OtpResendResult> RequestResendAsync(OtpChallenge challenge, string? language, CancellationToken ct);
}
```

Lifetime: registered by the consumer **before** `AddMisaConnectESign(...)`. The DI extension does NOT register a default — if the consumer does not register one, `SignPdf` falls back to rethrowing the `AuthenticationFailedException` (the explicit-pair path). This is the only Application-layer port in slice 2 that has no Infrastructure default — by design (FR-032b).

### 2.2. `OtpChallenge` (Application.Abstractions) — NEW record

```csharp
public sealed record OtpChallenge(string UserName, string CorrelationId);
```

Constructed inside `SignPdf` when it catches `AuthenticationFailedException(Requires2FA = true)`. Carries the `userName` from the exception's new `Username` property and the active `CorrelationId` from `ICorrelationIdAccessor.Current`. No password, no partial token, no PII.

### 2.3. `OtpSubmission` (Application.Abstractions) — NEW record

```csharp
public sealed record OtpSubmission(string Code, OtpDeliveryChannel OtpType, bool Remember);
```

Returned by `IOtpProvider.ProvideAsync(...)`. Threaded into `ExchangeOtp.ExecuteAsync(...)` as a single value object.

### 2.4. `OtpResendResult` (Application.Abstractions) — NEW record

```csharp
public sealed record OtpResendResult(
    bool Success,
    string? RawCode,
    string? UserMsg,
    string? DevMsg,
    string CorrelationId);
```

Returned by `ResendOtp.ExecuteAsync(...)` and by `IOtpProvider.RequestResendAsync(...)`. The Client layer wraps it in `OtpResendResultDto` for the public facade.

### 2.5. `ExchangeOtp` (Application.UseCases) — NEW

| Step | Detail |
|---|---|
| Inputs | `userName` (from the catch block or the explicit `SignInWithOtpAsync` call site), `OtpSubmission`, `CancellationToken` |
| Behavior | (1) Compose cache key via `ITokenCacheKeySelector`. (2) Acquire single-flight slot via `SingleFlightRefresh.RefreshAsync(cacheKey, factory)`. (3) Inside the factory: call `IMisaESignWireClient.TwoFactorAuthAsync(userName, otpCode, otpType, remember, ct)`. (4) Map the returned `AuthSession` to an `AccessToken` via `EnsureAccessToken.ToAccessToken(...)` (shared helper). (5) Write to `ITokenCache.SetAsync(cacheKey, accessToken, ct)`. (6) Return the `AccessToken`. |
| Output | `AccessToken` (the single-flight `Task<AccessToken>` waiters all see) |
| Failure | If the wire client throws a typed exception (`InvalidOtpException` / `ExpiredOtpException` / `ExhaustedOtpAttemptsException` / `OtpRejectedException` / `ESignTransportException`), the exception propagates out of the single-flight slot to all waiters. The cache is NOT written on failure. |

### 2.6. `ResendOtp` (Application.UseCases) — NEW

| Step | Detail |
|---|---|
| Inputs | `userName`, `language` (nullable — defaults to `MisaESignOptions.Otp.DefaultResendLanguage` if null), `CancellationToken` |
| Behavior | Calls `IMisaESignWireClient.ResendOtpAsync(userName, languageOrDefault, ct)` directly. No cache write. No single-flight (concurrent resends are harmless — MISA is the rate-limiter). |
| Output | `OtpResendResult` |
| Failure semantics | HTTP non-2xx with a `ResponseError` envelope → `OtpResendResult { Success = false, RawCode = envelope.errorCode, UserMsg, DevMsg, CorrelationId }`. HTTP 200 with `status.error = true` → same. Transport failure (5xx / 429 retry budget exhausted) → `ESignTransportException` (FR-043). |

### 2.7. `SignPdf` (Application.UseCases) — EDITED

The slice-1 `ExecuteAsync` body is wrapped in a `try { ... }` block. On `catch (AuthenticationFailedException ex) when (ex.Requires2FA && _otpProvider is not null)`:

1. Construct `OtpChallenge(ex.Username, _correlation.Current)`.
2. Invoke `_otpProvider.ProvideAsync(challenge, ct)` → `OtpSubmission`.
3. Invoke `ExchangeOtp.ExecuteAsync(ex.Username, submission, ct)`.
4. **Recursively** invoke `this.ExecuteAsync(request, ct)` ONCE (bounded by R-10 — no internal retry on a typed OTP rejection).

If `_otpProvider` is null, the exception propagates unchanged (explicit-pair path). If `ExchangeOtp` itself throws (e.g. `InvalidOtpException`), the exception propagates — the orchestrator does NOT consult the provider a second time within the same `SignPdfAsync` call.

The `_otpProvider` field is injected via constructor and is nullable (`IOtpProvider?`). DI registration: `services.TryAddScoped<IOtpProvider, NullOtpProvider>()` is **not** done — if the consumer doesn't register one, the field stays null. The DI extension reads `serviceProvider.GetService<IOtpProvider>()` (nullable resolution) at facade-construction time.

### 2.8. `OtpErrorMapper` (Application.Errors) — NEW

Sibling of `ESignErrorMapper`. Owns the `/two-factor-auth` and `/resend-otp-auth` paths.

```csharp
public static class OtpErrorMapper
{
    public const string EndpointTwoFactorAuth = "api/auth/api/v1/auth/two-factor-auth";
    public const string EndpointResendOtp     = "webdev/api/auth/api/v1/auth/resend-otp-auth";

    public static ESignException MapTwoFactor(
        int statusCode,
        ResponseError? envelope,
        string correlationId,
        bool includeRawErrorMessage = false,
        HttpStatusCode? lastStatusCode = null,
        int? attemptCount = null);

    public static OtpResendResult MapResendResult(
        int statusCode,
        ResponseError? envelope,
        string correlationId,
        bool includeRawErrorMessage = false);
}
```

The two-factor mapper returns an `ESignException` (throwable). The resend mapper returns an `OtpResendResult` (typed value) — the consumer pattern is "fire and inspect" for resend (R-6).

Mapping table (canonical first, substring fallback second) — see [contracts/error-mapping.md](./contracts/error-mapping.md) for the exhaustive list and the keyword table that drives the fallback.

### 2.9. `EnsureAccessToken` (Application.UseCases) — EDITED

Single edit: the helper `ToAccessToken(AuthSession)` is made `internal` (it already is `internal static` per slice 1's implementation). No new behavior. The 122-detection logic stays in `ESignErrorMapper.MapLogin(...)` (slice-1 location); slice 2 confirms it surfaces `AuthenticationFailedException(Requires2FA = true)` carrying the `userName` (via the new property — see §1.3).

---

## 3. Infrastructure layer — wire DTOs, mapping edits, options, DI

### 3.1. `TwoFactorAuthRequestDto` (Infrastructure.ESign.Wire) — NEW

| Field | Type | Wire name | Notes |
|---|---|---|---|
| `UserName` | `string` | `userName` | Same value as the originating `/login-api` call. |
| `Code` | `string` | `code` | The OTP value. Subject to `ESignLogScrubber` redaction (R-7). |
| `OtpType` | `int` | `otpType` | Serialized as integer 0 or 1 — System.Text.Json's default integer-enum behavior. |
| `Remember` | `bool` | `remember` | Pass-through to MISA; no client-side persistence (FR-035). |

### 3.2. `TwoFactorAuthResponseDto` (Infrastructure.ESign.Wire) — NEW (alias)

Type alias: `using TwoFactorAuthResponseDto = LoginResponseDto;` defined in `TwoFactorAuthDtos.cs`. The MISA doc says the 2FA response is "the same as login" — slice-2 honors that verbatim by re-using the existing `LoginResponseDto` parsing path (`AuthSessionMapper.FromLoginResponse`).

### 3.3. `ResendOtpRequestDto` (Infrastructure.ESign.Wire) — NEW

| Field | Type | Wire name | Notes |
|---|---|---|---|
| `UserName` | `string` | `userName` | Same value as the originating `/login-api` call. |
| `Language` | `string` | `language` | Defaults to `"en-US"` if the consumer didn't override (FR-037). Not validated client-side. |

### 3.4. `ResendOtpResponseDto` (Infrastructure.ESign.Wire) — NEW

| Field | Type | Wire name | Notes |
|---|---|---|---|
| `Status` | `LoginStatusBlockDto?` | `status` | Re-uses slice-1's status block parser. `status.error == true` indicates a typed failure. |
| `Data` | `ResendOtpDataDto?` | `data` | Optional. |

Nested `ResendOtpDataDto`:
| Field | Type | Wire name | Notes |
|---|---|---|---|
| `User` | `ResendOtpUserDto?` | `user` | Optional. |

Nested `ResendOtpUserDto`:
| Field | Type | Wire name | Notes |
|---|---|---|---|
| `Username` | `string?` | `username` | Optional. Surfaced for completeness; not propagated to the consumer's `OtpResendResultDto`. |

### 3.5. `AuthSessionMapper` (Infrastructure.ESign.Mapping) — EDITED

Adds one method:

```csharp
public static AuthSession FromTwoFactorAuthResponse(LoginResponseDto dto, DateTimeOffset acquiredAtUtc)
    => FromLoginResponse(dto, acquiredAtUtc);
```

One-line forwarder. Exists so the call site in `MisaESignWireClient.TwoFactorAuthAsync(...)` reads as `AuthSessionMapper.FromTwoFactorAuthResponse(...)`, not `FromLoginResponse(...)` — clarity at the source.

### 3.6. `ESignHttpRoutes` (Infrastructure.ESign) — EDITED

Adds two route constants:

| Constant | Value |
|---|---|
| `AuthTwoFactor` | `"api/auth/api/v1/auth/two-factor-auth"` |
| `AuthResendOtp` | `"webdev/api/auth/api/v1/auth/resend-otp-auth"` |

### 3.7. `MisaESignWireClient` (Infrastructure.ESign) — EDITED

Implements the new methods on `IMisaESignWireClient`:

```csharp
Task<AuthSession> TwoFactorAuthAsync(string userName, string code, OtpDeliveryChannel otpType, bool remember, CancellationToken ct);
Task<OtpResendResult> ResendOtpAsync(string userName, string language, CancellationToken ct);
```

`TwoFactorAuthAsync` mirrors `LoginAsync`'s structure: POST with `attachAuthorization: false`, deserialize the envelope, throw via `OtpErrorMapper.MapTwoFactor(...)` on non-success, return `AuthSessionMapper.FromTwoFactorAuthResponse(dto, _clock.UtcNow)` on success.

`ResendOtpAsync` POSTs `{userName, language}`, deserializes the response, calls `OtpErrorMapper.MapResendResult(...)` to produce the typed result (success OR typed-failure result).

### 3.8. `RemoteSigningAuthHandler` (Infrastructure.Http) — EDITED

The existing `IsAuthEndpoint(...)` helper gains two more clauses:

```csharp
return path.EndsWith("/login-api", StringComparison.OrdinalIgnoreCase)
    || path.EndsWith("/refreshtoken", StringComparison.OrdinalIgnoreCase)
    || path.EndsWith("/two-factor-auth", StringComparison.OrdinalIgnoreCase)
    || path.EndsWith("/resend-otp-auth", StringComparison.OrdinalIgnoreCase);
```

This ensures the `AuthorizationRM` header is never injected on the two new endpoints (FR-033), and a 401 from them is NOT looped through the refresh path (Assumption 5 — they have no refreshable cached token at the point of call).

### 3.9. `ESignLogScrubber` (Infrastructure.Logging) — EDITED

Adds JSON-path rules (R-7):

| Rule | When | Replacement |
|---|---|---|
| Drop `$.code` on request bodies to `*/two-factor-auth` | Request stage | `"<redacted-otp>"` |
| Drop `$..device*` on any captured body | Both stages | `"<redacted-device>"` (defensive — no current MISA field matches, future-proof) |
| Already-present token redactions for `$.data.accessToken`, `$.data.remoteSigningAccessToken`, `$.data.refreshToken` | Response stage | continue to apply (envelope shape identical to `/login-api`) |

The captured-log unit test extends to cover OTP exchanges per SC-012. The scrubber implementation already supports per-endpoint matchers via a list of `(predicate, jsonPath, replacement)` tuples — slice-2 adds three new tuples and no new infrastructure.

### 3.10. `MisaESignOptions` (Infrastructure.Configuration) — EDITED

Adds one nested options class:

```csharp
public sealed class MisaESignOtpOptions
{
    public string DefaultResendLanguage { get; set; } = "en-US";
}
```

Hooked onto `MisaESignOptions`:

```csharp
public MisaESignOtpOptions Otp { get; set; } = new();
```

The validator (`MisaESignOptionsValidator`) gains one new rule: `Otp.DefaultResendLanguage` MUST be non-empty after configuration binding. No format validation — MISA owns the format authority.

### 3.11. `ServiceCollectionExtensions` (Infrastructure.DependencyInjection) — EDITED

In `AddCoreServices(...)`:

| Edit | Detail |
|---|---|
| `services.AddScoped<ExchangeOtp>()` | Forwards to `ExchangeOtp` constructor with `IMisaESignWireClient`, `ITokenCache`, `ITokenCacheKeySelector`, `SingleFlightRefresh` (as a delegate, per R-9), `ISystemClock`, `ICorrelationIdAccessor`. |
| `services.AddScoped<ResendOtp>()` | Forwards to `ResendOtp` constructor with `IMisaESignWireClient`, `IOptions<MisaESignOptions>`, `ICorrelationIdAccessor`. |
| `services.AddScoped<SignPdf>(...)` (existing) | Lambda updated to resolve `IOtpProvider?` from `sp.GetService<IOtpProvider>()` and pass it to the `SignPdf` constructor. Resolves to `null` when no consumer registration exists — the orchestrator's catch-block stays inert. |

`IOtpProvider` itself is NOT registered by the DI extension. Consumers register their own implementation **before** calling `AddMisaConnectESign(...)`. Documented in [quickstart.md](./quickstart.md) and the public-surface contract.

---

## 4. Client layer — facade additions

### 4.1. `IMisaESignClient` (Client) — EDITED

Adds two methods:

```csharp
Task SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken ct = default);
Task<OtpResendResultDto> ResendOtpAsync(string? language = null, CancellationToken ct = default);
```

Both methods read the originating `userName` from the most recently raised `AuthenticationFailedException`'s `Username`. The facade caches the `Username` from the last raised 2FA-required exception in an `AsyncLocal<string?>` so `SignInWithOtpAsync` does NOT take a `userName` argument — the consumer flow is `catch + supply OTP`, not `catch + extract userName + supply userName + supply OTP`. Rationale: the `userName` is already in `MisaESignOptions.UserName`, so requiring the consumer to thread it is redundant and error-prone. The `AsyncLocal<string?>` is set on the `AuthenticationFailedException`'s throw site and cleared on a successful `SignInWithOtpAsync` (or any next successful `SignPdfAsync`).

> **Note (audit)** — the `AsyncLocal<string?>` could in principle leak the `userName` to an unrelated async flow if a consumer interleaves multiple `SignPdfAsync` calls for different accounts in the same execution context. This is acceptable because: (a) the `userName` is not secret (it's already in the consumer's configuration), (b) the `ITokenCacheKeySelector` default already keys by `userName + clientId + base-URL host`, so consumers handling multiple accounts must already swap the selector — multi-account consumers do not rely on the default flow. The audit point is documented here and tested in `tests/MisaConnect.ESign.UnitTests/Authentication/MisaESignClientUsernameContextTests.cs` (new).

### 4.2. `MisaESignClient` (Client) — EDITED

Wires:

| Method | Implementation |
|---|---|
| `SignInWithOtpAsync(otpCode, otpType, remember, ct)` | Reads the captured `userName` from the `AsyncLocal<string?>`. If unset, throws `InvalidOperationException("SignInWithOtpAsync must be invoked only after catching an AuthenticationFailedException with Requires2FA = true.")`. Calls `ExchangeOtp.ExecuteAsync(userName, new OtpSubmission(otpCode, otpType, remember), ct)`. Clears the `AsyncLocal<string?>` on success. |
| `ResendOtpAsync(language, ct)` | Reads the captured `userName`. Same precondition. Calls `ResendOtp.ExecuteAsync(userName, language, ct)`. Returns the result wrapped as `OtpResendResultDto`. |
| `SignPdfAsync(request, ct)` (existing) | On `AuthenticationFailedException` with `Requires2FA = true`, sets the `AsyncLocal<string?>` to `ex.Username` before rethrowing (only if the consumer did NOT register an `IOtpProvider` — when a provider is registered, the catch is internal to `SignPdf` and the consumer never sees the exception). |

### 4.3. `OtpResendResultDto` (Client.Dtos) — NEW

Public consumer-facing shape mirroring `OtpResendResult`:

```csharp
public sealed record OtpResendResultDto(
    bool Success,
    string? RawCode,
    string? UserMsg,
    string? DevMsg,
    string CorrelationId);
```

Mapped 1:1 from `OtpResendResult` in `MisaESignClient.ResendOtpAsync(...)`.

---

## 5. State machine — slice-2 paths

```
┌─────────────────────┐
│ Consumer calls      │
│ SignPdfAsync        │
└──────────┬──────────┘
           │
           ▼
┌─────────────────────────────────────────┐
│ EnsureAccessToken → IMisaESignWireClient│
│ .LoginAsync (cache miss)                │
└──────────┬──────────────────────────────┘
           │
   ┌───────┴───────────────────┐
   │                           │
   ▼ (200 / non-122)           ▼ (4xx errorCode=122)
┌──────────┐         ┌──────────────────────────────┐
│ proceed  │         │ ESignErrorMapper.MapLogin →  │
│ to sign  │         │ AuthenticationFailedException│
│ (slice 1)│         │ (Requires2FA = true,         │
└──────────┘         │  Username = supplied user)   │
                     └──────────┬───────────────────┘
                                │
              ┌─────────────────┴─────────────────┐
              │ provider registered?              │
              └─────────────────┬─────────────────┘
                 yes            │            no
                                │
              ┌─────────────────┴────────────────┐
              ▼                                  ▼
   ┌──────────────────────┐         ┌───────────────────────────┐
   │ SignPdf catch-block: │         │ Exception escapes to      │
   │ provider.ProvideAsync│         │ consumer. Consumer calls  │
   │ → OtpSubmission      │         │ SignInWithOtpAsync(...)   │
   └──────────┬───────────┘         └──────────┬────────────────┘
              │                                │
              └────────────┬───────────────────┘
                           ▼
              ┌─────────────────────────────────────┐
              │ ExchangeOtp.ExecuteAsync (single-   │
              │ flight per cache key) →             │
              │ MisaESignWireClient.TwoFactorAuth   │
              └────────────────┬────────────────────┘
                               │
              ┌────────────────┴─────────────────┐
              │                                  │
              ▼ (200)                            ▼ (4xx)
   ┌─────────────────────┐         ┌──────────────────────────────────────┐
   │ AuthSession→        │         │ OtpErrorMapper.MapTwoFactor:         │
   │ AccessToken→        │         │   122 again → AuthenticationFailed   │
   │ ITokenCache.SetAsync│         │   InvalidOtp* → InvalidOtpException  │
   │ (FR-035 same shape) │         │   ExpiredOtp* → ExpiredOtpException  │
   └──────────┬──────────┘         │   Exhausted* → Exhausted...          │
              │                    │   else → OtpRejectedException        │
              ▼                    │                                      │
   ┌─────────────────────┐         │ (cache NOT written; exception        │
   │ (transparent path)  │         │  propagates; consumer's next         │
   │ SignPdf recurses    │         │  SignPdfAsync restarts from login)   │
   │ once. (explicit     │         └──────────────────────────────────────┘
   │ path) consumer's    │
   │ next SignPdfAsync   │
   │ reuses cache.       │
   └─────────────────────┘
```

The `ResendOtp` path is independent and orthogonal: the consumer (or the provider via `RequestResendAsync`) may invoke it at any time between a 2FA-required signal and a successful `SignInWithOtpAsync`. It does not consult or update the cache.

---

## 6. Validation summary (slice-2 only)

- `OtpSubmission.Code`: non-null, non-whitespace. `OtpSubmission` is a record — validation lives on `OtpSubmissionValidator` (new) in `Application.Validation`. Wired in via `services.AddScoped<OtpSubmissionValidator>()`. Called from `MisaESignClient.SignInWithOtpAsync(...)` and from `SignPdf`'s catch-block (immediately after `_otpProvider.ProvideAsync(...)` returns).
- `OtpSubmission.OtpType`: must be one of `OtpDeliveryChannel.SmsOrEmail` / `OtpDeliveryChannel.Authenticator`. Enforced by the compiler (closed enum) — the validator does not need a runtime check.
- `OtpSubmission.Remember`: required boolean — no nullable, no default. The validator does not need a runtime check.
- `ResendOtpAsync.language`: NOT validated client-side per FR-037. The validator does not touch `language`.
- `MisaESignOptions.Otp.DefaultResendLanguage`: validated by `MisaESignOptionsValidator` (existing) — must be non-empty after binding.

---

## 7. Out-of-scope confirmation

The following entities and behaviors are **not** introduced or modified by slice 2 (per [research.md R-11](./research.md#r-11-scope-discipline--what-slice-2-explicitly-does-not-touch)):

- `Certificate`, `CertificateChain`, `KeyStatus` (slice 1) — unchanged.
- `PdfDocument`, `SignedDocument`, `SignatureInfo`, `SignatureDescription`, `SignTransaction`, `SignStatus`, `HashAlgorithm` (slice 1) — unchanged.
- `ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`, `ISystemClock`, `ICorrelationIdAccessor`, `IMisaESignWireClient` (slice 1) — interface shapes unchanged except for `IMisaESignWireClient` which gains two new methods (additive, non-breaking).
- `MisaESignOptions.Polling`, `MisaESignOptions.TransportRetry`, `MisaESignOptions.Errors` — unchanged.
- Wire DTOs: `LoginRequestDto`, `LoginResponseDto`, `RefreshTokenRequestDto`/`ResponseDto`, `CertificateDto`, `HashRequestDto`/`ResponseDto`, `SignHashRequestDto`/`ResponseDto`, `SignStatusResponseDto`, `AttachmentRequestDto`/`ResponseDto`, `ResponseErrorDto` — unchanged.

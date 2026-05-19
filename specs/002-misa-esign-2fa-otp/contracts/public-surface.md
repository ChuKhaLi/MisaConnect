# Public surface contract — `MisaConnect.ESign` slice 2 additions

This file pins the slice-2 deltas on the public types consumers depend on. Per Constitution Principle II, every change here is a semver event for the `MisaConnect.ESign` NuGet package. Slice 2 is purely additive — no breaking changes — so it ships as a minor pre-release bump (`2.0.0-preview.2`).

The slice-1 public surface listed in [specs/001-misa-esign-pdf-sign-flow/contracts/public-surface.md](../../001-misa-esign-pdf-sign-flow/contracts/public-surface.md) remains authoritative for everything not enumerated here.

---

## 1. Facade additions — `IMisaESignClient`

`namespace MisaConnect.ESign.Client`

Two new methods on the existing interface:

```csharp
public interface IMisaESignClient
{
    // (existing) Task<SignPdfResultDto> SignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Complete an in-progress 2FA challenge. The consumer must have caught
    /// an <see cref="AuthenticationFailedException"/> with
    /// <see cref="AuthenticationFailedException.Requires2FA"/> == true from a
    /// preceding call (typically <see cref="SignPdfAsync"/>); the userName
    /// from that challenge is captured automatically. On success, the SDK
    /// caches the returned tokens and the next call resumes signing without
    /// further 2FA prompts.
    /// </summary>
    /// <exception cref="InvalidOperationException">No pending 2FA challenge captured for the current execution context.</exception>
    /// <exception cref="InvalidOtpException">The submitted OTP value is wrong.</exception>
    /// <exception cref="ExpiredOtpException">The submitted OTP value is stale.</exception>
    /// <exception cref="ExhaustedOtpAttemptsException">MISA refused further attempts on this challenge.</exception>
    /// <exception cref="OtpRejectedException">Any other typed OTP rejection.</exception>
    /// <exception cref="AuthenticationFailedException">MISA re-issued the 2FA challenge on the /two-factor-auth response itself (edge case — consumer must restart from a fresh /login-api call by retrying SignPdfAsync).</exception>
    /// <exception cref="ESignTransportException">Transport-retry budget exhausted.</exception>
    Task SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken ct = default);

    /// <summary>
    /// Ask MISA to redeliver the OTP for the in-progress 2FA challenge. The
    /// userName from the captured challenge is threaded into the request body.
    /// Defaults <paramref name="language"/> to <c>MisaESignOptions.Otp.DefaultResendLanguage</c>
    /// (which defaults to "en-US"). The SDK does NOT validate the language
    /// value client-side — MISA is the authority.
    /// </summary>
    /// <remarks>
    /// Resend failures returned by MISA in a typed response envelope surface
    /// as <c>OtpResendResultDto { Success = false, ... }</c> — this method
    /// does NOT throw on documented MISA rejections. Transport failures DO
    /// throw <see cref="ESignTransportException"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No pending 2FA challenge captured for the current execution context.</exception>
    /// <exception cref="ESignTransportException">Transport-retry budget exhausted.</exception>
    Task<OtpResendResultDto> ResendOtpAsync(string? language = null, CancellationToken ct = default);
}
```

Both methods consume the captured `userName` from the most recently raised `AuthenticationFailedException(Requires2FA = true)` in the current execution context (the facade keeps it in an `AsyncLocal<string?>` — see [data-model.md §4.1](../data-model.md#41-imisaesignclient-client--edited) for the audit note). Consumers do not pass `userName` directly.

---

## 2. New optional port — `IOtpProvider`

`namespace MisaConnect.ESign.Application.Abstractions`

```csharp
public interface IOtpProvider
{
    /// <summary>
    /// Produce an OTP submission for a captured 2FA challenge. Called by the
    /// SDK's <c>SignPdf</c> orchestrator when it encounters a 2FA-required
    /// signal AND the consumer has registered an <see cref="IOtpProvider"/>.
    /// </summary>
    Task<OtpSubmission> ProvideAsync(OtpChallenge challenge, CancellationToken ct);

    /// <summary>
    /// Request OTP re-delivery for a captured 2FA challenge. Called by the
    /// SDK when a transparent-flow provider implementation needs a fresh OTP
    /// (e.g. the original was lost). Provider implementations that don't
    /// support resend may throw <see cref="NotSupportedException"/>; the SDK
    /// surfaces that to the consumer as-is.
    /// </summary>
    Task<OtpResendResult> RequestResendAsync(OtpChallenge challenge, string? language, CancellationToken ct);
}

public sealed record OtpChallenge(string UserName, string CorrelationId);

public sealed record OtpSubmission(string Code, OtpDeliveryChannel OtpType, bool Remember);

public sealed record OtpResendResult(
    bool Success,
    string? RawCode,
    string? UserMsg,
    string? DevMsg,
    string CorrelationId);
```

Consumers register an implementation BEFORE calling `AddMisaConnectESign(...)`:

```csharp
services.AddSingleton<IOtpProvider, MyAuthenticatorOtpProvider>();
services.AddMisaConnectESign(configuration);
```

The DI extension does NOT register a default `IOtpProvider`. If no consumer registration exists, the `SignPdf` orchestrator falls back to rethrowing the 2FA-required `AuthenticationFailedException` and consumers complete the flow via the explicit `SignInWithOtpAsync` + `ResendOtpAsync` pair.

---

## 3. New domain enum — `OtpDeliveryChannel`

`namespace MisaConnect.ESign.Domain.Authentication`

```csharp
public enum OtpDeliveryChannel : byte
{
    /// <summary>OTP was delivered via SMS or email.</summary>
    SmsOrEmail = 0,

    /// <summary>OTP was obtained from an authenticator app (TOTP).</summary>
    Authenticator = 1,
}
```

Wire serialization is the integer value verbatim (System.Text.Json default behavior). No custom converter.

---

## 4. New typed exceptions — `Domain.Errors`

`namespace MisaConnect.ESign.Domain.Errors`

All four are sealed subclasses of `AuthenticationFailedException` (so consumers' existing `catch (AuthenticationFailedException)` blocks still pick them up — preserves the slice-1 ergonomics).

```csharp
public sealed class InvalidOtpException : AuthenticationFailedException
{
    public InvalidOtpException(string? rawCode, string detail, string correlationId, Exception? inner = null)
        : base(rawCode ?? "InvalidOtp", detail, correlationId, requires2FA: false, inner) { }
}

public sealed class ExpiredOtpException : AuthenticationFailedException
{
    public ExpiredOtpException(string? rawCode, string detail, string correlationId, Exception? inner = null)
        : base(rawCode ?? "ExpiredOtp", detail, correlationId, requires2FA: false, inner) { }
}

public sealed class ExhaustedOtpAttemptsException : AuthenticationFailedException
{
    public ExhaustedOtpAttemptsException(string? rawCode, string detail, string correlationId, Exception? inner = null)
        : base(rawCode ?? "ExhaustedOtpAttempts", detail, correlationId, requires2FA: false, inner) { }
}

public sealed class OtpRejectedException : AuthenticationFailedException
{
    public OtpRejectedException(string? rawCode, string detail, string correlationId, Exception? inner = null)
        : base(rawCode ?? "OtpRejected", detail, correlationId, requires2FA: false, inner) { }
}
```

Why subclasses (not parallel types extending `ESignException`)? Two reasons: (a) consumers who already catch `AuthenticationFailedException` keep working without changes; (b) `ESignErrorCategory.Authentication` is the right category for "OTP was rejected" — slice 2 doesn't need a new category.

`AuthenticationFailedException` (slice-1 type) gains one additive change:

```csharp
public class AuthenticationFailedException : ESignException
{
    public AuthenticationFailedException(
        string? rawCode,
        string detail,
        string correlationId,
        bool requires2FA = false,
        string username = "",      // NEW — optional, defaults to empty
        Exception? inner = null)
        : base(ESignErrorCategory.Authentication, rawCode, detail, correlationId, inner)
    {
        Requires2FA = requires2FA;
        Username = username ?? string.Empty;
    }

    public bool Requires2FA { get; }
    public string Username { get; }   // NEW — non-null, populated only on the 122 path
}
```

The new optional parameter is at position 5 (after `requires2FA`), preserving call-site compatibility for existing constructors that did not pass it.

---

## 5. New Client DTO — `OtpResendResultDto`

`namespace MisaConnect.ESign.Client.Dtos`

```csharp
public sealed record OtpResendResultDto(
    bool Success,
    string? RawCode,
    string? UserMsg,
    string? DevMsg,
    string CorrelationId);
```

1:1 wrapper over `Application.Abstractions.OtpResendResult`. The Client layer owns its own DTO so consumers don't need a `using MisaConnect.ESign.Application.Abstractions;` (which would expose them to other Application-layer types they shouldn't depend on).

---

## 6. Options additions — `MisaESignOptions.Otp`

`namespace MisaConnect.ESign.Infrastructure.Configuration`

```csharp
public sealed class MisaESignOptions
{
    // (existing fields...)

    public MisaESignOtpOptions Otp { get; set; } = new();   // NEW
}

public sealed class MisaESignOtpOptions
{
    /// <summary>
    /// Default value supplied to /resend-otp-auth when the consumer's
    /// ResendOtpAsync call does not override the language. Per FR-037, the
    /// SDK does not validate this value — MISA is the authority on supported
    /// values. Default: "en-US" (the only example value the MISA doc lists).
    /// </summary>
    public string DefaultResendLanguage { get; set; } = "en-US";
}
```

`MisaESignOptionsValidator` gains one rule: `Otp.DefaultResendLanguage` must be non-empty after binding.

---

## 7. DI extension — no signature change

`namespace MisaConnect.ESign.Client.DependencyInjection`

`ServiceCollectionExtensions.AddMisaConnectESign(...)` signatures (both overloads) are unchanged. Internally the extension:

- Registers `ExchangeOtp` and `ResendOtp` use cases (scoped).
- Honors a consumer-registered `IOtpProvider` via `sp.GetService<IOtpProvider>()` — no `TryAdd` for `IOtpProvider` because there is no default; absence is meaningful.
- Resolves the `IOtpProvider?` at facade-construction time and threads it into the `SignPdf` orchestrator's constructor.

No new public DI method introduced.

---

## 8. Semver impact summary

| Change | Semver classification |
|---|---|
| `IMisaESignClient.SignInWithOtpAsync(...)` added | Minor (additive) |
| `IMisaESignClient.ResendOtpAsync(...)` added | Minor (additive) |
| `IOtpProvider` (port + records) added | Minor (additive) |
| `OtpDeliveryChannel` enum added | Minor (additive) |
| `InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`, `OtpRejectedException` added | Minor (additive) |
| `AuthenticationFailedException.Username` property added (constructor gains optional parameter) | Minor (additive — default value preserves call-site compatibility) |
| `OtpResendResultDto` added | Minor (additive) |
| `MisaESignOptions.Otp` block added | Minor (additive — default `Otp = new()` preserves bindings) |
| `IMisaESignWireClient.TwoFactorAuthAsync(...)` added | Minor (additive on a not-yet-1.0 interface; documented in CHANGELOG) |
| `IMisaESignWireClient.ResendOtpAsync(...)` added | Minor (additive on a not-yet-1.0 interface; documented in CHANGELOG) |

Package version: `2.0.0-preview.1` → `2.0.0-preview.2`. `CHANGELOG.md` entry under `[Unreleased]` enumerates each addition with a one-line description.

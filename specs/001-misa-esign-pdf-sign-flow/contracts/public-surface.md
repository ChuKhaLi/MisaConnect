# Public surface contract — `MisaConnect.ESign`

This file pins the public types consumers depend on. Per Constitution Principle II, every change here is a semver event for the `MisaConnect.ESign` NuGet package. Per Principle VII, breaking changes only land in major versions; non-breaking additions land in minor versions.

> **Status note** — `MisaConnect.ESign` ships first as `2.0.0-preview.N`. The "stable" line begins with `2.0.0`. Slice 1 introduces every type below for the first time, so there are no breakage concerns within this slice.

---

## 1. DI entry point

`namespace MisaConnect.ESign.Client.DependencyInjection`

```csharp
public static class ServiceCollectionExtensions
{
    // Binds Misa:ESign from configuration, registers MisaESignOptions + IValidateOptions,
    // registers all infrastructure adapters and the facade. The configuration must include
    // Misa:ESign:Environment, Misa:ESign:BaseUrl, Misa:ESign:UserName, Misa:ESign:Password,
    // Misa:ESign:ClientId, Misa:ESign:ClientKey. Validation runs at startup (.ValidateOnStart()).
    public static IServiceCollection AddMisaConnectESign(this IServiceCollection services, IConfiguration configuration);

    // Code-first overload (parallels the eInvoice DI extension's Action<TOptions> overload).
    public static IServiceCollection AddMisaConnectESign(this IServiceCollection services, Action<MisaESignOptions> configure);
}
```

`AddMisaConnectESign` is the **only** public DI entry point. The Infrastructure-level `ServiceCollectionExtensions.AddMisaConnectESignCore` is `internal`; consumers cannot bypass option validation.

Both overloads register:
- `MisaESignOptions` (options pattern)
- Defaults for every port (see §3) — only if the consumer has not already registered their own
- Typed `HttpClient` for `MisaESignWireClient` with the handler pipeline (`TransientFailureRetryHandler → RemoteSigningAuthHandler → ClientHeadersHandler`)
- The `IMisaESignClient` facade

---

## 2. Facade — `IMisaESignClient`

`namespace MisaConnect.ESign.Client`

```csharp
public interface IMisaESignClient
{
    /// <summary>
    /// Sign a PDF end-to-end via MISA eSign RemoteSigning. Orchestrates login (or
    /// cached-token reuse), certificate selection, server-side hashing, signing,
    /// status polling, and signature attachment. Returns the signed PDF bytes.
    /// </summary>
    /// <exception cref="AuthenticationFailedException">
    /// Login or refresh was rejected. Inspect <c>Requires2FA</c> to detect "MISA wants 2FA".
    /// </exception>
    /// <exception cref="NoActiveCertificateException">No <c>ACTIVE</c> cert on the account.</exception>
    /// <exception cref="SignRejectedException">/Signing/hash rejected the submission.</exception>
    /// <exception cref="SignTerminalStateException">Sign transaction terminated in FAILED or CANCELLED.</exception>
    /// <exception cref="SignTimeoutException">Polling exceeded the configured TotalTimeout.</exception>
    /// <exception cref="ESignTransportException">Transport-retry budget exhausted.</exception>
    /// <exception cref="ESignException">Any other typed MISA error.</exception>
    Task<SignPdfResultDto> SignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default);
}
```

`SignPdfAsync` is the slice-1 surface. Listing certificates, hashing a doc without signing, or polling an arbitrary transaction are not exposed in slice 1.

---

## 3. Port interfaces (Application layer, public for consumer swap)

`namespace MisaConnect.ESign.Application.Abstractions`

```csharp
public interface ITokenCache
{
    Task<AccessToken?> TryGetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, AccessToken value, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}

public interface ITokenCacheKeySelector
{
    string Compose(MisaESignOptions options);
}

public interface ICertificateSelector
{
    /// <summary>
    /// Pick one certificate. The SDK has already filtered to KeyStatus == ACTIVE
    /// and asserted the list is non-empty before this is called.
    /// </summary>
    Task<Certificate> SelectAsync(IReadOnlyList<Certificate> activeCertificates, CancellationToken ct);
}

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ICorrelationIdAccessor
{
    string Current { get; }
}

public sealed record AccessToken(
    string Value,
    string RawAccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string UserId,
    string Username);
```

Consumers register custom adapters BEFORE calling `AddMisaConnectESign`:

```csharp
services.AddSingleton<ITokenCache, MyRedisTokenCache>();
services.AddSingleton<ICertificateSelector, PickByIssuerDnSelector>();
services.AddMisaConnectESign(configuration);
```

The DI extension uses `services.TryAddSingleton<TPort, TDefault>()`-style registration so consumer registrations win.

---

## 4. Configuration — `MisaESignOptions`

`namespace MisaConnect.ESign.Infrastructure.Configuration`

```csharp
public sealed class MisaESignOptions
{
    public const string SectionName = "Misa:ESign";
    public const string ProductionHost = "esignapp.misa.vn";

    public ESignEnvironment Environment { get; set; }   // Sandbox | Production
    public string BaseUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientKey { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public MisaESignPollingOptions Polling { get; set; } = new();
    public MisaESignTransportRetryOptions TransportRetry { get; set; } = new();
    public MisaESignErrorOptions Errors { get; set; } = new();
}

public sealed class MisaESignPollingOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

public sealed class MisaESignTransportRetryOptions
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(2);
}

public sealed class MisaESignErrorOptions
{
    public bool IncludeRawErrorMessage { get; set; } = false;
}

public enum ESignEnvironment { Sandbox = 0, Production = 1 }
```

Validation (`MisaESignOptionsValidator`) runs at startup via `.ValidateOnStart()`. See [research.md R-4](../research.md#r-4-configuration-shape--misaesignoptions-mirrors-misaeinvoiceoptions).

---

## 5. Public DTOs — `MisaConnect.ESign.Client.Dtos`

See [data-model.md §5](../data-model.md#5-client-facing-dtos-clientdtos) for the full shape. Summary:

- `SignPdfRequestDto` — input: PDF bytes + signature display metadata + signer info + signing context.
- `SignPdfResultDto` — output: signed PDF bytes + transaction ID + cert alias + completion timestamp.
- `SignaturePosInfoDto` — additional signature positions when one signature spans multiple pages.
- `CertificateDto` — internal use mostly; surfaced as a record-shape for a future "list certs" facade method.

---

## 6. Public exceptions — `MisaConnect.ESign.Domain.Errors`

See [data-model.md §1.10](../data-model.md#110-error-hierarchy-domainerrors) for the hierarchy and properties. Summary of the public types:

- `ESignException` (abstract base) — `Category`, `RawCode`, `CorrelationId`, `Detail`.
- `AuthenticationFailedException` — adds `Requires2FA`.
- `NoActiveCertificateException`.
- `SignRejectedException` — adds `RequiresUserCertSetup`.
- `SignTerminalStateException` — adds `TerminalStatus`, `TransactionId`.
- `SignTimeoutException` — adds `TransactionId`, `ElapsedTime`.
- `ESignTransportException` — adds `LastStatusCode`, `AttemptCount`.

Every public exception:
1. Is `sealed` except the abstract `ESignException` base.
2. Carries `CorrelationId` as a required, non-empty string.
3. Has a public constructor taking the full property set so consumers can construct in tests.

---

## 7. Thread-safety guarantees (consumer-visible)

`IMisaESignClient` is safe for use as a DI singleton with multiple in-flight `SignPdfAsync` calls — including across multiple cache keys.

Default port adapters (`InMemoryTokenCache`, `DefaultTokenCacheKeySelector`, `FirstActiveCertificateSelector`, `SystemClock`) are thread-safe and registered as singletons.

Consumer-supplied adapters must honor the same contract; the SDK does not introduce extra serialization around them. Concurrent 401 observers for the same cache key result in exactly one outbound `/auth/refreshtoken` request (FR-028 / SC-007).

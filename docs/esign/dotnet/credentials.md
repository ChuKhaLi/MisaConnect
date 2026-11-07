---
title: Credentials
parent: .NET
grand_parent: ESign
nav_order: 6
---

# Credentials
{: .no_toc }

A MISA principal is `ClientId`, `ClientKey`, `UserName` and `Password`. The concept (static vs per-call principals) is on the [ESign page](../index#credentials-modes). This page covers the .NET seams: `CredentialsMode`, `IMisaCredentialsAccessor`, `IWebhookCredentialsResolver` and custom token caches.

1. TOC
{:toc}

## Static credentials (default)

With `Misa:ESign:CredentialsMode` absent or `Static`, the four credentials come from the `Misa:ESign` options and the startup validator requires all four. One principal serves the whole process. This is the setup in [Getting started](./getting-started).

## Dynamic credentials: `IMisaCredentialsAccessor`

To supply MISA credentials per signing call (for example, each signer has their own MISA account), set `CredentialsMode = Dynamic` and register a custom `IMisaCredentialsAccessor` (namespace `MisaConnect.ESign.Application.Abstractions`). Its `Get()` returns a `MisaCredentials(ClientId, ClientKey, UserName, Password)` and is consulted **per call**, inside the awaited pipeline, for the login body, the `x-clientId` / `x-clientKey` headers and the token-cache key. So tokens and [OTP challenges](./two-factor#rules-for-the-explicit-flow) are kept per principal. `MisaCredentials.ToString()` redacts `ClientKey` and `Password`; the SDK never logs the values.

`CredentialsMode = Dynamic` itself only relaxes the startup validator (the four static values are no longer required) and selects the webhook-credentials behaviour below; it is the registered accessor that changes where credentials come from. The default accessor (`OptionsMisaCredentialsAccessor`, internal) reads the options, so the default path is unchanged from earlier releases.

### Register-before only

The default accessor is registered with `TryAddSingleton`, so a consumer override wins only when it is registered **before** `AddMisaConnectESign`:

<!-- snippet: partial -->
```csharp
services.AddSingleton<IMisaCredentialsAccessor, MyAmbientCredentialsAccessor>();
services.AddMisaConnectESign(config); // CredentialsMode: Dynamic
```

Unlike most other ports, this one is **register-before only** — do not register it "just after". A plain `AddSingleton` after the SDK's own `TryAddSingleton` appends a *second* descriptor: the options-default accessor stays constructible and both accessors surface through `IEnumerable<IMisaCredentialsAccessor>`, so it is undefined which one resolves. Registering before guarantees exactly one accessor is in effect.

### Lifetime contract

The accessor **must** be singleton-registrable and resolve the current call's credentials from ambient (`AsyncLocal`) or options state read inside `Get()`. It **must not** be scoped — the SDK resolves the accessor from singleton collaborators, so a scoped registration is a captive-dependency failure at build. `Get()` is read per call regardless of lifetime, so an ambient-backed singleton is sufficient and the only safe shape. Do not cache the result on the singleton.

If the resolved `UserName` or `ClientId` is empty (for example, the ambient value was not set around the call), the call fails with `InvalidOperationException` instead of sharing a token-cache slot across signers.

### Example: an `AsyncLocal` accessor

```csharp
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Infrastructure.Configuration;

var services = new ServiceCollection();
services.AddSingleton<IMisaCredentialsAccessor, AmbientCredentialsAccessor>(); // BEFORE AddMisaConnectESign
services.AddMisaConnectESign(o =>
{
    o.Environment = ESignEnvironment.Sandbox;
    o.BaseUrl = "https://<sandbox-host-issued-by-misa>/";
    o.CredentialsMode = CredentialsMode.Dynamic; // the four static values are not required
});

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var esign = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

MisaCredentials signer = await LoadSignerCredentialsAsync("nguyenvana");
SignPdfRequestDto request = BuildRequest();

using (AmbientCredentialsAccessor.Use(signer))
{
    var result = await esign.SignPdfAsync(request); // signs as "nguyenvana"
}

static Task<MisaCredentials> LoadSignerCredentialsAsync(string user) =>
    throw new NotImplementedException("read from your vault");

static SignPdfRequestDto BuildRequest() =>
    throw new NotImplementedException("see Operations");

internal sealed class AmbientCredentialsAccessor : IMisaCredentialsAccessor
{
    private static readonly AsyncLocal<MisaCredentials?> Current = new();

    public static IDisposable Use(MisaCredentials credentials)
    {
        var previous = Current.Value;
        Current.Value = credentials;
        return new Restore(previous);
    }

    public MisaCredentials Get() =>
        Current.Value ?? throw new InvalidOperationException("No MISA credentials set for this call.");

    private sealed class Restore(MisaCredentials? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
```

## Webhook credentials resolver

A webhook arrives without your caller's ambient context, so in `Dynamic` mode the finalize step cannot use `IMisaCredentialsAccessor`. Instead, the signing session records the `clientId` and `userName` that began it (never secrets), and the SDK asks `IWebhookCredentialsResolver.ResolveAsync(clientId, userName, ct)` for that principal's credentials.

- The default resolver returns the options credentials in `Static` mode and `null` in `Dynamic` mode, so Dynamic-mode finalize answers `webhook.finalize_failed` until you register your own.
- Register it **before** `AddMisaConnectESign` (the default uses `TryAddSingleton`).
- Return credentials for the same `clientId` and `userName`, otherwise finalize fails (`CredentialsMismatch`). Return `null` when none are available.
- In `Static` mode the SDK uses the options credentials and never calls the resolver, even a custom one.

```csharp
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Client.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<IWebhookCredentialsResolver, VaultWebhookCredentialsResolver>(); // before only
services.AddMisaConnectESign(o => { /* … CredentialsMode = Dynamic … */ });

internal sealed class VaultWebhookCredentialsResolver : IWebhookCredentialsResolver
{
    public async ValueTask<MisaCredentials?> ResolveAsync(string clientId, string userName, CancellationToken ct)
    {
        await Task.Yield(); // look the principal up in your vault here
        return null;        // null => webhook.finalize_failed
    }
}
```

More on Dynamic mode with webhooks (the `clientId` check, custom session stores): [Webhook mode → Dynamic mode and webhooks](./webhooks#dynamic-mode-and-webhooks).

## Tokens and `ITokenCache`

The SDK caches the tokens of each principal in `ITokenCache` under the key from `ITokenCacheKeySelector` — by default `{UserName}|{ClientId}|{host}`. How tokens are used:

- A cached token with 30 s or less left is refreshed proactively. Proactive and 401-driven refreshes for one cache key run in a single flight, so concurrent flows make one refresh call. A refresh is not cancelled when the caller that started it cancels; each waiter stops on its own `CancellationToken`, and the refresh completes for the others.
- `expiresIn` lifetime: a missing value means 300 s; 1–59 s is raised to 60 s so the refresh skew leaves a usable window; an explicit 0 or less is honoured as already expired (an expired refresh result falls back to login). It is read leniently: a numeric string such as `"3600"` is accepted, any other odd value counts as missing. This applies to login, two-factor and refresh.
- On a 401 the SDK refreshes and retries once. If the refresh fails for any reason, it reuses a fresh token another flow stored meanwhile, or evicts the stale token, logs in once more and retries; it fails with `Authentication` only if that fails too.
- Login, refresh and fallback calls carry the correlation ID of the flow that started them.

The default `InMemoryTokenCache` is per process. Replace it (a Redis or other distributed cache) to share tokens across instances; register it before or after `AddMisaConnectESign`.

### `ITokenCache` — custom caches

A custom token cache implements `TryGetAsync`, `SetAsync` and `RemoveAsync`. Since 2.7, the SDK evicts a stale token through `RemoveIfMatchesAsync(key, expectedValue, ct)`, which removes the entry only while it still holds the token that MISA rejected. The built-in in-memory cache does this atomically, so a token another flow has just stored is never evicted. The default implementation, used by a custom cache that does not override it, reads, compares and removes, which leaves a small race in which a just-stored token can be evicted (costing one extra login). A distributed cache that supports compare-and-delete (for example a Redis Lua script or `WATCH` / `MULTI`) should override it. Token-cache calls made during a shared refresh are not cancellable, so a custom cache must bound its own I/O with timeouts.

- **Decorators.** A cache that wraps another (for logging, metrics or a two-level cache) should forward `RemoveIfMatchesAsync` to the inner cache. If it does not, the default implementation runs against the wrapper, and the inner cache's atomic compare-and-remove is lost.
- **Test doubles.** A mock of `ITokenCache` (Moq, NSubstitute and similar) usually does not run the default body of `RemoveIfMatchesAsync`: an un-configured call just returns a completed task. A test that expects `RemoveAsync` to be called on the 401-fallback or failed-refresh path should set up or verify `RemoveIfMatchesAsync` instead.

A decorator that forwards every member:

<!-- snippet: partial -->
```csharp
using MisaConnect.ESign.Application.Abstractions;

internal sealed class CountingTokenCache(ITokenCache inner) : ITokenCache
{
    public int Hits { get; private set; }

    public async Task<AccessToken?> TryGetAsync(string key, CancellationToken ct)
    {
        var token = await inner.TryGetAsync(key, ct);
        if (token is not null) Hits++;
        return token;
    }

    public Task SetAsync(string key, AccessToken value, CancellationToken ct) => inner.SetAsync(key, value, ct);

    public Task RemoveAsync(string key, CancellationToken ct) => inner.RemoveAsync(key, ct);

    // Forward explicitly, so the inner cache's atomic compare-and-remove is kept.
    public Task RemoveIfMatchesAsync(string key, string expectedValue, CancellationToken ct) =>
        inner.RemoveIfMatchesAsync(key, expectedValue, ct);
}
```

`AccessToken` holds `Value`, `RawAccessToken`, `RefreshToken`, `ExpiresAtUtc`, `UserId` and `Username`; a distributed cache must store and return all of them. Treat the cache contents as secrets.

See [Architecture → ESign ports](../../architecture#esign-ports) for the full port list.

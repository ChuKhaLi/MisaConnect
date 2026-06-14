# Quickstart: Per-User Credentials Seam

Two ways to use `MisaConnect.ESign` after `2.2.0`. Existing consumers do nothing (Static is the default).

## 1. Existing single-account consumer (Static — no change)

No code or config change. `CredentialsMode` defaults to `Static`; the four credentials are read from options and required at startup, exactly as in 2.1.1.

```csharp
services.AddMisaConnectESign(opts =>
{
    opts.Environment = ESignEnvironment.Sandbox;
    opts.BaseUrl   = baseUrl;
    opts.ClientId  = clientId;
    opts.ClientKey = clientKey;
    opts.UserName  = userName;
    opts.Password  = password;
});
// header injection, login body, and token-cache key are byte-identical to 2.1.1
```

## 2. Per-user (Dynamic) consumer

Register a custom accessor **before** `AddMisaConnectESign` (so the SDK's `TryAddSingleton` yields to it), set `CredentialsMode = Dynamic`, and wrap each awaited SDK call in an ambient that supplies the current signer's credentials — the same shape as the existing certificate-selection ambient.

```csharp
// ambient credential selection (mirrors the existing cert-selection ambient)
public static class MisaCredentialSelection
{
    private static readonly AsyncLocal<MisaCredentials?> _current = new();
    public static MisaCredentials? Current => _current.Value;
    public static IDisposable Use(MisaCredentials creds)
    {
        var prev = _current.Value;
        _current.Value = creds;
        return new Pop(prev);
    }
    private sealed class Pop(MisaCredentials? prev) : IDisposable
    {
        public void Dispose() => _current.Value = prev;
    }
}

internal sealed class MyMisaCredentialsAccessor : IMisaCredentialsAccessor
{
    public MisaCredentials Get() =>
        MisaCredentialSelection.Current
        ?? throw new InvalidOperationException(
            "No MISA credentials set for the current call. Wrap the SDK call in MisaCredentialSelection.Use(...).");
}
```

```csharp
// Program.cs — register overrides BEFORE AddMisaConnectESign so TryAdd yields to them
services.AddSingleton<IMisaCredentialsAccessor, MyMisaCredentialsAccessor>();
services.AddMisaConnectESign(opts =>
{
    opts.Environment     = ESignEnvironment.Sandbox;
    opts.BaseUrl         = baseUrl;                  // only global keys remain
    opts.CredentialsMode = CredentialsMode.Dynamic;  // skip the four static-cred checks
    // ClientId/ClientKey/UserName/Password intentionally left empty
});
```

```csharp
// per call: supply the signer's credentials for the duration of one awaited call
var creds = DecryptCredentialsForCurrentSigner(); // plaintext, scope-lived
using (MisaCredentialSelection.Use(creds))
{
    return await client.SignPdfAsync(dto); // accessor read per-call inside the pipeline
}
```

Set the ambient around **every** SDK entry you call (sign, list-certs, OTP exchange/resend) so the per-user token-cache key is composed correctly and the `x-clientId`/`x-clientKey` headers match.

## Verify (acceptance highlights)

- Two different signers ⇒ two distinct logins, two distinct `x-clientId`/`x-clientKey` header sets, two distinct token-cache keys (no collision).
- Missing/empty credentials ⇒ `DefaultTokenCacheKeySelector.Compose()` throws `InvalidOperationException` (no silent `||host` collapse).
- `new MisaCredentials("cid","SECRET_KEY","user","SECRET_PWD").ToString()` contains neither `SECRET_KEY` nor `SECRET_PWD`.
- Default (Static, no accessor) path passes the existing E2E suite unmodified.

## Run the tests

```text
dotnet test tests/MisaConnect.ESign.UnitTests
dotnet test tests/MisaConnect.ESign.IntegrationTests
dotnet format MisaConnect.slnx
```

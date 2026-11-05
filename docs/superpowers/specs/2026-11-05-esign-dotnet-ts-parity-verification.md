# Verification of the TS-reported .NET ESign defects

Evidence for [the back-port design](2026-11-05-esign-dotnet-ts-parity-design.md).

Checked against `main` at `680c0d7` on 2026-11-05 by reading the code. Two read-only passes were run, and the key lines were spot-checked by hand. Paths are relative to `src/dotnet/` unless they start with `tests/` or `docs/`. Line numbers are as of that commit.

## Auth and transport

The HTTP pipeline on the typed `MisaESignWireClient` runs `TransientFailureRetryHandler` (outermost), then `RemoteSigningAuthHandler`, then `ClientHeadersHandler` (`Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs:329-331`). Every wire method sets `AuthorizationRM` itself through `ApplyAuth` (`MisaESignWireClient.cs:765-769`).

| # | Verdict | Current behaviour | Evidence |
|---|---|---|---|
| 1 | CONFIRMED | Use cases fetch the token once and pass `token.Value` to every call. On a 401, the handler refreshes, writes the cache and retries only that one request. The handler adds a token only when the header is absent, and the wire client always sets it. So later calls send the stale token, get a 401 and refresh again. | `Application/UseCases/SignPdf.cs:103,105,109,117,133,141`; `Infrastructure/Http/RemoteSigningAuthHandler.cs:53,90-94` |
| 2 | CONFIRMED | On a 401 with no refresh token, it throws `Authentication("401")`. If the refresh throws an `ESignException`, it evicts the cache entry and rethrows; there is no new login. A second 401 after a refresh also throws. **New:** an unparseable 2xx refresh body throws `InvalidOperationException`, which the handler does not catch, so the stale entry is not evicted. `EnsureAccessToken` does fall back to login, but only for an expired token, not after a 401. | `RemoteSigningAuthHandler.cs:68-73,84-87,95-101`; `Infrastructure/ESign/Mapping/AuthSessionMapper.cs:65`; `Application/UseCases/EnsureAccessToken.cs:57-64` |
| 3 | CONFIRMED (5xx/429) | One retry policy covers every call, Signing/hash included: `MaxAttempts=3`, 200 ms base delay, 2 s cap, full jitter, and 429 `Retry-After` honoured. It retries on 429, on status ≥500, on `HttpRequestException` (resets included) and on `TaskCanceledException` when the token was not cancelled. `HttpClient.Timeout` cancels the linked token, so a timeout is mostly not retried. | `Infrastructure/Http/TransientFailureRetryHandler.cs:72-83,124-125`; `Infrastructure/Configuration/MisaESignOptions.cs:62-64` |
| 4 | CONFIRMED | Only `ListCertificatesByUserIdAsync` has the guard. Every hash, Signing/hash, Signing/status and attachment response is unguarded. Their `Deserialize` swallows `JsonException`, so an HTML body surfaces as `EmptyResponse`. Auth calls use the same lenient deserialisation. | `MisaESignWireClient.cs:235,238-249` (guard); `:302,357,445,523,549,599,652,720` (unguarded); `:777-787` |
| 5 | PARTIAL | `ResponseErrorDto.Error` is `bool?`, so null is accepted, but the string `"true"` fails. The wire options have no bool converter, so the whole envelope is dropped. **New:** the nested login status block has a non-nullable `bool Error`, so even `"error": null` drops the login or two-factor response. | `Infrastructure/ESign/Wire/ResponseErrorDto.cs:11-12`; `Infrastructure/ESign/Wire/LoginDtos.cs:34-35`; `Infrastructure/ESign/ESignJsonOptions.cs:12-17` |
| 7 | PARTIAL (the claim "only on 2xx" is wrong) | A 2xx login checks only the nested `{status:{…}}` shape; a flat body gives `EmptyResponse`. A non-2xx login checks only the flat shape; a nested body yields no code, so `Requires2FA=false`. The two-factor endpoint is closer: on non-2xx it tries nested then flat, on 2xx nested only. | `MisaESignWireClient.cs:59-61,65,75,103-104,162-179,836`; `Application/Errors/ESignErrorMapper.cs:111`; `OtpErrorMapper.cs:42` |
| 8 | CONFIRMED (dead code) | `_refreshHintAccessor` is set to 30 s but never read, and no option exposes it. `EnsureAccessToken` refreshes only once the token has expired (zero skew), then falls back to login. | `Application/UseCases/EnsureAccessToken.cs:18,35,44-61` |

## Webhooks, credentials and OTP

| # | Verdict | Current behaviour | Evidence |
|---|---|---|---|
| 6 | PARTIAL | `HandleAsync` catches only `WebhookValidationException`, and those cases do get an ACK: clientId mismatch, unknown transaction, incomplete success, documentId mismatch. Three cases escape as exceptions:<br>• An unknown status throws `MalformedEnvelopeException` in `ToDomain`, before the try block. Nothing catches it.<br>• A null `signatures[]` entry throws a NullReferenceException.<br>• A null `messageId` is not checked at all. | `Application/UseCases/HandleWebhook.cs:55-58,93`; `Client/MisaESignClient.cs:270,313,328-334`; `Application/Errors/ESignErrorMapper.cs:249-265`; `WebhookEnvelopeValidator.cs:11` |
| 9 | CONFIRMED (documented deferral, slice 008 FR-015) | `SigningSession` holds no userName. The session's clientId and the anti-spoof value both come from the static `options.ClientId`. Finalize calls `EnsureAccessToken` with no principal. With no ambient credentials, `DefaultTokenCacheKeySelector.Compose` throws `InvalidOperationException`. In Dynamic mode, `ClientId` may be blank, so every webhook would fail the clientId match. | `Application/Sessions/SigningSession.cs:7-16`; `Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs:193,213,233,253,282`; `FinalizeFromWebhook.cs:68`; `specs/008-esign-per-user-credentials/spec.md:127` |
| R10 | CONFIRMED | There is no Signing/status call on the webhook path; the first signature in the body is used directly. Only success is cached for dedupe. **New:** FAILED and CANCELLED are never stored, so each repeat is delivered to the hook again. A FAILED webhook after a cached success still delivers `TerminalWithoutFinalize`. MessageIds are recorded but only logged. | `FinalizeFromWebhook.cs:58,72,90`; `HandleWebhook.cs:61,63-73,78-89` |
| R11 | REFUTED (no prompt), but … | `FinalizeFromWebhook` has no OTP provider, so it never prompts. **New:** token, certificate and selector calls sit outside the `try`, so a `Requires2FA` `AuthenticationFailedException` (or any auth or certificate error) escapes `HandleWebhookAsync`. | `FinalizeFromWebhook.cs:25-36,68-70,74` |
| R12 | Current behaviour recorded | • Validation failure: `FailureWithError` is delivered to the hook; any exception the hook throws is swallowed.<br>• FAILED or CANCELLED: `TerminalWithoutFinalize` is delivered.<br>• Success: `SuccessWithSignedBytes` is delivered.<br>• Finalize failure: nothing is delivered; it returns `webhook.finalize_failed`.<br>• Cached duplicate: nothing is delivered. | `HandleWebhook.cs:71,107-114`; `FinalizeFromWebhook.cs:93,101-119` |
| OTP | CONFIRMED | There is one `_pendingUserName` per client instance. The client is scoped, so this means one pending challenge per DI scope, not one per principal. | `Client/MisaESignClient.cs:20,85-87,161,178`; `Client/.../ServiceCollectionExtensions.cs:42` |

## Docs

| Page | Verdict | Evidence |
|---|---|---|
| getting-started.md: internal namespace | CONFIRMED | `:47` uses `MisaConnect.ESign.Infrastructure.DependencyInjection`, which is internal. The public entry point is in `MisaConnect.ESign.Client.DependencyInjection`. |
| getting-started.md: `LogoImageBase64: null` | CONFIRMED | `:77`; `SignPdfRequestValidator.cs:61-63` rejects a blank logo. |
| getting-started.md: `SignXmlAsync` overloads | CONFIRMED | `:108`; `IMisaESignClient.cs:30` has a single request-DTO overload. |
| configuration.md: https-only | PARTIAL | `:17,80`. The validator also allows http for loopback (`MisaESignOptionsValidator.cs:16-19,109-112`). There is no webhook URL option. |
| configuration.md: "before or after" | PARTIAL | `:88`. This is true for `IWebhookDeliveryHook` (registered with `TryAddSingleton`). `IMisaCredentialsAccessor` must be registered before (`:107`). |
| sandbox-setup.md: reachability probe | CONFIRMED | `:40`. `tests/dotnet/MisaConnect.ESign.IntegrationTests/Sandbox/SandboxFact.cs` checks env vars only. |

## Other facts

- The ESign Client csproj declares `<Version>2.2.0</Version>`, which is stale. The release workflow packs with `-p:Version=` from the tag (`.github/workflows/release.yml:57`). The latest CHANGELOG entry is `[2.4.0] - 2026-11-04`.
- Existing tests to extend:
  - `tests/dotnet/MisaConnect.ESign.UnitTests/Authentication/{EnsureAccessTokenTests,RefreshTokenSingleFlightTests,TwoFactorAuthTransportRetryTests}.cs`
  - `tests/dotnet/MisaConnect.ESign.IntegrationTests/EndToEnd/{RefreshOn401FakeServerTests,EsrmContentTypeGuardFakeServerTests}.cs`
  - No unit test covers the content-type guard.

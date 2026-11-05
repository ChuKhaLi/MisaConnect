# MisaConnect.ESign 2.5.0 — TS-parity back-port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the TypeScript `@misaconnect/esign` fixes and security rulings back into `MisaConnect.ESign` and ship them as 2.5.0.

**Architecture:** Most fixes are local to the HTTP pipeline (`RemoteSigningAuthHandler`, `TransientFailureRetryHandler`) and the wire client (`MisaESignWireClient`). The webhook path is reworked so that MISA's `Signing/status` decides the outcome. The webhook body is only a trigger. The first terminal state is stored once. `HandleWebhookAsync` never throws on bad input. Per-user credentials reach the webhook path through an internal override slot (`EffectiveCredentials`) and a new public `IWebhookCredentialsResolver`. Every public change is additive.

**Tech Stack:** .NET 8 (`net8.0`), xUnit 2.4.2, `Microsoft.AspNetCore` fake server (`FakeMisaESignServer`), System.Text.Json.

**Spec:** [docs/superpowers/specs/2026-11-05-esign-dotnet-ts-parity-design.md](../specs/2026-11-05-esign-dotnet-ts-parity-design.md). The evidence (current behaviour, file:line) is in [the verification notes](../specs/2026-11-05-esign-dotnet-ts-parity-verification.md). The TS reference implementation is under `src/typescript/esign/src/` (`application/use-cases/finalize-webhook.ts`, `application/webhook-envelope.ts`, `client-internal.ts`).

**Branch:** `feat/esign-ts-parity-backport`.

## Global Constraints

- `TreatWarningsAsErrors=true`: zero warnings. `dotnet format MisaConnect.slnx --verify-no-changes` must pass on Linux CI.
- Unit tests (`tests/dotnet/MisaConnect.ESign.UnitTests`) never touch the network; the project stays under 30 s.
- Semver: minor. No public member is removed or changes signature. New public members are additive, and new members on a public interface get a default implementation. Every new public type or member gets XML doc comments.
- Wire casing and envelope shapes stay verbatim (constitution Principle IV).
- Never put tokens, refresh tokens, passwords, client keys, OTPs or document content in logs or exception messages (FR-021). Content-type and parse errors report the content type and body **length** only.
- The ACK texts are fixed and stay verbatim from `ESignErrorMapper.MapWebhookValidationToAck`. They are also used for `webhook.finalize_failed`: `"Finalize attempt failed; MISA may retry."` / `"Signing finalization failed; please wait for retry."`.
- Proactive refresh skew: **30 seconds**, inclusive (refresh when `expiresAt - 30 s <= now`).
- Pending two-factor challenges: at most **1000** principals per client instance; the oldest is evicted first.
- Option `MisaESignOptions.Webhook.DeliverValidationFailuresToHook`, default **`true`** (owner decision). The default flips in 3.0.
- Release: `MisaConnect.ESign` **2.5.0**; `MisaConnect.EInvoice` has no code changes.

## Review Focus

1. **Two flows in one scope get a 401 at the same moment.** Expected: exactly one refresh, and both flows finish. Pinned in Task 1 (`Concurrent_401s_cause_one_refresh`).
2. **MISA sends JSON as `application/json; charset=utf-8` or `application/problem+json` on a hash or status endpoint.** Expected: accepted. The guard must not reject a charset parameter or a `+json` subtype. Pinned in Task 3.
3. **An integrator replaced `ISigningSessionStore` and did not implement the new terminal-cache member.** Expected: everything works; terminals are just re-delivered as in 2.4. Pinned in Task 7 (`Custom_store_without_terminal_cache_still_works`).
4. **The caller cancels during `HandleWebhookAsync`.** Expected: `OperationCanceledException` propagates, not a failure ACK. Pinned in Task 6.
5. **The integrator's hook throws while a success is being delivered.** Expected: the exception propagates (as in 2.4 and in TS), but the success is already cached, so MISA's retry gets the cached ACK without a second attach. Pinned in Task 7.

---

### Task 1: Token lifecycle: current token, login fallback, proactive refresh (FR-001–004)

**Files:**
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/Http/RemoteSigningAuthHandler.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Application/UseCases/EnsureAccessToken.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` (pass `ISystemClock` to the handler)
- Modify: `tests/dotnet/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs`
- Create: `tests/dotnet/MisaConnect.ESign.IntegrationTests/EndToEnd/TokenLifecycleFakeServerTests.cs`
- Test: `tests/dotnet/MisaConnect.ESign.UnitTests/Authentication/EnsureAccessTokenTests.cs`

**Interfaces:**
- Produces, for the fake server (used by Tasks 2–7):
  - `public sealed record FakeRequest(string Route, string Body, string? Bearer);`
  - `public sealed record FakeResponse(int StatusCode, string Body, string ContentType = "application/json");`
  - `Scenario.Intercept`: `public Func<FakeRequest, FakeResponse?>? Intercept { get; set; }`. It is evaluated first by every route handler. A non-null result is written verbatim and the counter is still incremented. Route keys: `"login"`, `"refresh"`, `"two-factor"`, `"resend-otp"`, `"certificates"`, `"hash"`, `"signing-hash"`, `"signing-status"`, `"attachment"`.
  - `public IReadOnlyList<(string Route, string? Bearer)> EsrmBearers { get; }`: the `AuthorizationRM` bearer, without the `Bearer ` prefix, seen on every ESRM request, in order.
  - `Scenario.RevokeInitialBearerAfterCertificates` (`bool`): once the first certificates call has completed, any ESRM request carrying bearer `rs-token` gets `401`.
  - The first login returns `remoteSigningAccessToken` `rs-token`, as today. Every later login returns `rs-token-relogin`.
- Produces: `public static readonly TimeSpan ProactiveRefreshSkew = TimeSpan.FromSeconds(30);` on `EnsureAccessToken`. The unused `_refreshHintAccessor` field is deleted.
- Produces: `RemoteSigningAuthHandler` ctor gains a trailing `ISystemClock clock` parameter.

- [ ] **Step 1: Extend the fake server** as specified in Interfaces. Existing tests must stay green; the only new default behaviour is the `rs-token-relogin` value on the second and later logins.

- [ ] **Step 2: Write the failing tests** in `TokenLifecycleFakeServerTests`. Each signs a PDF with `TestPdfFixture.SampleRequest()` through `TestServiceProvider.Build(server.BaseUrl)`.
  - `Refreshed_token_is_used_for_the_rest_of_the_flow`: set `RevokeInitialBearerAfterCertificates = true`. The sign succeeds; `Calls.Refresh == 1`; every `EsrmBearers` entry after the first `"hash"` entry is `"rs-token-2"`.
  - `Failed_refresh_falls_back_to_one_login`: revoke as above, and `Intercept` returns `refresh → (500, "{}")`. The sign succeeds; `Calls.Login == 2`; `Calls.Refresh == 1`; the last bearer is `"rs-token-relogin"`.
  - `Unparseable_refresh_body_falls_back_to_login`: same, but `refresh → (200, "not json")`. The sign succeeds; `Calls.Login == 2`.
  - `Already_expired_refresh_result_falls_back_to_login`: `refresh → (200, "{\"accessToken\":\"a\",\"remoteSigningAccessToken\":\"rs-token-2\",\"refreshToken\":\"r\",\"expiresIn\":0}")`. Then `Calls.Login == 2`.
  - `Missing_refresh_token_falls_back_to_login`: revoke as above, and `Intercept` makes the first `login` return the normal body without `refreshToken`. The sign succeeds; `Calls.Refresh == 0`; `Calls.Login == 2`.
  - `Second_401_after_fallback_fails_with_Authentication`: `hash → (401, "{}")` always. The sign throws `AuthenticationFailedException`; `Calls.Login == 2`. A second sign in a new scope then performs a fresh login (`Calls.Login == 3` before any certificates call), which proves the eviction.
  - `Concurrent_401s_cause_one_refresh` (Review Focus 1): revoke as above, and run two `SignPdfAsync` calls concurrently with `Task.WhenAll` in one scope. Both succeed; `Calls.Refresh == 1`.

- [ ] **Step 3: Write the failing unit tests** in `EnsureAccessTokenTests`. Use a fake clock and a cached token:
  - `Token_with_exactly_30s_left_is_refreshed`: the refresh use case is called once.
  - `Token_with_31s_left_is_reused`: no refresh and no login.

- [ ] **Step 4: Run them to verify they fail.**
  Run: `dotnet test tests/dotnet/MisaConnect.ESign.IntegrationTests --filter "FullyQualifiedName~TokenLifecycle"` and `dotnet test tests/dotnet/MisaConnect.ESign.UnitTests --filter "FullyQualifiedName~EnsureAccessTokenTests"`
  Expected: the new tests FAIL. `Refreshed_token…` sees `rs-token` after the refresh.

- [ ] **Step 5: Implement.**
  - `EnsureAccessToken.ExecuteAsync` returns the cached token only when `existing.ExpiresAtUtc - ProactiveRefreshSkew > now`.
  - In `RemoteSigningAuthHandler.SendAsync`, for every non-auth request, **always** overwrite `AuthorizationRM` with `(await _ensureFactory().ExecuteAsync(ct)).Value`, ignoring what the wire client set.
  - On a 401:
    1. Re-read the cache. If the cached token differs from the one just sent, retry once with it and do not refresh; another flow already refreshed.
    2. Otherwise refresh through `SingleFlightRefresh`.
    3. If there is no cached refresh token, or the refresh throws anything except caller cancellation, or it returns `ExpiresAtUtc <= clock.UtcNow`, evict the entry and obtain a token via `_ensureFactory().ExecuteAsync`. The cache is now empty, so this logs in. Login errors propagate unchanged, including `Requires2FA`.
    4. Retry the request once. A 401 on that retry evicts the entry and throws `AuthenticationFailedException(rawCode: "401")` with today's message.

- [ ] **Step 6: Run the ESign unit and integration suites.** Both must pass, including `RefreshOn401FakeServerTests` and `RefreshTokenSingleFlightTests`. Then commit: `feat(esign): use refreshed token for the rest of the flow; login fallback; 30 s proactive refresh`.

---

### Task 2: `Signing/hash` is never retried after it may have reached MISA (FR-005)

**Files:**
- Create: `src/dotnet/MisaConnect.ESign.Infrastructure/Http/ESignRequestOptions.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/Http/TransientFailureRetryHandler.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs` (`SubmitSignHashAsync`)
- Create: `tests/dotnet/MisaConnect.ESign.UnitTests/Http/TransientFailureRetryHandlerTests.cs`
- Test: `tests/dotnet/MisaConnect.ESign.IntegrationTests/EndToEnd/TokenLifecycleFakeServerTests.cs` (one added fact)

**Interfaces:**
- Produces: `internal static class ESignRequestOptions { public static readonly HttpRequestOptionsKey<bool> NotRetryableAfterSend = new("MisaConnect.ESign.NotRetryableAfterSend"); }`
- `SubmitSignHashAsync` sets `req.Options.Set(ESignRequestOptions.NotRetryableAfterSend, true)`. The retry handler reads it from the **original** request, because `CloneRequest` does not copy `Options`.

- [ ] **Step 1: Write the failing unit tests.** Drive the handler with a stub inner `HttpMessageHandler` that replays a queue of results, plus the existing `FakeDelayer`. Use default options (`MaxAttempts = 3`). Requests that carry the flag:
  - `Flagged_500_is_sent_once`: throws `ESignTransportException` with `LastStatusCode == 500` and `AttemptCount == 1`; 1 send.
  - `Flagged_timeout_is_sent_once`: inner throws `TaskCanceledException` (caller token not cancelled); `ESignTransportException`; 1 send.
  - `Flagged_response_ended_is_sent_once`: `new HttpRequestException(HttpRequestError.ResponseEnded, "x")`; 1 send.
  - `Flagged_429_is_retried`: 429, then 200 → returns 200; 2 sends.
  - `Flagged_connection_refused_is_retried`: `HttpRequestError.ConnectionError`, then 200; 2 sends.
  - `Flagged_dns_failure_is_retried`: `HttpRequestError.NameResolutionError`, then 200; 2 sends.

  And unflagged requests:
  - `Unflagged_500_is_retried_to_MaxAttempts`: 3 sends; `ESignTransportException` with `AttemptCount == 3`.
  - `Caller_cancellation_propagates`: `TaskCanceledException`, not `ESignTransportException`.

- [ ] **Step 2: Add the integration fact** `Signing_hash_500_is_sent_once`: `Intercept` returns `signing-hash → (500, "{}")`. `SignPdfAsync` throws `ESignTransportException`; `Calls.SignHash == 1`.

- [ ] **Step 3: Run to verify they fail** (the flagged cases send 3 times).
  Run: `dotnet test tests/dotnet/MisaConnect.ESign.UnitTests --filter "FullyQualifiedName~TransientFailureRetryHandlerTests"`

- [ ] **Step 4: Implement.** In `TransientFailureRetryHandler.SendAsync`, read the flag once. When it is set:
  - a non-429 response is not retried: 5xx throws `ESignTransportException(lastStatusCode, attemptCount: attempt, …)`, and other statuses return;
  - an exception is retried only if it is `HttpRequestException { HttpRequestError: NameResolutionError or ConnectionError }`;
  - every other exception, and a non-caller `TaskCanceledException`, throws `ESignTransportException` with `attemptCount: attempt`, `inner: ex`.

  Unflagged behaviour is unchanged. Update the class XML doc to state the rule.

- [ ] **Step 5: Run the ESign unit and integration suites; commit** `fix(esign): never retry Signing/hash after it may have reached MISA`.

---

### Task 3: Content-type guard on every ESRM JSON response (FR-006)

**Files:**
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs`
- Modify: `tests/dotnet/MisaConnect.ESign.IntegrationTests/EndToEnd/EsrmContentTypeGuardFakeServerTests.cs` (snippet assertions become length assertions)
- Create: `tests/dotnet/MisaConnect.ESign.IntegrationTests/EndToEnd/EsrmContentTypeGuardAllEndpointsFakeServerTests.cs`

**Interfaces:**
- Produces, as a private wire-client helper: `T? ReadEsrmJson<T>(HttpResponseMessage resp, string body, string endpoint) where T : class`.
  - It calls `EnsureEsrmJsonResponse`, then deserialises.
  - A non-blank body that fails to parse throws `ESignGeneralException(ESignErrorCategory.MisaUnknown, "UnparseableResponse", $"MISA {endpoint} returned a {(int)resp.StatusCode} success with a JSON content-type but a body ({body.Length} bytes) that could not be parsed.", …)`.
  - A blank body returns `null`, so callers keep their existing `EmptyResponse` errors.
- `EnsureEsrmJsonResponse` drops the body snippet. Its detail ends `"... (content-type '{mediaType ?? "(none)"}', body {body.Length} bytes)."`, and `BodySnippetMax` is deleted.

- [ ] **Step 1: Write the failing tests.** A theory over the routes `"hash"`, `"signing-hash"`, `"signing-status"`, `"attachment"` (PDF flow):
  - `Html_success_is_UnexpectedContentType`: `Intercept` returns `(200, "<html><body>spa</body></html>", "text/html")`. `ESignGeneralException.RawCode == "UnexpectedContentType"`; the message contains `"text/html"` and does not contain `"<html"`.
  - `Unparseable_json_is_UnparseableResponse`: `(200, "{not json", "application/json")`. `RawCode == "UnparseableResponse"`; the message contains `"bytes"`.
  - `Json_with_charset_and_plus_json_is_accepted` (Review Focus 2): `signing-status` answers its normal body with content type `application/json; charset=utf-8`, then `application/problem+json`. The sign succeeds.
  - `Auth_calls_stay_lenient`: `login` answers the normal login body with `text/plain`. The sign succeeds.

- [ ] **Step 2: Run to verify they fail** (HTML currently surfaces as `EmptyResponse`).

- [ ] **Step 3: Implement.** Use `ReadEsrmJson<T>` in `HashPdfAsync`, `HashXmlAsync`, the Word/Excel hash path, `SubmitSignHashAsync`, `GetSignStatusAsync`, and the three attachment methods. `ListCertificatesByUserIdAsync` keeps its certificate-specific `UnparseableResponse` text. Auth methods are untouched.

- [ ] **Step 4: Run the ESign suites; commit** `fix(esign): content-type guard on every ESRM JSON response`.

---

### Task 4: Loose `error` field and two-factor code `122` in both shapes (FR-007, FR-008)

**Files:**
- Create: `src/dotnet/MisaConnect.ESign.Infrastructure/ESign/Wire/LooseNullableBooleanConverter.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/ESign/Wire/ResponseErrorDto.cs`, `src/dotnet/MisaConnect.ESign.Infrastructure/ESign/Wire/LoginDtos.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs` (`LoginAsync`, `TwoFactorAuthAsync`)
- Create: `tests/dotnet/MisaConnect.ESign.UnitTests/Signing/Wire/LooseNullableBooleanConverterTests.cs`
- Create: `tests/dotnet/MisaConnect.ESign.IntegrationTests/EndToEnd/LoginErrorShapesFakeServerTests.cs`

**Interfaces:**
- Produces: `internal sealed class LooseNullableBooleanConverter : JsonConverter<bool?>`.
  - Reads JSON `true`/`false` as is, and the strings `"true"`/`"false"` (trimmed, case-insensitive).
  - Reads `null` and any other string or number as `null`.
  - Writes a boolean or `null`.
- `ResponseErrorDto.Error` (already `bool?`) and `LoginStatusBlockDto.Error` (becomes `bool?`) carry `[JsonConverter(typeof(LooseNullableBooleanConverter))]`. Existing `is { Error: true }` patterns keep compiling.
- Produces, as a private wire-client helper: `void ThrowIfLoginError(string json, HttpStatusCode status, string endpoint, string? userName)`.
  - It checks the nested `LoginResponseDto.Status` first (`Error == true` → `ThrowFromLoginEnvelope`).
  - Then it checks the flat `ResponseErrorDto` (`Error == true` or a non-empty `ErrorCode` → `ESignErrorMapper.Map` with `statusCode: (int)status` and `userName`).
  - It returns when neither shape carries an error.

- [ ] **Step 1: Write the failing tests.**
  - Converter theory: `true`, `false`, `"true"`, `"FALSE"`, `" true "`, `null`, `"yes"`, `1` map to `true`, `false`, `true`, `false`, `true`, `null`, `null`, `null`.
  - `LoginErrorShapesFakeServerTests.Code_122_is_detected` theory over `(status, body)`, with `Intercept` on `login`:
    - `(200, "{\"error\":\"true\",\"errorCode\":\"122\"}")`
    - `(200, "{\"status\":{\"code\":200,\"error\":\"true\",\"errorCode\":\"122\"}}")`
    - `(400, "{\"status\":{\"code\":400,\"error\":true,\"errorCode\":\"122\"}}")`
    - `(400, "{\"error\":true,\"errorCode\":\"122\"}")`

    `SignPdfAsync` throws `AuthenticationFailedException` with `Requires2FA == true`.
  - `Null_error_in_login_status_keeps_the_response`: the normal login body with `"error":null` inside `status`. The sign succeeds.
  - `String_error_keeps_the_error_code`: `hash → (400, "{\"error\":\"true\",\"errorCode\":\"DocumentInvalid\"}")`. The exception's `RawCode == "DocumentInvalid"`.

- [ ] **Step 2: Run to verify they fail.**

- [ ] **Step 3: Implement.** In `LoginAsync`, call `ThrowIfLoginError` on non-2xx before falling back to `ThrowMappedAsync`, and on 2xx before the `Data is null` check. Do the same in `TwoFactorAuthAsync` for the 2xx flat shape; its non-2xx path already tries both.

- [ ] **Step 4: Run the ESign suites; commit** `fix(esign): accept loose error flags; detect 2FA 122 in both shapes on any status`.

---

### Task 5: Effective credentials and per-principal two-factor challenges (FR-017)

**Files:**
- Create: `src/dotnet/MisaConnect.ESign.Infrastructure/Credentials/EffectiveCredentials.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/Caching/DefaultTokenCacheKeySelector.cs`, `src/dotnet/MisaConnect.ESign.Infrastructure/Http/ClientHeadersHandler.cs`, `src/dotnet/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs`, `src/dotnet/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` (the four `IMisaCredentialsAccessor` consumers switch to `EffectiveCredentials`)
- Create: `src/dotnet/MisaConnect.ESign.Client/PendingOtpChallenges.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Client/MisaESignClient.cs`
- Create: `tests/dotnet/MisaConnect.ESign.UnitTests/Abstractions/EffectiveCredentialsTests.cs`, `tests/dotnet/MisaConnect.ESign.UnitTests/Authentication/PendingOtpChallengesTests.cs`
- Test: `tests/dotnet/MisaConnect.ESign.IntegrationTests/EndToEnd/DynamicCredentialsFakeServerTests.cs` (one added fact)

**Interfaces:**
- Produces: `internal sealed class EffectiveCredentials`, registered as a singleton.
  - `public EffectiveCredentials(IMisaCredentialsAccessor inner)`
  - `public MisaCredentials Get()` returns the override if one is set, otherwise `inner.Get()`.
  - `public IDisposable Use(MisaCredentials credentials)` sets an `AsyncLocal` override and restores the previous value on dispose.
- Produces: `internal sealed class PendingOtpChallenges`.
  - `public PendingOtpChallenges(int capacity = 1000)`
  - `public void Add(string clientId, string userName)`: insertion order; evicts the oldest past capacity; re-adding refreshes recency.
  - `public bool Contains(string clientId, string userName)`
  - `public void Remove(string clientId, string userName)`
  - Ordinal comparison; thread-safe.
- `MisaESignClient` ctor gains `EffectiveCredentials credentials`; `_pendingUserName` and `_pendingLock` are deleted.

- [ ] **Step 1: Write the failing tests.**
  - `EffectiveCredentialsTests`:
    - `Get_returns_inner_without_override`
    - `Use_overrides_and_restores_on_dispose`
    - `Nested_use_restores_outer`
    - `Override_does_not_leak_into_a_parallel_flow`: two `Task.Run` calls, each with its own `Use`, each sees only its own value.
  - `PendingOtpChallengesTests`:
    - `Add_then_Contains`
    - `Remove_clears_only_that_principal`
    - `Capacity_evicts_oldest`: capacity 2, add A, B, C; A is gone, B and C remain.
    - `Same_user_different_client_is_distinct`
  - Dynamic-mode integration fact `Otp_challenge_is_per_principal`:
    - `Intercept` answers `login` with `(200, "{\"status\":{\"code\":200,\"error\":true,\"errorCode\":\"122\"}}")` while the login body contains `"userA"` or `"userB"` and no OTP has been exchanged yet.
    - In one scope, sign as A and as B (both throw `Requires2FA`).
    - With A's credentials current, `SignInWithOtpAsync("123456", OtpDeliveryChannel.SmsOrEmail, false)` succeeds, and the recorded two-factor body has `"userName":"userA"`.
    - With B current, `ResendOtpAsync()` still succeeds; B's challenge is still pending.
    - With a third user C current, `SignInWithOtpAsync` throws `InvalidOperationException`.

- [ ] **Step 2: Run to verify they fail** (types missing).

- [ ] **Step 3: Implement.** On every `Requires2FA` catch, call `Add(credentials.Get().ClientId, ex.Username)`. On a successful sign or begin, call `Remove` with the current principal. `SignInWithOtpAsync` and `ResendOtpAsync` use the current principal's `UserName` when `Contains` is true. Otherwise they throw the existing `InvalidOperationException` texts unchanged, and `SignInWithOtpAsync` calls `Remove` on success. Delete `ClearPendingUserNameForTests` if nothing references it.

- [ ] **Step 4: Run the ESign suites; commit** `feat(esign): per-principal 2FA challenges; internal effective-credentials override`.

---

### Task 6: Webhooks never throw on bad input; validation-failure delivery option (FR-009, FR-010, FR-013)

**Files:**
- Create: `src/dotnet/MisaConnect.ESign.Client/Webhook/WebhookEnvelopeParser.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Client/MisaESignClient.cs` (`HandleWebhookAsync`; delete `ToDomain` and `ParseStatus`)
- Modify: `src/dotnet/MisaConnect.ESign.Application/UseCases/HandleWebhook.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/Configuration/MisaESignWebhookOptions.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Create: `tests/dotnet/MisaConnect.ESign.UnitTests/Webhook/WebhookEnvelopeParserTests.cs`
- Test: `tests/dotnet/MisaConnect.ESign.UnitTests/Webhook/HandleWebhookTests.cs`

**Interfaces:**
- Produces: on `MisaESignWebhookOptions`, `public bool DeliverValidationFailuresToHook { get; set; } = true;`, with XML doc stating the 3.0 default flip and the reason: a public endpoint lets anyone trigger hook calls.
- Produces: `internal static class WebhookEnvelopeParser`, with `public static bool TryParse(WebhookEnvelopeDto dto, [NotNullWhen(true)] out WebhookEnvelope? envelope, [NotNullWhen(false)] out string? malformedField)`.
  - `messageId`, `clientId` and `transactionId` must be non-blank.
  - `status` must match `SUCCESS`, `FAILED` or `CANCELLED`, case-insensitively.
  - `signatures`: `null` is treated as empty; any `null` element is malformed.
  - `malformedField` is the offending JSON field name (`"messageId"`, `"status"`, `"signatures"`, …) and is safe to log.
- Produces: `HandleWebhook.HandleMalformedAsync(string? transactionId, string malformedField, CancellationToken ct)`, returning `Task<WebhookHandleResult>`.
  - ACK: `MapWebhookValidationToAck` for `MalformedEnvelope`.
  - Outcome: `FailureWithError(transactionId ?? "", DocumentFormat.Unknown, WebhookValidationCategory.MalformedEnvelope, null, correlationId)`.
- `HandleWebhook` ctor gains a trailing `Func<bool> deliverValidationFailuresAccessor`. It gates the hook call both in the existing `WebhookValidationException` catch and in `HandleMalformedAsync`. Hook exceptions stay swallowed on these paths.
- `HandleWebhook.HandleAsync` gains a final catch, `catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))`:
  - It returns `webhook.finalize_failed`, plus `FailureWithError(transactionId, format or DocumentFormat.Unknown, WebhookValidationCategory.None, (ex as ESignException)?.RawCode, correlationId)`.
  - The hook is not called. The log carries `ex.GetType().Name` only.

- [ ] **Step 1: Write the failing tests.**
  - `WebhookEnvelopeParserTests`:
    - A theory over each blank or missing required field, an unknown status (`"DONE"`), and `signatures` containing `null`. Each returns `false` with that field name.
    - `Status_is_case_insensitive`: `"success"` and `"Cancelled"` parse.
    - `Null_signatures_is_empty_list`.
  - `HandleWebhookTests`:
    - `Validation_failure_is_delivered_when_option_true`: unknown transaction → hook called once with `FailureWithError`.
    - `Validation_failure_is_not_delivered_when_option_false`: hook not called; ACK `webhook.unknown_transaction`.
    - `Malformed_is_delivered_only_when_option_true`: both option values.
    - `Unexpected_exception_becomes_finalize_failed`: the session store throws `InvalidOperationException` → ACK `webhook.finalize_failed`; no exception; hook not called.
    - `Caller_cancellation_propagates` (Review Focus 4): a cancelled token and a store that honours it → `OperationCanceledException`.

- [ ] **Step 2: Run to verify they fail.**

- [ ] **Step 3: Implement.** `MisaESignClient.HandleWebhookAsync` calls `TryParse`. On failure it calls `HandleMalformedAsync(dto.TransactionId, field, ct)`; on success it calls `HandleAsync`. Map both results as today. DI passes `() => options.Value.Webhook.DeliverValidationFailuresToHook`.

- [ ] **Step 4: Run the ESign suites; commit** `fix(esign): webhooks answer malformed input with an ACK; DeliverValidationFailuresToHook option`.

---

### Task 7: MISA-confirmed webhook finalize, terminal stored once, per-user credentials (FR-011, FR-012, FR-014, FR-015, FR-016)

**Files:**
- Modify: `src/dotnet/MisaConnect.ESign.Application/Sessions/SigningSession.cs`
- Create: `src/dotnet/MisaConnect.ESign.Application/Sessions/SigningSessionCachedTerminal.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Application/Abstractions/ISigningSessionStore.cs`, `src/dotnet/MisaConnect.ESign.Infrastructure/Sessions/InMemorySigningSessionStore.cs`
- Create: `src/dotnet/MisaConnect.ESign.Application/Abstractions/IWebhookCredentialsResolver.cs`, `src/dotnet/MisaConnect.ESign.Infrastructure/Credentials/OptionsWebhookCredentialsResolver.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Application/UseCases/BeginSignPdf.cs`, `BeginSignXml.cs`, `BeginSignWord.cs`, `BeginSignExcel.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Application/UseCases/FinalizeFromWebhook.cs`, `src/dotnet/MisaConnect.ESign.Application/UseCases/HandleWebhook.cs`
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Modify: `tests/dotnet/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs` (record attachment bodies; status `signatures[0].signature` is `"misa-signature"`)
- Create: `tests/dotnet/MisaConnect.ESign.IntegrationTests/EndToEnd/WebhookConfirmationFakeServerTests.cs`
- Test (update setup): `BeginAndWebhookHappyPathFakeServerTests.cs`, `WebhookDedupeFakeServerTests.cs`, `WebhookStatusFailedFakeServerTests.cs`, and `tests/dotnet/MisaConnect.ESign.UnitTests/Webhook/{FinalizeFromWebhookTests,HandleWebhookTests,DedupeAndIdempotencyTests,InMemorySigningSessionStoreTests}.cs`

**Interfaces:**
- Produces: `public sealed record SigningSessionCachedTerminal(string TriggeringMessageId, WebhookAck Ack, WebhookStatus Status, string? MisaErrorCode);`
- `SigningSession` gains two init-only properties (no constructor change): `public string? UserName { get; init; }` and `public SigningSessionCachedTerminal? CachedTerminal { get; init; }`.
- `ISigningSessionStore` gains a default-implemented member: `ValueTask CacheTerminalAsync(string clientId, string transactionId, string triggeringMessageId, WebhookAck ack, WebhookStatus status, string? misaErrorCode, CancellationToken ct) => ValueTask.CompletedTask;`. `InMemorySigningSessionStore` implements it, and the first write wins.
- Produces: `public interface IWebhookCredentialsResolver { ValueTask<MisaCredentials?> ResolveAsync(string clientId, string userName, CancellationToken ct); }`
  - XML doc: used only in `CredentialsMode.Dynamic` on the webhook path; register before `AddMisaConnectESign`; never logged.
  - Default `internal sealed class OptionsWebhookCredentialsResolver`: Static mode returns the options credentials; Dynamic mode returns `null`. It is registered with `TryAddSingleton`.
- Each `BeginSign*` ctor gains a trailing optional `Func<string?>? userNameAccessor = null` and sets `UserName` on the session it registers.
  - DI passes `() => effective.Get().UserName`.
  - DI changes `clientIdAccessor` to `() => effective.Get().ClientId`, using `EffectiveCredentials` from Task 5.
- `FinalizeFromWebhook` ctor keeps its current parameters and gains `IMisaESignWireClient wire` (for one `GetSignStatusAsync` call), `IWebhookCredentialsResolver credentialsResolver`, `Func<MisaCredentials, IDisposable> useCredentials` (DI: `effective.Use`), and `Func<bool> isDynamicMode`.
  - Signature: `RunAsync(SigningSession session, WebhookEnvelope envelope, string correlationId, CancellationToken ct)`, unchanged.
- `HandleWebhook`: skip `AssertClientIdMatch` when the configured `ClientId` is blank (Dynamic mode). In that case the session lookup by `(envelope.ClientId, transactionId)` is the spoof check. Move `AssertSuccessShapeIsValid` into `FinalizeFromWebhook` after the precedence checks.

**Finalize algorithm** (follow `src/typescript/esign/src/application/use-cases/finalize-webhook.ts`). Under the existing finalize lock:
1. Re-read the session; if it was evicted, throw `UnknownTransactionException` (as today).
2. If `CachedSuccess` is set, return its ACK and a `SuccessWithSignedBytes`. If `CachedTerminal` is set, return its ACK and a `TerminalWithoutFinalize` built from it. Neither case calls MISA or the hook.
3. If the envelope status is SUCCESS, call `AssertSuccessShapeIsValid`. Its `WebhookValidationException` flows to `HandleWebhook`'s existing catch.
4. In Dynamic mode, resolve credentials.
   - `session.UserName` is null → `finalize_failed`, rawCode `MissingPrincipal`.
   - The resolver returns `null` → `NoWebhookCredentials`.
   - The resolved `ClientId` or `UserName` differs from the session's → `CredentialsMismatch`.
   - Otherwise run steps 5–6 inside `useCredentials(resolved)`.
5. Get a token through `EnsureAccessToken`, then call `GetSignStatusAsync(token, transactionId)`.
   - **SUCCESS:** `FirstSignatureData` must be non-empty (else `finalize_failed`, `MissingSignature`). List certificates, select one, and attach with **MISA's** `FirstSignatureData`, never the envelope's signature. Then `CacheSuccessAsync`, deliver `SuccessWithSignedBytes`, and ACK success.
   - **FAILED or CANCELLED:** `CacheTerminalAsync` with MISA's status and error code, deliver `TerminalWithoutFinalize` with MISA's status, and ACK success.
   - **Anything else:** `finalize_failed` with rawCode `SignNotFinal`.
6. Any exception in steps 4–5 other than caller cancellation becomes `webhook.finalize_failed` plus `FailureWithError(transactionId, session.Format, WebhookValidationCategory.None, rawCode, correlationId)`. This includes `AuthenticationFailedException` with `Requires2FA`. Nothing is cached, the hook is not called, and no OTP is requested.

  Hook exceptions on success or terminal delivery propagate (Review Focus 5), but the cache write comes before the hook call.

- [ ] **Step 1: Update existing webhook tests' setup.** Fake-server webhook tests set `PendingsBeforeSuccess = 0` and use `StatusAlwaysFailed` or `StatusAlwaysCancelled` where they send FAILED or CANCELLED. Unit tests stub `GetSignStatusAsync`. Assertions stay the same, except:
  - FAILED and CANCELLED repeats are no longer re-delivered;
  - the status of a terminal outcome now comes from MISA.

- [ ] **Step 2: Write the failing tests** in `WebhookConfirmationFakeServerTests`. Begin with `BeginSignPdfAsync`, then use `FakeMisaESignServer.PostSyntheticWebhookAsync` or `HandleWebhookAsync` directly. The hook is a recording `IWebhookDeliveryHook`.
  - `Forged_success_while_pending_is_not_delivered`: `StatusAlwaysPending`. ACK `webhook.finalize_failed`; hook not called; `Calls.Attachment == 0`.
  - `Attach_uses_MISAs_signature_not_the_envelopes`: the envelope signature is `"forged"`. The recorded attachment body contains `"misa-signature"` and not `"forged"`.
  - `Failed_webhook_while_MISA_reports_success_is_signed`: hook receives `SuccessWithSignedBytes`.
  - `Terminal_is_delivered_once`: `StatusAlwaysFailed`, and the same FAILED webhook is posted twice. The hook is called once, and the second ACK equals the first.
  - `Success_after_recorded_failure_is_not_delivered`: FAILED recorded, then a SUCCESS webhook arrives. The hook count stays 1, and `Calls.Attachment == 0`.
  - `Two_factor_during_finalize_is_a_failure_ack`:
    1. After begin, set `RevokeInitialBearerAfterCertificates`.
    2. Make `refresh` return 500 and `login` answer 122.
    3. Post the webhook.

    Expected: ACK `webhook.finalize_failed`; no exception; `Calls.TwoFactorAuth == 0`.
  - `Dynamic_webhook_uses_resolved_principal`:
    1. Use Dynamic mode, with a resolver that returns A's credentials for `("client-A", "userA")`.
    2. Begin as A with the ambient set.
    3. Post the webhook with no ambient.

    Expected: success, and the last login has `x-clientId == "client-A"`.
  - `Dynamic_webhook_without_resolver_fails_cleanly`: the default resolver. ACK `webhook.finalize_failed`; no exception.
  - `Dynamic_webhook_with_other_principal_is_CredentialsMismatch`: the resolver returns B's credentials. The outcome's `MisaErrorCode == "CredentialsMismatch"`.
  - `Custom_store_without_terminal_cache_still_works` (Review Focus 3): register a wrapper `ISigningSessionStore` that delegates the four original members to `InMemorySigningSessionStore` and does not override `CacheTerminalAsync`. Success webhooks complete; FAILED repeats are delivered each time (the 2.4 behaviour).
  - `Hook_exception_on_success_propagates_but_retry_is_cached` (Review Focus 5): the hook throws once. The first `HandleWebhookAsync` throws; the second returns ACK `"0"` with `Calls.Attachment == 1`.

- [ ] **Step 3: Run to verify they fail.**

- [ ] **Step 4: Implement** as specified in Interfaces and the finalize algorithm.

- [ ] **Step 5: Run the ESign unit and integration suites; commit** `feat(esign): MISA-confirmed webhook finalize; terminal stored once; per-user credentials in webhook mode`.

---

### Task 8: Docs, CHANGELOG, version (FR-018–021)

**Files:**
- Modify: `docs/esign/getting-started.md`, `docs/esign/configuration.md`, `docs/esign/sandbox-setup.md`
- Modify: `CHANGELOG.md` (`[Unreleased]`)
- Modify: `src/dotnet/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj`, `src/dotnet/MisaConnect.EInvoice.Client/MisaConnect.EInvoice.Client.csproj`
- Modify: `src/typescript/esign/README.md` (section "Differences from the .NET SDK")

- [ ] **Step 1: Fix `getting-started.md`.**
  - Use `using MisaConnect.ESign.Client.DependencyInjection;` (not `…Infrastructure…`).
  - Pass a valid base64 PNG for `LogoImageBase64`: use the 1×1 PNG from `TestPdfFixture`.
  - List only facade methods present on `IMisaESignClient`; `SignXmlAsync` has a single request-DTO overload.
  - Make the sample compile: paste its code into a scratch console project referencing `src/dotnet/MisaConnect.ESign.Client` and run `dotnet build` (0 errors), then delete the scratch project.
- [ ] **Step 2: Fix `configuration.md`.**
  - URL rule: absolute `https://`, plus `http://` for loopback hosts.
  - Override ordering: `IWebhookDeliveryHook` may be registered before or after `AddMisaConnectESign`; `IMisaCredentialsAccessor` and `IWebhookCredentialsResolver` must be registered before.
  - Document `Webhook.DeliverValidationFailuresToHook`, MISA-confirmed finalize (one extra `Signing/status` call per webhook), and the webhook ACK table including `webhook.finalize_failed`.
- [ ] **Step 3: Fix `sandbox-setup.md`.** Sandbox tests skip on missing env vars and on NoActiveCertificate unless `MISACONNECT_ESIGN_SANDBOX_REQUIRED=1`. Remove the reachability-probe claim.
- [ ] **Step 4: Write the CHANGELOG `[Unreleased]` entry** for `MisaConnect.ESign` 2.5.0. It has three sections:
  - **Fixed:** FR-001–008.
  - **Added:** `IWebhookCredentialsResolver`, `Webhook.DeliverValidationFailuresToHook`, `ISigningSessionStore.CacheTerminalAsync` (default no-op).
  - **Behaviour changes:**
    - `Signing/hash` retry rule;
    - HTML or unparseable ESRM responses now map to `UnexpectedContentType` or `UnparseableResponse` instead of `EmptyResponse`;
    - the guard message no longer includes a body snippet;
    - webhook outcomes are confirmed with `Signing/status`, and MISA's signature is used;
    - a terminal state is delivered once;
    - malformed webhooks get ACK `webhook.malformed` instead of an exception;
    - finalize errors return `webhook.finalize_failed` instead of throwing;
    - two-factor challenges are per principal;
    - the 3.0 default flip for `DeliverValidationFailuresToHook`.

  State that `MisaConnect.EInvoice` ships unchanged.
- [ ] **Step 5: Set `<Version>2.5.0</Version>` in both Client csproj files**, each with the comment `<!-- release.yml overrides this from the v* tag; keep in step -->`.
- [ ] **Step 6: Update the TS README "Differences from the .NET SDK".** Items 1–9 and the webhook rulings now match .NET 2.5.0 and move under "Fixed in .NET 2.5.0". The only remaining difference is the validation-failure delivery default.
- [ ] **Step 7: Check formatting.** Run `npx prettier --check` on the changed Markdown files and `dotnet format MisaConnect.slnx --verify-no-changes` (ignore CRLF-only ENDOFLINE noise on Windows). Commit `docs(esign): 2.5.0 docs, changelog and version`.

---

### Task 9: Final verification

- [ ] **Step 1:** `dotnet build MisaConnect.slnx -c Release`. Expected: `0 Warning(s)`, `0 Error(s)`.
- [ ] **Step 2:** `dotnet test tests/dotnet/MisaConnect.ESign.UnitTests -c Release --no-build`. Expected: all pass, duration under 30 s.
- [ ] **Step 3:** `dotnet test tests/dotnet/MisaConnect.ESign.IntegrationTests -c Release --no-build`. Expected: all pass; the 5 sandbox tests skip.
- [ ] **Step 4:** `dotnet test tests/dotnet/MisaConnect.EInvoice.UnitTests -c Release --no-build` and `tests/dotnet/MisaConnect.EInvoice.IntegrationTests`. Expected: unchanged green.
- [ ] **Step 5:** `npm test -w @misaconnect/esign`. Expected: green; TS code is untouched apart from the README.
- [ ] **Step 6:** Search the diff for secrets in log templates: `git diff main -- src/dotnet | grep -nE "Log(Information|Warning|Error|Debug).*(Password|ClientKey|Token|Otp|Signature)"`. Expected: no new matches.
- [ ] **Step 7:** Push the branch, open a PR, and wait for `ci` (including "Guard ESign sandbox test discovery") to pass.

# ESign auth follow-ups + Microsoft.Extensions 10.x Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the four parked ESign auth follow-ups, default a missing `expiresIn` in both SDKs, and move the remaining shipped Microsoft.Extensions/System.Text.Json packages to 10.0.14, prepared as MisaConnect 2.6.0.

**Architecture:** All .NET changes live in `MisaConnect.ESign.Infrastructure` (internal types only); no public API change. TS change is one function in `src/typescript/esign/src/infrastructure/wire/auth.ts`.

**Tech Stack:** .NET 8 (`net8.0`), xUnit 2.9, TypeScript/vitest.

**Spec:** `docs/superpowers/specs/2026-11-06-esign-auth-followups-design.md`

## Global Constraints

- `TreatWarningsAsErrors=true`; `dotnet format MisaConnect.slnx --verify-no-changes` (whitespace/EOL noise on CRLF checkouts is expected; CHARSET is not).
- Unit tests never touch the network; total < 30 s.
- No public type/member added, removed or changed in `MisaConnect.ESign.*` or `MisaConnect.EInvoice.*`.
- Default lifetime for a missing/non-integer `expiresIn`: **300 s**. Explicit values (including 0 and negatives) are honoured.
- Dependency target: **10.0.14** for `Microsoft.Extensions.Http`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `System.Text.Json`.
- Files UTF-8 (no cp1252 em-dashes).

## Review Focus

1. A waiter cancelled while the shared refresh is still running — it must throw `OperationCanceledException` promptly while the flight completes for the others (Task 1 test 1).
2. All waiters cancelled — the flight still completes and its result is cached; the slot is cleared (Task 1 test 2).
3. Refresh-after-401 fails while another flow has stored a fresh token — reuse it, no login (Task 2 test 1).
4. Retry still 401 with a different token already in the cache — do not evict the other flow's token (Task 2 test 3).
5. A refresh response with explicit `expiresIn: 0` — still counts as already expired and falls back to login (existing `TokenLifecycleFakeServerTests.Already_expired_refresh_result_falls_back_to_login` must stay green; Task 4).

---

### Task 1: SingleFlightRefresh — flight independent of the first caller's cancellation

**Files:**
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/Http/SingleFlightRefresh.cs`
- Test: `tests/dotnet/MisaConnect.ESign.UnitTests/Authentication/RefreshTokenSingleFlightTests.cs`

**Interfaces:** signature unchanged: `Task<AccessToken> RefreshAsync(string cacheKey, Func<CancellationToken, Task<AccessToken>> factory, CancellationToken ct)`. The factory now always receives `CancellationToken.None`; the returned task is `flight.WaitAsync(ct)`.

- [ ] **Step 1: Failing tests** in `RefreshTokenSingleFlightTests`:
  - `First_caller_cancelling_does_not_cancel_the_flight_for_other_waiters`: factory records the token it got and awaits a gate; caller A uses a `CancellationTokenSource`, caller B `None`. Cancel A → `await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a)`; then complete the gate → B returns `"rs"`; assert the factory's token `CanBeCanceled == false`; invocations == 1.
  - `Flight_completes_even_when_every_waiter_cancels`: one caller with a CTS, cancel it, assert it throws OCE, complete the gate, then await a short `Task.Delay(50)` and assert a new `RefreshAsync` on the same key invokes the factory again (slot cleared after completion, not after cancellation of the waiter) — invocations == 2 and the first flight's task completed (capture it via the factory).
- [ ] **Step 2:** `dotnet test tests/dotnet/MisaConnect.ESign.UnitTests --filter "FullyQualifiedName~RefreshTokenSingleFlightTests"` → the two new tests FAIL.
- [ ] **Step 3:** Implement: `new Lazy<Task<AccessToken>>(() => factory(CancellationToken.None), …)`; keep the slot-removal continuation on the flight task; return `task.WaitAsync(ct)`. Update the XML doc.
- [ ] **Step 4:** Same command → all PASS. Also `--filter "FullyQualifiedName~TwoFactorAuthSingleFlightTests|FullyQualifiedName~EnsureAccessTokenTests"` PASS.
- [ ] **Step 5:** Commit `fix(esign): shared token refresh no longer cancelled by its first caller`.

### Task 2: RemoteSigningAuthHandler — conditional eviction

**Files:**
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/Http/RemoteSigningAuthHandler.cs`
- Create: `tests/dotnet/MisaConnect.ESign.UnitTests/Http/RemoteSigningAuthHandlerTests.cs`

**Interfaces:** consumes `EnsureAccessToken.ProactiveRefreshSkew` (public static) for the freshness check: fresh ⇔ `ExpiresAtUtc - ProactiveRefreshSkew > clock.UtcNow`.

Test harness: build the handler directly with `InMemoryTokenCache`, a static key selector, `new SingleFlightRefresh()`, `FakeClock`, `StubCorrelationIdAccessor`, `Options.Create(new MisaESignOptions())`, `EnsureAccessToken`/`RefreshAccessToken` over a `StubWireClient`, and an inner `HttpMessageHandler` stub whose responses are scripted per call; send through `HttpMessageInvoker` to `https://esrm.example/api/v1/certificates` (non-auth path).

- [ ] **Step 1: Failing tests:**
  - `Failed_refresh_reuses_fresh_token_stored_by_another_flow`: cache holds `T1` (fresh). Inner: 1st call 401. `OnRefresh` stores `T2` (fresh) into the cache itself, then throws. Inner 2nd call 200. Assert `LoginCalls == 0`, second request bearer `T2`, cache value `T2`.
  - `Failed_refresh_evicts_the_sent_token_then_logs_in`: cache `T1`; 401; refresh throws; login returns `T3`. Assert `LoginCalls == 1`, retry bearer `T3`.
  - `Retry_401_does_not_evict_a_different_token`: cache `T1`; 1st 401; refresh returns `T2` and while handling the retry the inner stub replaces the cache with `T9` then answers 401. Assert `AuthenticationFailedException` and cache still `T9`.
  - `Retry_401_evicts_the_rejected_token`: as above without the `T9` swap → cache empty.
- [ ] **Step 2:** `dotnet test tests/dotnet/MisaConnect.ESign.UnitTests --filter "FullyQualifiedName~RemoteSigningAuthHandlerTests"` → tests 1 and 3 FAIL.
- [ ] **Step 3:** In `RecoverTokenAsync`'s catch: re-read the cache; return it if fresh; `RemoveAsync` only if it still holds `sent.Value`; then `_ensureFactory().ExecuteAsync`. In `SendAsync`'s retry-401 branch: evict only if the cache still holds `token.Value`. Extract `private async Task EvictIfHoldsAsync(string key, string value, CancellationToken ct)`.
- [ ] **Step 4:** Same filter PASS; `dotnet test tests/dotnet/MisaConnect.ESign.IntegrationTests --filter "FullyQualifiedName~RefreshOn401|FullyQualifiedName~TokenLifecycle"` PASS.
- [ ] **Step 5:** Commit `fix(esign): 401 fallback evicts only the stale token`.

### Task 3: Auth calls carry the flow's correlation ID

**Files:**
- Create: `src/dotnet/MisaConnect.ESign.Infrastructure/Http/AmbientCorrelationId.cs`
- Modify: `RemoteSigningAuthHandler.cs`, `ClientHeadersHandler.cs`, `ESign/MisaESignWireClient.cs` (request stamp ~l.765 and log ~l.976), `Logging/ESignCallLogger.cs`
- Modify: `tests/dotnet/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs` (`FakeRequest` gains trailing `string? CorrelationId = null`, filled from `X-Correlation-Id`)
- Test: `tests/dotnet/MisaConnect.ESign.UnitTests/Http/RemoteSigningAuthHandlerTests.cs`, `tests/dotnet/MisaConnect.ESign.IntegrationTests/EndToEnd/RefreshOn401FakeServerTests.cs`

**Interfaces (produced):**
```csharp
internal static class AmbientCorrelationId
{
    public static string? Current { get; }                      // AsyncLocal-backed
    public static IDisposable Push(string? correlationId);      // null/empty → no-op scope; Dispose restores previous
    public static string Resolve(ICorrelationIdAccessor accessor); // Current ?? accessor.Current
}
```

- [ ] **Step 1: Failing tests:**
  - Unit `Auth_calls_inside_handler_see_the_request_correlation_id`: request carries `X-Correlation-Id: flow-cid`; stub accessor returns `handler-cid`; cache empty so `OnLogin` runs — capture `AmbientCorrelationId.Current` inside `OnLogin` → `"flow-cid"`; after `SendAsync`, `AmbientCorrelationId.Current` is null. Also: thrown `AuthenticationFailedException.CorrelationId == "flow-cid"` on the double-401 path.
  - Integration `Refresh_after_401_carries_the_flow_correlation_id`: `CertsForce401Once = true`; `Intercept` records `(Route, CorrelationId)` and returns null; after `SignPdfAsync`, the `refresh` entry's correlation id equals the `certificates` entries'.
- [ ] **Step 2:** Run both filters → FAIL.
- [ ] **Step 3:** Implement `AmbientCorrelationId`; in `RemoteSigningAuthHandler.SendAsync` wrap everything after the auth-endpoint early return in `using var _ = AmbientCorrelationId.Push(<request X-Correlation-Id header first value>)`; replace every Infrastructure read of `_correlation.Current` listed above with `AmbientCorrelationId.Resolve(_correlation)`.
- [ ] **Step 4:** Both filters PASS; full `MisaConnect.ESign.UnitTests` PASS.
- [ ] **Step 5:** Commit `fix(esign): auth calls made by the 401 handler carry the flow correlation id`.

### Task 4: Default lifetime for a missing `expiresIn` (.NET + TS)

**Files:**
- Modify: `src/dotnet/MisaConnect.ESign.Infrastructure/ESign/Mapping/AuthSessionMapper.cs`
- Modify: `src/typescript/esign/src/infrastructure/wire/auth.ts` (`expiresInSeconds`)
- Test: `tests/dotnet/MisaConnect.ESign.UnitTests/ESign/AuthSessionMapperTests.cs` (create if absent), `src/typescript/esign/test/unit/token-manager.test.ts`

**Interfaces (produced):** `internal const int DefaultExpiresInSeconds = 300;` on `AuthSessionMapper`; TS `export const DEFAULT_EXPIRES_IN_SECONDS = 300` in `auth.ts`.

- [ ] **Step 1: Failing tests:**
  - .NET: login (envelope), refresh (envelope) and refresh (flat) with `ExpiresIn = null` → `ExpiresAtUtc == now + 300 s`; with `ExpiresIn = 0` → `== now`; with `3600` → `now + 3600 s`.
  - TS: `defaults a missing expiresIn to 300 s` (login response without `expiresIn` → `expiresAt == T0 + 300_000`), and `honours an explicit expiresIn of 0` via a refresh returning `expiresIn: 0` → falls back to login (login count 1).
- [ ] **Step 2:** `dotnet test … --filter "FullyQualifiedName~AuthSessionMapperTests"` and `npm test -w @misaconnect/esign -- token-manager` → new tests FAIL.
- [ ] **Step 3:** .NET: `data.ExpiresIn ?? DefaultExpiresInSeconds` at all three sites. TS: `expiresInSeconds` returns `DEFAULT_EXPIRES_IN_SECONDS` for `undefined`/non-number/non-digit-string; update its doc comment.
- [ ] **Step 4:** Same commands PASS; `TokenLifecycleFakeServerTests` PASS.
- [ ] **Step 5:** Commit `fix(esign): default a missing expiresIn to 300 s (.NET + TS)`.

### Task 5: Dependencies to 10.0.14, docs, release prep

**Files:**
- Modify: `Directory.Packages.props`, `CHANGELOG.md`, `Directory.Build.props` (version → 2.6.0 if the version lives there; else the csproj that holds it), `src/typescript/esign/CHANGELOG.md` if the package keeps one.

- [ ] **Step 1:** Set the five package versions to `10.0.14`.
- [ ] **Step 2:** `dotnet build MisaConnect.slnx -c Release` → 0 warnings, 0 errors.
- [ ] **Step 3:** Full verification: `dotnet test` on all four .NET test projects; `npm run lint && npm run typecheck && npm test`; `dotnet format MisaConnect.slnx --verify-no-changes` (CHARSET clean).
- [ ] **Step 4:** CHANGELOG: rename `[Unreleased]` to `[2.6.0] - <date>` only when the owner asks to release; for now extend `[Unreleased]` with the four fixes, the `expiresIn` default and the completed 10.x move. TS change noted for the next `@misaconnect/esign` patch.
- [ ] **Step 5:** Commit `chore(deps): shipped Microsoft.Extensions and System.Text.Json to 10.0.14; changelog`.

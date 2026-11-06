# ESign review minors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the seven 2.6.0 review minors and the TS expired-refresh gap.

**Architecture:** .NET changes in `MisaConnect.ESign.Application` (one additive default interface method) and `MisaConnect.ESign.Infrastructure`; TS changes in `src/typescript/esign/src`.

**Tech Stack:** .NET 8, xUnit 2.9, TypeScript/vitest.

**Spec:** `docs/superpowers/specs/2026-11-06-esign-review-minors-design.md`

## Global Constraints

- `TreatWarningsAsErrors=true`; Unit tests < 30 s, no network.
- Only public change: `ITokenCache.RemoveIfMatchesAsync(string key, string expectedValue, CancellationToken ct)` as a default interface method.
- Files UTF-8; `dotnet format` IMPORTS/WHITESPACE clean on touched files; Prettier clean.

## Review Focus

1. A custom `ITokenCache` that does not override `RemoveIfMatchesAsync` — default path evicts only on match (Task 2 test).
2. `expiresIn` as `"3600"`, `3600.5`, `"abc"`, `true`, `{}` — login still succeeds; lifetime 3600 / 300 / 300 / 300 / 300 (Task 3 test).
3. Nested `AmbientCorrelationId` scopes restore in LIFO order (Task 1 test).
4. TS refresh returning `expiresIn: 0` falls back to login exactly once (Task 4 test).
5. All waiters cancel and the flight faults — no `UnobservedTaskException` (Task 1 test).

---

### Task 1: SingleFlightRefresh observes faults; docs; scope-restore test

**Files:** `src/dotnet/MisaConnect.ESign.Infrastructure/Http/SingleFlightRefresh.cs`; tests `tests/dotnet/MisaConnect.ESign.UnitTests/Authentication/RefreshTokenSingleFlightTests.cs`, create `tests/dotnet/MisaConnect.ESign.UnitTests/Http/AmbientCorrelationIdTests.cs`.

- [ ] Failing test `Faulted_flight_with_no_waiters_left_is_observed`: subscribe `TaskScheduler.UnobservedTaskException` (record events whose exception contains the test's unique message), single caller with CTS, cancel, fault the gate with that message, wait for the slot to clear, then `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();` → no recorded event.
- [ ] Test `Nested_push_restores_in_lifo_order` (sync): Push("a") → Current "a"; Push("b") → "b"; dispose b → "a"; dispose a → null; `Push(null)` and `Push("")` leave Current unchanged.
- [ ] Implement: `_ = completed.Exception;` in the continuation; XML doc per spec goal 3.
- [ ] Run `dotnet test tests/dotnet/MisaConnect.ESign.UnitTests --filter "FullyQualifiedName~RefreshTokenSingleFlightTests|FullyQualifiedName~AmbientCorrelationIdTests"` → PASS. Commit.

### Task 2: ITokenCache.RemoveIfMatchesAsync

**Files:** `src/dotnet/MisaConnect.ESign.Application/Abstractions/ITokenCache.cs`, `src/dotnet/MisaConnect.ESign.Infrastructure/Caching/InMemoryTokenCache.cs`, `src/dotnet/MisaConnect.ESign.Application/UseCases/EnsureAccessToken.cs`, `src/dotnet/MisaConnect.ESign.Infrastructure/Http/RemoteSigningAuthHandler.cs`; test create `tests/dotnet/MisaConnect.ESign.UnitTests/Caching/TokenCacheRemoveIfMatchesTests.cs`.

- [ ] Failing tests: InMemory — match removes, mismatch keeps, missing no-op; default DIM on a minimal custom cache (only the 3 old members) — same three cases.
- [ ] Implement DIM (get-compare-remove), InMemory override via `ICollection<KeyValuePair<,>>.Remove` on the exact stored instance after a value check loop (retry while the stored value matches and removal lost a race). Replace `EvictIfHoldsAsync` body and the `EnsureAccessToken` compare-then-remove with `RemoveIfMatchesAsync`.
- [ ] Run ESign unit + `RefreshOn401|TokenLifecycle` integration → PASS. Commit.

### Task 3: Lenient `expiresIn` reading (.NET)

**Files:** create `src/dotnet/MisaConnect.ESign.Infrastructure/ESign/Wire/LenientInt32Converter.cs`; apply `[JsonConverter(typeof(LenientInt32Converter))]` to `ExpiresIn` in `LoginDtos.cs` and `RefreshTokenDtos.cs`; test extend `tests/dotnet/MisaConnect.ESign.UnitTests/ESign/AuthSessionMapperTests.cs`.

- [ ] Failing theory `Lenient_expiresIn_json`: deserialize `{"data":{"remoteSigningAccessToken":"rs","expiresIn":<raw>}}` as `LoginResponseDto` for raw `3600`, `"3600"`, `" 42 "`, `"-1"`, `3600.5`, `"abc"`, `true`, `{}`, `[]`, `null`, `99999999999` → `ExpiresIn` = 3600, 3600, 42, -1, null ×7; same for flat `RefreshTokenResponseDto`.
- [ ] Implement converter (`Read`: Number → `TryGetInt32` else null; String → trimmed `int.TryParse(NumberStyles.AllowLeadingSign, Invariant)` else null; StartObject/StartArray → `reader.Skip()` → null; else null. `Write`: number or null).
- [ ] Run filter → PASS. Commit.

### Task 4: TS — integer-only expiresIn, expired refresh → login

**Files:** `src/typescript/esign/src/infrastructure/wire/auth.ts`, `src/typescript/esign/src/application/token-manager.ts` (refresh at ~l.201–216); test `src/typescript/esign/test/unit/token-manager.test.ts`.

- [ ] Failing tests: login with `expiresIn: 10.5` → 300 s; refresh returning `expiresIn: 0` → login count 1 and token from login; replace the 2.6 "honours explicit 0" login case expectations unchanged (still 0 / -5 s on login).
- [ ] Implement `Number.isInteger`; in the refresh branch treat `expiryTime(refreshed.expiresAt) <= clock.now().getTime()` as a failed refresh.
- [ ] `npm test -w @misaconnect/esign`, lint, typecheck, prettier → PASS. Commit.

### Task 5: Docs + full verification

- [ ] CHANGELOG `[Unreleased]` (.NET: Added `RemoveIfMatchesAsync`; Fixed items) and TS CHANGELOG `[Unreleased]`; spec of 2.6 (`2026-11-06-esign-auth-followups-design.md`) known-gap line marked closed.
- [ ] Full: Release build; all four .NET test projects; npm lint/typecheck/format/test; `dotnet format --verify-no-changes` on touched files. Commit.

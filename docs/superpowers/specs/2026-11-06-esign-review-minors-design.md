# ESign 2.6.0 review minors + TS expired-refresh rule — design (2026-11-06)

Requested by the owner on 2026-11-06 ("làm nốt mấy việc đó đi") after MisaConnect 2.6.0 shipped.
Source: the seven deferred minors from the 2.6.0 whole-branch review, and the TS gap recorded in
`docs/superpowers/specs/2026-11-06-esign-auth-followups-design.md`. Out of scope (blocked
externally): live sandbox verification, TypeScript 7.

## Goals

1. **Observed flight faults.** `SingleFlightRefresh` observes a faulted flight's exception in its
   cleanup continuation, so a flight whose waiters all cancelled never raises
   `TaskScheduler.UnobservedTaskException`.
2. **Atomic compare-and-remove.** `ITokenCache` gains a default interface method
   `Task RemoveIfMatchesAsync(string key, string expectedValue, CancellationToken ct)`: remove the
   entry only while its `Value` equals `expectedValue`. The default implementation is
   get-compare-remove (existing custom caches keep working unchanged); `InMemoryTokenCache`
   overrides it atomically. `EnsureAccessToken` and `RemoteSigningAuthHandler` use it for every
   "evict only the stale token" path.
3. **Docs say what the code does.** `SingleFlightRefresh`: the HTTP calls inside the flight are
   bounded by `HttpClient.Timeout`; `ITokenCache` calls inside it are not cancellable, so a custom
   cache must bound its own I/O. A shared flight's auth calls (and its exception, seen by every
   waiter) carry the correlation ID of the flow that started it.
4. **`expiresIn` parity on odd input.** .NET reads `expiresIn` leniently: an integer number as-is;
   a string of an optional `-` and digits parsed; anything else (non-integer number, other string,
   bool, object, array, overflow) → treated as missing (→ 300 s by the existing rule). TS treats a
   finite non-integer number as missing too (`Number.isInteger`), matching both the doc and .NET.
5. **Scope-restore test.** A synchronous `AmbientCorrelationId.Push`/`Dispose` test, nested, proves
   restore (the existing async assertion cannot).
6. **TS expired refresh → login.** A refresh result whose `expiresAt` is not after `now` counts as
   a failed refresh in `@misaconnect/esign` (falls back to login), as in .NET since 2.5.0.

## Non-goals

No change to public behaviour beyond the above; no release (CHANGELOG `[Unreleased]`, next .NET
version 2.7.0 because goal 2 adds a public member; next TS version 0.1.3).

## Anti-goals

- A custom `ITokenCache` written against 2.6 must compile and behave exactly as before.
- Lenient `expiresIn` reading must never turn a malformed field into a failed login.

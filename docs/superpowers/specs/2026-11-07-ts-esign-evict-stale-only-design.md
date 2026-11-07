# TS esign: evict only the stale token — design (2026-11-07)

Approved by the owner on 2026-11-07 ("ok fix nốt đi") after the .NET/TS parity check. Brings
`@misaconnect/esign` level with .NET `MisaConnect.ESign` 2.6–2.7.

## Gaps

- **G1.** `renew()` evicts the cache entry after a failed refresh whatever token it holds
  (`evictOwned`). The `acquire` and `reauth` flights use different single-flight slots, so one
  can evict a token the other just stored: one extra login (or a spurious OTP prompt for a 2FA
  principal).
- **G2.** The `TokenCache` port has no compare-and-delete, so a custom cache cannot evict
  atomically (.NET: `ITokenCache.RemoveIfMatchesAsync`).

## Goals

1. `TokenCache` gains an **optional** `deleteIfMatches?(key, value)`: remove the entry only while
   its token `value` equals `value`. Optional, so existing custom caches compile and behave as
   before. `createInMemoryTokenCache` implements it (synchronous, hence atomic in JS).
2. After a failed refresh, `renew()`:
   1. re-reads the cache and returns a **fresh** token owned by the same credentials whose value
      differs from the one it tried to refresh;
   2. otherwise evicts only the stale token: `deleteIfMatches(key, staleValue)` when the cache has
      it, else read-compare-`delete` (small race, as the .NET default implementation);
   3. then logs in, as before.
3. Release as `@misaconnect/esign` **0.3.0** (new optional port member).

## Non-goals

No change to the single-flight slots, to `reauthenticate()`'s reuse rule, or to the .NET SDK.

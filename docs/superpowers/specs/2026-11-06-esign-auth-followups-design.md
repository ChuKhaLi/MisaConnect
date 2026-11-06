# ESign auth follow-ups + Microsoft.Extensions 10.x — design (2026-11-06)

Approved in chat on 2026-11-06 ("yep làm đi"). Source: the four follow-ups parked by the 2.5.0 final
review (`docs/superpowers/handoffs/2026-11-06-0013-esign-2.5.0-released.md`, Next actions 2.1–2.4)
and the mixed 8.x/10.x Microsoft.Extensions dependency set left by Dependabot (#14, #16, #17).

## Intent

Remove the rare-but-real auth inefficiencies left in 2.5.0 (worst case: one extra login, a spurious
OTP prompt for a 2FA principal), make every auth call traceable under the flow's correlation ID, and
ship a coherent dependency set. Target release: **MisaConnect 2.6.0** (minor, because consumers now
resolve Microsoft.Extensions 10.x). Release is prepared, not tagged, until the owner says so.

## Goals

1. **Shared refresh is not cancelled by its first caller.** `SingleFlightRefresh` runs the shared
   flight with `CancellationToken.None`; each waiter observes its own token via `task.WaitAsync(ct)`.
   A cancelled waiter gets `OperationCanceledException`; the others still get the token. Applies to
   every user of `SingleFlightRefresh` (proactive refresh, 401 refresh, OTP exchange).
2. **401 fallback does not evict a token another flow stored.** `RemoteSigningAuthHandler`'s
   failed-refresh path follows the `EnsureAccessToken` rule: re-read the cache; if it holds a fresh
   token, reuse it; evict only while the entry still holds the token that was sent; then log in via
   `EnsureAccessToken`. The "401 even after a fresh token" eviction is also conditional on the entry
   still holding the token that was just rejected.
3. **Auth calls carry the flow's correlation ID.** The handler reads `X-Correlation-Id` from the
   request it handles and sets an Infrastructure-internal `AsyncLocal` override
   (`AmbientCorrelationId`) around its auth work. Infrastructure readers of the correlation ID
   (`MisaESignWireClient` request stamping, `ClientHeadersHandler`, `ESignCallLogger`,
   `RemoteSigningAuthHandler`'s own exception) prefer the override when set.
4. **`expiresIn` lifetime rule.** Missing (or non-integer) → **300 s**; an explicit value of 0 or
   less is honoured (already expired); **1–59 s → 60 s**; 60 s and above as-is. Applies to login,
   2FA and refresh, in both the envelope and flat shapes. Same rule in `@misaconnect/esign` (TS).
5. **Shipped dependencies coherent on 10.x.** `Microsoft.Extensions.Http`,
   `Logging.Abstractions`, `Options`, `Options.ConfigurationExtensions` and `System.Text.Json` move
   to 10.0.x. Packages keep targeting `net8.0`.

## Non-goals

- No public API change on either .NET family; `ICorrelationIdAccessor` is unchanged.
- No change to test-only or ASP.NET Core 8-bound packages (`Mvc.Testing`, `HealthChecks`,
  `Logging.Console` in samples).
- No TS change beyond the `expiresIn` default (TS has its own single-flight/correlation design).
- No tag, no publish.

## Anti-goals

- Must not revive an already-expired refresh result: an **explicit** `expiresIn` of 0 or less is
  honoured as-is, so the 2.5.0 rule "a refresh result that is already expired counts as a failed
  refresh and falls back to login" keeps working
  (`TokenLifecycleFakeServerTests.Already_expired_refresh_result_falls_back_to_login`).
- Must not keep a refresh running forever: the HTTP client's own timeout still bounds it.

## Deviation from the chat proposal (owner-visible)

The chat proposal floored missing/≤ 0 to 300 s and 1–59 s to 60 s. Flooring an explicit 0 would
silently disable the 2.5.0 rule above, so an explicit value ≤ 0 is honoured instead. The first
draft of this doc also dropped the 60 s floor; the owner's side-note on 2026-11-06 caught that, and
the floor is back. Only the explicit-≤ 0 case differs from what was approved.

Known gap, not in scope: TS has no "already-expired refresh result falls back to login" rule
(.NET does since 2.5.0); TS returns the expired token and recovers through its 401 path.
*Closed by `docs/superpowers/specs/2026-11-06-esign-review-minors-design.md` (goal 6).*

## Left to the builder

Test shapes, the exact name/location of the AsyncLocal helper, and CHANGELOG wording.

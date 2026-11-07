# .NET 10 multi-targeting — design (2026-11-07)

Requested by the owner ("làm lần lượt đi", 2026-11-06) after the build/migrate survey; dependency
strategy **Option A** chosen by the owner on 2026-11-07.

## Why

.NET 8 (and Microsoft.Extensions 8.x) reach end of support on **2026-11-10**. Both packages ship
only `lib/net8.0`. .NET 10 is the current LTS.

## Goals

1. Both packages ship `lib/net8.0` **and** `lib/net10.0` (`<TargetFrameworks>net8.0;net10.0`),
   so apps still on .NET 8 keep working and .NET 10 apps get a native build.
2. **Option A:** both targets depend on Microsoft.Extensions **10.0.14** (supported on .NET 8; the
   8.x packages go out of support with .NET 8). Docs state the requirement plainly: an app must
   use Microsoft.Extensions ≥ 10.0.14; an 8.x pin is reported at restore as NU1605.
3. Unit tests (and `EInvoice.TestSupport`) run on **both** targets; integration tests and samples
   move to **net10.0** (ASP.NET Core 10, `Mvc.Testing`/`HealthChecks`/sample logging+config 10.0.14).
4. The package smoke gate runs its console and ASP.NET Core apps on **net8.0 and net10.0**.
5. Release as **2.8.0** (a new target framework is a minor change). No API or behaviour change.

## Non-goals

No per-target dependency split (Option B, declined). No drop of `net8.0`. No TS change.

## Anti-goals

- No new warnings: `TreatWarningsAsErrors` stays; fix or justify any net10 analyzer finding.
- CI test result files must not collide across target frameworks.

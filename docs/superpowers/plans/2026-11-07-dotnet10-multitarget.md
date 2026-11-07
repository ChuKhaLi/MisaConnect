# .NET 10 multi-targeting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `net8.0` + `net10.0` builds of both packages on Microsoft.Extensions 10.0.14.

**Architecture:** Target frameworks are set centrally in `Directory.Build.props` by project location/name; package versions in `Directory.Packages.props`.

**Tech Stack:** .NET SDK 10.0.1xx, xUnit 2.9, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-07-dotnet10-multitarget-design.md`

## Global Constraints

- Libraries (`src/dotnet/**`), `*.UnitTests`, `*.TestSupport`: `net8.0;net10.0`. Everything else in the solution: `net10.0`.
- Microsoft.Extensions/System.Text.Json: 10.0.14 for every target (Option A).
- `TreatWarningsAsErrors=true` stays; 0 warnings.

## Review Focus

1. Both nupkgs contain `lib/net8.0` and `lib/net10.0` with all bundled assemblies and a dependency group per target.
2. A net8.0 and a net10.0 consumer app (console + ASP.NET Core) load the packages (smoke gate).
3. CI TRX files do not overwrite each other for multi-targeted test projects.
4. `release.yml` unit tests and packing still work with two targets.
5. Docs no longer say "targets .NET 8" only, and state the Microsoft.Extensions ≥ 10.0.14 requirement.

---

### Task 1: Target frameworks + package versions

**Files:** `Directory.Build.props`, `Directory.Packages.props`.

- [ ] Default `<TargetFramework>net10.0</TargetFramework>`; for projects under `src/dotnet/` or named `*.UnitTests` / `*.TestSupport`, clear it and set `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`.
- [ ] `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.Diagnostics.HealthChecks`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.Configuration.UserSecrets`, `Microsoft.Extensions.Logging.Console`, `Microsoft.Extensions.Configuration.EnvironmentVariables` → 10.0.14.
- [ ] `dotnet build MisaConnect.slnx -c Release` → 0 warnings, 0 errors (fix any net10 finding); all four test projects pass (unit tests on both targets).
- [ ] `dotnet pack` both Clients → each nupkg has `lib/net8.0/*` and `lib/net10.0/*` (4 assemblies each) and two dependency groups. Commit.

### Task 2: Smoke gate on both targets; CI

**Files:** `tests/package-smoke/Directory.Build.props`, `scripts/package-smoke.sh`, `.github/workflows/ci.yml`.

- [ ] Smoke props: `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`; the script runs each app with `--framework net8.0` and `--framework net10.0` (4 runs), each must print `SMOKE OK`.
- [ ] `ci.yml`: TRX `LogFileName=` → `LogFilePrefix=` for the multi-targeted unit test steps.
- [ ] Run the script → 4 × SMOKE OK. Commit.

### Task 3: Docs, CHANGELOG, version 2.8.0

**Files:** `README.md`, `docs/index.md`, `docs/esign/getting-started.md`, `docs/esign/index.md`, `CHANGELOG.md`, both Client csproj `<Version>`.

- [ ] Replace ".NET 8" target statements with ".NET 8 and .NET 10"; add the Microsoft.Extensions ≥ 10.0.14 requirement (and the NU1605 hint) to README and the getting-started page.
- [ ] CHANGELOG `[2.8.0]` (Added: `net10.0` builds; Changed: tests/samples on net10; smoke on both targets). `<Version>` 2.8.0.
- [ ] Full verification; commit.

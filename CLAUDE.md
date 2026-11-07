# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# MisaConnect

This repository is **MisaConnect** — a community .NET SDK for MISA cloud APIs. Two product families ship as independent NuGet packages: `MisaConnect.EInvoice` (v1.x, MISA eInvoice operations) and `MisaConnect.ESign` (v2.x, MISA eSign RemoteSigning operations). Each family is consumed via its own NuGet package; the API/host layer is shipped only as a sample under `samples/`. Development uses the superpowers flow: a design under `docs/superpowers/specs/`, then a plan under `docs/superpowers/plans/`. `specs/001`–`009` are archived Spec Kit slices (read-only). See [docs/architecture.md](docs/architecture.md) for the layered design (Domain → Application → Infrastructure → Client), [docs/constitution.md](docs/constitution.md) for the binding project principles, and [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow.

## Build & test

```
dotnet build MisaConnect.slnx
dotnet test --project tests/dotnet/MisaConnect.EInvoice.UnitTests                                                         # fast unit tests, no network
dotnet test --project tests/dotnet/MisaConnect.EInvoice.IntegrationTests                                                  # integration + sandbox tests (sandbox facts skip cleanly without MISA creds)
dotnet test --project tests/dotnet/MisaConnect.ESign.UnitTests                                                            # ESign unit tests (slice 1–4), fast, no network
dotnet test --project tests/dotnet/MisaConnect.ESign.IntegrationTests                                                     # ESign integration + sandbox tests (sandbox facts skip cleanly)
dotnet test --project tests/dotnet/MisaConnect.EInvoice.UnitTests --filter "FullyQualifiedName~SaveDraftInvoicesTests"    # single test class
dotnet format MisaConnect.slnx                                                                           # required before PR
```

TypeScript (pnpm workspace at repo root — `pnpm-workspace.yaml`, version pinned by `packageManager`, use `corepack pnpm` or an installed pnpm 12; packages under `src/typescript/*`; dev toolchain needs Node ≥ 22.18, packages support Node ≥ 22). Publishing stays on the npm CLI (`npm stage publish` in `release-ts.yml`):

```
pnpm install --frozen-lockfile                   # install workspace
pnpm run build                                   # tsup → ESM + CJS + .d.ts per package
pnpm test                                        # vitest, all packages (builds first)
pnpm --filter @misaconnect/einvoice run test     # one package
pnpm run lint && pnpm run format:check           # required before PR
pnpm run typecheck
```

Tests run on xunit.v3 over Microsoft Testing Platform (`global.json` → `test.runner`), so `dotnet test` needs `--project <dir>`; `--filter` still takes VSTest syntax, and a filter that matches no test exits 8.

The unit/integration split is by project, not by xUnit trait — no test uses `[Trait("Category", ...)]`. Sandbox-credential-requiring tests use `[SandboxFact]`, which skips when the relevant env vars are absent or the sandbox host is unreachable: EInvoice tests check `MISACONNECT_SANDBOX_*`, ESign tests check `MISACONNECT_ESIGN_SANDBOX_*`.

- `TreatWarningsAsErrors=true` is set in `Directory.Build.props` — warnings break the build, including in PRs.
- Unit tests must stay under 30s total and never touch the network.
- Integration tests must skip cleanly when the MISA sandbox is unreachable.

## Architecture invariants

- `Domain` has zero external dependencies (applies to both `MisaConnect.EInvoice.Domain` and `MisaConnect.ESign.Domain`).
- `Application` depends only on `Domain` + `Microsoft.Extensions.Logging.Abstractions`.
- `Infrastructure` is `internal` except for the family's `ServiceCollectionExtensions.AddMisaConnect<Product>` entry point, public options type (`MisaEInvoiceOptions` / `MisaESignOptions`), and other DI-touching types.
- `Client` is the consumer-facing NuGet surface (one package per product family). Treat changes to public types as semver events for that family.
- DTO field casing, date formats, and envelope shapes match MISA's published API verbatim — see [docs/misa-api-reference/](docs/misa-api-reference/).
- Swappable collaborators (token cache, RefId generator, system clock, validator, correlation ID accessor, template resolver, delete-options accessor) live as Application-layer ports with Infrastructure-layer defaults. Add new collaborators in the same shape.

## Adding an operation

All work, .NET and TypeScript, follows the superpowers flow (constitution v1.3.0, Principle V): `superpowers:brainstorming` produces a design in `docs/superpowers/specs/YYYY-MM-DD-<topic>-design.md`, then `superpowers:writing-plans` produces a plan in `docs/superpowers/plans/`, executed with `superpowers:subagent-driven-development` or `superpowers:executing-plans`. Do not use Spec Kit or add `specs/NNN-*` slices. For .NET:

1. Design + plan as above.
2. Implement Domain types → Application use case + port → Infrastructure adapter → Client facade.
3. Unit tests required. Integration tests against the relevant sandbox or in-repo fake server (`tests/dotnet/MisaConnect.EInvoice.IntegrationTests/MisaFake/` for EInvoice, `tests/dotnet/MisaConnect.ESign.IntegrationTests/EsignFake/` for ESign).
4. Update `README.md` supported-operations table and add a `CHANGELOG.md` entry under `[Unreleased]`.

## Public surface

### MisaConnect.EInvoice (v1.x)

- Entry point: `services.AddMisaConnectEInvoice(IConfiguration)` — binds the `Misa:EInvoice` section.
- Options type: `MisaEInvoiceOptions` (section name constant `Misa:EInvoice`).
- Client facade interface: `IMisaEInvoiceClient`.
- Use cases (resolved via DI): `ListActiveTemplates`, `PreviewInvoice`, `SaveDraftInvoices`, `GetDraftPdfByRefId`, `DeleteDraftInvoice`, `LookupByRefIds`, `LookupStandard`, `LookupCalculating`, `IssueReplacementInvoice`, `IssueAdjustmentInvoice`.

### MisaConnect.ESign (v2.x)

- Entry point: `services.AddMisaConnectESign(IConfiguration)` — binds the `Misa:ESign` section.
- Options type: `MisaESignOptions` (section name constant `Misa:ESign`).
- Client facade interface: `IMisaESignClient`.
- Facade methods (resolved via DI): `SignPdfAsync`, `SignInWithOtpAsync`, `ResendOtpAsync`, `SignXmlAsync` (string + bytes overloads), `SignWordAsync`, `SignExcelAsync`, `BeginSignPdfAsync`, `BeginSignXmlAsync`, `BeginSignWordAsync`, `BeginSignExcelAsync`, `HandleWebhookAsync`.

## Logging

Never log secrets, tokens, or buyer PII (names, addresses, line-item content). Raw MISA error messages surface only when `MisaEInvoiceOptions.Delete.IncludeRawErrorMessage = true` (or the equivalent opt-in flag on other operations). Correlation IDs are mandatory and structured.

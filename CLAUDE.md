# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# MisaConnect

This repository is **MisaConnect** — a community .NET SDK for MISA cloud APIs. Two product families ship as independent NuGet packages: `MisaConnect.EInvoice` (v1.x, MISA eInvoice operations) and `MisaConnect.ESign` (v2.x, MISA eSign RemoteSigning operations). Each family is consumed via its own NuGet package; the API/host layer is shipped only as a sample under `samples/`. Development uses Spec Kit slices under `specs/`. See [docs/architecture.md](docs/architecture.md) for the layered design (Domain → Application → Infrastructure → Client), [.specify/memory/constitution.md](.specify/memory/constitution.md) for the binding project principles, and [CONTRIBUTING.md](CONTRIBUTING.md) for the slice workflow.

## Build & test

```
dotnet build MisaConnect.slnx
dotnet test tests/MisaConnect.EInvoice.UnitTests                                                         # fast unit tests, no network
dotnet test tests/MisaConnect.EInvoice.IntegrationTests                                                  # integration + sandbox tests (sandbox facts skip cleanly without MISA creds)
dotnet test tests/MisaConnect.ESign.UnitTests                                                            # ESign unit tests (slice 1–4), fast, no network
dotnet test tests/MisaConnect.ESign.IntegrationTests                                                     # ESign integration + sandbox tests (sandbox facts skip cleanly)
dotnet test tests/MisaConnect.EInvoice.UnitTests --filter "FullyQualifiedName~SaveDraftInvoicesTests"    # single test class
dotnet format MisaConnect.slnx                                                                           # required before PR
```

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

1. New slice under `specs/NNN-name/` via `/speckit-specify` (spec.md, plan.md, tasks.md, contracts/).
2. Implement Domain types → Application use case + port → Infrastructure adapter → Client facade.
3. Unit tests required. Integration tests against the relevant sandbox or in-repo fake server (`tests/MisaConnect.EInvoice.IntegrationTests/MisaFake/` for EInvoice, `tests/MisaConnect.ESign.IntegrationTests/EsignFake/` for ESign).
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

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan at
[specs/006-fix-esrm-routing/plan.md](specs/006-fix-esrm-routing/plan.md).
<!-- SPECKIT END -->

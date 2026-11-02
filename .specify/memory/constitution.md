# MisaConnect Constitution

Ratified: 2026-05-13 · Last amended: 2026-11-03 · Version: 1.2.0

MisaConnect is a community SDK for MISA cloud APIs, shipped as .NET (NuGet) and TypeScript (npm) packages. These principles govern how this codebase evolves. Each is a hard rule — exceptions require an entry in `CHANGELOG.md` and the slice plan that introduced the exception.

## Principle I — Layered architecture is non-negotiable

For the .NET packages: Domain has zero external dependencies. Application depends only on Domain and `Microsoft.Extensions.Logging.Abstractions`. Infrastructure depends on Application + Domain + standard .NET extensions. Client depends on Application + Domain + Infrastructure. No layer reaches across.

For the TypeScript packages: `domain/` imports nothing outside itself; `application/` imports only `domain/`; `infrastructure/` may additionally use Node built-ins. Published packages have zero runtime npm dependencies.

## Principle II — Public surface is small and stable

For the .NET packages: each product-family package exposes its public surface from exactly two layers: `MisaConnect.<Product>.Client` and `MisaConnect.<Product>.Domain`, where `<Product>` is one of `EInvoice` or `ESign`. Application and Infrastructure types are `internal` unless explicitly part of the consumer-extension contract (port interfaces, DI options, the family's `AddMisaConnect<Product>` extension method). Every public-surface change requires a `CHANGELOG.md` entry and a semver-appropriate version bump on the affected family's package. New product families MUST follow this two-layer public surface pattern.

For the TypeScript packages: the public surface is exactly what the package's `src/index.ts` exports; the entry point is `createMisa<Product>Client(options)`. Every public-surface change requires a package `CHANGELOG.md` entry and a semver-appropriate version bump on that npm package.

## Principle III — Port-and-adapter for extensibility

All collaborators consumers may want to swap (token cache, RefId generator, system clock, validator, correlation ID accessor, template resolver, delete-options accessor) are exposed as interfaces in the Application layer with sensible defaults in Infrastructure. New collaborators added in this style only.

## Principle IV — Wire format mirrors MISA's documentation verbatim

DTO field casing, date formats, and envelope shapes match MISA's published CURL examples exactly. Any divergence is documented inline with the rationale and a link to the deviation reason. Reference: `docs/misa-api-reference/`.

## Principle V — Slice-driven development

Each new operation or feature ships as a Spec Kit slice under `specs/NNN-name/` with spec, plan, tasks, and contracts. No code lands without a corresponding slice or a trivial-fix justification (typo, dep bump, doc-only change).

TypeScript packages may instead ship from a design document under `docs/superpowers/specs/` and an implementation plan under `docs/superpowers/plans/`. .NET work keeps the Spec Kit requirement.

## Principle VI — Tests are the spec

Unit tests are mandatory and must run under 30 seconds without network. Integration tests run against the MISA sandbox and must skip cleanly when the sandbox is unreachable. No mocks for the MISA HTTP boundary in integration tests — use the in-repo fake server (`tests/.../MisaFake/`) if a deterministic harness is needed.

## Principle VII — Semver discipline

For the .NET packages: breaking public-surface changes only in major versions. Minor versions add operations or non-breaking extensions. Patch versions are bug fixes only. Each product-family NuGet package (`MisaConnect.EInvoice`, `MisaConnect.ESign`, and any future family) is versioned independently — a major bump on one family does not require a bump on another. Pre-release identifiers (`-preview.N`, `-rc.N`) may be used for unstable iterations within a family.

For the TypeScript packages: each npm package (`@misaconnect/einvoice`, `@misaconnect/esign`) is versioned independently of the other and of the NuGet packages. While a package is `0.x`, minor versions may break the public surface; from `1.0.0` the .NET rules above apply.

## Principle VIII — Logs never leak secrets or PII

No tokens, no buyer names/addresses, no line-item content, no raw MISA error messages in default logs. Opt-in flag required to surface raw vendor errors (e.g. `MisaEInvoiceOptions.Delete.IncludeRawErrorMessage`). Correlation IDs are mandatory and structured.

## Governance

- Constitution changes require a PR with rationale and a sync impact report.
- Changes to Principles I–IV require a major version bump on every affected product-family package.
- Changes to Principles V–VIII may be MINOR if backward-compatible. Editorial changes (additive enumeration of existing product families, typo fixes, formatting) MAY be MINOR even when touching Principles I–IV, provided no semantic restriction is added or removed and the sync-impact note explains why.

This document is the source of truth. CLAUDE.md, CONTRIBUTING.md, and slice templates derive from it.

---

## Sync-impact note — 2026-11-03 (constitution v1.2.0)

MisaConnect adds TypeScript (npm) packages alongside the .NET ones; see [docs/superpowers/specs/2026-11-02-typescript-sdk-design.md](../../docs/superpowers/specs/2026-11-02-typescript-sdk-design.md). Principles I, II and VII now prefix their existing text with "For the .NET packages:" — every pre-existing sentence is kept verbatim — and add a "For the TypeScript packages:" paragraph. Principle V adds a TypeScript-only process alternative (superpowers design + plan instead of a Spec Kit slice).

The amendment is **additive**: no restriction on any .NET package has been added or removed, and the new TypeScript rules govern packages that have not yet shipped. Per the Governance clause this is MINOR (1.1.0 → 1.2.0).

Dependent documents reviewed for cascade: CLAUDE.md and CONTRIBUTING.md (one paragraph each on the TypeScript process; repository paths updated separately with the `dotnet/` restructure), `.specify/templates/*` (no change — they govern .NET slices only).

## Sync-impact note — 2026-05-23 (constitution v1.1.0)

The first stable release of `MisaConnect.ESign 2.0.0` (Spec Kit slice 005-release-esign-2-0) made the EInvoice-only wording of Principles II and VII inconsistent with shipping reality. This amendment widens both principles to enumerate the two product families (`MisaConnect.EInvoice`, `MisaConnect.ESign`) and to govern each family's versioning independently.

The amendment is **additive**: no semantic restriction has been added or removed. Every existing `MisaConnect.EInvoice 1.1.0` artifact and every preview-tagged `MisaConnect.ESign` artifact already complies with the new wording. Per the new governance clause, this constitution change is therefore MINOR (1.0.0 → 1.1.0) rather than MAJOR; the Complexity Tracking section of [specs/005-release-esign-2-0/plan.md](../../specs/005-release-esign-2-0/plan.md) records the rationale.

Dependent documents reviewed for cascade: CLAUDE.md (no change required — it references "MisaConnect" generically), CONTRIBUTING.md (added a one-line release convention note under `## Releasing`), `.specify/templates/*` (no template literally references EInvoice as the sole product surface).

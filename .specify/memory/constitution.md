# MisaConnect Constitution

Ratified: 2026-05-13 · Last amended: 2026-05-23 · Version: 1.1.0

MisaConnect is a community .NET SDK for MISA cloud APIs. These principles govern how this codebase evolves. Each is a hard rule — exceptions require an entry in `CHANGELOG.md` and the slice plan that introduced the exception.

## Principle I — Layered architecture is non-negotiable

Domain has zero external dependencies. Application depends only on Domain and `Microsoft.Extensions.Logging.Abstractions`. Infrastructure depends on Application + Domain + standard .NET extensions. Client depends on Application + Domain + Infrastructure. No layer reaches across.

## Principle II — Public surface is small and stable

Each product-family package exposes its public surface from exactly two layers: `MisaConnect.<Product>.Client` and `MisaConnect.<Product>.Domain`, where `<Product>` is one of `EInvoice` or `ESign`. Application and Infrastructure types are `internal` unless explicitly part of the consumer-extension contract (port interfaces, DI options, the family's `AddMisaConnect<Product>` extension method). Every public-surface change requires a `CHANGELOG.md` entry and a semver-appropriate version bump on the affected family's package. New product families MUST follow this two-layer public surface pattern.

## Principle III — Port-and-adapter for extensibility

All collaborators consumers may want to swap (token cache, RefId generator, system clock, validator, correlation ID accessor, template resolver, delete-options accessor) are exposed as interfaces in the Application layer with sensible defaults in Infrastructure. New collaborators added in this style only.

## Principle IV — Wire format mirrors MISA's documentation verbatim

DTO field casing, date formats, and envelope shapes match MISA's published CURL examples exactly. Any divergence is documented inline with the rationale and a link to the deviation reason. Reference: `docs/misa-api-reference/`.

## Principle V — Slice-driven development

Each new operation or feature ships as a Spec Kit slice under `specs/NNN-name/` with spec, plan, tasks, and contracts. No code lands without a corresponding slice or a trivial-fix justification (typo, dep bump, doc-only change).

## Principle VI — Tests are the spec

Unit tests are mandatory and must run under 30 seconds without network. Integration tests run against the MISA sandbox and must skip cleanly when the sandbox is unreachable. No mocks for the MISA HTTP boundary in integration tests — use the in-repo fake server (`tests/.../MisaFake/`) if a deterministic harness is needed.

## Principle VII — Semver discipline

Breaking public-surface changes only in major versions. Minor versions add operations or non-breaking extensions. Patch versions are bug fixes only. Each product-family NuGet package (`MisaConnect.EInvoice`, `MisaConnect.ESign`, and any future family) is versioned independently — a major bump on one family does not require a bump on another. Pre-release identifiers (`-preview.N`, `-rc.N`) may be used for unstable iterations within a family.

## Principle VIII — Logs never leak secrets or PII

No tokens, no buyer names/addresses, no line-item content, no raw MISA error messages in default logs. Opt-in flag required to surface raw vendor errors (e.g. `MisaEInvoiceOptions.Delete.IncludeRawErrorMessage`). Correlation IDs are mandatory and structured.

## Governance

- Constitution changes require a PR with rationale and a sync impact report.
- Changes to Principles I–IV require a major version bump on every affected product-family package.
- Changes to Principles V–VIII may be MINOR if backward-compatible. Editorial changes (additive enumeration of existing product families, typo fixes, formatting) MAY be MINOR even when touching Principles I–IV, provided no semantic restriction is added or removed and the sync-impact note explains why.

This document is the source of truth. CLAUDE.md, CONTRIBUTING.md, and slice templates derive from it.

---

## Sync-impact note — 2026-05-23 (constitution v1.1.0)

The first stable release of `MisaConnect.ESign 2.0.0` (Spec Kit slice 005-release-esign-2-0) made the EInvoice-only wording of Principles II and VII inconsistent with shipping reality. This amendment widens both principles to enumerate the two product families (`MisaConnect.EInvoice`, `MisaConnect.ESign`) and to govern each family's versioning independently.

The amendment is **additive**: no semantic restriction has been added or removed. Every existing `MisaConnect.EInvoice 1.1.0` artifact and every preview-tagged `MisaConnect.ESign` artifact already complies with the new wording. Per the new governance clause, this constitution change is therefore MINOR (1.0.0 → 1.1.0) rather than MAJOR; the Complexity Tracking section of [specs/005-release-esign-2-0/plan.md](../../specs/005-release-esign-2-0/plan.md) records the rationale.

Dependent documents reviewed for cascade: CLAUDE.md (no change required — it references "MisaConnect" generically), CONTRIBUTING.md (added a one-line release convention note under `## Releasing`), `.specify/templates/*` (no template literally references EInvoice as the sole product surface).

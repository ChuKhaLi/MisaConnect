# MisaConnect Constitution

Ratified: 2026-05-13 · Version: 1.0.0

MisaConnect is a community .NET SDK for MISA cloud APIs. These principles govern how this codebase evolves. Each is a hard rule — exceptions require an entry in `CHANGELOG.md` and the slice plan that introduced the exception.

## Principle I — Layered architecture is non-negotiable

Domain has zero external dependencies. Application depends only on Domain and `Microsoft.Extensions.Logging.Abstractions`. Infrastructure depends on Application + Domain + standard .NET extensions. Client depends on Application + Domain + Infrastructure. No layer reaches across.

## Principle II — Public surface is small and stable

Only types in `MisaConnect.EInvoice.Client` and `MisaConnect.EInvoice.Domain` are public API. Application and Infrastructure types are `internal` unless explicitly part of the consumer-extension contract (port interfaces, DI options, `AddMisaConnectEInvoice`). Every public-surface change requires a `CHANGELOG.md` entry and a semver-appropriate version bump.

## Principle III — Port-and-adapter for extensibility

All collaborators consumers may want to swap (token cache, RefId generator, system clock, validator, correlation ID accessor, template resolver, delete-options accessor) are exposed as interfaces in the Application layer with sensible defaults in Infrastructure. New collaborators added in this style only.

## Principle IV — Wire format mirrors MISA's documentation verbatim

DTO field casing, date formats, and envelope shapes match MISA's published CURL examples exactly. Any divergence is documented inline with the rationale and a link to the deviation reason. Reference: `docs/misa-api-reference/`.

## Principle V — Slice-driven development

Each new operation or feature ships as a Spec Kit slice under `specs/NNN-name/` with spec, plan, tasks, and contracts. No code lands without a corresponding slice or a trivial-fix justification (typo, dep bump, doc-only change).

## Principle VI — Tests are the spec

Unit tests are mandatory and must run under 30 seconds without network. Integration tests run against the MISA sandbox and must skip cleanly when the sandbox is unreachable. No mocks for the MISA HTTP boundary in integration tests — use the in-repo fake server (`tests/.../MisaFake/`) if a deterministic harness is needed.

## Principle VII — Semver discipline

Breaking public-surface changes only in major versions. Minor versions add operations or non-breaking extensions. Patch versions are bug fixes only. The `MisaConnect.EInvoice` package version drives the contract; pre-release identifiers (`-preview.N`) may be used for unstable iterations.

## Principle VIII — Logs never leak secrets or PII

No tokens, no buyer names/addresses, no line-item content, no raw MISA error messages in default logs. Opt-in flag required to surface raw vendor errors (e.g. `MisaEInvoiceOptions.Delete.IncludeRawErrorMessage`). Correlation IDs are mandatory and structured.

## Governance

- Constitution changes require a PR with rationale and a sync impact report.
- Changes to Principles I–IV require a major version bump on `MisaConnect.EInvoice`.
- Changes to Principles V–VIII may be MINOR if backward-compatible.

This document is the source of truth. CLAUDE.md, CONTRIBUTING.md, and slice templates derive from it.

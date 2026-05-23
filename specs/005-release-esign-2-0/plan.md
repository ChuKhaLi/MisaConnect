# Implementation Plan: Release `MisaConnect.ESign` 2.0.0 (GA)

**Branch**: `005-release-esign-2-0` | **Date**: 2026-05-23 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/005-release-esign-2-0/spec.md`

## Summary

Ship the first stable NuGet release of `MisaConnect.ESign 2.0.0` covering the public surface accumulated across four implemented slices (PDF / 2FA OTP / XML+Word+Excel / webhook). The change is release engineering rather than new code: bump the Client csproj `<Version>` from `2.0.0-preview.2` to `2.0.0`, promote the lumped `[Unreleased]` CHANGELOG block to a dated `[2.0.0]` section, widen `ci.yml` / `release.yml` to cover both product families, close stale bookkeeping in `specs/003-misa-esign-multi-format/tasks.md` (T096–T115 — files already exist on disk via Slice 4's regression work), and amend Principles II and VII of the constitution so governance acknowledges `MisaConnect.ESign` as a sibling product family to `MisaConnect.EInvoice`. After merge, pushing tag `v2.0.0` triggers the (newly multi-package-aware) `release.yml` to pack, push, and create a GitHub Release for `MisaConnect.ESign 2.0.0`.

## Technical Context

**Language/Version**: C# / .NET SDK 10.0.x (build SDK); `<TargetFramework>net8.0</TargetFramework>` (consumer-facing assemblies) per [Directory.Build.props](Directory.Build.props) and [global.json](global.json) precedent established in v1.1.0
**Primary Dependencies**: `Microsoft.Extensions.Configuration`, `.DependencyInjection`, `.Options` (referenced by `MisaConnect.ESign.Client.csproj`); no new dependencies
**Storage**: N/A — release-engineering work; no persistent storage
**Testing**: xUnit unit/integration suites already in place under [tests/MisaConnect.ESign.UnitTests/](tests/MisaConnect.ESign.UnitTests/) and [tests/MisaConnect.ESign.IntegrationTests/](tests/MisaConnect.ESign.IntegrationTests/); `[SandboxFact]` skips integration tests when `MISACONNECT_ESIGN_SANDBOX_*` env vars are absent
**Target Platform**: nuget.org (NuGet v3 protocol); GitHub Releases on `github.com/<owner>/MisaConnect`; GitHub Actions Linux runners (`ubuntu-latest`)
**Project Type**: NuGet library SDK — single `MisaConnect.slnx` solution with two product families (`MisaConnect.EInvoice.*`, `MisaConnect.ESign.*`). Each family is a four-layer port-and-adapter stack bundled into a single Client NuGet package via `BundleReferencedProjects` MSBuild target.
**Performance Goals**: Tag-to-published-package SLA: full `release.yml` run under 15 minutes on first attempt (SC-001). Unit test suite under 30 seconds per Principle VI (SC-007).
**Constraints**: Zero new build warnings under `TreatWarningsAsErrors=true` ([Directory.Build.props](Directory.Build.props)); `dotnet format MisaConnect.slnx` must produce zero diff; `MisaConnect.EInvoice 1.1.0` must remain bit-identical on nuget.org (SC-008) — protected by `--skip-duplicate` on `dotnet nuget push`.
**Scale/Scope**: 1 new git tag, 1 new NuGet package version, 2 NuGet pushes per release run (EInvoice no-ops via `--skip-duplicate`), ~9 files modified, 0 new files of production code, 4 specs in scope (slices 1–4).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Gates derived from [.specify/memory/constitution.md](.specify/memory/constitution.md):

| Principle | Applies? | Status |
|---|---|---|
| **I — Layered architecture is non-negotiable** | No new code → no layering changes. The existing ESign layering is unchanged. | ✅ Pass |
| **II — Public surface is small and stable** | Bumping the Client csproj version from `-preview.2` to `2.0.0` is a *stability commitment* on the existing surface, not a change to it. No new public types added. The principle itself is being widened (additive) to name `MisaConnect.ESign` alongside `MisaConnect.EInvoice` — see Complexity Tracking. | ✅ Pass (with constitution amendment recorded) |
| **III — Port-and-adapter for extensibility** | No collaborators added. | ✅ Pass |
| **IV — Wire format mirrors MISA's documentation verbatim** | No DTO changes. | ✅ Pass |
| **V — Slice-driven development** | This release is itself a slice (`specs/005-release-esign-2-0/`) with spec, plan, contracts, tasks. | ✅ Pass |
| **VI — Tests are the spec** | FR-017 mandates green unit + integration suites pre-tag; ESign unit suite must stay under 30s (SC-007). CI is being widened to enforce this on every PR (FR-013). Slice 3 bookkeeping closure (FR-008) actually strengthens this principle by ensuring test files match the FRs they claim to cover. | ✅ Pass (strengthens enforcement) |
| **VII — Semver discipline** | `2.0.0-preview.2` → `2.0.0` is the canonical GA promotion under semver. The principle's wording is being widened so it explicitly governs **both** product families independently (additive — current wording is restrictive to `MisaConnect.EInvoice` only). See Complexity Tracking. | ✅ Pass (with constitution amendment recorded) |
| **VIII — Logs never leak secrets or PII** | No logging changes. The existing `ESignLogScrubber` (slice 1) already covers tokens, refresh tokens, `AuthorizationRM`, doc hashes, and PII fields per spec; slices 2–4 extended it for OTP, per-format secrets, and webhook secrets. | ✅ Pass |

**Gate result: PASS.** The two principles that need text edits (II and VII) are being amended *additively* — the new wording covers both product families and is strictly more permissive than the EInvoice-only wording, so no existing slice violates the new constitution.

## Project Structure

### Documentation (this feature)

```text
specs/005-release-esign-2-0/
├── spec.md                         # Approved feature spec (already written)
├── plan.md                         # This file
├── research.md                     # Phase 0 — release engineering decisions
├── data-model.md                   # Phase 1 — release artifacts + their fields
├── quickstart.md                   # Phase 1 — how a maintainer cuts the release
├── contracts/
│   ├── changelog-format.md         # The shape of the `[2.0.0]` CHANGELOG section
│   ├── workflow-contract.md        # Observable behavior expected from ci.yml / release.yml
│   ├── package-metadata.md         # Required .nupkg contents and metadata
│   └── constitution-amendment.md   # Exact text of the Principle II + VII edits
├── checklists/
│   └── requirements.md             # (already written) — spec quality validation
└── tasks.md                        # Phase 2 — created by /speckit-tasks (NOT this command)
```

### Source Code (repository root)

This feature does not add production code. It modifies release / governance / bookkeeping artifacts in the existing layout. The repo structure (from CLAUDE.md and prior slices) is:

```text
src/
├── MisaConnect.EInvoice.Domain/             # unchanged
├── MisaConnect.EInvoice.Application/        # unchanged
├── MisaConnect.EInvoice.Infrastructure/     # unchanged
├── MisaConnect.EInvoice.Client/             # unchanged
├── MisaConnect.ESign.Domain/                # unchanged
├── MisaConnect.ESign.Application/           # unchanged
├── MisaConnect.ESign.Infrastructure/        # unchanged
└── MisaConnect.ESign.Client/                # csproj <Version> bump only

tests/
├── MisaConnect.EInvoice.UnitTests/          # unchanged; still gates PRs
├── MisaConnect.EInvoice.IntegrationTests/   # unchanged
├── MisaConnect.ESign.UnitTests/             # already exists; will be added to ci.yml
└── MisaConnect.ESign.IntegrationTests/      # already exists; will be added to integration.yml (if present)

samples/
├── MisaConnect.Samples.Api/                 # unchanged; resolves via project reference
└── MisaConnect.Samples.Console/             # unchanged

.github/workflows/
├── ci.yml                                   # add ESign unit-test step
├── integration.yml                          # add ESign integration-test step (if file exists)
└── release.yml                              # add `dotnet pack` for ESign + fix release title

.specify/memory/
└── constitution.md                          # amend Principle II + VII + governance footer; bump 1.0.0 → 1.1.0

specs/
├── 001-misa-esign-pdf-sign-flow/            # already complete; no change
├── 002-misa-esign-2fa-otp/                  # already complete; no change
├── 003-misa-esign-multi-format/             # tasks.md T096–T115 closure
├── 004-misa-esign-webhook/                  # already complete; no change
└── 005-release-esign-2-0/                   # this feature

CHANGELOG.md                                 # promote [Unreleased] → [2.0.0]
CONTRIBUTING.md                              # add one-line tag-per-product note
README.md                                    # ensure ESign install snippets read --version 2.0.0
```

**Structure Decision**: NuGet library SDK with two product families under one solution. No new projects. All deliverables are version metadata, workflow YAML, governance prose, or bookkeeping markers in existing files. The `BundleReferencedProjects` MSBuild target in [src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj:36-45](src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj#L36-L45) — which bundles Domain/Application/Infrastructure DLLs into the Client package — is reused as-is.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Amending Principle II of the constitution (a Principle I–IV change) without the major version bump on every existing product-family package that the governance footer normally requires | The amendment is **additive**: new wording is a strict superset that covers both `MisaConnect.EInvoice` and `MisaConnect.ESign`. No public surface is removed or restricted. The existing `MisaConnect.EInvoice 1.1.0` package already complies with the new wording (it still defines its public surface only in `.Client` and `.Domain`). Forcing a `MisaConnect.EInvoice 2.0.0` would falsely signal a breaking change to consumers. | Bumping `MisaConnect.EInvoice` to 2.0.0 to comply with the footer's letter — rejected because it would be misleading semver. Splitting the amendment into a separate constitutional PR — rejected because it leaves the 2.0.0 release sitting on top of a constitution that explicitly contradicts it (Principle II names only EInvoice as the public surface). |
| Constitution version bump `1.0.0` → `1.1.0` rather than `2.0.0` for a Principle II edit | Principles II edits would normally fall under "Changes to Principles I–IV require a major version bump on `MisaConnect.EInvoice`." The Governance section's intent (per its own wording) is to protect consumers from silent contract breaks — which an additive product-family enumeration does not cause. The amendment text is being rewritten to make this additivity explicit. | A `2.0.0` constitution bump — rejected because semver, even for the governance doc, should reflect breaking changes. Naming a second product family is not a breaking change to the principle's meaning. The bump to `1.1.0` is recorded with a sync-impact note (FR-012) so the reasoning is preserved. |

These two entries are the only intentional deviations. Both are localized to the constitution amendment and explained in `contracts/constitution-amendment.md` (Phase 1 output).

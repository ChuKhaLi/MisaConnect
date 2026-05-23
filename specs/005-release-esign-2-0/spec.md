# Feature Specification: Release `MisaConnect.ESign` 2.0.0 (GA)

**Feature Branch**: `005-release-esign-2-0`
**Created**: 2026-05-23
**Status**: Draft
**Input**: User description: "I want to release new nuget version that support misa esign, into new package. - check if we fully implement misa esign @docs/misa-esign-spec-plan.md  - then propose plan to release new version"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Ship `MisaConnect.ESign` 2.0.0 GA to nuget.org (Priority: P1)

A .NET developer integrating MISA eSign into their app runs `dotnet add package MisaConnect.ESign --version 2.0.0`, calls `services.AddMisaConnectESign(IConfiguration)` once, and gets a stable, semver-compliant API covering every operation implemented across slices 1–4 (PDF / OTP / XML / Word / Excel signing, plus webhook-mode initiation and delivery). The package listing on nuget.org displays the project README, icon, and a release-notes excerpt drawn from the `[2.0.0]` section of the changelog.

**Why this priority**: This is the entire reason for the feature. Without this, no consumer can take a versioned dependency on the work that has accumulated across four slices, and the product family remains stuck in preview.

**Independent Test**: After the tag is pushed, a fresh scratch .NET 8 console app can `dotnet add package MisaConnect.ESign --version 2.0.0`, register the SDK, and invoke any one facade method (e.g. `SignPdfAsync` against the fake server) without errors. The nuget.org listing shows the README, icon, tags, version 2.0.0, and links back to the GitHub Release.

**Acceptance Scenarios**:

1. **Given** the release branch is merged to `main` and a `v2.0.0` git tag is pushed, **When** the tag-triggered release workflow runs to completion, **Then** `MisaConnect.ESign 2.0.0` appears on nuget.org with README, icon, and the slice-1..4 change list pulled from `CHANGELOG.md`.
2. **Given** a consumer has the package installed at version 2.0.0, **When** they configure `Misa:ESign` and call `services.AddMisaConnectESign(configuration)`, **Then** every public facade method documented in the changelog resolves and compiles against the package's `lib/net8.0/` assemblies without needing additional MisaConnect.ESign.* sub-packages.
3. **Given** the `v2.0.0` tag is pushed, **When** the workflow creates the GitHub Release, **Then** the release title reads `MisaConnect.ESign 2.0.0`, the body matches the `[2.0.0]` CHANGELOG section, and both `MisaConnect.ESign.2.0.0.nupkg` and its `.snupkg` are attached.

---

### User Story 2 — Bookkeeping reflects shipping reality (Priority: P2)

A maintainer auditing the repo immediately after release can open any spec / governance / docs surface and find a single, internally-consistent story: every slice marked complete, the package version on every artifact agrees, the constitution acknowledges both product families, and the changelog has a real `[2.0.0]` section rather than an open `[Unreleased]` block. There are no stale "Shipping as preview.N" claims, no unchecked tasks that have actually been built, and no governance text that names only one product.

**Why this priority**: This is what makes the release maintainable across product families. A 2.0.0 release with inconsistent paperwork creates ambiguity for every subsequent slice and PR review.

**Independent Test**: A grep audit across the repo finds (a) zero `[ ]` markers in `specs/003-misa-esign-multi-format/tasks.md`, (b) the `MisaConnect.ESign` Client csproj `<Version>` element reads `2.0.0`, (c) [`CHANGELOG.md`](CHANGELOG.md) has a dated `## [2.0.0] - YYYY-MM-DD` section above an empty `[Unreleased]`, (d) the constitution's Principles II and VII name both `MisaConnect.EInvoice` and `MisaConnect.ESign`, and (e) README install snippets reference `--version 2.0.0`, not any `-preview.N` tag.

**Acceptance Scenarios**:

1. **Given** slice 3's tasks T096–T115 are unchecked but the test files they reference exist on disk, **When** a maintainer walks each task, **Then** they either confirm coverage and mark `[X]`, or add the missing assertion(s) in place before marking — no `[X]` exists without verified coverage.
2. **Given** the changelog still lists `[Unreleased]` with all four ESign slices, **When** the release is cut, **Then** the entire ESign block is moved under a new dated `## [2.0.0]` section, inline preview-shipping claims are consolidated into one provenance sentence, and a fresh empty `[Unreleased]` section is left at the top.
3. **Given** the constitution names only `MisaConnect.EInvoice` in Principle II (public surface) and Principle VII (semver discipline), **When** the release lands, **Then** both principles refer to the family pattern (`MisaConnect.<Product>.{Client, Domain}`) covering both `EInvoice` and `ESign`, the governance footer requires major bumps on every affected family, and the constitution version is bumped to `1.1.0` with a sync-impact note citing this release.

---

### User Story 3 — CI and release plumbing cover both product families (Priority: P2)

A contributor opens a PR that touches `MisaConnect.ESign.*` code and sees the CI workflow run the ESign unit tests (not only EInvoice). When a `v2.0.0` tag is pushed, the release workflow packs and pushes both `MisaConnect.EInvoice` and `MisaConnect.ESign` nupkgs without manual intervention, while the `--skip-duplicate` flag protects already-published EInvoice versions.

**Why this priority**: Without this, the release won't actually publish the new package, and ESign regressions can land on `main` undetected. Strictly enabling the technical release path.

**Independent Test**: On a feature branch that deliberately breaks an ESign unit test, the `ci.yml` workflow fails. On a dry-run with a throwaway tag (e.g. `v2.0.0-rc-test`), the `release.yml` workflow produces two `.nupkg` files in `artifacts/`, attempts to push both, and creates a single GitHub Release pointing at the right CHANGELOG section.

**Acceptance Scenarios**:

1. **Given** a PR with a failing ESign unit test, **When** `ci.yml` runs, **Then** the workflow fails on the ESign unit-test step (not silently passing because only EInvoice tests ran).
2. **Given** the `v2.0.0` git tag is pushed, **When** `release.yml` runs, **Then** both `MisaConnect.EInvoice.<v>.nupkg` (no-op via `--skip-duplicate` if version already published) and `MisaConnect.ESign.2.0.0.nupkg` are uploaded to nuget.org, and both `.nupkg` files appear as GitHub Release assets.
3. **Given** an integration test workflow (if present) exists, **When** it runs, **Then** both EInvoice and ESign integration test projects execute under the same skip-cleanly contract for sandbox credentials.

---

### Edge Cases

- **Stale tag re-push** — if `v2.0.0` is ever re-pushed (force-tag or accidental re-run), `--skip-duplicate` on `dotnet nuget push` MUST prevent re-publication, but the GitHub Release create step will fail with `tag already exists`. Acceptable: maintainer manually deletes the release before re-pushing.
- **Constitutional update breaks dependent templates** — if widening Principle II / VII causes `.specify/templates/*` to disagree (e.g. a template literal-references "MisaConnect.EInvoice" as the only public surface), the dependent file MUST be updated in the same commit.
- **Slice 3 task verification finds a real gap** — if walking T096–T115 reveals a test file that exists but doesn't actually cover the FR/SC the task names, the gap MUST be closed in code before the task is marked `[X]`. This blocks the release until fixed.
- **`CHANGELOG.md` extraction returns empty** — the `awk` extractor in `release.yml` depends on `## [VERSION]` heading exactly matching the pushed tag minus `v`. Section heading drift between CHANGELOG and tag MUST be caught locally before pushing the tag.
- **Sample API drifts off the new version** — `samples/MisaConnect.Samples.Api/` references `MisaConnect.ESign.Client` by project reference, not by NuGet, so it builds against whatever `<Version>` is in the csproj at the moment. Bumping the csproj version is sufficient; no sample edit needed.
- **Webhook signature verification** — explicitly out of scope per [specs/004-misa-esign-webhook/spec.md](specs/004-misa-esign-webhook/spec.md). The 2.0.0 surface MUST NOT promise it.
- **Per-layer packages requested post-release** — declined. The Client package bundles the other three layers' DLLs via the existing `BundleReferencedProjects` MSBuild target. Reopening this is a 2.x scope item, not a 2.0.0 blocker.

## Requirements *(mandatory)*

### Functional Requirements

**Versioning and package metadata**

- **FR-001**: The system MUST publish a single NuGet package named `MisaConnect.ESign` at version `2.0.0` that bundles the Client, Application, Infrastructure, and Domain assemblies in its `lib/net8.0/` output.
- **FR-002**: The `MisaConnect.ESign.Client` project file MUST declare `<Version>2.0.0</Version>` for local builds; the CI release workflow MAY override via `-p:Version=` from the pushed tag.
- **FR-003**: The package MUST embed the repo README and icon at the package root so they render on nuget.org.
- **FR-004**: The `MisaConnect.EInvoice` package MUST remain unaffected by the ESign release — its version stays at `1.1.0`, no public-surface changes, and the `--skip-duplicate` push flag protects it from accidental re-publication.

**Changelog and documentation**

- **FR-005**: `CHANGELOG.md` MUST have a dated `## [2.0.0] - <release-date>` section listing every public-surface addition shipped across ESign slices 1, 2, 3, and 4, with one sentence at the top noting the prior preview tags (`2.0.0-preview.1` through `2.0.0-preview.4`).
- **FR-006**: `CHANGELOG.md` MUST retain an empty `## [Unreleased]` heading above the `[2.0.0]` section for post-2.0 work to accumulate against.
- **FR-007**: `README.md` MUST list every public method on `IMisaESignClient` actually shipping in 2.0.0 in its ESign supported-operations table, and every install-snippet under the ESign section MUST reference `--version 2.0.0` (no `-preview.N`).
- **FR-008**: Slice 3's `tasks.md` MUST have every task marked `[X]` by the time the release tag is pushed, with each `[X]` backed by a test file (or doc artifact) that demonstrably covers the FR / SC the task references.

**Governance**

- **FR-009**: The constitution's Principle II MUST identify the public surface in terms of a `MisaConnect.<Product>.{Client, Domain}` family pattern that explicitly enumerates both `EInvoice` and `ESign`.
- **FR-010**: The constitution's Principle VII MUST state that each product-family package is versioned independently, with major bumps confined to that family.
- **FR-011**: The constitution's governance footer MUST require major version bumps on **every** affected product-family package when Principles I–IV change.
- **FR-012**: The constitution MUST be bumped to version `1.1.0` with a sync-impact note citing this release as the trigger.

**CI and release plumbing**

- **FR-013**: `.github/workflows/ci.yml` MUST run unit tests for both `MisaConnect.EInvoice.UnitTests` and `MisaConnect.ESign.UnitTests` on every push and pull request.
- **FR-014**: `.github/workflows/release.yml` MUST, on `v*` tags, pack both `src/MisaConnect.EInvoice.Client/MisaConnect.EInvoice.Client.csproj` and `src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj` into `artifacts/` and push the resulting nupkg files to nuget.org with `--skip-duplicate`.
- **FR-015**: The GitHub Release created by `release.yml` for the `v2.0.0` tag MUST have title `MisaConnect.ESign 2.0.0` and body content matching the new `[2.0.0]` section in `CHANGELOG.md`.
- **FR-016**: If `.github/workflows/integration.yml` exists, it MUST also run the `MisaConnect.ESign.IntegrationTests` project, and that project MUST continue to skip sandbox-credential tests cleanly when `MISACONNECT_ESIGN_SANDBOX_*` env vars are absent.

**Release verification**

- **FR-017**: Before tagging, the system MUST pass `dotnet format MisaConnect.slnx` with zero diff, `dotnet build MisaConnect.slnx --configuration Release` with zero warnings under `TreatWarningsAsErrors=true`, and every unit and integration test project under `tests/`.
- **FR-018**: A local `dotnet pack` of `MisaConnect.ESign.Client.csproj` at version `2.0.0` MUST produce a `.nupkg` containing all four ESign assemblies in `lib/net8.0/`, plus README and icon at the package root.
- **FR-019**: The `CHANGELOG.md` release-notes extractor (`awk` script in `release.yml`) MUST produce non-empty output when run against `## [2.0.0]` locally — verifying the heading exactly matches the tag-minus-`v` pattern.

### Key Entities

- **Package `MisaConnect.ESign` v2.0.0** — the NuGet artifact being shipped. Contains four bundled DLLs (Client, Application, Infrastructure, Domain) plus README and icon. PackageId = `MisaConnect.ESign`; assemblies live at `lib/net8.0/`.
- **Git tag `v2.0.0`** — the trigger that drives publication. Pushed only after every FR-017 gate is green locally.
- **`CHANGELOG.md` `[2.0.0]` section** — both the consumer-facing release-notes source (via `awk` extraction) and the durable record of what shipped under this version.
- **Slice 3 `tasks.md`** — the bookkeeping artifact that must be reconciled before release. Currently has 20 unchecked tasks (T096–T115) whose underlying files were created by slice 4.
- **Constitution `.specify/memory/constitution.md`** — the governance source of truth. Version bumps from `1.0.0` to `1.1.0` to reflect product-family parity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The `v2.0.0` tag-triggered workflow run completes end-to-end (build → tests → pack → push → GitHub Release) in under 15 minutes on its first attempt, with zero manual interventions.
- **SC-002**: The published `MisaConnect.ESign 2.0.0` package is installable via `dotnet add package MisaConnect.ESign --version 2.0.0` within 30 minutes of the GitHub Release going live (allowing for nuget.org indexing delay).
- **SC-003**: A scratch .NET 8 console app that takes a dependency on the published 2.0.0 package can call any one ESign facade method (e.g. `SignPdfAsync`, `SignXmlAsync`, `HandleWebhookAsync`) without adding additional MisaConnect.ESign.* package references.
- **SC-004**: Zero `[ ]` markers remain in `specs/003-misa-esign-multi-format/tasks.md` after the release is cut — every task is either verified `[X]` or explicitly retired with a one-line note.
- **SC-005**: Zero stale "Shipping as `2.0.0-preview.N`" claims remain in the `[2.0.0]` CHANGELOG section after the section is created — provenance is captured in one consolidated sentence rather than scattered across slice bullets.
- **SC-006**: A `grep -i misaconnect.einvoice` against `.specify/memory/constitution.md` MUST return at least one match where `MisaConnect.ESign` also appears in the same principle — proving both product families are named together.
- **SC-007**: The `MisaConnect.ESign` unit test suite runs in under 30 seconds without network access, matching the Principle VI standard already enforced on EInvoice.
- **SC-008**: The `MisaConnect.EInvoice` package version on nuget.org is unchanged by the `v2.0.0` release (latest EInvoice version remains `1.1.0`); the `--skip-duplicate` flag prevents accidental re-publication.

## Assumptions

- The four ESign slice specs in `specs/001-misa-esign-pdf-sign-flow/` through `specs/004-misa-esign-webhook/` are the **complete** scope of 2.0.0. No new ESign operations land in this release.
- The single-bundled-package model (PackageId `MisaConnect.ESign` carrying all four DLLs) matches consumer expectations; per-layer sub-packages are out of scope.
- Tag-per-product-family is the release convention going forward: `v2.0.0` is dedicated to ESign GA; future EInvoice releases use their own `v1.x.y` tags. This is captured in a one-line note under `## Releasing` in `CONTRIBUTING.md`.
- The existing `release.yml` workflow's NuGet push step uses `--skip-duplicate`, so accidentally including the EInvoice package in the same multi-pack invocation is safe (it will no-op if its current version is already published).
- The `MISACONNECT_ESIGN_SANDBOX_*` env vars are configured as required GitHub Actions secrets for integration runs; absent that, `[SandboxFact]` tests skip cleanly per Principle VI.
- Slice 3's bookkeeping closure is genuinely a bookkeeping exercise — every test file the unchecked tasks reference already exists. If a walkthrough finds a real coverage gap, the release blocks until it's fixed in code.
- The constitution change from `1.0.0` to `1.1.0` is backward-compatible under its own governance rules (Principles V–VIII may be MINOR bumps). Principle II is technically a Principles I–IV change, but the existing text already restricts public surface in a way the new wording preserves — the widening is additive, not restrictive.
- Webhook signature verification, durable webhook queueing, registration / discovery, mass-document signing in a single transaction, alternative MFA providers, and migration tooling between polling and webhook modes are all explicitly out of scope for 2.0.0.

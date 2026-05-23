# Phase 1 Data Model — Release `MisaConnect.ESign` 2.0.0

This is a release-engineering feature; "entities" here are release artifacts rather than runtime domain objects. Each entity below lists its fields (the things that must hold a specific value at the moment of release) and the state-transition that brings it from its pre-release to its post-release form.

---

## E-1 — NuGet package `MisaConnect.ESign`

| Field | Pre-release value | Required post-release value | Source of truth |
|---|---|---|---|
| `PackageId` | `MisaConnect.ESign` | `MisaConnect.ESign` (unchanged) | [src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj:6](src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj#L6) |
| `Version` (csproj literal) | `2.0.0-preview.2` | `2.0.0` | [src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj:7](src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj#L7) |
| `Version` (CI override) | n/a | `2.0.0` (derived from tag `v2.0.0` by `release.yml` step `Derive version from tag`) | [.github/workflows/release.yml:25-29](.github/workflows/release.yml#L25-L29) |
| `Description` | unchanged | unchanged (PDF + multi-format + webhook description already includes the full surface) | csproj line 8 |
| `PackageTags` | `misa;esign;remotesigning;vietnam;pdf;signature;sdk` | unchanged | csproj line 9 |
| Bundled assemblies in `lib/net8.0/` | n/a (not yet built) | `MisaConnect.ESign.Client.dll`, `.Application.dll`, `.Infrastructure.dll`, `.Domain.dll` (+ PDBs) | `BundleReferencedProjects` MSBuild target on csproj lines 36–45 |
| Bundled root files | n/a | `README.md`, `icon.png` | csproj lines 27–28 |
| Published on nuget.org | not yet (only previews) | `MisaConnect.ESign 2.0.0` | nuget.org/packages/MisaConnect.ESign |

**State transition**: Pre-release → workflow run cuts a Release-configuration build with `-p:Version=2.0.0` → `dotnet pack` produces `artifacts/MisaConnect.ESign.2.0.0.nupkg` + `.snupkg` → `dotnet nuget push --skip-duplicate` uploads to nuget.org → `gh release create` attaches both files to the GitHub Release.

## E-2 — Git tag `v2.0.0`

| Field | Required value |
|---|---|
| Tag name | `v2.0.0` |
| Tag type | annotated (created via `git tag -a v2.0.0 -m "..."`) |
| Tag target | merge commit of `005-release-esign-2-0` branch into `main` |
| Workflow trigger | `release.yml` (`on.push.tags: ['v*']`) |

**State transition**: Tag is pushed only after every FR-017 local gate passes (`dotnet format`, `dotnet build`, all four test projects).

## E-3 — `CHANGELOG.md` `[2.0.0]` section

| Field | Pre-release value | Required post-release value |
|---|---|---|
| Section heading | `## [Unreleased]` (contains all four ESign slice blocks) | `## [2.0.0] - <release-date>` (new) above an empty `## [Unreleased]` |
| Provenance sentence | scattered "Shipping as `2.0.0-preview.N`" markers in each slice block | one sentence at the top of `[2.0.0]`: `"Public API is the union of slices 1–4, previewed through 2.0.0-preview.1..preview.4."` |
| Slice ordering inside the section | reverse chronological (Slice 4 first) | slice-priority (Slice 1 → Slice 4) for readability |
| awk-extractor output | empty for `[2.0.0]` (section doesn't exist) | non-empty (verified via FR-019 local check) |

**State transition**: Insert new heading → move existing `[Unreleased]` content under it → rewrite provenance sentence → leave empty `[Unreleased]` at top.

## E-4 — Slice 3 `tasks.md` (bookkeeping closure)

| Field | Pre-release value | Required post-release value |
|---|---|---|
| Tasks T001–T095 | `[X]` | unchanged |
| Tasks T096–T100 (US2 dispatch tests) | `[ ]` | `[X]` after verification per R-6 |
| Tasks T101–T106 (US3 error-mapping tests) | `[ ]` | `[X]` after verification per R-6 |
| Tasks T107–T111 (regression + layer audit + quickstart validation) | `[ ]` | `[X]` after verification per R-6 |
| Tasks T112–T115 (final gates: format / build / unit / integration) | `[ ]` | `[X]` after running the four commands literally |

**State transition**: Per-task verification (file exists → assertions cover FR/SC → optionally add missing assertion → mark `[X]`). FR-008 prohibits a `[X]` mark without verified coverage.

## E-5 — Constitution `.specify/memory/constitution.md`

| Field | Pre-release value | Required post-release value |
|---|---|---|
| Version line | `Ratified: 2026-05-13 · Version: 1.0.0` | `Ratified: 2026-05-13 · Last amended: 2026-MM-DD · Version: 1.1.0` |
| Principle II — public surface | names `MisaConnect.EInvoice.Client` and `.Domain` only | names `MisaConnect.<Product>.{Client, Domain}` family pattern; enumerates `EInvoice` and `ESign` |
| Principle VII — semver | "The `MisaConnect.EInvoice` package version drives the contract..." | "Each product-family NuGet package (`MisaConnect.EInvoice`, `MisaConnect.ESign`, …) is versioned independently..." |
| Governance footer | "...major version bump on `MisaConnect.EInvoice`" | "...major version bump on **every** affected product-family package" |
| Sync-impact note | absent | one paragraph at the bottom citing this release as the trigger |

**State transition**: Apply text edits → bump version → add sync-impact note → grep for stale references to "the package" (singular) in CLAUDE.md, CONTRIBUTING.md, `.specify/templates/*` and qualify where ambiguous.

## E-6 — GitHub Release `v2.0.0`

| Field | Required post-release value |
|---|---|
| Tag | `v2.0.0` |
| Title | `MisaConnect.ESign 2.0.0` |
| Body | output of `awk "/^## \[2.0.0\]/{flag=1; next} /^## \[/{flag=0} flag" CHANGELOG.md` |
| Attached assets | `MisaConnect.ESign.2.0.0.nupkg`, `MisaConnect.ESign.2.0.0.snupkg`. (EInvoice nupkg may or may not be attached depending on whether `dotnet pack` is also invoked for it in the multi-pack step; if attached, it's a no-op via `--skip-duplicate` on the push side.) |
| Created by | `release.yml` step `Create GitHub Release` via `gh release create` |

**State transition**: Created automatically by `release.yml` after `dotnet nuget push` succeeds.

## E-7 — CI workflow `.github/workflows/ci.yml`

| Field | Pre-release value | Required post-release value |
|---|---|---|
| Unit-test step | one step running only `MisaConnect.EInvoice.UnitTests` | two sequential steps: one for `EInvoice.UnitTests`, one for `ESign.UnitTests`. Both honor `inputs.test_filter` and `inputs.configuration`. |
| Test result artifacts | one `trx` file uploaded under `test-results/` | both `trx` files uploaded under `test-results/` (rename per project to avoid collision) |

## E-8 — Release workflow `.github/workflows/release.yml`

| Field | Pre-release value | Required post-release value |
|---|---|---|
| Unit-test step | one step running only `MisaConnect.EInvoice.UnitTests` | mirror E-7's split |
| Pack step | one `dotnet pack` for `MisaConnect.EInvoice.Client.csproj` | two `dotnet pack` invocations (EInvoice, then ESign), both into `artifacts/` |
| Release title | hardcoded `MisaConnect.EInvoice $VERSION` | `MisaConnect.ESign $VERSION` for this release (tag-per-product convention; revisit if/when EInvoice ships under a `v*` tag in the same convention) |
| Release notes | extracted via existing awk command | unchanged — awk works against the new `[2.0.0]` heading |
| NuGet push glob | `artifacts/*.nupkg` (already multi-package safe) | unchanged |

## E-9 — `CONTRIBUTING.md` `## Releasing` section

| Field | Required post-release value |
|---|---|
| New convention line | one sentence describing tag-per-product: e.g. "Each product-family package (`MisaConnect.EInvoice`, `MisaConnect.ESign`) ships under its own `v<MAJOR>.<MINOR>.<PATCH>` tag; the GA tag's title in `release.yml` names that family." |

## E-10 — `README.md` ESign supported-operations table

| Field | Required post-release value |
|---|---|
| Public methods listed | every public method on `IMisaESignClient`: `SignPdfAsync`, `SignInWithOtpAsync`, `ResendOtpAsync`, `SignXmlAsync` (string + bytes overloads), `SignWordAsync`, `SignExcelAsync`, `BeginSignPdfAsync`, `BeginSignXmlAsync`, `BeginSignWordAsync`, `BeginSignExcelAsync`, `HandleWebhookAsync` |
| Install snippet version | `--version 2.0.0` (replace any `--version 2.0.0-preview.N`) |
| Status phrasing | replace any "planned for v2.0" with "released in v2.0" |

---

## Relationships

- **E-1 (package)** depends on **E-3 (CHANGELOG)** for release-notes content and on **E-5 (constitution)** for governance compliance.
- **E-2 (tag)** depends on **E-1 (package)** being ready to pack — i.e. csproj `<Version>` bumped and CHANGELOG promoted.
- **E-6 (GitHub Release)** is created automatically by **E-2 (tag)** push triggering **E-8 (release.yml)**, and reads from **E-3 (CHANGELOG)**.
- **E-7 (ci.yml)** changes are independent of the tag push — they take effect immediately on merge to `005-release-esign-2-0` and block accidental ESign regressions in subsequent PRs.
- **E-4 (slice 3 tasks)** must be closed before **E-2 (tag)** is pushed — FR-008 / SC-004.
- **E-9 (CONTRIBUTING)** documents the convention decided in R-3; landed in the same PR as **E-8 (release.yml)** edits.
- **E-10 (README)** is part of the **E-1 (package)** content (README.md is bundled into the nupkg); must be updated before pack.

---

## Validation rules

Per FR-017–FR-019:

- `dotnet format MisaConnect.slnx` → zero diff
- `dotnet build MisaConnect.slnx --configuration Release` → zero warnings under `TreatWarningsAsErrors=true`
- Each test project listed in E-7 / E-8 → green; ESign unit suite under 30s (SC-007)
- `dotnet pack` of E-1 → `.nupkg` contains all four DLLs in `lib/net8.0/` + README + icon at root
- `awk "/^## \[2.0.0\]/{flag=1; next} /^## \[/{flag=0} flag" CHANGELOG.md` → non-empty output
- `grep -i misaconnect.einvoice` against `.specify/memory/constitution.md` → at least one match co-located with a `MisaConnect.ESign` mention in the same principle (SC-006)

No state transitions are valid that bypass these gates.

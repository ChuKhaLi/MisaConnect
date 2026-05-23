---
description: "Task list for release-esign-2-0 — ship MisaConnect.ESign 2.0.0 GA NuGet package"
---

# Tasks: Release `MisaConnect.ESign` 2.0.0 (GA)

**Branch**: `005-release-esign-2-0` | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)
**Input**: Design documents from `specs/005-release-esign-2-0/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: This is a release-engineering feature. There is **no new production code** — every "test" is a verification gate run against existing test suites (per FR-017) or an inspection of an existing artifact (per FR-018, FR-019). Verification gates are listed as concrete tasks in Phase 6.

**Organization**: Tasks are grouped by user story so each layer of the release can be implemented and verified independently. Because this is a release-engineering slice, User Story 1 (the actual release) depends on User Stories 2 and 3 being landed first — see Dependencies section.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps task to user story for traceability (US1, US2, US3); Setup / Foundational / Polish phases have no story label
- All file paths are repository-relative

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the working tree is on `005-release-esign-2-0`, clean, and the baseline build / unit / integration suites are green before any release-engineering edits land. No new projects or dependencies — every artifact already exists from slices 1–4.

- [X] T001 Confirm working tree is on branch `005-release-esign-2-0` and clean by running `git status` and `git branch --show-current` from repo root; if uncommitted changes exist that aren't release artifacts, stash or commit them before proceeding
- [X] T002 [P] Verify baseline build is green by running `dotnet build MisaConnect.slnx --configuration Release` from repo root (warnings break the build under `TreatWarningsAsErrors=true` per [Directory.Build.props](Directory.Build.props))
- [X] T003 [P] Verify baseline EInvoice unit tests are green by running `dotnet test tests/MisaConnect.EInvoice.UnitTests/MisaConnect.EInvoice.UnitTests.csproj --no-build --configuration Release` from repo root
- [X] T004 [P] Verify baseline ESign unit tests are green and stay under 30s (SC-007) by running `dotnet test tests/MisaConnect.ESign.UnitTests/MisaConnect.ESign.UnitTests.csproj --no-build --configuration Release` from repo root
- [X] T005 [P] Verify baseline integration tests skip cleanly without sandbox creds by running `dotnet test tests/MisaConnect.EInvoice.IntegrationTests/MisaConnect.EInvoice.IntegrationTests.csproj --no-build --configuration Release` and `dotnet test tests/MisaConnect.ESign.IntegrationTests/MisaConnect.ESign.IntegrationTests.csproj --no-build --configuration Release` from repo root

**Checkpoint**: Baseline is green. The release-engineering work can begin without inheriting regressions.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Close slice-3 bookkeeping in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md). Per FR-008, every `[ ]` task must be either verified `[X]` or have its underlying gap fixed in code before any release-side edits land. Slice 4's regression work already created the files these tasks reference (`PerFormatErrorMapperTests.cs`, `FormatPropertyContractTests.cs`, `MultiArrayResponseDispatchFakeServerTests.cs`, `MissingFormatArrayFakeServerTests.cs`, `PerFormatErrorMappingFakeServerTests.cs`, `PdfRegressionFakeServerTests.cs`, etc.) — see research.md R-6 for the walk procedure.

**⚠️ CRITICAL**: No user story work begins until this phase is complete. SC-004 requires zero `[ ]` markers in slice 3's tasks.md after the release is cut.

- [X] T006 Walk slice-3 task T096 ([specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md) ~line 230) — open [tests/MisaConnect.ESign.UnitTests/Application/PerFormatResponseDispatchTests.cs](tests/MisaConnect.ESign.UnitTests/Application/PerFormatResponseDispatchTests.cs) (or the file the task names), verify its assertions cover the FR/SC the task references; if complete, mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md); if partial, add missing assertion(s) first, then mark
- [X] T007 Walk slice-3 task T097 — same procedure as T006 applied to the file the task names in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T008 Walk slice-3 task T098 — verify [tests/MisaConnect.ESign.UnitTests/Application/MissingFormatArrayUnitTests.cs](tests/MisaConnect.ESign.UnitTests/Application/MissingFormatArrayUnitTests.cs) (or the file the task names) covers its FR/SC; mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T009 Walk slice-3 task T099 — verify [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/MultiArrayResponseDispatchFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/MultiArrayResponseDispatchFakeServerTests.cs) covers its FR/SC; mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T010 Walk slice-3 task T100 — verify [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/MissingFormatArrayFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/MissingFormatArrayFakeServerTests.cs) covers its FR/SC; mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T011 Walk slice-3 task T101 — verify [tests/MisaConnect.ESign.UnitTests/Errors/PerFormatErrorMapperTests.cs](tests/MisaConnect.ESign.UnitTests/Errors/PerFormatErrorMapperTests.cs) `[Theory]` rows match [specs/003-misa-esign-multi-format/contracts/error-mapping.md §A.10](specs/003-misa-esign-multi-format/contracts/error-mapping.md); mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T012 Walk slice-3 task T102 — verify [tests/MisaConnect.ESign.UnitTests/Errors/FormatPropertyContractTests.cs](tests/MisaConnect.ESign.UnitTests/Errors/FormatPropertyContractTests.cs) covers every typed exception per [specs/003-misa-esign-multi-format/contracts/public-surface.md §5](specs/003-misa-esign-multi-format/contracts/public-surface.md); mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T013 Walk slice-3 task T103 — verify [tests/MisaConnect.ESign.UnitTests/Errors/ESignErrorMapperTests.cs](tests/MisaConnect.ESign.UnitTests/Errors/ESignErrorMapperTests.cs) asserts `Format == DocumentFormat.Pdf` on every slice-1 PDF row (per FR-062 + SC-019); add the assertion if missing, then mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T014 Walk slice-3 task T104 — verify [tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs](tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs) covers per-format scrub-row assertions per FR-063 / SC-020; add missing rows if needed, mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T015 Walk slice-3 task T105 — verify [tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs](tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs) supports scriptable per-format rejection fixtures (malformed XML, missing-`mainDom` on Excel, missing-`signatureId` on XML, substring fallbacks); mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T016 Walk slice-3 task T106 — verify [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/PerFormatErrorMappingFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/PerFormatErrorMappingFakeServerTests.cs) drives the scripted fixtures and asserts `Format`, `RawCode`, correlation ID + zero secret leakage; mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T017 Walk slice-3 task T107 — verify [tests/MisaConnect.ESign.IntegrationTests/EndToEnd/PdfRegressionFakeServerTests.cs](tests/MisaConnect.ESign.IntegrationTests/EndToEnd/PdfRegressionFakeServerTests.cs) re-runs slice-1's PDF happy path and asserts byte-identical wire shapes + `Format == DocumentFormat.Pdf` everywhere (per FR-065 + SC-021); mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T018 Walk slice-3 task T108 — verify [tests/MisaConnect.ESign.UnitTests/Layering/MisaConnectESignLayerAuditTests.cs](tests/MisaConnect.ESign.UnitTests/Layering/MisaConnectESignLayerAuditTests.cs) extends its reflection audit to cover slice-3 namespaces (`Application.UseCases.{SignXml, SignWord, SignExcel, HashXmlDocument, HashWordDocument, HashExcelDocument, AttachSignatureToXml, AttachSignatureToWordExcel}`, `Domain.Documents.DocumentFormat`, `Domain.Signing.XmlSignatureContext`, `Application.Abstractions.{XmlHashOutput, WordExcelHashOutput}`); mark `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)
- [X] T019 Walk slice-3 task T109 — confirm [README.md](README.md) ESign supported-operations table already lists `SignXmlAsync` (both overloads), `SignWordAsync`, `SignExcelAsync` (it does, per the explore agent's earlier audit); if any are missing, add them before marking `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md). NOTE: the full README sweep happens in T021 — this task only confirms slice-3 specific entries
- [X] T020 Walk slice-3 task T110 — confirm [CHANGELOG.md](CHANGELOG.md) `[Unreleased]` block already enumerates slice-3's public-surface additions per [specs/003-misa-esign-multi-format/contracts/public-surface.md §9](specs/003-misa-esign-multi-format/contracts/public-surface.md); if any entry is missing, add it before marking `[X]` in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md). NOTE: the full CHANGELOG promotion happens in T024 — this task only confirms slice-3 specific entries are captured

**Checkpoint**: Slice-3 bookkeeping is closed; tasks T096–T110 in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md) all read `[X]`. T111–T115 of slice 3 are deferred to Phase 6 of this slice (they're the same `dotnet format`/build/test gates this release also needs).

---

## Phase 3: User Story 1 — Ship `MisaConnect.ESign 2.0.0` to nuget.org (Priority: P1) 🎯 MVP

**Goal**: A .NET developer runs `dotnet add package MisaConnect.ESign --version 2.0.0` and gets a stable SDK covering every operation across slices 1–4. The published package displays the project README, icon, and a release-notes excerpt.

**Independent Test**: After tag push, a scratch .NET 8 console app installs the published 2.0.0 package and resolves `IMisaESignClient.SignPdfAsync` (or any other facade method) without additional MisaConnect.ESign.* package references (SC-003).

**Note**: User Story 1's *consumer-visible artifact* is the published nupkg. The tasks below produce the package metadata that ends up inside the nupkg. The actual NuGet publication is handled by the polish phase (T037 onward) once US2 + US3 work has landed.

### Implementation for User Story 1

- [X] T021 [US1] Bump `<Version>2.0.0-preview.2</Version>` to `<Version>2.0.0</Version>` at [src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj:7](src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj#L7); confirm the rest of the csproj is unchanged (PackageId, Description, Tags, BundleReferencedProjects target, PrivateAssets="all" on the three ProjectReferences) per [contracts/package-metadata.md](contracts/package-metadata.md)
- [X] T022 [P] [US1] Sweep [README.md](README.md): confirm the ESign supported-operations table lists every public method on `IMisaESignClient` actually shipping in 2.0 — `SignPdfAsync`, `SignInWithOtpAsync`, `ResendOtpAsync`, `SignXmlAsync` (both overloads), `SignWordAsync`, `SignExcelAsync`, `BeginSignPdfAsync`, `BeginSignXmlAsync`, `BeginSignWordAsync`, `BeginSignExcelAsync`, `HandleWebhookAsync` (per data-model.md E-10); add any missing entries
- [X] T023 [P] [US1] Sweep [README.md](README.md): change every install snippet under the ESign section from `--version 2.0.0-preview.4` (or any other `-preview.N`) to `--version 2.0.0`; replace any "planned for v2.0" wording with "released in v2.0"

**Checkpoint**: User Story 1's nupkg-content artifacts are ready. The pack step in Phase 6 will produce a deployable `.nupkg` once US2 + US3 land.

---

## Phase 4: User Story 2 — Bookkeeping reflects shipping reality (Priority: P2)

**Goal**: Every spec / governance / docs surface tells one consistent story: every slice is complete, package versions agree across artifacts, the constitution names both product families, and the CHANGELOG has a real `[2.0.0]` section.

**Independent Test**: A repo-wide grep audit returns: zero `[ ]` markers in [specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md), a dated `## [2.0.0]` heading in [CHANGELOG.md](CHANGELOG.md), and at least one constitution principle naming both `MisaConnect.EInvoice` and `MisaConnect.ESign` (SC-004, SC-005, SC-006).

### Implementation for User Story 2

- [X] T024 [US2] Promote [CHANGELOG.md](CHANGELOG.md) `[Unreleased]` block: insert a new `## [2.0.0] - <release-date>` heading above the current `[Unreleased]` body (use the actual date the commit lands, ISO format `YYYY-MM-DD`); move every ESign slice-1/2/3/4 block under the new heading; consolidate the scattered "Shipping as `2.0.0-preview.N`" sentences into one provenance line at the top per [contracts/changelog-format.md](contracts/changelog-format.md); leave an empty `## [Unreleased]` section above the new dated heading
- [X] T025 [US2] Re-group bullets inside the new `## [2.0.0]` section in slice-priority order (Slice 1 → Slice 2 → Slice 3 → Slice 4) per the structure in [contracts/changelog-format.md](contracts/changelog-format.md); add a `### Changed` sub-section noting the constitution amendment
- [X] T026 [US2] Apply Edit 1 of [contracts/constitution-amendment.md](contracts/constitution-amendment.md): change line 3 of [.specify/memory/constitution.md](.specify/memory/constitution.md) from `Ratified: 2026-05-13 · Version: 1.0.0` to `Ratified: 2026-05-13 · Last amended: <today> · Version: 1.1.0`
- [X] T027 [US2] Apply Edit 2 of [contracts/constitution-amendment.md](contracts/constitution-amendment.md): replace the Principle II paragraph in [.specify/memory/constitution.md](.specify/memory/constitution.md) with the widened wording naming `MisaConnect.<Product>.{Client, Domain}` and enumerating `EInvoice` + `ESign`
- [X] T028 [US2] Apply Edit 3 of [contracts/constitution-amendment.md](contracts/constitution-amendment.md): replace the Principle VII paragraph in [.specify/memory/constitution.md](.specify/memory/constitution.md) with the independent per-family versioning wording
- [X] T029 [US2] Apply Edit 4 of [contracts/constitution-amendment.md](contracts/constitution-amendment.md): rewrite the Governance footer line in [.specify/memory/constitution.md](.specify/memory/constitution.md) to require major bumps on every affected product-family package, and add the editorial-additivity clarification sentence
- [X] T030 [US2] Apply Edit 5 of [contracts/constitution-amendment.md](contracts/constitution-amendment.md): append the dated sync-impact note at the bottom of [.specify/memory/constitution.md](.specify/memory/constitution.md), citing slice 005-release-esign-2-0 as the trigger and recording the additive-amendment rationale
- [X] T031 [P] [US2] Run the dependent-doc sweep per [contracts/constitution-amendment.md](contracts/constitution-amendment.md) "Required dependent-doc sweep": grep [CLAUDE.md](CLAUDE.md), [CONTRIBUTING.md](CONTRIBUTING.md), and `.specify/templates/*` for ambiguous singular references to "the package" / "the SDK" / `MisaConnect.EInvoice`-as-sole-product; qualify with the product name where the reference is ambiguous. Most occurrences are expected to read fine without changes.
- [X] T032 [P] [US2] Verify SC-006 by running `Select-String -Path .specify/memory/constitution.md -Pattern '(MisaConnect\.EInvoice|MisaConnect\.ESign)'` and confirming both names appear in at least one common principle block (II or VII)

**Checkpoint**: Bookkeeping is consistent. Every artifact that calls itself "2.0.0" agrees. Constitution is at 1.1.0 with a sync-impact note. Slice 3's tasks.md is closed.

---

## Phase 5: User Story 3 — CI and release plumbing cover both product families (Priority: P2)

**Goal**: PRs touching `MisaConnect.ESign.*` code see ESign unit tests run as part of CI. A `v2.0.0` tag triggers the release workflow to pack and push both EInvoice and ESign nupkgs without manual intervention.

**Independent Test**: On a feature branch that deliberately breaks an ESign unit test, the `ci.yml` workflow fails on the ESign step (proving the test ran). On a workflow dispatch dry-run, both `dotnet pack` invocations produce `.nupkg` files in `artifacts/` (SC-001 supporting acceptance R-1).

### Implementation for User Story 3

- [X] T033 [US3] Split [.github/workflows/ci.yml:41](.github/workflows/ci.yml#L41) `Unit tests` step into two sequential steps: one for `MisaConnect.EInvoice.UnitTests` (existing), one for `MisaConnect.ESign.UnitTests` (new); both honor `inputs.test_filter` and `inputs.configuration`; emit `.trx` results with distinct filenames (e.g. `einvoice-unit-tests.trx`, `esign-unit-tests.trx`) per [contracts/workflow-contract.md](contracts/workflow-contract.md) ci.yml acceptance C-1..C-5
- [X] T034 [US3] Update [.github/workflows/ci.yml](.github/workflows/ci.yml) `Upload test results` step to upload both `.trx` files under `artifacts/test-results/` without filename collision
- [X] T035 [P] [US3] Check whether [.github/workflows/integration.yml](.github/workflows/integration.yml) exists; if it does, apply the same EInvoice + ESign split for integration tests, preserving the existing `[SandboxFact]` skip-on-missing-credentials contract per [contracts/workflow-contract.md](contracts/workflow-contract.md). If the file does not exist, skip this task and note it in a comment in tasks.md when marking complete
- [X] T036 [US3] Edit [.github/workflows/release.yml:38](.github/workflows/release.yml#L38) `Unit tests` step: mirror the ci.yml split into two sequential steps (EInvoice + ESign), each invoking `dotnet test --no-build --configuration Release`
- [X] T037 [US3] Edit [.github/workflows/release.yml:41](.github/workflows/release.yml#L41) `Pack` step: add a second `dotnet pack src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj --configuration Release -p:Version=${{ steps.ver.outputs.version }} -o artifacts` line after the existing EInvoice pack line; both packs share the `artifacts/` directory per [contracts/workflow-contract.md](contracts/workflow-contract.md) release.yml step 5+6
- [X] T038 [US3] Edit [.github/workflows/release.yml:59](.github/workflows/release.yml#L59) `Create GitHub Release` step: change the `--title` value from `"MisaConnect.EInvoice $VERSION"` to `"MisaConnect.ESign $VERSION"` per R-3 in [research.md](research.md) and FR-015 in [spec.md](spec.md). Leave the awk extractor and `gh release create ... artifacts/*.nupkg artifacts/*.snupkg` unchanged — the existing glob already handles multi-package output
- [X] T039 [P] [US3] Add a one-sentence tag-per-product convention note under `## Releasing` in [CONTRIBUTING.md](CONTRIBUTING.md): `"Each product-family package (\`MisaConnect.EInvoice\`, \`MisaConnect.ESign\`) ships under its own \`v<MAJOR>.<MINOR>.<PATCH>\` git tag; \`release.yml\` names that family in the GitHub Release title."` per R-3

**Checkpoint**: CI gates ESign on every PR. `release.yml` packs both product families. Tag-per-product convention is documented.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Run every verification gate (FR-017, FR-018, FR-019), open the release PR, merge, tag, and observe the publication. Equivalent to slice 3's deferred T111–T115 plus the actual release ops.

### Verification gates (FR-017)

- [X] T040 Run `dotnet format MisaConnect.slnx` from repo root and confirm zero diff (FR-017 + slice-3 T112)
- [X] T041 Run `dotnet build MisaConnect.slnx --configuration Release -p:Version=2.0.0` from repo root and confirm zero warnings under `TreatWarningsAsErrors=true` (FR-017 + slice-3 T113)
- [X] T042 [P] Run `dotnet test tests/MisaConnect.EInvoice.UnitTests/MisaConnect.EInvoice.UnitTests.csproj --no-build --configuration Release` from repo root and confirm green (FR-017)
- [X] T043 [P] Run `dotnet test tests/MisaConnect.ESign.UnitTests/MisaConnect.ESign.UnitTests.csproj --no-build --configuration Release` from repo root and confirm green and <30s (FR-017 + SC-007 + slice-3 T114)
- [X] T044 [P] Run `dotnet test tests/MisaConnect.EInvoice.IntegrationTests/MisaConnect.EInvoice.IntegrationTests.csproj --no-build --configuration Release` from repo root and confirm it skips sandbox tests cleanly without credentials (FR-017)
- [X] T045 [P] Run `dotnet test tests/MisaConnect.ESign.IntegrationTests/MisaConnect.ESign.IntegrationTests.csproj --no-build --configuration Release` from repo root and confirm it skips sandbox tests cleanly without `MISACONNECT_ESIGN_SANDBOX_*` env vars (FR-017 + SC-023 + slice-3 T115)

### Local pack smoke test (FR-018)

- [X] T046 Run `dotnet pack src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj --configuration Release -p:Version=2.0.0 -o artifacts/local` from repo root
- [X] T047 Inspect `artifacts/local/MisaConnect.ESign.2.0.0.nupkg` as a ZIP per the verification script in [contracts/package-metadata.md](contracts/package-metadata.md): confirm `lib/net8.0/MisaConnect.ESign.{Client,Application,Infrastructure,Domain}.dll` all present, `README.md` and `icon.png` at the package root, `MisaConnect.ESign.nuspec` has `<id>MisaConnect.ESign</id>` + `<version>2.0.0</version>`, no `MisaConnect.EInvoice.*` DLL leaked into the bundle
- [X] T048 [P] Verify `.snupkg` symbol package is produced alongside the `.nupkg` and contains `.pdb` files for all four assemblies

### CHANGELOG extraction sanity check (FR-019)

- [X] T049 Run `awk "/^## \[2.0.0\]/{flag=1; next} /^## \[/{flag=0} flag" CHANGELOG.md` from repo root and confirm the output is non-empty and matches the expected slice-1..4 bullet structure per [contracts/changelog-format.md](contracts/changelog-format.md)

### Slice-3 bookkeeping final verification (SC-004)

- [X] T050 Run `Select-String -Path specs/003-misa-esign-multi-format/tasks.md -Pattern '^\s*-\s*\[\s\]'` from repo root and confirm zero matches (SC-004)

### Release ops

- [ ] T051 Stage every modified file with explicit `git add` (do not use `git add -A` or `.` per Git Safety Protocol): the ESign Client csproj, README.md, CHANGELOG.md, .specify/memory/constitution.md, CONTRIBUTING.md, .github/workflows/ci.yml, .github/workflows/release.yml, optionally .github/workflows/integration.yml, specs/003-misa-esign-multi-format/tasks.md (slice-3 closures), and specs/005-release-esign-2-0/tasks.md (this file)
- [ ] T052 Commit with message `"Release MisaConnect.ESign 2.0.0\n\nPer specs/005-release-esign-2-0/spec.md. Implements FR-001..FR-019."`; push with `git push -u origin 005-release-esign-2-0`
- [ ] T053 Open PR via `gh pr create --title "Release MisaConnect.ESign 2.0.0" --body "Per specs/005-release-esign-2-0/spec.md. Implements FR-001..FR-019."`; wait for the new `ci.yml` to run both unit-test steps green
- [ ] T054 After PR review and merge to `main`, switch to `main` (`git checkout main && git pull`), then create and push the annotated tag: `git tag -a v2.0.0 -m "MisaConnect.ESign 2.0.0"; git push origin v2.0.0`. CONFIRM WITH USER BEFORE PUSHING THE TAG — this is the irreversible step that triggers NuGet publication
- [ ] T055 Watch the `release.yml` run via `gh run watch`; confirm SC-001 (<15 min end-to-end), SC-002 (`MisaConnect.ESign 2.0.0` listed on nuget.org within 30 min of release), and SC-008 (`MisaConnect.EInvoice 1.1.0` unchanged on nuget.org)

### Post-publish verification (SC-003)

- [ ] T056 In a scratch directory outside the repo: `dotnet new console -n esign-2-0-smoke; cd esign-2-0-smoke; dotnet add package MisaConnect.ESign --version 2.0.0`; add a `services.AddMisaConnectESign(...)` call resolving `IMisaESignClient`; build and confirm zero additional `MisaConnect.ESign.*` package references are required (SC-003)
- [ ] T057 [P] Manually verify the nuget.org listing for `MisaConnect.ESign 2.0.0`: README renders, icon displays, tags appear, version reads 2.0.0, link back to GitHub Release is correct
- [ ] T058 [P] Manually verify the GitHub Release `v2.0.0` exists with title `MisaConnect.ESign 2.0.0`, body matches the `[2.0.0]` CHANGELOG section, and both `MisaConnect.ESign.2.0.0.nupkg` and `MisaConnect.ESign.2.0.0.snupkg` are attached as assets

**Checkpoint**: `MisaConnect.ESign 2.0.0` is live on nuget.org. Every success criterion in spec.md is verifiable.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — runs immediately after branch is created
- **Foundational (Phase 2)**: Depends on Setup; closes slice-3 bookkeeping. **Blocks every user story phase** per FR-008 + SC-004
- **User Story 1 (Phase 3)**: Depends on Foundational; produces nupkg-content metadata (csproj version, README install snippets). Does **not** depend on US2 or US3 at the file level — but its consumer-facing outcome (a published package) depends on US3's release.yml widening (T037) being merged before the tag push
- **User Story 2 (Phase 4)**: Depends on Foundational; produces CHANGELOG section + constitution amendment. Independent of US1 and US3 at the file level
- **User Story 3 (Phase 5)**: Depends on Foundational; produces CI / release.yml / CONTRIBUTING.md edits. Independent of US1 and US2 at the file level
- **Polish (Phase 6)**: Depends on US1 + US2 + US3 all complete. Verification gates run against the cumulative state; release ops cannot happen until the PR (containing US1 + US2 + US3 changes) is merged

### User Story Dependencies

- **US1 (P1)**: File-level independent of US2 and US3. *Outcome-level* depends on US2 (CHANGELOG must exist for `release.yml` to extract release notes; SC-005) and US3 (release.yml must pack ESign for the package to publish).
- **US2 (P2)**: Independent at the file level — only modifies CHANGELOG.md, .specify/memory/constitution.md, optionally CLAUDE.md / CONTRIBUTING.md / .specify/templates/*
- **US3 (P2)**: Independent at the file level — only modifies .github/workflows/*.yml and CONTRIBUTING.md (one shared file with US2: T039 vs T031 both touch CONTRIBUTING.md; resolve by landing T039 first or in same commit)

### Within Each User Story

- **US1**: T021 (csproj bump) precedes T046 (local pack); T022 / T023 (README) are independent of T021
- **US2**: T024 (CHANGELOG promotion) precedes T025 (re-grouping); T026..T030 (constitution edits) are sequential because they modify the same file; T031 / T032 are independent of the constitution edits
- **US3**: T033 (ci.yml split) and T036 (release.yml unit-tests split) follow the same pattern; T037 (release.yml pack) and T038 (release.yml title) are sequential because they modify the same file; T035 (integration.yml) is independent; T039 (CONTRIBUTING.md note) is independent

### Parallel Opportunities

- **Phase 1**: T002, T003, T004, T005 all marked [P] — all run independently against the unchanged baseline
- **Phase 2**: T006..T020 are **sequential** because each modifies the same file ([specs/003-misa-esign-multi-format/tasks.md](specs/003-misa-esign-multi-format/tasks.md)); cannot be parallelized safely
- **Phase 3 (US1)**: T022 and T023 marked [P] — same file (README.md), but each addresses a distinct subset of lines; if a developer treats them sequentially that's also fine. T021 is sequential before either
- **Phase 4 (US2)**: T031 and T032 marked [P] — both run after the constitution edits land; verification doesn't modify anything
- **Phase 5 (US3)**: T035 and T039 marked [P] — different files (integration.yml vs CONTRIBUTING.md)
- **Phase 6**: T042..T045 marked [P] — four `dotnet test` invocations, different test projects, no shared state; T048, T057, T058 marked [P]
- **Cross-phase parallelism**: After Foundational completes, **US2 and US3 can be developed by different team members in parallel**, with US1's csproj bump (T021) sequenced anywhere. US1's README sweeps (T022/T023) can run alongside.

---

## Parallel Example: Phase 1 baseline verification

```pwsh
# Launch all baseline verification tasks in parallel (different test projects, no shared state)
dotnet build MisaConnect.slnx --configuration Release                                                              # T002
dotnet test tests/MisaConnect.EInvoice.UnitTests/MisaConnect.EInvoice.UnitTests.csproj --no-build --configuration Release    # T003
dotnet test tests/MisaConnect.ESign.UnitTests/MisaConnect.ESign.UnitTests.csproj --no-build --configuration Release          # T004
dotnet test tests/MisaConnect.EInvoice.IntegrationTests/MisaConnect.EInvoice.IntegrationTests.csproj --no-build --configuration Release  # T005 (a)
dotnet test tests/MisaConnect.ESign.IntegrationTests/MisaConnect.ESign.IntegrationTests.csproj --no-build --configuration Release        # T005 (b)
```

## Parallel Example: Phase 6 verification gates after US1+US2+US3 land

```pwsh
# Verification gates — all independent, all read-only against the working tree
dotnet test tests/MisaConnect.EInvoice.UnitTests/MisaConnect.EInvoice.UnitTests.csproj --no-build --configuration Release    # T042
dotnet test tests/MisaConnect.ESign.UnitTests/MisaConnect.ESign.UnitTests.csproj --no-build --configuration Release          # T043
dotnet test tests/MisaConnect.EInvoice.IntegrationTests/MisaConnect.EInvoice.IntegrationTests.csproj --no-build --configuration Release  # T044
dotnet test tests/MisaConnect.ESign.IntegrationTests/MisaConnect.ESign.IntegrationTests.csproj --no-build --configuration Release        # T045
```

---

## Implementation Strategy

### MVP first (User Story 1 only, plus mandatory prerequisites)

For a release-engineering slice, "MVP" inverts: User Story 1 *is* the deployment, so it can't ship alone. The minimum viable release is **Phase 1 (setup) → Phase 2 (foundational) → Phase 3 (US1) → Phase 4 (US2) → Phase 5 (US3) → Phase 6 (polish + release)**. Skipping US2 means `release.yml`'s awk extractor returns empty release notes; skipping US3 means the ESign package never gets packed or pushed.

The closest analog to an MVP increment is to land Phase 1 + Phase 2 + Phase 5 first (gets the new package onto CI/PR runs as a `-preview.5`) and Phase 3/4 in a follow-up. That isn't recommended for this release because the GA target is fixed and the work fits in a single PR.

### Sequential delivery (recommended for this slice)

1. Phase 1 — Setup (baseline checks); ~5 min
2. Phase 2 — Foundational (slice 3 closure); ~30 min
3. Phase 3 — US1 (csproj version + README sweep); ~10 min
4. Phase 4 — US2 (CHANGELOG + constitution); ~30 min
5. Phase 5 — US3 (workflows + CONTRIBUTING); ~30 min
6. Phase 6 — Polish (verification + PR + merge + tag); ~30–60 min including PR review

Total: ~2–3 hours of active maintainer time, depending on PR review turnaround.

### Parallel team strategy (if multiple maintainers)

After Phase 2 completes:

- Maintainer A: Phase 3 (US1) — README + csproj
- Maintainer B: Phase 4 (US2) — CHANGELOG + constitution
- Maintainer C: Phase 5 (US3) — workflows + CONTRIBUTING

Resolve the T031 / T039 CONTRIBUTING.md conflict by coordinating: one maintainer applies both edits in one pass, or merge in alternating commits.

Phase 6 runs after all three branches re-merge — it's gate-only until T054 (the tag push).

---

## Notes

- [P] tasks = different files or independent verification gates, no dependencies
- [Story] label maps each task to a user story for traceability
- Phase 2 (slice 3 bookkeeping) cannot be parallelized because every task modifies the same `tasks.md` file
- Phase 4 constitution edits (T026..T030) cannot be parallelized for the same reason
- Tag push (T054) requires explicit user confirmation per the Git Safety Protocol — destructive irreversible action
- nuget.org does not allow deleting a published version, only unlisting it; treat T054 as a one-way door
- Commit boundaries: one combined commit per US is fine, or one per logical change inside the US; the final PR's commit history doesn't constrain how this slice is staged

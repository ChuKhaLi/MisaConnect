# Quickstart — Cutting `MisaConnect.ESign 2.0.0` (GA)

This guide walks a maintainer through the entire release, from clean checkout to NuGet listing. Roughly 60–90 minutes end to end, most of which is gate verification.

## Prerequisites

- Working tree clean on `005-release-esign-2-0`
- `pwsh` (PowerShell 7+) available
- .NET SDK 10.0.x installed
- `gh` CLI authenticated against the repo
- (Optional, integration tests) `MISACONNECT_SANDBOX_*` env vars for EInvoice and `MISACONNECT_ESIGN_SANDBOX_*` for ESign

## Step 1 — Close slice 3 bookkeeping (`specs/003-misa-esign-multi-format/tasks.md`)

Walk tasks T096 through T115 in order. For each `[ ]`:

```pwsh
# Open the task file
code specs/003-misa-esign-multi-format/tasks.md

# For each unchecked task, open the file the task names — e.g. for T101:
code tests/MisaConnect.ESign.UnitTests/Errors/PerFormatErrorMapperTests.cs

# Compare the file's assertions against the FR/SC the task references (e.g. T101 → contracts/error-mapping.md §A.10).
# If coverage is complete, edit tasks.md to flip [ ] to [X] for that task only.
# If coverage is partial, add the missing assertion(s) in the test file first, then flip.
```

For tasks T112–T115, run the literal commands the task names before flipping:

```pwsh
dotnet format MisaConnect.slnx                              # T112: must produce zero diff
dotnet build MisaConnect.slnx                               # T113: must produce zero warnings
dotnet test tests/MisaConnect.ESign.UnitTests/...           # T114: must finish < 30s, no network
dotnet test tests/MisaConnect.ESign.IntegrationTests/...    # T115: must skip cleanly without sandbox creds
```

After every unchecked task is `[X]`, the file should grep cleanly:

```pwsh
Select-String -Path specs/003-misa-esign-multi-format/tasks.md -Pattern '^\s*-\s*\[\s\]'
# Expected: zero matches (SC-004)
```

## Step 2 — Version bump on the ESign Client csproj

Edit [src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj:7](src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj#L7):

```diff
- <Version>2.0.0-preview.2</Version>
+ <Version>2.0.0</Version>
```

Confirm via `dotnet build`:

```pwsh
dotnet build src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj --configuration Release
# Should report 'MisaConnect.ESign -> ... MisaConnect.ESign.Client.dll' with no version warnings.
```

## Step 3 — Promote `CHANGELOG.md`

Edit [CHANGELOG.md](CHANGELOG.md). Per [contracts/changelog-format.md](contracts/changelog-format.md):

1. Above the existing `## [Unreleased]` block, insert:
   ```markdown
   ## [Unreleased]

   ## [2.0.0] - 2026-MM-DD

   Public API is the union of slices 1–4, previewed through `2.0.0-preview.1`..`preview.4`. This is the first stable release of the `MisaConnect.ESign` product family.

   ### Added
   ```
   (substituting today's date for `2026-MM-DD`).
2. Move every ESign bullet from the old `[Unreleased]` body under the new `### Added` heading.
3. Re-group bullets by slice in priority order: Slice 1 → Slice 2 → Slice 3 → Slice 4.
4. Remove the "Shipping as `2.0.0-preview.N`" sentences scattered through the slice blocks — they're replaced by the single provenance sentence at the top.
5. Add a `### Changed` sub-section listing the constitution amendment.

Verify the awk extractor produces non-empty output:

```pwsh
awk "/^## \[2.0.0\]/{flag=1; next} /^## \[/{flag=0} flag" CHANGELOG.md
# Expected: the slice 1–4 bullet list, exactly as it will appear in the GitHub Release.
```

## Step 4 — README sweep

Edit [README.md](README.md):

- ESign supported-operations table lists every `IMisaESignClient` method shipping in 2.0 (see [data-model.md](data-model.md) entity E-10 for the full list).
- Every install snippet under the ESign section reads `dotnet add package MisaConnect.ESign --version 2.0.0`.
- Replace any "planned for v2.0" wording with "released in v2.0".

## Step 5 — Constitution amendment

Apply the five edits in [contracts/constitution-amendment.md](contracts/constitution-amendment.md) to [.specify/memory/constitution.md](.specify/memory/constitution.md). Then sweep dependent docs per the same contract.

Verify (SC-006):

```pwsh
Select-String -Path .specify/memory/constitution.md -Pattern '(MisaConnect\.EInvoice|MisaConnect\.ESign)'
# Expected: at least one principle (II or VII) where both names appear in the same block.
```

## Step 6 — Workflow widening

Edit [.github/workflows/ci.yml](.github/workflows/ci.yml) per [contracts/workflow-contract.md](contracts/workflow-contract.md) — split the unit-test step into two (EInvoice, ESign).

Edit [.github/workflows/release.yml](.github/workflows/release.yml):

- Split the `Unit tests` step into two.
- Add a second `dotnet pack` line for `src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj`.
- Change the `gh release create --title` from `MisaConnect.EInvoice $VERSION` to `MisaConnect.ESign $VERSION`.

If [.github/workflows/integration.yml](.github/workflows/integration.yml) exists, apply the same split for integration tests.

Add the tag-per-product convention sentence to [CONTRIBUTING.md](CONTRIBUTING.md) under `## Releasing`.

## Step 7 — Local gate verification (FR-017)

Run each command from the repo root. All MUST pass before tagging.

```pwsh
dotnet format MisaConnect.slnx                                                                # zero diff
dotnet build MisaConnect.slnx --configuration Release -p:Version=2.0.0                        # zero warnings
dotnet test tests/MisaConnect.EInvoice.UnitTests/...     --no-build --configuration Release   # green
dotnet test tests/MisaConnect.ESign.UnitTests/...        --no-build --configuration Release   # green, <30s
dotnet test tests/MisaConnect.EInvoice.IntegrationTests/ --no-build --configuration Release   # skips cleanly w/o creds
dotnet test tests/MisaConnect.ESign.IntegrationTests/    --no-build --configuration Release   # skips cleanly w/o creds
```

## Step 8 — Local pack smoke test (FR-018)

```pwsh
dotnet pack src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj `
  --configuration Release `
  -p:Version=2.0.0 `
  -o artifacts/local

# Inspect contents
$nupkg = "artifacts/local/MisaConnect.ESign.2.0.0.nupkg"
$tmp = Join-Path $env:TEMP ([guid]::NewGuid())
Expand-Archive -Path $nupkg -DestinationPath $tmp
Get-ChildItem $tmp -Recurse | Select-Object FullName
```

Confirm against [contracts/package-metadata.md](contracts/package-metadata.md):

- `lib/net8.0/MisaConnect.ESign.{Client,Application,Infrastructure,Domain}.dll` all present
- `README.md` and `icon.png` at the package root
- `MisaConnect.ESign.nuspec` has `<id>MisaConnect.ESign</id>` and `<version>2.0.0</version>`
- No `MisaConnect.EInvoice.*` DLLs leaked

## Step 9 — Open the release PR

```pwsh
git add -A
git status                            # review every staged file matches the chunks above
git commit -m "Release MisaConnect.ESign 2.0.0"
git push -u origin 005-release-esign-2-0
gh pr create --title "Release MisaConnect.ESign 2.0.0" --body "Per specs/005-release-esign-2-0/spec.md. Implements FR-001..FR-019."
```

PR must be reviewed and merged before tagging. CI on the PR runs the new `ci.yml` flow — both unit-test steps must be green.

## Step 10 — Tag and watch the release workflow

After merge to `main`:

```pwsh
git checkout main
git pull
git tag -a v2.0.0 -m "MisaConnect.ESign 2.0.0"
git push origin v2.0.0

# Watch the workflow
gh run watch
```

Expected (per [contracts/workflow-contract.md](contracts/workflow-contract.md) R-1):

- `release.yml` completes green in <15 min (SC-001)
- `MisaConnect.ESign 2.0.0` appears on nuget.org within 30 minutes (SC-002)
- GitHub Release titled `MisaConnect.ESign 2.0.0` is created with both `.nupkg` and `.snupkg` attached
- `MisaConnect.EInvoice 1.1.0` on nuget.org is unchanged (SC-008) — `--skip-duplicate` no-op'd the EInvoice push

## Step 11 — Post-publish verification (SC-003)

In a scratch directory:

```pwsh
dotnet new console -n esign-2-0-smoke
cd esign-2-0-smoke
dotnet add package MisaConnect.ESign --version 2.0.0
# Edit Program.cs to call services.AddMisaConnectESign(...) and resolve IMisaESignClient
dotnet build
```

Build must succeed without requiring additional `MisaConnect.ESign.*` package references.

## Rollback

If something goes wrong **before** the NuGet push succeeds:

- `dotnet nuget push` is the only externally-visible step; everything before it is repo-local. Re-run the workflow after fixing.

If something goes wrong **after** the NuGet push:

- nuget.org does not allow deleting a package version; it can only be **unlisted**. To unlist:
  ```pwsh
  dotnet nuget delete MisaConnect.ESign 2.0.0 --source https://api.nuget.org/v3/index.json --api-key <key>
  ```
  This hides the version from search but does NOT remove it. Plan to publish `2.0.1` with a fix rather than relying on unlist.
- The GitHub Release can be deleted via `gh release delete v2.0.0 --cleanup-tag` if it was created in error.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `gh release create` fails with "tag already exists" | Tag was pushed before, workflow now retrying | Delete the GitHub Release manually before re-pushing |
| `release.yml` GitHub Release body is empty | `awk` extractor matched nothing — `## [2.0.0]` heading missing or malformed | Verify the CHANGELOG heading is exactly `## [2.0.0] - <date>` (see [contracts/changelog-format.md](contracts/changelog-format.md)) |
| `dotnet pack` errors with "version cannot be both <Version> and -p:Version=" | Likely benign — `release.yml` passes `-p:Version=$VERSION` which overrides csproj. Build should still succeed | Confirm the produced nupkg version matches the tag |
| Local pack produces a nupkg missing one of the four DLLs | `BundleReferencedProjects` target broken or `PrivateAssets="all"` flag removed | Revert csproj changes to the known-good shape in [contracts/package-metadata.md](contracts/package-metadata.md) |
| ESign unit tests run >30s in CI | Network call leaked into a unit test (Principle VI violation) | Run the test locally with `--blame-hang-timeout 60s` to find the hang; replace any `HttpClient` usage with the fake server |

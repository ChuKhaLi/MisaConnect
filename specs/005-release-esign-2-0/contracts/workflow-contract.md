# Contract — CI and Release Workflow Behavior

Specifies the observable behavior the workflows must produce **after** the edits in this release. The "how" (YAML syntax) is left to the implementation; the "what" (observable outcomes) is contractual.

---

## `.github/workflows/ci.yml`

### Triggers (unchanged)

- `push` to `main`
- `pull_request` to `main`
- `workflow_dispatch` with `test_filter` and `configuration` inputs

### Required observable behavior after this release

1. **Build step** runs `dotnet build MisaConnect.slnx --no-restore --configuration <Configuration>` and fails on any warning under `TreatWarningsAsErrors=true`.
2. **EInvoice unit tests step** runs `dotnet test tests/MisaConnect.EInvoice.UnitTests/MisaConnect.EInvoice.UnitTests.csproj --no-build --configuration <Configuration>` and fails if any test fails.
3. **ESign unit tests step** runs `dotnet test tests/MisaConnect.ESign.UnitTests/MisaConnect.ESign.UnitTests.csproj --no-build --configuration <Configuration>` and fails if any test fails. The suite MUST complete in under 30 seconds (SC-007) on the standard `ubuntu-latest` runner.
4. **Test result artifacts** — both steps emit `.trx` results with distinct filenames (e.g. `einvoice-unit-tests.trx`, `esign-unit-tests.trx`) so the upload-artifact step can collect them without filename collision.
5. The `inputs.test_filter` and `inputs.configuration` inputs from `workflow_dispatch` apply to **both** test projects.

### Test cases (acceptance for the CI contract)

| # | Setup | Expected ci.yml result |
|---|---|---|
| C-1 | Clean main branch | Workflow green, both unit-test steps green |
| C-2 | A PR introduces a failing test in `MisaConnect.ESign.UnitTests` | Workflow fails on the ESign step (not silently passing) |
| C-3 | A PR introduces a failing test in `MisaConnect.EInvoice.UnitTests` | Workflow fails on the EInvoice step |
| C-4 | A PR adds a build warning under `TreatWarningsAsErrors=true` | Workflow fails at the Build step |
| C-5 | Manual `workflow_dispatch` with `test_filter="FullyQualifiedName~SignXml"` | Both unit-test steps run with the filter; ESign step exercises only matching tests |

---

## `.github/workflows/integration.yml` (if present)

### Required observable behavior

If the file exists (the explore agent flagged it; verify during implementation):

1. **EInvoice integration tests step** runs `dotnet test tests/MisaConnect.EInvoice.IntegrationTests/...` against the MISA EInvoice sandbox; sandbox tests skip cleanly when `MISACONNECT_SANDBOX_*` env vars are absent.
2. **ESign integration tests step** runs `dotnet test tests/MisaConnect.ESign.IntegrationTests/...` against the MISA eSign sandbox; sandbox tests skip cleanly when `MISACONNECT_ESIGN_SANDBOX_*` env vars are absent (FR-016).
3. Both steps use the existing `[SandboxFact]` skip-on-missing-credentials contract — no new conditional logic in YAML.

If the file does NOT exist, skip this contract — integration coverage in `ci.yml` is not required (Principle VI permits integration tests outside the PR-gate flow).

---

## `.github/workflows/release.yml`

### Trigger (unchanged)

- `push` of any tag matching `v*`

### Required observable behavior after this release

1. **Derive version step** extracts `VERSION = "${GITHUB_REF#refs/tags/v}"` (unchanged).
2. **Restore + Build step** runs `dotnet build MisaConnect.slnx --no-restore --configuration Release -p:Version=<VERSION>` (unchanged).
3. **EInvoice unit tests step** runs `dotnet test tests/MisaConnect.EInvoice.UnitTests/...` and fails if red.
4. **ESign unit tests step** runs `dotnet test tests/MisaConnect.ESign.UnitTests/...` and fails if red.
5. **EInvoice pack step** runs `dotnet pack src/MisaConnect.EInvoice.Client/MisaConnect.EInvoice.Client.csproj --configuration Release -p:Version=<VERSION> -o artifacts`.
6. **ESign pack step** runs `dotnet pack src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj --configuration Release -p:Version=<VERSION> -o artifacts`. Both packs share the `artifacts/` directory.
7. **NuGet login + push** runs `dotnet nuget push "artifacts/*.nupkg" --api-key <key> --source https://api.nuget.org/v3/index.json --skip-duplicate` (unchanged glob; `--skip-duplicate` protects against re-publishing EInvoice 1.x).
8. **Create GitHub Release step**:
   - Extracts release notes from `CHANGELOG.md` via `awk "/^## \[$VERSION\]/{flag=1; next} /^## \[/{flag=0} flag"`.
   - Creates the release with title `MisaConnect.ESign $VERSION` (changed from `MisaConnect.EInvoice $VERSION`).
   - Attaches every `*.nupkg` and `*.snupkg` in `artifacts/` as release assets.

### Edge-case behavior

| # | Trigger | Expected outcome |
|---|---|---|
| R-1 | Tag `v2.0.0` pushed on a commit where every gate is green | `MisaConnect.ESign 2.0.0` published to nuget.org; GitHub Release titled `MisaConnect.ESign 2.0.0` created with both `.nupkg` + `.snupkg` assets attached |
| R-2 | Tag `v2.0.0` pushed, but the `CHANGELOG.md` `[2.0.0]` section is missing | `gh release create` runs with an empty `RELEASE_NOTES.md`; the GitHub Release is created with no body. **Detection**: FR-019 catches this locally before the tag push. |
| R-3 | Tag `v2.0.0` re-pushed (force or accidental re-run) on a different commit | `dotnet nuget push --skip-duplicate` no-ops because `MisaConnect.ESign 2.0.0` is already on nuget.org; the `gh release create` call fails with `tag already exists`. **Recovery**: maintainer manually `gh release delete v2.0.0`, then re-pushes. |
| R-4 | Tag `v2.0.0` pushed on a branch where ESign unit tests fail | Workflow fails at the ESign unit tests step; nothing is packed or pushed. |
| R-5 | Tag `v1.1.1` (future EInvoice patch) pushed under the same release.yml | The ESign pack step still runs and produces `MisaConnect.ESign.1.1.1.nupkg` (an undesirable artifact); `--skip-duplicate` on push prevents it landing on nuget.org. The GitHub Release would title `MisaConnect.ESign 1.1.1` — **wrong**. **Mitigation**: file a follow-up to switch to tag-per-product naming (`esign-v*` / `einvoice-v*`) once the second product family ships a release under this convention. Out of scope for 2.0.0. |
| R-6 | Tag `v0.0.0-test` pushed for a dry-run | Workflow runs end-to-end; `--skip-duplicate` allows the no-op push; GitHub Release is created. **Cleanup**: maintainer manually deletes the release and the tag. |

### Non-required behavior (deliberately unchanged)

- Matrix strategy across the two product families — not introduced; sequential steps are simpler and have no measurable runtime cost at this scale.
- Separate workflow files per product family — not introduced; one workflow with two pack steps is cheaper to maintain.
- Caching strategies beyond what `setup-dotnet` already provides — not changed.
- Signing or notarization of the `.nupkg` — not changed; out of scope.

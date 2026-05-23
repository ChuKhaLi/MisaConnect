# Phase 0 Research — Release `MisaConnect.ESign` 2.0.0

This document captures the release-engineering decisions taken before any code edits. There are **no** `[NEEDS CLARIFICATION]` markers from the spec; everything below was either decided directly with the user during the `/speckit-specify` clarification round or follows existing repo precedent.

---

## R-1 — Package shape: single bundled `MisaConnect.ESign` package

- **Decision**: Ship one NuGet package, `PackageId = MisaConnect.ESign`, that bundles the Client, Application, Infrastructure, and Domain assemblies (and their PDBs) into `lib/net8.0/` via the existing `BundleReferencedProjects` MSBuild target on [src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj:36-45](src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj#L36-L45). The Domain/Application/Infrastructure csproj files keep `<IsPackable>` false (already configured).
- **Rationale**: Matches `MisaConnect.EInvoice`'s precedent exactly. Consumers get one `dotnet add package` for the whole SDK and never need to reason about layer boundaries. The `PrivateAssets="all"` flags on the three `<ProjectReference>` entries already prevent transitive layer exposure.
- **Alternatives considered**:
  - **Per-layer packages** (`MisaConnect.ESign.Domain`, `.Application`, `.Infrastructure`, `.Client`) — rejected by the user in clarification (matches EInvoice, smaller versioning surface area, simpler consumer story).
  - **Reuse the EInvoice package** by adding ESign types into it — rejected: violates Constitution Principle II's "small and stable surface" by mixing unrelated product families, and `MisaConnect.EInvoice 1.x` consumers would get unwanted dependencies.

## R-2 — Version flavor: GA `2.0.0` (not RC, not preview)

- **Decision**: Release as final `2.0.0`. CHANGELOG promotes `[Unreleased]` to `[2.0.0]`; git tag is `v2.0.0`; csproj `<Version>` reads `2.0.0`.
- **Rationale**: All four slices implemented per `docs/misa-esign-spec-plan.md`. Slice 1 has 115/115 tasks complete, Slice 2 has 65/65, Slice 4 has 115/115. Slice 3's underlying code and test files exist (Slice 4's regression work created them); only bookkeeping is stale. Three preview iterations (`-preview.1`, `-preview.2`, plus the inline references to preview.3 / preview.4 in CHANGELOG) provide adequate pre-release exposure under the existing internal-testing convention. Holding for an `rc.1` cycle delays the release without adding signal — no breaking changes are anticipated.
- **Alternatives considered**:
  - **`2.0.0-rc.1`** — rejected: no concrete validation signal an RC would produce that the existing preview chain hasn't.
  - **`2.0.0-preview.5`** — rejected: another preview is a non-commitment; the four slices ARE the surface for 2.0.0. Continuing to preview would imply ongoing API churn that isn't planned.

## R-3 — Tagging convention: tag-per-product-family

- **Decision**: `v2.0.0` is dedicated to `MisaConnect.ESign` GA. `MisaConnect.EInvoice` continues to use its own `v1.x.y` tags (its current published version is `1.1.0`). Document this convention in `CONTRIBUTING.md` under `## Releasing`.
- **Rationale**: The `MisaConnect.EInvoice` package is at v1.1.0; an unprefixed `v2.0.0` tag unambiguously means ESign GA. Each product family already has its own CHANGELOG section pattern (`[2.0.0]` for ESign, `[1.1.0]` / `[1.0.0]` for EInvoice). Adding a prefix scheme (`esign-v2.0.0`, `einvoice-v1.1.0`) would require rewriting the `release.yml` trigger pattern (`tags: [v*]`) and the awk-based CHANGELOG extractor.
- **Alternatives considered**:
  - **Prefixed tags** (`esign-v2.0.0` / `einvoice-v1.1.0`) — rejected as out-of-scope for this release; can be revisited when both families need a release in the same week, which has not happened yet.
  - **Shared tag for both** (`v2.0.0` packs and pushes both at once) — rejected: a shared `v2.0.0` would imply bumping EInvoice to 2.0.0 (false major bump signal). The `--skip-duplicate` push flag means we can technically pack both safely, but the GitHub Release title and CHANGELOG semantics get muddled.

## R-4 — Workflow widening strategy: split-test-then-multi-pack

- **Decision**: In `.github/workflows/ci.yml`, split the single `dotnet test` step into two sequential steps, one per unit-test project. In `.github/workflows/release.yml`, mirror the same split for the `Unit tests` step, then add a second `dotnet pack` line for the ESign Client csproj (sharing `-o artifacts`). The existing `dotnet nuget push "artifacts/*.nupkg" --skip-duplicate` glob and `gh release create ... artifacts/*.nupkg artifacts/*.snupkg` already handle multi-package output without changes.
- **Rationale**: Smallest possible workflow diff. No new jobs, no matrix strategy, no third-party actions. Each unit-test step stays under its own <30s budget per Principle VI. The `--skip-duplicate` flag is the existing defensive mechanism — already in place at [.github/workflows/release.yml:50](.github/workflows/release.yml#L50).
- **Alternatives considered**:
  - **Matrix strategy** over `[einvoice, esign]` — rejected: adds CI surface area, complicates log-reading, no clear win at two products. Revisit if/when there are three or more.
  - **Separate `release-esign.yml` / `release-einvoice.yml` workflows** with prefixed tag triggers — rejected as paired with R-3; if we don't prefix tags, we don't split workflows.
  - **`dotnet pack MisaConnect.slnx`** to pack everything packable — rejected: would also try to pack Domain/Application/Infrastructure if `<IsPackable>` ever flipped accidentally, with no warning. Explicit csproj-by-csproj is safer.

## R-5 — CHANGELOG promotion mechanics

- **Decision**: Insert a new `## [2.0.0] - 2026-MM-DD` heading (date filled in on the tag day). Move every ESign-related bullet currently under `[Unreleased]` into it, in slice-priority order (Slice 1 → Slice 2 → Slice 3 → Slice 4). Consolidate the inline "Shipping as `2.0.0-preview.N`" claims into one provenance sentence at the top: `"Public API is the union of slices 1–4, previewed through 2.0.0-preview.1..preview.4."` Leave an empty `## [Unreleased]` heading above the new section for post-2.0 work.
- **Rationale**: Honors the existing CHANGELOG schema (`[Unreleased]` → dated sections in reverse chronological order, see [CHANGELOG.md:53-72](CHANGELOG.md#L53-L72) for the v1.1.0 / v1.0.0 pattern). Single provenance sentence avoids cluttering the GA notes with preview-iteration noise that no longer matters to consumers.
- **Alternatives considered**:
  - **Keep the inline preview markers** — rejected: redundant in a GA release; consumer is reading "what does 2.0.0 give me," not "how did we get there."
  - **Re-order bullets by API surface (facade method, options, errors)** instead of by slice — rejected for now: per-slice grouping makes the release notes easier to map back to the spec history. Re-ordering can be a polish pass during the actual edit.

## R-6 — Slice 3 bookkeeping closure procedure

- **Decision**: Walk `specs/003-misa-esign-multi-format/tasks.md` tasks T096–T115 in order. For each `[ ]`:
  1. Locate the file the task names.
  2. Open the file and verify its assertions actually cover the FR/SC the task references.
  3. If coverage is complete → mark `[X]` with no other edit.
  4. If coverage is partial → add the missing assertion(s) in place, then mark `[X]`.
  5. Tasks T112–T115 are global gate-runs (`dotnet format`, `dotnet build`, `dotnet test`); execute them literally on the working tree before flipping.
- **Rationale**: Honest bookkeeping. The user's explicit guidance: "verify + mark complete (treat as bookkeeping cleanup, not re-implementation)." This procedure makes the verification step concrete and prevents retroactive `[X]` marks that hide real gaps.
- **Alternatives considered**:
  - **Mass-mark all `[ ]` as `[X]` after one round of `dotnet test`** — rejected: a green test run says the tests pass, not that they cover the FR/SC each task names. The walk catches files that exist but don't actually assert what they should.
  - **Re-run `/speckit-implement` on Phase 4–6** — rejected: the work is done; this would re-create existing files and force unnecessary churn.

## R-7 — Constitution amendment scope

- **Decision**: Amend two principles plus one governance line, bump the constitution from `1.0.0` to `1.1.0`, and add a one-paragraph sync-impact note at the bottom citing this release. Exact text in `contracts/constitution-amendment.md`. Touch dependent docs (CLAUDE.md, CONTRIBUTING.md, `.specify/templates/*`) only where "the package" (singular) is ambiguous.
- **Rationale**: Per spec FR-009..FR-012 and the user's clarification choice. Limiting the amendment to additive enumeration (no semantic restriction is added or removed) means the change is backward-compatible under the constitution's own governance rules — see Complexity Tracking in plan.md for the reasoning.
- **Alternatives considered**:
  - **Defer constitutional update to a separate slice** — rejected by user: leaves a documentation inconsistency at the moment of release.
  - **Rewrite the constitution from scratch for two product families** — rejected: out of scope; the additive amendment achieves correctness without rewriting principles that aren't affected.

## R-8 — Release verification depth

- **Decision**: Run FR-017's gates locally before pushing the tag. Also verify the local `.nupkg` contents (FR-018) and run the awk extractor against the new CHANGELOG section (FR-019) to catch heading drift. Skip the throwaway-tag dry-run unless the workflow has been substantively rewritten (we're only adding lines, not restructuring).
- **Rationale**: The same workflow has shipped `MisaConnect.EInvoice 1.0.0` and `1.1.0` successfully — confidence is high. The local pack + awk dry-run catches the two most plausible failure modes (CHANGELOG heading mismatch and missing DLLs in the nupkg) without burning a tag.
- **Alternatives considered**:
  - **Throwaway tag (`v2.0.0-rc-test`)** — rejected unless something in the workflow's structure (not just line content) changes. Adds operational cost and a release that must be deleted.
  - **Skip local pack verification entirely** — rejected: too easy to miss a missing DLL in the bundle (e.g. if a transitive layer ref drops `PrivateAssets="all"` someday).

---

**Phase 0 result**: All NEEDS CLARIFICATION markers resolved (there were none from the spec; the user-clarification round in `/speckit-specify` answered the four open release-shape questions). Ready for Phase 1.

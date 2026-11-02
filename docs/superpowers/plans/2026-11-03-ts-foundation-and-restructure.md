# TS Foundation & Repo Restructure Implementation Plan (Plan 1 of 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Amend the constitution for TypeScript, move all .NET code under `dotnet/` subfolders without changing any shipped artifact, and stand up an empty-but-working TypeScript workspace with path-filtered CI, so Plan 2 can build `@misaconnect/einvoice` on top.

**Architecture:** Pure `git mv` of the .NET tree into `src/dotnet`, `tests/dotnet`, `samples/dotnet`, then fix every relative path. A root `package.json` with npm workspaces over `src/typescript/*` and `samples/typescript/*`; one skeleton package `src/typescript/einvoice` that builds ESM+CJS+d.ts. CI split by language with `paths:` filters.

**Tech Stack:** .NET 8 / SDK 10 (existing), Node ≥ 20, npm workspaces, TypeScript (strict), tsup, Vitest, ESLint (flat config, typescript-eslint), Prettier, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-02-typescript-sdk-design.md` (§5, §8, §10, §11a, §12 steps 0–2). Plan 2 (`@misaconnect/einvoice` operations) is written after this plan lands.

## Global Constraints

- Branch: `feat/typescript-sdk`. One commit per task.
- .NET: `TreatWarningsAsErrors=true`; `dotnet format MisaConnect.slnx --verify-no-changes` must pass.
- The two NuGet packages produced after the move must contain the same file list as before (incl. `icon.png`, `README.md`).
- `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `nuget.config`, `icon.png`, `LICENSE` stay at repo root.
- Do not create a top-level `ts/` folder. No Nx / Turborepo.
- TS: `strict: true`; zero runtime `dependencies` in published packages; Node `engines: ">=20"`.
- npm package names: `@misaconnect/einvoice`, `@misaconnect/esign`. Skeleton version `0.0.0`, `"private": true` until Plan 2's release task.
- Unit tests never touch the network.
- `main` has no branch protection (verified 2026-11-03), so plain workflow-level `paths:` filters are safe.

## Review Focus

1. **Sandbox fixture pool lookup after the move** — `tests/MisaConnect.EInvoice.IntegrationTests/Sandbox/FixturePoolLoader.cs:68-83` walks up looking for `tests/MisaConnect.EInvoice.IntegrationTests/Sandbox`; after the move it would throw, and `SandboxFact` turns that into a *skip*, so sandbox tests silently stop running. Expect: pool still found. Test added in Task 2 Step 1.
2. **NuGet icon silently dropped** — `..\..\icon.png` is guarded by `Condition="Exists(...)"`. Expect: `icon.png` still in both `.nupkg`. Checked in Task 2 Step 8.
3. **Path-filtered .NET workflow skipped on a change that matters** (e.g. editing `Directory.Packages.props` or the workflow itself). Expect: those trigger .NET CI. Covered by the filter list in Task 4.
4. **CJS consumers** — `require('@misaconnect/einvoice')` from a CommonJS app must work, not only `import`. Smoke test in Task 3.
5. **Windows line endings / path separators** — repo is developed on Windows; `git mv` and csproj `..\` paths must still work on the Linux CI runner. Expect: CI green on ubuntu-latest (Task 4 Step 4).

---

### Task 1: Constitution amendment v1.2.0 + process docs

**Files:**
- Modify: `.specify/memory/constitution.md`
- Modify: `CONTRIBUTING.md` (slice-workflow section)
- Modify: `CLAUDE.md` (intro paragraph + "Adding an operation")

**Interfaces:** Produces the rule text later PRs cite: "TypeScript packages follow a superpowers design under `docs/superpowers/specs/`".

- [ ] **Step 1: Amend the constitution.** Keep every existing .NET rule's meaning unchanged. Changes:
  - Line 5 preamble: "community SDK for MISA cloud APIs, shipped as .NET (NuGet) and TypeScript (npm) packages".
  - Principles I, II, VII: prefix the existing text with "For the .NET packages:"; append a "For the TypeScript packages:" sentence each:
    - I: `domain/` imports nothing outside itself; `application/` imports only `domain/`; `infrastructure/` may use Node built-ins; zero runtime npm dependencies.
    - II: public surface is only what `src/index.ts` exports; entry point is `createMisa<Product>Client(options)`.
    - VII: each npm package (`@misaconnect/einvoice`, `@misaconnect/esign`) is versioned independently from each other and from NuGet; `0.x` may break in minor versions.
  - Principle V: add "TypeScript packages may instead ship from a design document under `docs/superpowers/specs/` and a plan under `docs/superpowers/plans/`. .NET work keeps the Spec Kit requirement."
  - Header: `Last amended: 2026-11-03 · Version: 1.2.0`.
  - Append `## Sync-impact note — 2026-11-03 (constitution v1.2.0)` explaining: additive (TS rules apply only to TS packages; no .NET restriction added or removed) → MINOR per Governance; links the design doc.
- [ ] **Step 2: Sync `CONTRIBUTING.md` and `CLAUDE.md`** — one paragraph each stating the TS process exception and pointing to the design doc. (Path updates come in Task 2; TS commands in Task 3.)
- [ ] **Step 3: Verify** — `git diff --stat` touches only the three files; re-read Principles I–IV and confirm every pre-existing sentence is still present verbatim after its "For the .NET packages:" prefix.
- [ ] **Step 4: Commit** — `git commit -m "docs(constitution): v1.2.0 — TypeScript packages and superpowers process"`

---

### Task 2: Move .NET tree under `dotnet/` subfolders

**Files:**
- Move: `src/MisaConnect.*` → `src/dotnet/`, `tests/MisaConnect.*` → `tests/dotnet/`, `samples/MisaConnect.*` → `samples/dotnet/`
- Modify: `MisaConnect.slnx`; every `.csproj` under `samples/dotnet` and `tests/dotnet`; both `src/dotnet/*.Client/*.csproj` (icon path)
- Modify: `tests/dotnet/MisaConnect.EInvoice.IntegrationTests/Sandbox/FixturePoolLoader.cs`, `SandboxFact.cs`, `FixturePoolSkipTests.cs`; `src/dotnet/MisaConnect.EInvoice.Application/Mapping/InvoiceStatusMapper.cs` (doc comment)
- Modify: `.github/workflows/ci.yml`, `integration.yml`, `release.yml`
- Modify: `CLAUDE.md`, `CONTRIBUTING.md`, `README.md`, `CHANGELOG.md` (`[Unreleased]` note), `docs/architecture.md`, `docs/einvoice/getting-started.md`, `docs/einvoice/sandbox-setup.md`, `docs/esign/getting-started.md`, `docs/esign/sandbox-setup.md`
- Test: `tests/dotnet/MisaConnect.EInvoice.IntegrationTests/Sandbox/FixturePoolLoaderPathTests.cs`

**Interfaces:** Produces the layout Plan 2 and Task 3 assume: `src/dotnet/<Project>/`, `tests/dotnet/<Project>/`, `samples/dotnet/<Project>/`.

- [ ] **Step 0a: Baseline test counts (before moving anything)** — run both IntegrationTests projects with `--logger "trx"` and record passed / skipped / failed counts per project in the scratchpad. If `MISACONNECT_SANDBOX_*` credentials are available in the environment, record that sandbox facts actually *ran* (not skipped); if not, ask the owner whether to run Step 6b with credentials or accept the skip-count comparison only.
- [ ] **Step 0: Baseline packs (before moving anything)**
  Run: `dotnet pack src/MisaConnect.EInvoice.Client -c Release -o /tmp/before` and the same for `src/MisaConnect.ESign.Client`; list contents with `unzip -l` into `before-einvoice.txt` / `before-esign.txt` in the scratchpad.
- [ ] **Step 1: Write the failing test** (in current location, then moved with the tree)

```csharp
public class FixturePoolLoaderPathTests
{
    [Fact]
    public void DefaultPoolDirectory_IsFoundUnderTestsDotnet()
    {
        var dir = FixturePoolLoader.DefaultPoolDirectory();
        Assert.True(Directory.Exists(dir));
        Assert.EndsWith(Path.Combine("tests", "dotnet", "MisaConnect.EInvoice.IntegrationTests", "Sandbox"), dir);
    }
}
```

- [ ] **Step 2: Move with history** — `git mv` each project folder (8 src, 5 tests, 2 samples) into its `dotnet/` subfolder.
- [ ] **Step 3: Fix paths**
  - `MisaConnect.slnx`: every `Path=` gains the `dotnet/` segment; solution folders stay `/src/`, `/tests/`, `/samples/`.
  - `tests/dotnet/**/*.csproj` and `samples/dotnet/**/*.csproj`: `..\..\src\` → `..\..\..\src\dotnet\`; `..\..\samples\` → `..\..\..\samples\dotnet\`. Sibling refs `..\X` unchanged.
  - `src/dotnet/*.Client/*.csproj`: `..\..\icon.png` → `..\..\..\icon.png` (both the `Include` and the `Condition`).
  - `FixturePoolLoader.DefaultPoolDirectory`: candidate becomes `Path.Combine(dir, "tests", "dotnet", "MisaConnect.EInvoice.IntegrationTests", "Sandbox")`; update the three user-facing "top up via tests/…" strings and the doc comment in `InvoiceStatusMapper.cs`.
  - Workflows: every `src/…`, `tests/…` path gains `dotnet/`.
  - Docs listed above: replace `src/MisaConnect`, `tests/MisaConnect`, `samples/MisaConnect` with the `dotnet/` paths; `CLAUDE.md` build & test command block too. Leave `specs/**` and `CHANGELOG.md` history untouched (historical record); add one `[Unreleased]` line: "Repository layout: .NET projects moved under `src/dotnet`, `tests/dotnet`, `samples/dotnet`. No package changes."
- [ ] **Step 4: Grep for leftovers**
  Run: `git grep -nE "(src|tests|samples)[/\\\\]MisaConnect\." -- ':!specs' ':!CHANGELOG.md' ':!docs/superpowers'`
  Expected: no output.
- [ ] **Step 5: Build** — `dotnet build MisaConnect.slnx -c Release` → 0 warnings, 0 errors.
- [ ] **Step 6: Tests** — run all five test projects (four suites + the new path test):
  `dotnet test tests/dotnet/MisaConnect.EInvoice.UnitTests`, `…ESign.UnitTests`, `…EInvoice.IntegrationTests`, `…ESign.IntegrationTests` → all pass/skip as on `main`; `FixturePoolLoaderPathTests` PASS.
- [ ] **Step 6b: Skip-count guard** — compare passed/skipped counts with Step 0a. Expected: skipped count for each IntegrationTests project is **not higher** than baseline (passed = baseline + 1 for the new path test). A rise in skips means a path lookup broke silently — "green" is not enough here. With sandbox credentials, also confirm no skip reason mentions `FixturePoolExhausted … loader error` or `pool file missing`.
- [ ] **Step 7: Format** — `dotnet format MisaConnect.slnx --verify-no-changes` → exit 0.
- [ ] **Step 8: Pack diff** — re-pack both Client projects from `src/dotnet/…` into `/tmp/after`, list contents, `diff` against Step 0 lists ignoring timestamps/sizes. Expected: identical file names, including `icon.png` and `README.md`.
- [ ] **Step 9: Commit** — `git commit -m "chore(repo): move .NET projects under src/dotnet, tests/dotnet, samples/dotnet"`

---

### Task 3: TypeScript workspace + `@misaconnect/einvoice` skeleton

**Files:**
- Create: `package.json` (root), `package-lock.json`, `tsconfig.base.json`, `eslint.config.mjs`, `.prettierrc.json`, `.prettierignore`, `.nvmrc` (`20`)
- Modify: `.gitignore` (add `node_modules/`, `dist/`, `coverage/`, `*.tsbuildinfo`)
- Create: `src/typescript/einvoice/package.json`, `tsconfig.json`, `tsup.config.ts`, `vitest.config.ts`, `README.md`, `src/index.ts`
- Test: `src/typescript/einvoice/test/package-exports.test.ts`
- Modify: `CLAUDE.md`, `CONTRIBUTING.md` (TS build/test commands)

**Interfaces:**
- Produces: root scripts `npm run build`, `npm run test`, `npm run lint`, `npm run format:check` — each runs across all workspaces (`--workspaces --if-present`).
- Produces: package layout Plan 2 fills in — `src/typescript/einvoice/src/{domain,application,infrastructure}/`, public exports only via `src/index.ts`; tests under `src/typescript/einvoice/test/`.
- Produces: `export const PACKAGE_NAME = '@misaconnect/einvoice'` in `src/index.ts` (placeholder export so the build has a surface; Plan 2 may remove it).

- [ ] **Step 1: Root `package.json`** — `"private": true`, `"workspaces": ["src/typescript/*", "samples/typescript/*"]`, `"engines": { "node": ">=20" }`, the four scripts above; devDependencies at root: `typescript`, `tsup`, `vitest`, `eslint`, `typescript-eslint`, `@eslint/js`, `prettier`, `@types/node` (latest stable; lockfile pins).
- [ ] **Step 2: `tsconfig.base.json`** — `strict`, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`, `target: ES2022`, `module`/`moduleResolution: NodeNext`, `lib: ["ES2022"]`, `types: ["node"]`, `declaration: true`, `isolatedModules: true`, `verbatimModuleSyntax: true`.
- [ ] **Step 3: Package `package.json`** — name `@misaconnect/einvoice`, version `0.0.0`, `"private": true`, `"type": "module"`, no `dependencies`, `"files": ["dist", "README.md"]`, `"sideEffects": false`, `"engines": { "node": ">=20" }`, `exports`:

```json
{ ".": {
    "import": { "types": "./dist/index.d.ts",  "default": "./dist/index.js" },
    "require": { "types": "./dist/index.d.cts", "default": "./dist/index.cjs" } } }
```

  plus `main`/`module`/`types` fallbacks; scripts `build` (tsup), `test` (vitest run), `lint` (eslint .), `typecheck` (tsc --noEmit). `tsup.config.ts`: entry `src/index.ts`, formats `esm`+`cjs`, `dts: true`, `clean: true`, `target: 'node20'`.
- [ ] **Step 4: Write the failing test** — `test/package-exports.test.ts` (runs against the built `dist`, so `test` script runs `build` first via `pretest`):

```ts
import { createRequire } from 'node:module';
import { describe, it, expect } from 'vitest';

describe('package exports', () => {
  it('loads via ESM import', async () => {
    const mod = await import('@misaconnect/einvoice');
    expect(mod.PACKAGE_NAME).toBe('@misaconnect/einvoice');
  });
  it('loads via CommonJS require', () => {
    const mod = createRequire(import.meta.url)('@misaconnect/einvoice');
    expect(mod.PACKAGE_NAME).toBe('@misaconnect/einvoice');
  });
});
```

- [ ] **Step 5: Run to verify it fails** — `npm install && npm test -w @misaconnect/einvoice` → FAIL (no `src/index.ts` / no `dist`).
- [ ] **Step 6: Implement `src/index.ts`** with the `PACKAGE_NAME` export; add empty `src/domain/`, `src/application/`, `src/infrastructure/` folders with a `.gitkeep` each.
- [ ] **Step 7: Run to verify it passes** — `npm test -w @misaconnect/einvoice` → 2 passed. Also `npm run lint` and `npm run format:check` → exit 0; `npm pack -w @misaconnect/einvoice --dry-run` lists only `dist/*`, `README.md`, `package.json`.
- [ ] **Step 8: ESLint boundary rule** — in `eslint.config.mjs`, add `no-restricted-imports` overrides: files in `src/typescript/*/src/domain/**` may not import `../application/**`, `../infrastructure/**`, or `node:*`; files in `application/**` may not import `../infrastructure/**`. (Constitution v1.2.0 Principle I.) Verify by temporarily adding a forbidden import in `domain/` → `npm run lint` fails; remove it.
- [ ] **Step 9: Docs** — add a "TypeScript" block to `CLAUDE.md` Build & test (`npm ci`, `npm run build`, `npm test`, `npm run lint`, `npm test -w @misaconnect/einvoice`) and the same to `CONTRIBUTING.md`; package `README.md` says "Work in progress — not yet published".
- [ ] **Step 10: Commit** — `git commit -m "build(ts): npm workspace and @misaconnect/einvoice skeleton"`

---

### Task 4: Path-filtered CI for both languages

**Files:**
- Modify: `.github/workflows/ci.yml`, `.github/workflows/integration.yml`
- Create: `.github/workflows/ts-ci.yml`

**Interfaces:** Consumes root scripts from Task 3. Plan 2 adds the `einvoice-ts-v*` publish workflow; not here.

- [ ] **Step 1: .NET filters** — on `push` and `pull_request` triggers of `ci.yml` and `integration.yml` (not `workflow_dispatch`/`schedule`) add:

```yaml
paths:
  - "src/dotnet/**"
  - "tests/dotnet/**"
  - "samples/dotnet/**"
  - "MisaConnect.slnx"
  - "Directory.Build.props"
  - "Directory.Packages.props"
  - "global.json"
  - "nuget.config"
  - ".editorconfig"
  - ".github/workflows/ci.yml"        # each file lists itself
```

- [ ] **Step 2: `ts-ci.yml`** — triggers `push`/`pull_request` on `main` + `workflow_dispatch`. Jobs:
  - `einvoice`: `paths` can't be per-job, so use a first job `changes` with `dorny/paths-filter@v3` producing outputs `einvoice` (`src/typescript/einvoice/**`, shared) and `esign` (`src/typescript/esign/**`, shared), where shared = `package.json`, `package-lock.json`, `tsconfig.base.json`, `eslint.config.mjs`, `.prettierrc.json`, `.nvmrc`, `.github/workflows/ts-ci.yml`.
  - `einvoice` job `if: needs.changes.outputs.einvoice == 'true'`, matrix `node: [20, 22, 24]`: `actions/setup-node@v4` with `cache: npm`, `npm ci`, `npm run lint`, `npm run format:check`, `npm run typecheck -w @misaconnect/einvoice`, `npm test -w @misaconnect/einvoice`.
  - `esign` job: same shape, `if` on `esign` output and `hashFiles('src/typescript/esign/package.json') != ''` so it no-ops until the package exists.
  - Workflow-level `paths:` on `src/typescript/**`, `samples/typescript/**` and the shared files so .NET-only changes don't start it at all.
- [ ] **Step 3: Validate locally** — `npx --yes action-validator .github/workflows/ts-ci.yml` (or `actionlint` if installed) → no errors; same for the two edited files.
- [ ] **Step 4: Verify on GitHub** — push the branch, open a draft PR. Expected: `ci` (.NET) and `ts-ci` both run (both trees changed on this branch) and pass, ubuntu-latest. Ask the owner before pushing (outward-facing).
- [ ] **Step 5: Commit** — `git commit -m "ci: path-filtered .NET workflows and new ts-ci workflow"`

---

## Self-review notes

- Spec coverage: §11a → Task 1; §5 → Task 2; §8 tooling + §5 layout for TS → Task 3; §10 CI → Task 4. §6/§7/§9 and §10 publishing/docs → Plan 2.
- `release.yml` keeps its `v*` tag trigger (no `paths`, tags only) — only its paths change in Task 2.

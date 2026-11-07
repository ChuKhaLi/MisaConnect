# pnpm workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** pnpm owns the TypeScript workspace, lockfile, local commands and CI; `npm stage publish` stays the publish step.

**Architecture:** Add `pnpm-workspace.yaml` + `packageManager`, regenerate the lockfile as `pnpm-lock.yaml`, switch root and CI commands to pnpm, keep package scripts and the publish step working under the npm CLI.

**Tech Stack:** pnpm 12.x (via `packageManager`, `pnpm/action-setup`), Node 22/24, tsup, vitest, eslint, prettier, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-07-pnpm-workspace-design.md`

## Global Constraints

- `"packageManager": "pnpm@12.18.0"` (or newest 12.x that works); isolated `node_modules` (no `shamefully-hoist`, no `node-linker=hoisted`).
- Overrides in `pnpm-workspace.yaml`: `minimatch: ^11.0.5`.
- Publish step unchanged: `npm stage publish --access public` in the package dir, npm CLI `>=11.15.0 <12`.
- No change to package code, `exports`, `files`, `engines`, versions. No .NET change.
- Toolchain Node ≥ 22.18; packages Node ≥ 22.

## Review Focus

1. `npm stage publish` → `prepublishOnly` (`npm run build`) must still find `tsup` with a pnpm-installed tree — verify by `npm pack --dry-run` in each package after a clean `pnpm install` (Task 1).
2. Packed tarball identical in file list to the npm-era tarball — record `npm pack --dry-run` file lists before and after (Task 1).
3. pnpm skipping `pre*` scripts would make `pnpm test`/`typecheck` run against stale `dist` — verify by deleting `dist` then `pnpm test` (Task 1).
4. Node 22.0.0 smoke jobs must not depend on `workspaces` (`npm -w`) any more — run smoke from the package dir (Task 2).
5. Dependabot keeps opening npm-ecosystem PRs against `pnpm-lock.yaml` — config stays `package-ecosystem: npm` at `/` (Task 2).

---

### Task 1: Workspace, lockfile, root scripts (local green)

**Files:**
- Create: `pnpm-workspace.yaml` (`packages: [src/typescript/*, samples/typescript/*]`, `overrides: {minimatch: ^11.0.5}`, build-script allow-list for what the toolchain needs, e.g. `esbuild`)
- Create: `pnpm-lock.yaml` (generated); Delete: `package-lock.json`
- Modify: `package.json` (drop `workspaces`, `overrides`; add `packageManager`; `build`/`test`/`typecheck` → `pnpm -r --if-present run <script>`; description says pnpm workspace)
- Modify: `samples/typescript/{express,nestjs}/package.json` (`@misaconnect/*`: `workspace:*`)
- Modify: package `package.json` scripts only if they break (pre-scripts / prepublishOnly)
- Modify: package/sample sources' `package.json` to declare any dependency strict mode reveals as undeclared

- [ ] **Step 1:** Before changing anything, record `npm pack --dry-run` file lists for both packages (baseline).
- [ ] **Step 2:** Make the edits above; `pnpm install` (via the pinned version, e.g. `corepack pnpm@12.18.0` or `npx pnpm@12.18.0`).
- [ ] **Step 3:** Run, from a clean tree (`dist` deleted): `pnpm install --frozen-lockfile`, `pnpm run lint`, `pnpm run format:check`, `pnpm run typecheck`, `pnpm test`, `pnpm --filter @misaconnect/einvoice run smoke`, `pnpm --filter @misaconnect/esign run smoke`, `pnpm --filter "./samples/**" run typecheck`, `pnpm audit`.
  Expected: all exit 0; vitest totals equal the npm-era totals; audit 0 vulnerabilities.
- [ ] **Step 4:** In each package dir, `npm pack --dry-run` → same file list as Step 1 and `prepublishOnly` build succeeds.
- [ ] **Step 5:** Commit `chore(ts): pnpm workspace + lockfile (replaces npm workspaces)`.

### Task 2: CI workflows + Dependabot

**Files:**
- Modify: `.github/workflows/ts-ci.yml`, `.github/workflows/ts-sandbox.yml`, `.github/workflows/release-ts.yml`, `.github/dependabot.yml`, `.github/scripts/ts-release-target.sh` (comments only unless it emits `-w` usage)

- [ ] **Step 1:** Every job that installs: `pnpm/action-setup` (version from `packageManager`) before `actions/setup-node` with `cache: pnpm`; `npm ci` → `pnpm install --frozen-lockfile`; `npm run X` → `pnpm run X`; `npm test -w P` / `npm run X -w P` → `pnpm --filter P run test|X`.
- [ ] **Step 2:** Path filters: `package-lock.json` → `pnpm-lock.yaml` + `pnpm-workspace.yaml` (both `on:` and `dorny/paths-filter` shared list).
- [ ] **Step 3:** `*-node22-min` jobs: no pnpm; symlink as today; run `npm run smoke` with `working-directory` = package dir.
- [ ] **Step 4:** `release-ts.yml`: install/lint/typecheck/test/build on pnpm; keep the npm CLI upgrade step and the `npm stage publish --access public` step byte-for-byte.
- [ ] **Step 5:** `dependabot.yml`: keep `package-ecosystem: npm`, update the comment to "pnpm workspace root".
- [ ] **Step 6:** Validate YAML (`python -c "import yaml,sys;[yaml.safe_load(open(f)) for f in sys.argv[1:]]" <files>`) and `bash .github/scripts/ts-release-target.sh --self-test`. Expected: exit 0.
- [ ] **Step 7:** Commit `ci(ts): run TypeScript CI and release on pnpm; publish stays npm stage publish`.

### Task 3: Docs + changelog

**Files:**
- Modify: `CLAUDE.md` (TypeScript block: pnpm commands), `CONTRIBUTING.md`, `src/typescript/{einvoice,esign}/README.md` and `samples/typescript/{express,nestjs}/README.md` (contributor/run commands only; consumer `npm install @misaconnect/...` lines stay), `docs/constitution.md` only if it names npm commands
- Modify: `CHANGELOG.md` `[Unreleased]` → `### Repo (nothing shipped)`: TypeScript workspace moves to pnpm; publishing still `npm stage publish`.

- [ ] **Step 1:** Edits. `git grep -n "npm ci\|npm run\|npm test\| -w @misaconnect"` → only consumer install lines and the publish step remain.
- [ ] **Step 2:** `pnpm run format:check` → exit 0.
- [ ] **Step 3:** Commit `docs: TypeScript workspace uses pnpm`; push branch; open PR; `ts-ci` green on all jobs.

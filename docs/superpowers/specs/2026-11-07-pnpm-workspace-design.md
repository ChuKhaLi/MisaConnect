# TypeScript workspace: npm → pnpm (publish stays on npm) — design (2026-11-07)

Owner asked on 2026-11-07: "Thay npm bằng pnpm đi", then "pnpm để quản lý repo, package, publish
thì dùng npm".

## Why

The npm workspace lockfile misbehaved on a routine bump (NestJS 12.3.3 → 12.4.0 in
`samples/typescript/nestjs`: `npm install` rewrote hoisting across unrelated packages and still
left 12.3.3 installed, "invalid" in `npm ls`). The owner wants pnpm to own installs, the
workspace and the lockfile. Publishing stays on the npm CLI because `release-ts.yml` uses npm
**trusted publishing (OIDC) with staged publishing** (`npm stage publish`, approved on npmjs.com
with 2FA), which pnpm does not provide.

## Goals

1. **pnpm owns the workspace.** `pnpm-workspace.yaml` lists `src/typescript/*` and
   `samples/typescript/*`; `package.json` loses `workspaces`; `pnpm-lock.yaml` replaces
   `package-lock.json` (deleted). Root `package.json` gets `"packageManager": "pnpm@12.18.0"`
   (latest stable at design time; builder may pick the newest 12.x that works).
2. **Overrides move to pnpm.** `overrides.minimatch ^11.0.5` becomes a pnpm override (in
   `pnpm-workspace.yaml` `overrides:`), still giving `pnpm audit` 0 vulnerabilities.
3. **Samples use the workspace protocol:** `"@misaconnect/einvoice": "workspace:*"` (and esign)
   in both samples, so they always link the in-repo packages.
4. **Root scripts use pnpm:** `build`/`test`/`typecheck` run `pnpm -r --if-present <script>`
   (same set of packages, same order guarantees as today: packages before samples via
   topological order). Package-level scripts that call `npm run …` (`prepublishOnly`, `pretest`,
   `pretypecheck`) are changed only if they break under pnpm; `prepublishOnly` must keep working
   under `npm stage publish`.
5. **Strict node_modules stays on** (pnpm default, isolated linker). If a package or sample
   imports something it does not declare, declare it (devDependency) — do not switch to
   `shamefully-hoist` / `node-linker=hoisted`.
6. **CI on pnpm:** `ts-ci.yml`, `ts-sandbox.yml` and `release-ts.yml` use `pnpm/action-setup`
   (version from `packageManager`) + `actions/setup-node` with `cache: pnpm`, and
   `pnpm install --frozen-lockfile`; `-w <pkg>` becomes `--filter <pkg>`. Path filters that list
   `package-lock.json` list `pnpm-lock.yaml` and `pnpm-workspace.yaml` instead. The
   `*-node22-min` jobs (Node 22.0.0, built `dist` only, symlinked package) keep working without
   installing pnpm tooling on 22.0.0.
7. **Publish stays npm.** In `release-ts.yml`, install/lint/typecheck/test/build run on pnpm;
   the final step is unchanged: `npm stage publish --access public` in the package directory,
   with the npm CLI `>=11.15.0 <12`. The published tarball must be the same shape as before
   (`files`, `exports`, no `workspace:` specifiers — the packages have zero runtime deps).
8. **Dependabot** keeps `package-ecosystem: npm` (it reads `pnpm-lock.yaml`); comments updated.
9. **Docs:** `CLAUDE.md` "Build & test", `CONTRIBUTING.md`, `docs/constitution.md` (only where it
   names npm commands), both package READMEs' contributor sections, and
   `.github/scripts/ts-release-target.sh` comments say pnpm. User-facing install instructions
   (`npm install @misaconnect/esign`) stay as they are — consumers can use any package manager.
10. Root `CHANGELOG.md` `[Unreleased]` → "Repo" note. No package release (published artifacts
    unchanged).

## Non-goals

- No change to package code, `exports`, build output, `engines`, or versions.
- No change to the .NET side.
- No move of publishing to pnpm, no npm token.

## Constraints

- Dev toolchain Node ≥ 22.18 (unchanged); packages support Node ≥ 22 (unchanged).
- Every existing CI check keeps running: lint, format:check, typecheck, unit tests on Node 22
  and 24, smoke (ESM + CJS), Node 22.0.0 smoke on built output, release tag self-test.
- Test counts unchanged.

## Calls made by the agent (owner may override)

- pnpm **12.x** pinned via `packageManager` (not 10.x, the version installed locally).
- Overrides in `pnpm-workspace.yaml` rather than `package.json` `pnpm.overrides`.
- Strict (isolated) `node_modules`; fix undeclared imports instead of hoisting.
- Dependency build scripts: allow only what the toolchain needs (e.g. `esbuild`) via pnpm's
  allow-list setting; nothing else runs install scripts.

## Proof of done

- Locally: `pnpm install --frozen-lockfile`, `pnpm run lint`, `pnpm run format:check`,
  `pnpm run typecheck`, `pnpm test`, both `smoke` scripts, `pnpm audit` → 0.
- `npm pack --dry-run` in each package lists the same files as before the change.
- `ts-ci` green on the PR (all jobs). `release-ts` cannot be exercised without a tag; its
  pnpm steps mirror `ts-ci`, and the publish step is byte-for-byte unchanged.

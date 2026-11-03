# Handoff: TypeScript SDK — @misaconnect/einvoice 0.1.1 shipped (2026-11-03 21:07)
Session: caf18abd-d47a-4a4c-8612-fd3bd96fa256 · Supersedes: none
Read first:
- docs/superpowers/specs/2026-11-02-typescript-sdk-design.md (approved design)
- docs/superpowers/plans/2026-11-03-ts-foundation-and-restructure.md (plan 1, done)
- docs/superpowers/plans/2026-11-03-ts-einvoice-package.md (plan 2, done)
- src/typescript/einvoice/README.md ("Differences from the .NET SDK", "Network / TLS") and CHANGELOG.md
- CONTRIBUTING.md "Releasing" (TS release flow)
- Memory: misa-sandbox-node22-tls-reset (ECONNRESET root cause)

## State (checked 2026-11-03 21:07)
- Repo F:\Projects\MyProjects\MisaConnect, branch `main` at `52fb593`, in sync with `origin/main` (pushed). Working tree clean.
- Tags pushed: `einvoice-ts-v0.1.0`, `einvoice-ts-v0.1.1`.
- npm: `@misaconnect/einvoice` versions `0.0.0-stage`, `0.1.0`, `0.1.1`; `latest = 0.1.1`; 0.1.1 has SLSA v1 provenance (`npm audit signatures` → "1 package has a verified attestation"). 0.1.0 has no provenance (manual publish).
- Tests run just now:
  - `npm test -w @misaconnect/einvoice` → 24 files, 535 passed. `npm run lint`, `npm run typecheck` (SDK + both samples), `npm run format:check` → green.
  - `dotnet build MisaConnect.slnx -c Release` → 0 warnings, 0 errors. `dotnet test tests/dotnet/...`: EInvoice.Unit 315/315, ESign.Unit 301/301, EInvoice.Integration 69 passed / 27 skipped / 0 failed, ESign.Integration 78 passed / 5 skipped.
- GitHub Actions (latest): `release-ts` success on 52fb593 (staged 0.1.1, approved by owner), `ts-ci` success on b836120. Earlier `release-ts` failure on b836120 = plain `npm publish` rejected (stage-only trusted publisher) — fixed.

## Done and verified
- Repo restructure: .NET under `src/dotnet`, `tests/dotnet`, `samples/dotnet`; TS under `src/typescript/*`, `samples/typescript/*`; constitution v1.2.0; path-filtered CI (`ci.yml` .NET, `ts-ci.yml` TS incl. Node 20 smoke).
- `@misaconnect/einvoice`: all 10 operations, fake MISA server, 535 tests; deferred review minors fixed in waves A–D (each reviewed).
- Fixed pre-existing bug: 52 EInvoice integration tests failed on main because Samples.Api always required eSign config → sample now wires eSign only when `Misa:ESign` exists; `ci.yml` runs integration tests on every push.
- Security: removed `__unsafeAllowAnyBaseUrl` escape hatch from the public factory (internal `createMisaEInvoiceClientInternal` in src/client-internal.ts for tests); JSON-injection money brand fix; BigInt DoS bounds.
- 0.1.1 TLS fix: default HTTP client over node:https with `ecdhCurve 'X25519:P-256:P-384'`, shared module-level keep-alive agents, 4 s idle timeout. Wire-level ClientHello test proves no `0x11ec`. Live sandbox runs on Node 22/24 now reach MISA's application layer (previously ECONNRESET).
- Release pipeline: tag `einvoice-ts-v<version>` → `release-ts.yml` (lint/typecheck/test/tag==version) → `npm stage publish` via GitHub OIDC (environment `npm-publish`, tag policy `einvoice-ts-v*`) → owner approves with 2FA. Verified end-to-end with 0.1.1.
- Local git SSH: `core.sshCommand` set for this repo to use `~/.ssh/github_chungdinhnguyen` (verified `git fetch`/`push` work).

## Done but NOT verified
- Full live sandbox suite (`ts-sandbox.yml`, Node 22/24) — blocked: MISA token endpoint answers `INVALID_TAXCODE` with the `misa-sandbox` environment secrets (`MISA_TAX_CODE`, `MISA_USERNAME`, `MISA_PASSWORD`, `MISA_APP_ID`). Prove by fixing the secrets, then `gh workflow run ts-sandbox.yml --ref main`.
- README proxy example `new ProxyAgent({ uri, requestTls: { ecdhCurve } })` — undici not installed locally; behaviour from undici source reading only.
- Exact Node boundary "22.20+ / 24" for OpenSSL 3.5 — evidence covers 22.23 and 24.22 only.

## In progress
none

## Next actions
1. Fix sandbox credentials (needs the user), then run `gh workflow run ts-sandbox.yml --repo ChuKhaLi/MisaConnect --ref main` and read failures.
2. Map MISA token-endpoint string errors (`INVALID_TAXCODE` → Configuration, `INVALID_USER` / wrong password → Authentication) instead of `MisaUnknown`; document as a deliberate .NET difference; release 0.1.2 (bump version + CHANGELOG, merge, push tag `einvoice-ts-v0.1.2`, owner approves the staged version). Confirm the exact MISA response shape from the sandbox run first.
3. Fix the 4 .NET defects from spec §9 (separate .NET work, Spec Kit flow): factory drops `Delete.IncludeRawErrorMessage`; `MeInvoiceCallLogger` logs customer tax code (PII); batch-size codes mapped to Validation not BatchOrSize; `docs/architecture.md` ICorrelationIdAccessor wording.
4. Brainstorm + design `@misaconnect/esign` (separate design doc). Account for the eSign single-BaseUrl routing flaw (memory: esrm-routing-bug-verified) from the start.

## Needs the user
- Valid MISA sandbox credentials (or confirmation from MISA that the v2 `/api/integration` sandbox account is still active; a third-party source mentions an `/api/v3` API).
- Decision: approve mapping INVALID_TAXCODE/INVALID_USER away from MisaUnknown (diverges from .NET).
- Whether to `npm deprecate @misaconnect/einvoice@0.0.0-stage`.
- Secret in git: `specs/006-fix-esrm-routing/bug-report.md` is tracked on the pushed `main` and (per memory note "ESRM bug report has live creds") contains live eSign sandbox credentials. Decide: rotate those credentials, redact the file, and whether history rewrite is wanted.

## Loose ends
- `.superpowers/sdd/ts-einvoice-tls/` (gitignored): brief + review diffs for the TLS fix — safe to delete.
- Scratchpad `C:\Users\chung\AppData\Local\Temp\claude\F--Projects-MyProjects-MisaConnect\caf18abd-d47a-4a4c-8612-fd3bd96fa256\scratchpad\`: throwaway scripts, `plan2-ledger.md` (plan 2 ledger backup with all rulings), `nest.log` (NestJS sample boot against sandbox with dummy creds), `npmcheck*/` install tests, a `dec/` .NET console used to measure decimal rounding.
- No dev servers / tunnels / browsers running (checked process list). No CLI logins created this session besides the user's own `npm login` (if done) and the repo-local `core.sshCommand`.
- Old local branches from earlier slices (`001-…` … `008-…`, `fix/esign-hash-empty-doc-arrays`) — pre-existing, untouched.

## Decisions (don't relitigate)
- TS SDK lives in this repo alongside .NET; same feature list and names (methods camelCase without `Async`; properties camelCase keeping acronyms: `ipTemplateID`, `vatAmountOC`).
- Layout `src|tests|samples/{dotnet,typescript}`; no Nx/Turborepo; path-filtered CI.
- superpowers process for TS (not Spec Kit); constitution v1.2.0.
- npm scope `@misaconnect`; publish via trusted publishing, stage-only, manual 2FA approval; first version published manually.
- Money = `number | string`; numbers rounded half-to-even to 28 places like .NET's JSON decimal reader; strings strict, bounded by decimal.MaxValue.
- saveDraft returns per-invoice results (only empty/oversize batch throws), per .NET.
- Default HTTP client pins classical ECDH curves (0.1.1).

## Gotchas
- Windows `core.autocrlf=true`: `dotnet format --verify-no-changes` reports thousands of ENDOFLINE diagnostics locally (CI on Linux is the gate); TS files forced LF via `.gitattributes`.
- Git-bash heredocs with backslashes get mangled → write Python helper scripts to the scratchpad instead.
- Root dev toolchain needs Node ≥ 22.18 (tsup 9 / vitest 5); package supports Node ≥ 20 (CI smoke on 20).
- Local npm is 10.9.9 → no `npm stage`; use `npx -y npm@11 stage …` or the npmjs.com Staged Packages tab.
- `release-ts.yml` must not set `registry-url` / `NODE_AUTH_TOKEN` (breaks OIDC); trusted publisher is stage-only → plain `npm publish` gets 403 "OIDC permission denied".
- Node fetch to MISA from non-VN hosts on Node ≥ 22 (OpenSSL 3.5) → ECONNRESET; curl works. From the user's VN machine everything works, so reproduce on GitHub runners.
- Prettier `--write` on the whole package touches golden JSON files; they are excluded in `.prettierignore` — format only changed files.
- The auto-mode classifier blocks deleting pushed tags and credential exploration (e.g. probing `~/.ssh` keys); the user ran those manually.

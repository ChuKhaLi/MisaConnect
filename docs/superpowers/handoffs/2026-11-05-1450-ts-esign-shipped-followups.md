# Handoff: TypeScript esign shipped; .NET follow-ups pending (2026-11-05 14:50)
Session: 4c889050-712e-4b31-bce8-fa9ad35d1b1d · Supersedes: docs/superpowers/handoffs/2026-11-03-2107-typescript-einvoice-sdk.md
Read first:
- docs/superpowers/specs/2026-11-04-ts-esign-design.md (esign design; §9 = .NET defects the TS port fixed)
- docs/superpowers/specs/2026-11-04-esign-dotnet-inventory.md (.NET ESign behaviour reference)
- docs/superpowers/plans/2026-11-04-ts-esign-package.md (plan, fully executed)
- specs/009-fix-einvoice-inventory-defects/ (last .NET Spec Kit slice, merged)
- src/typescript/esign/README.md "Differences from the .NET SDK" and CHANGELOG.md
- CONTRIBUTING.md "Releasing" (TS tag flow + manual-publish rule)
- Memory: misa-sandbox-node22-tls-reset (path-MTU blackhole), misa-einvoice-taxcode-appid-split

## State (checked 2026-11-05 14:44)
- Repo F:\Projects\MyProjects\MisaConnect, branch `main` at `a113635`, in sync with `origin/main`, working tree clean.
- `npm test` (root, just run): einvoice 540/540, esign 547/547.
- Latest CI on main: `ts-ci` success, `ts-sandbox` success (run 41969548992: einvoice 10 passed/1 skipped on Node 22+24; esign 3 skipped — no ACTIVE certificate), `ci` success (2026-11-04), `release` v2.4.0 success. `integration.yml` last run = failure (2026-11-04, ESign sandbox NoActiveCertificate — see Next actions #2).
- npm: `@misaconnect/einvoice` latest 0.1.4 (provenance verified). `@misaconnect/esign` latest 0.1.0 (manual publish, no provenance); **0.1.1 is STAGED** by CI (run 41989059982, staged id 778d1b37-2d40-4ec6-b5b4-0ac213cc396b), awaiting owner 2FA approval. `0.0.0-stage` of both packages deprecated.
- NuGet: MisaConnect.EInvoice / MisaConnect.ESign 2.4.0 (GitHub Release "MisaConnect 2.4.0").
- Tags pushed: v2.4.0, einvoice-ts-v0.1.2/3/4, esign-ts-v0.1.1.
- GitHub environment `npm-publish` deployment tag policies: `einvoice-ts-v*`, `esign-ts-v*` (the latter added this session).

## Done and verified
- `@misaconnect/einvoice` 0.1.2 (INVALID_USER→Authentication, INVALID_TAXCODE→Configuration), 0.1.3 (docs: path-MTU), 0.1.4 (2 s happy-eyeballs attempt budget) — all public, installs checked.
- Root cause of ECONNRESET from abroad: path-MTU blackhole (requests > ~1.4 KB); sandbox CI (TS + .NET) lowers MTU to 1280 — green.
- Root cause of intermittent ETIMEDOUT: Node `autoSelectFamily` 250 ms attempt vs ~210–250 ms TCP handshake (MISA has AAAA); proven with tc netem A/B on runners; fixed in both packages (PR #4).
- .NET slice 009 (factory Delete options, tax code out of logs, BatchOrSize, DI docs) merged and released as 2.4.0.
- `dotnet format --verify-no-changes` gate added to ci.yml (PR #2).
- `@misaconnect/esign`: full port (sign ×4 formats, 2FA, OTP resend, webhook begin/handle/finalize), 14 SDD tasks each reviewed + final whole-branch review + fix wave; PRs #3 and #5 merged. Security rulings R5/R9/R10/R11/R12 implemented (see design + README).
- `tokenFingerprintSecret` (owner choice B) shipped — note it went out in esign 0.1.0 because 0.1.0 was published from the feature branch; main now matches (PR #5).
- npm trusted publishing for esign validated (CI stage via OIDC succeeded).

## Done but NOT verified
- **Live eSign signing** (both .NET and TS): sandbox login works, but the sandbox account has no ACTIVE remote-signing certificate, so no document was ever signed live. Prove: get a certificate on the account, set `MISACONNECT_ESIGN_SANDBOX_REQUIRED: '1'` in `.github/workflows/ts-sandbox.yml` (esign job), run `gh workflow run ts-sandbox.yml --ref main` and `gh workflow run integration.yml --ref main -f test_filter="FullyQualifiedName~Sandbox"`.
- Whether MTU blackhole also affects the eSign host (login works with MTU 1280; untested at 1500).
- 2FA and webhook live flows (need a 2FA-enabled account and a reachable webhook URL; env vars in docs/superpowers/specs/2026-11-04-esign-dotnet-inventory.md §8).

## In progress
- esign 0.1.1 staged, not yet approved (owner action).

## Next actions
1. Owner approves staged `@misaconnect/esign@0.1.1` on npmjs.com (Staged Packages tab), then verify: `npm view @misaconnect/esign dist-tags.latest` → 0.1.1 and `npm audit signatures` in a scratch install shows a verified attestation.
2. Stop the weekly `.NET integration.yml` from going red: ESign sandbox tests (`tests/dotnet/MisaConnect.ESign.IntegrationTests/Sandbox/Sign{Xml,Word,Excel}SandboxTests.cs`) fail with `NoActiveCertificateException`. Mirror TS ruling R13: skip on NoActiveCertificate unless an env flag (e.g. `MISACONNECT_ESIGN_SANDBOX_REQUIRED=1`) is set. Test-only change; Spec Kit slice optional (small).
3. New .NET Spec Kit slice 010 (via `/speckit-specify`): back-port the TS esign fixes to `MisaConnect.ESign` — design §9 items 1–9 (stale token after 401 refresh, no login fallback, Signing/hash retried on 5xx/timeout, content-type guard only on certificates, ResponseError.error bool, webhook malformed input escapes, 122 missed on 4xx, unused refresh skew, dynamic creds unusable in webhook) plus the security rulings worth porting (webhook confirmation via Signing/status = R10, validation failures not delivered to hook = R12, no OTP in finalize = R11) and the docs errors listed in design §9 ("Docs-only"). Likely a minor version (behaviour changes).
4. Add `.github/dependabot.yml` (npm root workspace, NuGet, github-actions). `npm audit`: minimatch (moderate, ESLint dev-only) and multer (high, via `@nestjs/platform-express` in samples/typescript/nestjs only) — published packages have zero runtime deps and are unaffected; bump the NestJS sample.
5. Optional: EInvoice .NET DI uses `Add…` for all ports (ESign uses `TryAdd…`); switching is a behaviour change (minor bump). See specs/009-fix-einvoice-inventory-defects/research.md R4.
6. Optional cleanup batch: ~25 deferred minors from the esign SDD ledger, all triaged "can ship" by the final review. The ledger copy was deleted at the owner's request; the rulings live in the esign README "Differences from the .NET SDK", the design doc, and the PR #3 description.

## Needs the user
- Approve esign 0.1.1 stage (2FA).
- An ACTIVE remote-signing certificate on the eSign sandbox account (owner said: later).
- Whether slice 010 (.NET back-port) is wanted and at what version (minor suggested).

## Loose ends
- None. At the owner's request (2026-11-05 ~14:55): the 8 merged local branches were deleted, `.superpowers/sdd/ts-einvoice-tls/` and the session scratchpad were removed, and `npm logout` was run (`npm whoami` → ENEEDAUTH). No background processes remain.
- eInvoice and eSign sandbox credentials live only in the GitHub environment `misa-sandbox` secrets (the owner typed them in chat; nothing is on disk in the repo).

## Decisions (don't relitigate)
- eInvoice sandbox tax code is the bare MST; AppID separate (MISA's "MST-AppID" string split).
- Token-endpoint INVALID_USER/INVALID_TAXCODE map to Authentication/Configuration in TS (documented .NET difference).
- No git-history rewrite (no credentials were ever in history; rewrite would orphan release tags/provenance).
- Minor version bump for the .NET BatchOrSize behaviour change (2.4.0).
- PR workflow for merges; squash for branches with temporary diagnostics.
- dotnet format check in CI.
- esign: full port of the .NET facade; executed with subagent-driven development.
- `tokenFingerprintSecret` option (choice B) for cross-host token sharing.
- eSign sandbox certificate: deferred by the owner.

## Gotchas
- Auto mode blocks: creating/pushing tags, writing GitHub secrets, merging PRs the user did not explicitly ask to merge → give the owner the exact command (`! …`).
- PowerShell pipes add a trailing newline to native stdin → use `gh secret set … --body '…'`.
- Manual `npm publish` of a package with `publishConfig.provenance: true` needs `--provenance=false`; and it packs the working tree + runs the build — publish only from clean, up-to-date `main` (esign 0.1.0 shipped a feature branch).
- npm E404 on PUT of a scoped package = not logged in / no scope rights (check `npm whoami`).
- New npm package: publish once manually, then configure trusted publisher; the GitHub environment `npm-publish` must also list the tag pattern (`esign-ts-v*` was missing → "not allowed to deploy").
- GitHub runners: path-MTU blackhole to MISA (lower MTU 1280), Node 250 ms happy-eyeballs (fixed in SDK; raw `net.connect` probes need `autoSelectFamilyAttemptTimeout`), `tc netem` delays DNS too unless filtered by destination IP.
- Python edits via git-bash heredoc can turn `\n` inside strings into real newlines (broke a YAML once) — prefer the Edit tool for escapes.
- Windows CRLF working copy: `dotnet format` shows ENDOFLINE noise locally; index is LF; CI on Linux is the gate.

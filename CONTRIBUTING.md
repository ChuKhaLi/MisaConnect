# Contributing to MisaConnect

Thanks for considering a contribution. MisaConnect is a community .NET SDK for MISA cloud APIs.

## Workflow

1. **Open an issue** for any non-trivial change before sending a PR.
2. **Design-driven development** ([constitution](docs/constitution.md) v1.3.0, Principle V). Every feature or behaviour change, .NET or TypeScript, starts from a design document under `docs/superpowers/specs/` and an implementation plan under `docs/superpowers/plans/`. Example: [the TypeScript SDK design](docs/superpowers/specs/2026-11-02-typescript-sdk-design.md). `specs/001`–`009` are archived Spec Kit slices from before 2026-11-05; do not add new ones.
3. **Layered architecture is non-negotiable.** Domain → Application → Infrastructure → Client. No skipping layers; no reaching backwards. See [docs/architecture.md](docs/architecture.md).
4. **Tests are mandatory.** Each product family ships its own unit + integration test projects:
   - Unit tests (`tests/dotnet/MisaConnect.EInvoice.UnitTests`, `tests/dotnet/MisaConnect.ESign.UnitTests`) — no network, < 30 seconds total per project.
   - Integration tests (`tests/dotnet/MisaConnect.EInvoice.IntegrationTests`, `tests/dotnet/MisaConnect.ESign.IntegrationTests`) — sandbox or in-repo fake server (`MisaFake/` for EInvoice, `EsignFake/` for ESign). Must skip cleanly when the relevant sandbox is unreachable.
5. **Public surface changes require:**
   - A `CHANGELOG.md` entry under `[Unreleased]`.
   - A semver-appropriate version bump on release.
6. **Wire-format fidelity.** Match MISA's published CURL examples and DTO casing verbatim. Document any deviation inline with the reason.
7. **No secrets in logs.** Tokens, buyer names/addresses, line-item content, and raw MISA error messages stay out of default logs. Raw vendor errors only surface behind an opt-in flag.

## Branching

- `main` is always releasable.
- Feature branches: `feat/short-name`.
- Fix branches: `fix/short-name`.

## PR requirements

- `dotnet build MisaConnect.slnx` — clean, no warnings (`TreatWarningsAsErrors=true`).
- `dotnet test --project tests/dotnet/MisaConnect.EInvoice.UnitTests` and `dotnet test --project tests/dotnet/MisaConnect.ESign.UnitTests` — both unit test projects green.
- New public types or DI-exposed types need XML doc comments.
- TypeScript changes (`src/typescript/**`): `npm ci`, then `npm run lint`, `npm run format:check`, `npm run typecheck`, `npm test` — all green. The TS dev toolchain needs Node ≥ 22.18 (see `.nvmrc`); published packages support Node ≥ 20.
- One design/plan per PR. Bundle test + code + docs + changelog into a single change.

## Releasing

The two .NET packages (`MisaConnect.EInvoice`, `MisaConnect.ESign`) currently share one version line: a `v<MAJOR>.<MINOR>.<PATCH>` tag makes `release.yml` pack **both** at that version, push them to NuGet (`--skip-duplicate`) and create the GitHub Release "MisaConnect <version>" from the matching `## [<version>]` section of `CHANGELOG.md`. Before tagging, move the `[Unreleased]` entries into that section and say in its intro which family actually changed (the other ships unchanged at the new version).

TypeScript packages release separately. For `@misaconnect/einvoice`: bump `version` in `src/typescript/einvoice/package.json`, add the `CHANGELOG.md` entry, merge to `main`, then push the tag `einvoice-ts-v<version>` (it must equal the package version). `@misaconnect/esign` works the same way with `src/typescript/esign/package.json` and the tag `esign-ts-v<version>`; `release-ts.yml` derives the package and directory from the tag prefix (`bash .github/scripts/ts-release-target.sh --self-test` checks the mapping) and refuses any other ref. Before the first `esign-ts-v*` tag, the owner must add the npm trusted publisher for `@misaconnect/esign` on npmjs.com (owner `ChuKhaLi`, repo `MisaConnect`, workflow `release-ts.yml`, environment `npm-publish`), as was done for einvoice. `release-ts.yml` runs lint, typecheck and tests and stages the version with `npm stage publish` through npm trusted publishing (GitHub OIDC, environment `npm-publish`, no npm token in the repo; the trusted publisher may only stage). The version is not public until a maintainer approves it with 2FA (`npm stage approve <id>` or the Staged Packages tab on npmjs.com).

**Manual npm publish (first version of a package only, before its trusted publisher exists):** publish from an up-to-date, clean `main` — `git switch main && git pull && git status` must show nothing to commit. `npm publish` packs the files on disk and runs the package's build, so publishing from a feature branch ships unmerged code (this happened with `@misaconnect/esign` 0.1.0, which shipped the `tokenFingerprintSecret` branch before it was merged). Use `npm publish --access public --provenance=false --otp=<code>` (provenance only works in CI). Every later version goes through the tag + `release-ts.yml` flow above.

## Style

- C# `latest` language version, nullable enabled, file-scoped namespaces.
- `EditorConfig` is authoritative — run `dotnet format` before opening a PR.

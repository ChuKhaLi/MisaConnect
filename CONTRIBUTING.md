# Contributing to MisaConnect

Thanks for considering a contribution. MisaConnect is a community .NET SDK for MISA cloud APIs.

## Workflow

1. **Open an issue** for any non-trivial change before sending a PR.
2. **Slice-driven development.** Each feature lands as a Spec Kit slice under `specs/NNN-name/` with `spec.md`, `plan.md`, `tasks.md`, and (where applicable) `contracts/`. Run `/speckit-specify` to scaffold one.
3. **Layered architecture is non-negotiable.** Domain → Application → Infrastructure → Client. No skipping layers; no reaching backwards. See [docs/architecture.md](docs/architecture.md).
4. **Tests are mandatory.** Each product family ships its own unit + integration test projects:
   - Unit tests (`tests/MisaConnect.EInvoice.UnitTests`, `tests/MisaConnect.ESign.UnitTests`) — no network, < 30 seconds total per project.
   - Integration tests (`tests/MisaConnect.EInvoice.IntegrationTests`, `tests/MisaConnect.ESign.IntegrationTests`) — sandbox or in-repo fake server (`MisaFake/` for EInvoice, `EsignFake/` for ESign). Must skip cleanly when the relevant sandbox is unreachable.
5. **Public surface changes require:**
   - A `CHANGELOG.md` entry under `[Unreleased]`.
   - A semver-appropriate version bump on release.
6. **Wire-format fidelity.** Match MISA's published CURL examples and DTO casing verbatim. Document any deviation inline with the reason.
7. **No secrets in logs.** Tokens, buyer names/addresses, line-item content, and raw MISA error messages stay out of default logs. Raw vendor errors only surface behind an opt-in flag.

## Branching

- `main` is always releasable.
- Feature branches: `slice/NNN-short-name`.
- Fix branches: `fix/short-name`.

## PR requirements

- `dotnet build MisaConnect.slnx` — clean, no warnings (`TreatWarningsAsErrors=true`).
- `dotnet test tests/MisaConnect.EInvoice.UnitTests` and `dotnet test tests/MisaConnect.ESign.UnitTests` — both unit test projects green.
- New public types or DI-exposed types need XML doc comments.
- One slice per PR. Bundle test + code + docs + changelog into a single change.

## Releasing

Each product-family package (`MisaConnect.EInvoice`, `MisaConnect.ESign`) ships under its own `v<MAJOR>.<MINOR>.<PATCH>` git tag, with `release.yml` naming that family in the GitHub Release title. EInvoice tags are `v1.x.y`; ESign tags are `v2.x.y`. The release workflow packs and pushes both families' nupkgs on every tag (`--skip-duplicate` no-ops the already-published family).

## Style

- C# `latest` language version, nullable enabled, file-scoped namespaces.
- `EditorConfig` is authoritative — run `dotnet format` before opening a PR.

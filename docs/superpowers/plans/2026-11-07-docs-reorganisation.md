# Docs reorganisation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One just-the-docs site covering all four packages, organised product → language → topic; short package and root READMEs.

**Architecture:** Task 1 fixes the site skeleton (paths, front matter, nav, stubs). Tasks 2–5 fill the four product × language sections in parallel (independent directories). Task 6 shrinks READMEs and updates CLAUDE.md. Task 7 verifies (Jekyll build in Docker, link check, snippet compile/typecheck).

**Tech Stack:** Jekyll + just-the-docs v0.10.1 (remote theme), GitHub Pages from `main:/docs`; .NET 10 SDK; pnpm/TypeScript.

**Spec:** `docs/superpowers/specs/2026-11-07-docs-reorganisation-design.md`

## Global Constraints

- English. Product first. Site base URL `https://chung-nd.id.vn/MisaConnect/` (`baseurl: /MisaConnect`).
- Paths: `docs/einvoice/index.md` (`/einvoice/`), `docs/einvoice/dotnet/*.md`, `docs/einvoice/typescript/*.md`; same for `esign`. Old `docs/einvoice/{getting-started,configuration,sandbox-setup}.md` (and esign) become stubs: front matter `nav_exclude: true`, one line linking to the `.NET` page.
- Front matter: product index `has_children: true`; language index (`docs/<product>/<lang>/index.md`) `parent: <Product>`, `has_children: true`, `permalink: /<product>/<lang>/`; topic page `parent: .NET` or `TypeScript`, `grand_parent: <Product>`. Titles: products `EInvoice`, `ESign`; languages `.NET`, `TypeScript`.
- Topic pages per language: `getting-started.md`, `configuration.md`, `operations.md`, `errors.md`, `sandbox-setup.md`; ESign adds `two-factor.md`, `webhooks.md`, `credentials.md` per language (or one page each if the content is short). TypeScript pages end with "Differences from the .NET SDK" where the current README has one.
- Shared MISA concepts live on the product index page (`docs/<product>/index.md`), not repeated per language.
- Nothing dropped: every section of the current TS READMEs and `docs/einvoice|esign/*` lands on a page.
- .NET examples use the facades (`AddEInvoiceClient` → `IMisaEInvoiceClient`; `AddMisaConnectESign` → `IMisaESignClient`), not use cases from DI.
- Site/root README: relative links. Package READMEs: absolute `https://chung-nd.id.vn/MisaConnect/...` links.
- Code fences: `csharp` / `ts` / `json` / `bash`; a deliberately partial snippet carries `<!-- snippet: partial -->` on the line before the fence (excluded from compile checks).

## Review Focus

1. Old URLs (`/einvoice/getting-started`, `/esign/configuration`, …) still resolve and point to the new page — Task 1 stubs, checked in Task 7 build output.
2. Nav: every new page appears under the intended product/language and nothing orphaned — Task 7 build (just-the-docs warns/omits on bad `parent`).
3. Examples drift from the API — Task 7 compiles every full `csharp` block and typechecks every full `ts` block.
4. Package READMEs on nuget.org/npm with relative links — Task 6 uses absolute URLs; Task 7 greps.
5. Content dropped in the move — Task 7 heading inventory: every `##` heading of the old TS READMEs / docs pages maps to a new page (list in the PR).

---

### Task 1: Site skeleton

**Files:** `docs/_config.yml` (title/description "Community SDKs for MISA cloud APIs — .NET and TypeScript"; aux_links add npm packages; exclude `superpowers/` if not already invisible), `docs/index.md` (home: four-package table, product buttons), `docs/einvoice/index.md`, `docs/esign/index.md` (product intro + shared concepts + "choose .NET / TypeScript"), `docs/<product>/<lang>/index.md` ×4 (language landing: install + links to topic pages), stubs for the six old pages, `docs/architecture.md` nav unchanged.

- [ ] Write skeleton + shared concepts (move from current pages / READMEs).
- [ ] Commit `docs: site skeleton — product → language`.

### Tasks 2–5: Section content (parallel, one per product × language)

- Task 2 `docs/einvoice/dotnet/*` — from `docs/einvoice/*`, `src/dotnet/MisaConnect.EInvoice.Client/README.md`.
- Task 3 `docs/einvoice/typescript/*` — from `src/typescript/einvoice/README.md` (+ fixture comment `src/typescript/einvoice/test/fixtures/readme-invoice.ts` → new page).
- Task 4 `docs/esign/dotnet/*` — from `docs/esign/*`, `src/dotnet/MisaConnect.ESign.Client/README.md`.
- Task 5 `docs/esign/typescript/*` — from `src/typescript/esign/README.md`.

Each: write the topic pages per Global Constraints; operations page lists every public method (check against `IMisaEInvoiceClient.cs` / `IMisaESignClient.cs` / TS `src/client.ts`); report a heading-mapping list (old heading → new page). Commit `docs(<product>/<lang>): …`.

### Task 6: Short READMEs + CLAUDE.md

**Files:** `README.md`, `src/dotnet/MisaConnect.{EInvoice,ESign}.Client/README.md`, `src/typescript/{einvoice,esign}/README.md`, `CLAUDE.md` (Public surface: EInvoice 2.x + ticket templates + TS packages; doc links).

- [ ] Package READMEs ≤ ~80 lines: intro, install, quickstart, operations table, requirements, absolute links. Root README: four-package table, .NET + TS snippet, current layout, build/test for both toolchains.
- [ ] `pnpm run format:check` → 0. Commit `docs: short READMEs`.

### Task 7: Verify

- [ ] Jekyll build in Docker (`ruby` image, `gem install jekyll jekyll-remote-theme jekyll-seo-tag`, `jekyll build -s docs`); no errors; every new page in `_site` at the planned URL; stubs present.
- [ ] Link check script over `docs/**/*.md` + READMEs: relative links resolve to files/anchors; package READMEs contain no relative links.
- [ ] Extract full `csharp` blocks → scratch console project referencing the Client projects → `dotnet build` 0 errors; full `ts` blocks → scratch `.ts` files typechecked against workspace packages.
- [ ] Heading inventory (Review Focus 5). Commit fixes; push; open PR (owner merges — merge publishes the site).

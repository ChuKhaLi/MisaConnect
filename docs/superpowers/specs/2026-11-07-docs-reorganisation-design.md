# Docs reorganisation: one site for four packages, product first — design (2026-11-07)

Owner, 2026-11-07: reorganise README + docs for .NET and TypeScript; readers think "I want
e-invoices" first, language second; package READMEs stay short; English is fine (Vietnamese
optional).

## Why

Four packages ship from this repo — NuGet `MisaConnect.EInvoice`, `MisaConnect.ESign`; npm
`@misaconnect/einvoice`, `@misaconnect/esign` — but the docs site (GitHub Pages from `main:/docs`,
just-the-docs theme, live at https://chung-nd.id.vn/MisaConnect/) is titled "Community .NET SDK"
and has no TypeScript content. The TypeScript reference exists only as two long package READMEs
(307 and 364 lines). The root README's project layout is stale (`src/MisaConnect.*` instead of
`src/dotnet/…`, no `src/typescript/`, `samples/typescript/`, `tests/dotnet/`, pnpm). A reader
looking for "e-invoice in Node" has no entry point on the site.

## Goals

1. **Site structure, product first.** Navigation:
   - Home — what MisaConnect is, the four packages (table: product × language, version badges),
     "pick your product" buttons. Title/description: "Community SDKs for MISA cloud APIs — .NET
     and TypeScript".
   - **EInvoice** (section index: what it does, which MISA endpoints, shared MISA concepts) with
     children **.NET** and **TypeScript**, each with: Getting started, Configuration, Operations
     (every public method, inputs, outputs, errors, short example), Errors, Sandbox setup.
   - **ESign** — same shape; plus its flows (2FA/OTP, webhook mode, credentials modes, multi-format)
     as pages under each language or one shared concepts page with per-language snippets.
   - Architecture, Contributing pointer, Changelog links (root + per TS package).
2. **Shared concepts written once per product**, in the product index (or a "Concepts" child): for
   EInvoice — tax code = MST + AppID, with-code vs without-code, template series, RefId,
   draft lifecycle, paging limits, error categories, calling MISA from outside Vietnam (MTU /
   happy-eyeballs); for ESign — certificates, sign-hash flow, OTP, webhook ACK, credentials
   modes. Language pages link to them instead of repeating.
3. **Content moves, it is not rewritten from scratch.** Everything in the current TS READMEs and
   `docs/einvoice/*`, `docs/esign/*` lands somewhere on the site (nothing dropped). The TS
   "Differences from the .NET SDK" sections become one "Differences" section per TypeScript
   product page.
4. **Package READMEs short** (all four): one-paragraph intro, install, a 10–15-line quickstart, an
   operations table (one row per method), requirements (runtime, Microsoft.Extensions ≥ 10.0.14
   for .NET / Node ≥ 22 for TS), links to the site pages. Target ≤ ~80 lines each. They are what
   nuget.org / npmjs.com display; the change reaches those pages only with the next release of
   each package (no release is made for docs alone).
5. **Root README short and current:** what it is, the four-package table, a minimal .NET and a
   minimal TS snippet, correct repo layout (dotnet + typescript + samples + tests), build/test
   commands for both toolchains (pnpm), links.
6. **Examples are correct for today's API.** .NET guides use the `IMisaEInvoiceClient` /
   `IMisaESignClient` facades via `AddEInvoiceClient` / `AddMisaConnectESign` (current guides
   resolve use cases such as `ListActiveTemplates` from DI directly). Every code example on the
   site must compile/typecheck against the current packages (see Proof of done).
7. **TS README fixture.** `src/typescript/einvoice/test/fixtures/readme-invoice.ts` mirrors the
   README's "A complete invoice" example; when the example moves to the site, the fixture's
   comment points to the site page and the page carries the same object.
8. **`CLAUDE.md`** public-surface section updated (EInvoice is 2.x, `ListTicketTemplates` /
   `ListTicketTemplatesAsync` listed, TS packages mentioned) and its links follow the new docs paths.
9. No broken links: internal links on the site and in all READMEs resolve.

## Non-goals

- No Vietnamese translation in this round (owner: English is fine; Vietnamese is a later,
  separate piece of work).
- No code changes (the small deferred code fixes from 2.10.0 / pnpm go in a separate PR).
- No theme change, no custom domain change, no new site generator.
- No package release just for README changes.

## Constraints

- GitHub Pages builds from `main:/docs` with Jekyll + just-the-docs `v0.10.1` (`docs/_config.yml`);
  pages need front matter (`title`, `parent`, `grand_parent`, `nav_order`, `has_children`). Merging
  to `main` publishes immediately.
- `docs/superpowers/`, `docs/misa-api-reference/`, `docs/constitution.md` stay where they are
  (they must keep being excluded from or unobtrusive in site navigation as today).
- Existing public URLs (`/einvoice/`, `/esign/`, `/einvoice/getting-started`, …) should keep
  working: either keep those paths for the .NET pages or add `redirect_from`-free stubs that link
  to the new page (just-the-docs has no redirect plugin configured).

## Calls made by the agent (owner may override)

- English only this round.
- Product → language → topic hierarchy (three nav levels: section, language, page).
- Existing `/einvoice/…` and `/esign/…` URLs are kept as the product pages; .NET pages move to
  `/einvoice/dotnet/…`, `/esign/dotnet/…`, TS pages at `/einvoice/typescript/…`,
  `/esign/typescript/…`; old `/einvoice/getting-started` etc. become one-line stubs linking to the
  .NET page.
- Shared concepts on the product index page unless it grows past one screen, then a "Concepts" child.
- Deferred code fixes are a separate PR.
- Package READMEs link to the site with **absolute URLs** (`https://chung-nd.id.vn/MisaConnect/…`),
  because nuget.org / npmjs.com render them outside the repo. Those links can only be checked after
  merge (the site builds from `main`); the root README and site pages use relative links.
- **Deviation, decided while implementing:** the absolute links use `https://chukhali.github.io/MisaConnect/…`, not
  `chung-nd.id.vn`. On 2026-11-07 `chung-nd.id.vn` had no DNS records (the account's user site `ChuKhaLi.github.io`
  sets it as custom domain, so github.io answers with a 301 to it). The github.io address follows whatever domain
  the owner configures, so the links keep working once the domain is fixed or removed.

## Proof of done

- Site builds locally or in the Pages run without warnings about missing parents; every nav entry
  appears under the intended section (check the Pages build on the PR branch is not possible —
  Pages builds `main` only — so verify with a local `bundle exec jekyll build` if Ruby is
  available, else review front matter by script and check the live site right after merge).
- Link check over `docs/**/*.md` and all READMEs: zero broken internal links.
- Each code block tagged `csharp` on the site compiles in a scratch project against the current
  packages, and each `ts` block typechecks in a scratch file against the workspace packages
  (snippets that are deliberately partial are marked and excluded).
- `pnpm run format:check` clean (Prettier covers the TS READMEs); README line counts within target.

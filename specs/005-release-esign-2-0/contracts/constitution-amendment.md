# Contract — Constitution Amendment

Specifies the **exact** text of the three edits required to `.specify/memory/constitution.md` plus the version-bump bookkeeping. Implementation MUST apply these as literal text replacements rather than paraphrasing.

---

## Edit 1 — Header version line

**Current** (line 3):

```markdown
Ratified: 2026-05-13 · Version: 1.0.0
```

**Replace with** (substituting the actual amendment date — the day the release commit lands):

```markdown
Ratified: 2026-05-13 · Last amended: 2026-MM-DD · Version: 1.1.0
```

## Edit 2 — Principle II (Public surface)

**Current** (line 13):

```markdown
## Principle II — Public surface is small and stable

Only types in `MisaConnect.EInvoice.Client` and `MisaConnect.EInvoice.Domain` are public API. Application and Infrastructure types are `internal` unless explicitly part of the consumer-extension contract (port interfaces, DI options, `AddMisaConnectEInvoice`). Every public-surface change requires a `CHANGELOG.md` entry and a semver-appropriate version bump.
```

**Replace with**:

```markdown
## Principle II — Public surface is small and stable

Each product-family package exposes its public surface from exactly two layers: `MisaConnect.<Product>.Client` and `MisaConnect.<Product>.Domain`, where `<Product>` is one of `EInvoice` or `ESign`. Application and Infrastructure types are `internal` unless explicitly part of the consumer-extension contract (port interfaces, DI options, the family's `AddMisaConnect<Product>` extension method). Every public-surface change requires a `CHANGELOG.md` entry and a semver-appropriate version bump on the affected family's package. New product families MUST follow this two-layer public surface pattern.
```

## Edit 3 — Principle VII (Semver discipline)

**Current** (line 33):

```markdown
## Principle VII — Semver discipline

Breaking public-surface changes only in major versions. Minor versions add operations or non-breaking extensions. Patch versions are bug fixes only. The `MisaConnect.EInvoice` package version drives the contract; pre-release identifiers (`-preview.N`) may be used for unstable iterations.
```

**Replace with**:

```markdown
## Principle VII — Semver discipline

Breaking public-surface changes only in major versions. Minor versions add operations or non-breaking extensions. Patch versions are bug fixes only. Each product-family NuGet package (`MisaConnect.EInvoice`, `MisaConnect.ESign`, and any future family) is versioned independently — a major bump on one family does not require a bump on another. Pre-release identifiers (`-preview.N`, `-rc.N`) may be used for unstable iterations within a family.
```

## Edit 4 — Governance footer

**Current** (line 42):

```markdown
- Changes to Principles I–IV require a major version bump on `MisaConnect.EInvoice`.
- Changes to Principles V–VIII may be MINOR if backward-compatible.
```

**Replace with**:

```markdown
- Changes to Principles I–IV require a major version bump on every affected product-family package.
- Changes to Principles V–VIII may be MINOR if backward-compatible. Editorial changes (additive enumeration of existing product families, typo fixes, formatting) MAY be MINOR even when touching Principles I–IV, provided no semantic restriction is added or removed and the sync-impact note explains why.
```

## Edit 5 — Append a sync-impact note

**Insert** at the very bottom of the file (after the existing line `This document is the source of truth. CLAUDE.md, CONTRIBUTING.md, and slice templates derive from it.`):

```markdown

---

## Sync-impact note — 2026-MM-DD (constitution v1.1.0)

The first stable release of `MisaConnect.ESign 2.0.0` (Spec Kit slice 005-release-esign-2-0) made the EInvoice-only wording of Principles II and VII inconsistent with shipping reality. This amendment widens both principles to enumerate the two product families (`MisaConnect.EInvoice`, `MisaConnect.ESign`) and to govern each family's versioning independently.

The amendment is **additive**: no semantic restriction has been added or removed. Every existing `MisaConnect.EInvoice 1.1.0` artifact and every preview-tagged `MisaConnect.ESign` artifact already complies with the new wording. Per the new governance clause, this constitution change is therefore MINOR (1.0.0 → 1.1.0) rather than MAJOR; the Complexity Tracking section of [specs/005-release-esign-2-0/plan.md](../../specs/005-release-esign-2-0/plan.md) records the rationale.

Dependent documents reviewed for cascade: CLAUDE.md (no change required — it references "MisaConnect" generically), CONTRIBUTING.md (added a one-line release convention note under `## Releasing`), `.specify/templates/*` (no template literally references EInvoice as the sole product surface).
```

---

## Required dependent-doc sweep

After the constitution edits land, run this sweep and qualify any ambiguous singular references:

1. **CLAUDE.md** — grep for "the package", "the SDK", "the library". If any occurrence is read as "the single package" rather than "the project as a whole," qualify with the product name. Today's content reads as project-wide; expected no edits, but verify.
2. **CONTRIBUTING.md** — under `## Releasing`, add one sentence: `"Each product-family package (\`MisaConnect.EInvoice\`, \`MisaConnect.ESign\`) ships under its own \`v<MAJOR>.<MINOR>.<PATCH>\` git tag; the release.yml workflow names that family in the GitHub Release title."`
3. **`.specify/templates/*`** — grep for `MisaConnect.EInvoice`. Replace any literal that means "the canonical product" with the `<Product>` family pattern, leaving examples that genuinely refer to EInvoice intact.

## Verification

- `grep -nE '(MisaConnect\.EInvoice|MisaConnect\.ESign)' .specify/memory/constitution.md` — both family names appear in at least one common section (SC-006 acceptance).
- `head -5 .specify/memory/constitution.md` — version line reads `Version: 1.1.0`.
- `tail -20 .specify/memory/constitution.md` — sync-impact note present and dated.

## Out of scope for this contract

- Renaming any product family.
- Changing the layered architecture rule (Principle I).
- Changing the test budget (Principle VI).
- Changing the logging principle (Principle VIII).

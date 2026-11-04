# TypeScript SDK for MisaConnect — Design

Date: 2026-11-02
Status: Approved by owner (2026-11-03)
Scope of this document: repo restructure + shared TypeScript foundation + `@misaconnect/einvoice`. `@misaconnect/esign` gets its own short design later.

## 1. Intent and why

MisaConnect is a community .NET SDK for MISA cloud APIs (eInvoice, eSign). The owner wants to extend it to the **Node.js community**: any backend written in plain Node.js or a Node framework (Express, NestJS, Fastify, …) should be able to call MISA eInvoice and eSign through a typed SDK.

This is not driven by one internal consumer; it is a public, community-facing library. The .NET SDK keeps evolving in parallel — the TypeScript SDK is a sibling, not a replacement.

## 2. Goals

- Two npm packages, mirroring the two NuGet packages:
  - `@misaconnect/einvoice` — first (this design).
  - `@misaconnect/esign` — second (separate design).
- **Same feature list as .NET.** For eInvoice, all 10 client operations (see §6).
- **Same names as .NET**, translated to TS idiom (owner said "same names are fine"):
  - Methods: camelCase, `Async` suffix dropped (every method returns a Promise). `ListTemplatesAsync` → `listTemplates`, `IssueReplacementAsync` → `issueReplacement`.
  - Type / DTO names kept as-is (`InvoiceDto`, `SaveResultDto`, `MeInvoiceErrorCategory`, …).
  - **Property names on public types (owner decision): camelCase with acronyms preserved.** Rule: lowercase only the leading word; if the name starts with an acronym, lowercase that whole leading acronym; every later acronym keeps its original capitals. Examples: `IPTemplateID` → `ipTemplateID`, `VATAmountOC` → `vatAmountOC`, `TotalAmountWithoutVATOC` → `totalAmountWithoutVATOC`, `IsMoreVATRate` → `isMoreVATRate`, `AmountOC` → `amountOC`, `RefId` → `refId`. The public types mirror the .NET consumer shape in `Client/Dtos/Dtos.cs` (nested Buyer / Lines / Totals), not the wire shape; port the `Mapping/*Mapper.cs` classes to convert to and from MISA's verbatim wire fields. Where the rule is ambiguous, the builder picks a name and lists it in the package README's name table.
  - Wire field names sent to MISA kept verbatim per `docs/misa-api-reference/` (PascalCase where MISA uses it).
- Behavioural parity, not just HTTP wrappers: token acquisition/caching/401-retry, throttle retry, error-code mapping, client-side validation, template resolution, delete-message disambiguation, PII/secret-safe logging.
- Framework-agnostic: works in plain Node and in any framework with no adapter.

## 3. Non-goals

- Not mirroring the .NET internal architecture one-to-one (no DI container, no `IOptions`, no four separate packages per family).
- No browser support (credentials must never ship to a browser; Node only).
- No framework-specific adapters (NestJS module, etc.) in v1. Samples show integration instead.
- Not porting eSign in this design.
- Not fixing the .NET bugs listed in §9 — that is separate .NET work. The TS port simply does not reproduce them.

## 4. Anti-goals (would be a failure even if it "works")

- Logging secrets, tokens, or buyer PII (same rule as the .NET `CLAUDE.md` Logging section).
- Silent float drift that changes amounts sent to MISA.
- Wire shapes that diverge from MISA's documented API.
- Breaking the .NET build, tests, or published NuGet package contents during the restructure.
- Unit tests that touch the network.

## 5. Repo restructure (decided by owner)

Owner decision: keep everything under `src/`, `tests/`, `samples/`, split each by language. **Do not** create a top-level `ts/` folder.

Target layout:

```
src/
  dotnet/
    MisaConnect.EInvoice.Domain/ … (all 8 current projects, unchanged inside)
  typescript/
    einvoice/        → @misaconnect/einvoice  (tests live inside the package)
    esign/           → @misaconnect/esign      (later)
tests/
  dotnet/
    MisaConnect.EInvoice.UnitTests/ … (all 5 current test projects)
samples/
  dotnet/
    MisaConnect.Samples.Api/, MisaConnect.Samples.Console/
  typescript/
    express/, nestjs/   (eInvoice usage examples)
package.json         (root; npm workspaces: "src/typescript/*", "samples/typescript/*")
```

The restructure is **its own step, committed before any TypeScript code**. Known things that must change (verified in repo):

- `MisaConnect.slnx` project paths.
- `tests/*` csproj `ProjectReference` paths: `..\..\src\X` becomes `..\..\..\src\dotnet\X`.
- Both IntegrationTests csproj files reference `..\..\samples\MisaConnect.Samples.Api\…` → `..\..\..\samples\dotnet\MisaConnect.Samples.Api\…`.
- `src/*Client.csproj` icon reference `..\..\icon.png` — it is guarded by `Condition="Exists(...)"`, so a missed update **silently drops the icon** from the NuGet package. Must become `..\..\..\icon.png`.
- `.github/workflows/ci.yml`, `integration.yml`, `release.yml` — hard-coded `tests/…` and `src/…` paths.
- Docs: `CLAUDE.md`, `CONTRIBUTING.md`, `docs/architecture.md`, `README.md`, and any `docs/einvoice/*` paths.
- `Directory.Build.props` / `Directory.Packages.props` / `global.json` / `nuget.config` stay at repo root (MSBuild walks up, so they still apply).

Done when: `dotnet build MisaConnect.slnx`, all four test projects, and `dotnet format --verify-no-changes` pass; packing both Client projects yields packages with the same file list as before (including `icon.png` and `README.md`).

## 6. `@misaconnect/einvoice` — public surface

Entry point (replaces `MisaEInvoiceClientFactory.Create` + `AddMisaConnectEInvoice`):

```ts
const client = createMisaEInvoiceClient({
  environment: 'sandbox' | 'production',   // default 'sandbox'
  baseUrl, taxCode, userName, password, appId, // all required
  delete: { includeRawErrorMessage: false },
  // optional swappable collaborators (the .NET Application ports):
  tokenCache?, refIdGenerator?, clock?, validator?,
  correlationId?, templateResolver?, logger?, fetch?,
});
```

Interface `IMisaEInvoiceClient` (name kept) with methods mirroring .NET:

| .NET | TS | MISA call |
|---|---|---|
| `ListTemplatesAsync` | `listTemplates(withCode = true, opts?)` | `POST webapp/templates` |
| `PreviewAsync` | `preview(invoice, withCode = true, opts?)` | `POST webapp/preview` |
| `SaveDraftAsync` | `saveDraft(invoices, withCode = true, opts?)` | `POST webapp/insert` |
| `GetDraftPdfAsync` | `getDraftPdf(refId, withCode = true, opts?)` | `GET webapp/viewrefid` |
| `DeleteDraftAsync` | `deleteDraft(refId, invoiceWithCode, opts?)` | `DELETE webapp/delete` |
| `LookupByRefIdAsync` | `lookupByRefId(refIds, withCode, opts?)` | `POST webapp/getlist` (chunks of 50) |
| `LookupStandardAsync` | `lookupStandard(request, withCode, opts?)` | `POST webapp/paging` |
| `LookupCalculatingAsync` | `lookupCalculating(request, withCode, opts?)` | `POST webapp/paging/calculating` |
| `IssueReplacementAsync` | `issueReplacement(request, opts?)` | `POST webapp/insert`, `EInvoiceStatus="3"` |
| `IssueAdjustmentAsync` | `issueAdjustment(request, opts?)` | `POST webapp/insert`, `EInvoiceStatus="4"` |

`opts` is `{ signal?: AbortSignal; correlationId?: string }` — `signal` replaces `CancellationToken`.

The .NET source of truth for behaviour is `src/dotnet/MisaConnect.EInvoice.*` (paths after restructure). The port must match it on:

- **Auth**: `POST webapp/token` with `taxcode = "{TaxCode}-{AppId}"`; token cached by wire taxcode, 14 days minus 1h; `Authorization: Bearer` + `taxcode` headers; on 401 evict, re-acquire, retry once; second 401 → `Authentication` error.
- **Retry**: one retry on 429 / 5xx / timeout / connection failure after 1250ms + 0–500ms jitter; then `MisaThrottled` / `MisaUnavailable` / `TransportFailed`. Caller abort is never retried or wrapped.
- **Envelope**: `{success, data: stringified JSON | raw array, error, errorCode: string | string[]}` including the insert case where the code is inside the stringified `error`.
- **Errors**: `MeInvoiceException` (extends `Error`) with `category`, `rawErrorCode`, `field`, `component`, `refId`, `failures`; same 16 categories; same exact-code table and 3 prefix rules from `MeInvoiceErrorMapper.cs`; unknown → `MisaUnknown`.
- **Result types** (no throw): save results per invoice in input order, `DeleteDraftOutcome`, lookup outcomes — same statuses as .NET.
- **Validation**: all rules in `InvoiceValidator.cs` and the paged-lookup / amendment rules, run before any HTTP call.
- **Template resolution** and **delete disambiguation** (Vietnamese phrases, `ARCH-DEL-001` warning) as in .NET.
- **Options validation** at `createMisaEInvoiceClient` time: https absolute `baseUrl` whose host matches the environment (`testapi.meinvoice.vn` / `api.meinvoice.vn`); missing required fields throw a `Configuration` error.
- **Logging**: same fields as `MeInvoiceCallLogger`; scrubber rules from `MeInvoiceLogScrubber.cs`. Default logger is a no-op. The `logger` option is `{ debug, info, warn, error }(message: string, fields?: Record<string, unknown>)` — matches `console` and winston directly. pino's argument order is `(fields, message)`, so the package exports a tiny `fromPino(pinoLogger)` adapter (no pino dependency, structural typing only) and the README shows it. No runtime logger-type detection. *(Main agent's call.)*

## 7. Type mapping decisions

- **Money (`decimal`)**: type `Money = number | string`. Zero runtime dependencies, no decimal library.
  - A `string` must match `^-?\d+(\.\d+)?$` (validated). It is written to the request body **verbatim as a JSON number literal** (custom serialization, no `Number()` conversion), so a caller who computes amounts with their own decimal library never loses precision.
  - A `number` is serialized as-is.
  - The SDK itself does no money arithmetic except the ±0.5 discount-sum tolerance check, which tolerates float error.
  - The README warns that foreign-currency amounts (USD cents, exchange-rate multiplication) should be computed with a decimal library and passed as strings, because e-invoices are tax documents and a few-đồng drift can get them rejected or force a replacement invoice.
  - Response amounts: returned as `number` (read-only display). *(Main agent's call — added after a reviewer flagged float drift on foreign-currency invoices.)*
- **`DateOnly`** → `string` in `yyyy-MM-dd` (validated); also accept `Date` and format it in local-calendar terms. **`DateTimeOffset`** → ISO-8601 string.
- **`byte[]`** (PDF content) → `Uint8Array` (Node `Buffer` is a subclass, so callers can use either).
- **C# records / discriminated unions** → TS interfaces + string-literal unions with a `kind`/`status` discriminator.
- **Enums** → string-literal union types plus a frozen const object of the same name, so `MeInvoiceErrorCategory.Validation` still reads like .NET.
- Nulls omitted from request bodies (matches `System.Text.Json` settings).

## 8. Tooling and package shape

- TypeScript `strict`, Node ≥ 20, built-in `fetch` (injectable via `fetch` option for tests/proxies). Zero runtime dependencies.
- Build: tsup → ESM + CJS + `.d.ts`, `exports` map in `package.json`.
- Test: Vitest. Three layers mirroring .NET:
  - unit tests (no network, < 30s);
  - integration tests against an in-process fake MISA server (port of `tests/dotnet/MisaConnect.EInvoice.IntegrationTests/MisaFake/`);
  - sandbox tests that skip cleanly unless `MISACONNECT_SANDBOX_*` env vars are set (same vars as .NET).
- Lint/format: ESLint + Prettier; lint errors fail CI (parity with `TreatWarningsAsErrors`).
- Internal folders: `src/domain`, `src/application`, `src/infrastructure`, `src/index.ts` (public exports only). Lighter than .NET; same dependency direction.

## 9. Known .NET defects — do not reproduce

Found during inventory; fix in .NET separately, not in this work. **All four fixed in .NET by Spec Kit slice `specs/009-fix-einvoice-inventory-defects/` (2026-11-04)**; the TS drift guard now expects no category deviations.

1. `MisaEInvoiceClientFactory.Create(options)` drops `Delete.IncludeRawErrorMessage`.
2. Call logger logs customer tax code, contradicting the scrubber's rule. TS: do not log it.
3. Batch-size MISA codes map to `Validation` instead of `BatchOrSize`. TS: map to `BatchOrSize`.
4. `docs/architecture.md` misdescribes `ICorrelationIdAccessor` registration (docs only).

## 10. Versioning and release

- npm versions independent from NuGet. Start at `0.1.0`; `1.0.0` once all 10 operations pass against the MISA sandbox.
- **No monorepo tool (Nx/Turborepo) — owner decision.** CI speed comes from path-filtered jobs instead. Nx (`@nx/dotnet`) can be added later if CI becomes slow; the `src/dotnet` / `src/typescript` layout keeps that door open.
- GitHub Actions, path-filtered:
  - Existing .NET `ci.yml` / `integration.yml` gain `paths:` filters (`src/dotnet/**`, `tests/dotnet/**`, `samples/dotnet/**`, root MSBuild files, the workflow file itself) so TS-only changes skip them.
  - New TS workflow runs build + lint + unit + fake-server tests, per package (einvoice job filtered on `src/typescript/einvoice/**`, esign job on `src/typescript/esign/**`; shared root files `package.json`, lockfile, tsconfig base, the workflow file trigger both).
  - If branch protection requires these checks, path-skipped required checks must not block merges (use a always-running summary job or `dorny/paths-filter`-style in-job filtering) — builder's call on mechanism.
- Publish on tag `einvoice-ts-v*` (must not match the existing .NET `v*` trigger in `release.yml`), using npm provenance.
- npm org `@misaconnect` is being created by the owner (npm user `chukhali`); must exist before first publish.
- Docs: TS section in root `README.md` supported-operations table, a package `README.md`, and `CHANGELOG.md` entries (builder's call: separate `src/typescript/einvoice/CHANGELOG.md`).

## 11. Left to the builder

- Exact internal module split, helper naming, and how the fake server is implemented (node:http vs. a lightweight lib, as a dev dependency only).
- Whether the express/nestjs samples land with eInvoice 0.1.0 or just after.
- tsconfig/eslint presets.

## 11a. Process and the project constitution

Owner decision: the TypeScript work uses the superpowers flow (this design → plan → implementation), **not** Spec Kit slices — Spec Kit is too heavy for this.

`.specify/memory/constitution.md` is currently written as a .NET-only document (Principle V requires a `specs/NNN-name/` slice for every change; Principles I/II/VII name .NET/NuGet specifics). So that reviewers don't block the TS PRs, the first commit of the work amends it *(main agent's call on wording; owner reviews it in the PR)*:

- Scope the .NET-specific principles to the .NET packages; state the TS equivalents (zero-runtime-dependency domain, framework-agnostic entry point, npm semver per package).
- Principle V: TypeScript packages may use a superpowers design doc under `docs/superpowers/specs/` instead of a Spec Kit slice. .NET work is unchanged.
- Sync `CLAUDE.md` and `CONTRIBUTING.md` (paths after restructure, TS build/test commands, the process exception).

## 12. Order of work

0. Constitution amendment + `CLAUDE.md` / `CONTRIBUTING.md` sync (§11a).
1. Repo restructure (§5), verified green, separate commit.
2. TS workspace foundation (root `package.json`, tooling, CI workflow).
3. `@misaconnect/einvoice`: domain types & errors → validation → HTTP/auth/retry → each operation (TDD) → fake-server integration tests → sandbox tests → README/samples.
4. Publish `0.1.0`.
5. Brainstorm + design `@misaconnect/esign`.

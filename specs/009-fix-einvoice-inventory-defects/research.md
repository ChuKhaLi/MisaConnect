# Research: Fix MisaConnect.EInvoice inventory defects

All four defects were verified against `main` (2026-11-04). No open NEEDS CLARIFICATION items.

## R1 — How the factory should carry options over

- **Decision**: In `Create(MisaEInvoiceOptions options)`, copy every top-level property and copy `Delete` by value (`new MisaEInvoiceDeleteOptions { IncludeRawErrorMessage = options.Delete.IncludeRawErrorMessage }`). Add a reflection-based unit test: set every public settable property (recursing into nested option classes) to a non-default value, build via the factory, resolve `IOptions<MisaEInvoiceOptions>` from the built provider, and assert equality property by property.
- **Rationale**: The bug happened because the copy is hand-written; a reflection test turns any future omission into a test failure (FR-002). Copying by value avoids sharing the caller's mutable `Delete` instance (edge case in spec).
- **Alternatives considered**: (a) Pass the caller's instance through `Options.Create(options)` — changes how validation and DI wiring work (`AddEInvoiceClient(configure)` is the single wiring path), larger blast radius. (b) A generic deep-copy helper — overkill for two option classes.
- **Test reachability**: the factory returns only `IMisaEInvoiceClient`. The test needs the resolved options. Approach: test the copy step in isolation by extracting it into an `internal static void CopyTo(MisaEInvoiceOptions source, MisaEInvoiceOptions target)` on the factory, visible to UnitTests via the existing `InternalsVisibleTo` (verify; add if missing). Behaviour of `Create` is unchanged apart from the fix.

## R2 — Removing the tax code from call logs

- **Decision**: Delete the `customerTaxCode={TaxCode}` segment and its argument from every template in `MeInvoiceCallLogger` (both `Run` overloads: started, completed-success, completed-error, completed-unexpected; with and without change reason). Drop the `taxCode` locals and the XML doc mention.
- **Rationale**: FR-003/FR-004. Principle VIII + the scrubber's own `taxcode` rule. The TS SDK already omits it.
- **Alternatives considered**: Masking (e.g. last 3 digits) — still partially identifying, and no consumer need for it was identified; correlation ID already identifies the call.
- **Test**: capture log entries with a list-backed `ILogger`, assert neither `FormattedMessage` nor any structured `KeyValuePair` contains the configured tax code, for success, MISA error, and unexpected exception paths. Existing log-shape tests keep asserting the other fields (FR-004).

## R3 — Batch-size category and version impact

- **Decision**: Mapper: `InvoiceQuantityTooLarge`, `XMLTooLong` → `BatchOrSize`. `SaveDraftInvoices`: count > 30 → `BatchOrSize`, raw `InvoiceQuantityTooLarge`, failure `FR-030` kept; count == 0 → `Validation`, raw `ValidationFailed`, failure message "Batch is empty; submit between 1 and 30 invoices." (rule id `FR-030` kept for both).
- **Rationale**: `BatchOrSize` exists for exactly this and is documented; Validation hid an actionable condition (split and retry). Splitting the empty case fixes a misleading message and matches TS.
- **Version recommendation**: ship in the next **minor** of `MisaConnect.EInvoice` rather than a patch. Strictly it is a bug fix (Principle VII allows a patch), but integrators that `catch` on `Validation` for oversize batches will see a different category; a minor bump plus an explicit "Behaviour change" CHANGELOG note is the safer signal. Final call at release.
- **Alternatives considered**: Keep `Validation` and document — leaves the dedicated category unused and keeps the .NET/TS divergence.
- **Downstream**: sample API maps unknown/other categories to 400 (`InvoiceEndpoints.StatusCodeForCategory` default arm), so `POST /api/invoices/draft` with 31 items still returns 400 with `InvoiceQuantityTooLarge` + `FR-030` — existing integration test stays green unchanged.

## R4 — Correlation ID docs

- **Decision**: In both collaborator tables in `docs/architecture.md`, replace "(you must register)" with "`LibraryCorrelationIdAccessor` (internal; generates a new ID per scope)" and keep the override note. Check `LibraryCorrelationIdAccessor` behaviour before writing the description.
- **Found while implementing (2026-11-04)**: not only `ICorrelationIdAccessor` — every EInvoice default is registered with `Add…` (no `TryAdd…`), while ESign uses `TryAdd…` throughout. The EInvoice table's lead sentence ("register before `AddMisaConnectEInvoice`") was therefore wrong for all ports: a registration made before the call is overridden. Docs now say "after" for EInvoice and keep "before" for ESign.
- **Out of scope**: switching EInvoice to `TryAdd…` (would make both orders work and match ESign, but changes DI behaviour for anyone relying on last-wins). Candidate for a later slice.

## R5 — TypeScript sync

- **Decision**: `BATCH_OR_SIZE_DEVIATIONS` in `src/typescript/einvoice/test/unit/error-mapper.test.ts` becomes empty (or the deviation logic is removed); README "Differences from the .NET SDK" drops the batch-size bullet's first sentence, keeping the TS-only details that still differ (empty batch code is now identical, so that clause goes too). The comment in `src/typescript/einvoice/src/application/error-mapper.ts` ("except … spec 9.3 deviation") is updated. No npm release needed for a test/docs change; it rides the next one.

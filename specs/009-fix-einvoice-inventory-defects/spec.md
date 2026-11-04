# Feature Specification: Fix MisaConnect.EInvoice inventory defects

**Feature Branch**: `009-fix-einvoice-inventory-defects`
**Created**: 2026-11-04
**Status**: Draft
**Input**: User description: "Fix four verified MisaConnect.EInvoice (.NET) defects found during the TypeScript SDK inventory (docs/superpowers/specs/2026-11-02-typescript-sdk-design.md §9): factory drops the delete options; call logger logs the account tax code; batch-size error codes map to Validation instead of BatchOrSize; docs/architecture.md misdescribes ICorrelationIdAccessor registration."

All four defects were re-verified against `main` on 2026-11-04 before this spec was written.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Factory-built clients honour the raw-error opt-in (Priority: P1)

An integrator who builds the client with `MisaEInvoiceClientFactory.Create(options)` (no generic host) and sets `options.Delete.IncludeRawErrorMessage = true` expects delete and lookup failures to carry MISA's raw error message, exactly as a DI-built client does. Today the factory copies only the six connection settings, so the opt-in is silently ignored.

**Why this priority**: A documented option that silently does nothing; integrators debugging delete/lookup failures get no message and have no way to tell why.

**Independent Test**: Build one client with the factory and one with DI from the same options object; every option value (including nested delete options) is identical in both.

**Acceptance Scenarios**:

1. **Given** options with `Delete.IncludeRawErrorMessage = true`, **When** a client is built with `MisaEInvoiceClientFactory.Create(options)` and a delete fails at MISA, **Then** the failure carries MISA's raw error message.
2. **Given** options with `Delete.IncludeRawErrorMessage = false` (default), **When** the same delete fails, **Then** no raw message is surfaced (unchanged behaviour).
3. **Given** a new public option is added to `MisaEInvoiceOptions` in future, **When** the factory does not carry it over, **Then** a unit test fails.

---

### User Story 2 - The account tax code never appears in SDK logs (Priority: P1)

An operator shipping SDK logs to a central log store expects the SDK's structured call logs to contain no value its own scrubber classes as sensitive. Today every call's start and completion log line contains `customerTaxCode=<configured tax code>`.

**Why this priority**: Privacy/compliance. The project rule is "never log secrets, tokens, or PII", and the SDK's scrubber treats tax codes as sensitive, so the logger contradicts the SDK's own policy on every call.

**Independent Test**: Run every operation with a capturing logger; no log entry (message or structured property) contains the configured tax code.

**Acceptance Scenarios**:

1. **Given** a configured tax code, **When** any operation succeeds, fails with a MISA error, or fails unexpectedly, **Then** neither the start nor the completion log entry contains the tax code, as text or as a structured property.
2. **Given** existing log consumers, **When** they read call logs, **Then** all other fields (endpoint, correlation ID, outcome, error category, duration, RefId label, template ID, statuses) are still present.

---

### User Story 3 - Batch-size limits report the dedicated category (Priority: P2)

An integrator who handles errors by category expects "too many invoices" and "XML too long" to arrive as `BatchOrSize`, the category that exists for exactly this, so they can split the batch and retry. Today both MISA codes, and the SDK's own client-side check for a batch of more than 30 invoices, report `Validation`, which is indistinguishable from bad invoice data.

**Why this priority**: Correctness of the error model. It is a deliberate behaviour change for anyone catching `Validation`, so it ships with an explicit CHANGELOG note.

**Independent Test**: Map each code through the error mapper, and submit a 31-invoice batch; each reports `BatchOrSize`.

**Acceptance Scenarios**:

1. **Given** MISA returns `InvoiceQuantityTooLarge` or `XMLTooLong` (any letter case), **When** the SDK maps it, **Then** the category is `BatchOrSize` and the raw code is preserved.
2. **Given** a save-draft batch of more than 30 invoices, **When** it is submitted, **Then** the SDK throws `BatchOrSize` with raw code `InvoiceQuantityTooLarge` before any network call.
3. **Given** an empty save-draft batch, **When** it is submitted, **Then** the SDK throws `Validation` with raw code `ValidationFailed` and a message saying the batch is empty (not "exceeds the limit"), before any network call.
4. **Given** the TypeScript SDK's drift-guard test against the .NET mapper, **When** it runs, **Then** it passes with no documented category deviations left.

---

### User Story 4 - Architecture docs describe correlation IDs correctly (Priority: P3)

A contributor reading `docs/architecture.md` learns that `ICorrelationIdAccessor` has a built-in default and that registering their own is optional, instead of being told they must register one.

**Why this priority**: Docs only; misleads contributors but breaks nothing.

**Independent Test**: Read the two collaborator tables in `docs/architecture.md`; both describe the default and how to override it.

**Acceptance Scenarios**:

1. **Given** the collaborator tables, **When** a reader looks up `ICorrelationIdAccessor`, **Then** the default column names the built-in library accessor instead of "(you must register)".

### Edge Cases

- Factory called with options whose `Delete` object is replaced by a new instance → the copy carries the values, not a shared reference that later mutation could change.
- A failure log entry that includes a scrubbed MISA error message → the tax code is absent there too (scrubber already removes `taxcode` JSON fields; the logger must not add it back).
- Exactly 30 invoices → still accepted (limit is inclusive).
- Mixed-case MISA codes (`xmltoolong`) → map like the canonical spelling (mapper is case-insensitive today; keep it so).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `MisaEInvoiceClientFactory.Create(MisaEInvoiceOptions)` MUST produce a client configured with every public option value of the input, including all nested delete options.
- **FR-002**: A unit test MUST fail when a public settable property of `MisaEInvoiceOptions` or its nested option types is not carried over by the factory.
- **FR-003**: SDK call log entries (start and completion, every outcome) MUST NOT contain the configured tax code, either as message text or as a structured property.
- **FR-004**: Removing the tax code MUST NOT remove or rename any other call-log field.
- **FR-005**: MISA error codes `InvoiceQuantityTooLarge` and `XMLTooLong` MUST map to `BatchOrSize`, case-insensitively, with the raw code preserved.
- **FR-006**: A save-draft batch larger than the 30-invoice limit MUST be rejected before any network call with `BatchOrSize` and raw code `InvoiceQuantityTooLarge`.
- **FR-007**: An empty save-draft batch MUST be rejected before any network call with `Validation`, raw code `ValidationFailed`, and a message stating the batch is empty.
- **FR-008**: `docs/architecture.md` MUST describe `ICorrelationIdAccessor` as having a built-in default that integrators may override.
- **FR-009**: The TypeScript drift-guard test against the .NET error mapper MUST pass with an empty deviation list, and the TypeScript README's "Differences from the .NET SDK" MUST drop the batch-size bullet that no longer applies.
- **FR-010**: `CHANGELOG.md` `[Unreleased]` MUST list all four fixes and call out FR-005–FR-007 as an error-category behaviour change.

### Key Entities

- **Client options**: the connection settings plus nested delete options that configure one client.
- **Call log entry**: one structured start or completion record per SDK call.
- **Error category**: the classification integrators branch on (`Validation`, `BatchOrSize`, …), plus the preserved raw MISA code.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of public option values round-trip through the factory (verified by an automated test that enumerates them).
- **SC-002**: 0 SDK log entries across all ten operations and all outcomes contain the configured tax code.
- **SC-003**: Both batch-size codes and the client-side over-limit check report `BatchOrSize` in 100% of cases; an empty batch reports `Validation` with an accurate message.
- **SC-004**: The existing .NET unit, integration and sandbox suites and the TypeScript suite all still pass, with only the tests that asserted the old defective behaviour updated.

## Assumptions

- Only `MisaConnect.EInvoice` packages change; `MisaConnect.ESign` is untouched. The `AddScoped` (EInvoice) vs `TryAddScoped` (ESign) registration inconsistency for `ICorrelationIdAccessor` is noted but out of scope.
- The logged tax code is the integrator's own account tax code (`Misa:EInvoice:TaxCode`), not buyer data; it is still removed because the SDK's scrubber classifies tax codes as sensitive and the TypeScript SDK never logs it.
- The empty-batch raw code `ValidationFailed` matches the TypeScript SDK. It is a change from today's misleading `InvoiceQuantityTooLarge`.
- Version impact is decided at release time; FR-005–FR-007 change observable categories, so the release notes treat them as a deliberate behaviour change.
- No MISA wire format changes.

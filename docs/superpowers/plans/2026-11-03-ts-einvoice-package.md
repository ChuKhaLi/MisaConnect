# `@misaconnect/einvoice` Implementation Plan (Plan 2 of 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port all 10 MisaConnect.EInvoice operations to the `@misaconnect/einvoice` npm package with behavioural parity (auth, retry, error mapping, validation, template resolution, delete disambiguation, safe logging) and publish `0.1.0`.

**Architecture:** One package under `src/typescript/einvoice` with `src/domain` (types, errors — no imports), `src/application` (validation, error mapping, use cases, ports — imports only domain), `src/infrastructure` (fetch transport, wire mapping, defaults, logging). `createMisaEInvoiceClient(options)` in `src/client.ts` wires defaults; `src/index.ts` is the only public surface. Tests: unit (no network), integration against an in-process fake MISA server (`node:http`), sandbox (skips without env vars).

**Tech Stack:** TypeScript ~6.0 strict, Node ≥ 20 runtime (`fetch`, `AbortSignal`, `crypto.randomUUID`), tsup, Vitest, `node:http` for the fake server. Zero runtime dependencies.

**Spec:** `docs/superpowers/specs/2026-11-02-typescript-sdk-design.md` (§2, §6, §7, §9, §10). The .NET source under `src/dotnet/MisaConnect.EInvoice.*` is the behavioural source of truth the spec points to; every "port X.cs" step means *same inputs → same outputs*, translated to TS names.

## Global Constraints

- Public names: methods camelCase without `Async`; type names kept from .NET (`InvoiceDto`, `SaveResultDto`, `MeInvoiceErrorCategory`, …); **property names: lowercase only the leading word/acronym, keep later acronyms** — `IPTemplateID`→`ipTemplateID`, `VATAmountOC`→`vatAmountOC`, `TotalAmountWithoutVATOC`→`totalAmountWithoutVATOC`, `IsMoreVATRate`→`isMoreVATRate`, `AmountOC`→`amountOC`, `RefId`→`refId`, `OrgRefID`→`orgRefID`, `InvSeries`→`invSeries`.
- Wire field names sent to MISA are verbatim from `src/dotnet/MisaConnect.EInvoice.Infrastructure/MeInvoice/Wire/*.cs` and `docs/misa-api-reference/`.
- Enums: string-literal union type + frozen const object of the same name (`MeInvoiceErrorCategory.Validation === 'Validation'`).
- Money: `type Money = number | string`; strings must match `^-?\d+(\.\d+)?$` and are written to JSON **verbatim as number literals**; response amounts are `number`.
- `DateOnly` → `string` `yyyy-MM-dd` (also accept `Date`, formatted from its local calendar fields); `DateTimeOffset` → ISO-8601 string; `byte[]` → `Uint8Array`. Nulls/undefined omitted from request bodies.
- Every method takes optional last arg `opts?: { signal?: AbortSignal; correlationId?: string }`. Caller abort propagates as the native `AbortError`, never retried or wrapped.
- Logger interface `{ debug, info, warn, error }(message: string, fields?: Record<string, unknown>)`; default no-op; export `fromPino(pino)`.
- Never log: tokens, password, appId, taxcode headers, buyer name/address/contact, line content, amounts, **customer tax code** (spec §9.2), raw MISA messages unless `delete.includeRawErrorMessage`.
- Do not reproduce spec §9: `delete.includeRawErrorMessage` is honoured on every path; batch-size codes `InvoiceQuantityTooLarge`, `XMLTooLong` map to `BatchOrSize`.
- ESLint layer rule (constitution v1.2.0 Principle I) must pass: no `node:*` in domain/application.
- Validation field paths use TS property names (`buyer.taxCode`, `lines[0].description`, `totals.totalDiscountAmountOC`, `invoices[2].currency`, `$batch.count`); rule IDs and message text match .NET.
- Unit tests < 30 s, no network. Sandbox env vars: `MISACONNECT_SANDBOX_TAXCODE`, `_USERNAME`, `_PASSWORD`, `_APPID` (same as .NET).
- Package stays `"private": true` until Task 15.

## Review Focus

1. **Money strings with many decimals / huge integers** (`"12345678901234567890.123456"`) — must reach the wire byte-for-byte. Test in Task 2.
2. **Concurrent first calls** — 10 parallel calls on a cold client must acquire **one** token, not ten. Test in Task 6.
3. **AbortSignal during the retry backoff** — aborting while waiting the 1250–1750 ms backoff rejects promptly with `AbortError`, no second request. Test in Task 6.
4. **Non-JSON / HTML error bodies** (gateway 502 page, empty 200 body) — surface as `MisaUnavailable` / `MisaUnknown "EmptyResponse"`, never a raw `SyntaxError`. Test in Task 6.
5. **Emoji in buyer/line text written as surrogate pairs vs. BMP symbols** (`"Cà phê ☕"` U+2615 is BMP and allowed; `"😀"` is rejected). Test in Task 4.

---

## File structure (all under `src/typescript/einvoice/`)

| File | Responsibility |
|---|---|
| `src/domain/errors.ts` | `MeInvoiceErrorCategory`, `ValidationFailure`, `MeInvoiceException` |
| `src/domain/dtos.ts` | every public DTO type from `Client/Dtos/Dtos.cs` + `Money`, `DateOnly` aliases |
| `src/domain/statuses.ts` | `DeleteDraftStatus`, `LookupStatus`, `LookupBatchStatus`, `SaveOutcomeDto`, `InvoiceStatus`, `DeleteDraftOutcome` |
| `src/domain/vat-rate.ts` | parse/format VAT names (port `Domain/Invoices/VatRate.cs`) |
| `src/application/ports.ts` | `TokenCache`, `RefIdGenerator`, `Clock`, `InvoiceValidator`, `TemplateResolver`, `Logger`, `MeInvoiceTransport` |
| `src/application/error-mapper.ts` | `mapErrorCode` (port `MeInvoiceErrorMapper.cs`) |
| `src/application/delete-disambiguator.ts` | port `InvalidTransactionDisambiguator.cs` |
| `src/application/invoice-validator.ts` | port `InvoiceValidator.cs` + paged/amendment request rules |
| `src/application/template-resolver.ts` | port `TemplateResolver.cs` |
| `src/application/status-mapper.ts` | port `InvoiceStatusMapper.cs` |
| `src/application/use-cases/*.ts` | one file per operation + `ensure-access-token.ts` |
| `src/infrastructure/json.ts` | request serializer (Money verbatim, null omission, dates) |
| `src/infrastructure/options.ts` | `MisaEInvoiceOptions`, `validateOptions` |
| `src/infrastructure/transport.ts` | fetch + retry + bearer/401 + envelope reading |
| `src/infrastructure/envelope.ts` | envelope types + `extractErrorCode`/`parseInsertErrors` |
| `src/infrastructure/wire/*.ts` | wire types + public↔wire mappers (port `Wire/*.cs`, `Mapping/*.cs`, `Client/Mapping/*.cs`) |
| `src/infrastructure/defaults.ts` | in-memory token cache, `randomUUID` RefId, system clock |
| `src/infrastructure/logging.ts` | `scrub`, `fromPino`, call-logging wrapper |
| `src/client.ts` | `IMisaEInvoiceClient`, `createMisaEInvoiceClient` |
| `test/fake/fake-misa-server.ts` | in-process fake (port `FakeMisaServer.cs` + preview/viewrefid) |
| `test/unit/**`, `test/integration/**`, `test/sandbox/**` | test layers |

---

### Task 1: Domain types and errors

**Files:** Create `src/domain/errors.ts`, `src/domain/dtos.ts`, `src/domain/statuses.ts`, `src/domain/vat-rate.ts`; Test `test/unit/domain.test.ts`; Modify `src/index.ts` (export them, remove `PACKAGE_NAME`), `test/package-exports.test.ts`, `scripts/smoke.*` (assert on `MeInvoiceErrorCategory.Validation` instead of `PACKAGE_NAME`).

**Interfaces — Produces:**
- `MeInvoiceErrorCategory` with the 16 values of `Domain/Errors/MeInvoiceErrorCategory.cs`, same order.
- `class MeInvoiceException extends Error { category; rawErrorCode?; field?; component?; refId?; failures? }`, `name = 'MeInvoiceException'`, `message = message ?? rawErrorCode ?? category`.
- `interface ValidationFailure { fieldPath: string; message: string; ruleId?: string }`.
- All DTOs of `Client/Dtos/Dtos.cs` with the naming rule; nullable .NET members → optional (`?:`); `InvoiceDto.customFields?: Record<number, string>`; `PdfDocumentDto { content: Uint8Array; contentType: string }`.
- Statuses: `DeleteDraftStatus` (9), `LookupStatus` (9), `LookupBatchStatus` (`Completed`,`Validation`), `SaveOutcomeDto` (`Success`,`Error`), `InvoiceStatus` (6) — values from `Application/Results/*.cs`, `Domain/Invoices/InvoiceStatus.cs`.
- `DeleteDraftOutcome { status: DeleteDraftStatus; rawErrorCode?; message?; field?; refId? }`.
- `parseVatRate(name: string): VatRate | undefined`, `type VatRate = { kind: 'KCT'|'KKKNT'|'0'|'5'|'8'|'10' } | { kind: 'KHAC'; rate: number }` (port `VatRate.cs` parsing incl. `KHAC:x%`).

- [ ] **Step 1: Failing tests** — `MeInvoiceErrorCategory` has exactly 16 keys equal to their values and is frozen; `new MeInvoiceException('Validation', { rawErrorCode: 'X' })` has `message 'X'`, `instanceof Error`, `name 'MeInvoiceException'`; `parseVatRate('KHAC:5.5%')` → `{kind:'KHAC', rate:5.5}`, `parseVatRate('10%')` per `VatRate.cs` accepted spellings, `parseVatRate('abc')` → `undefined`; a type-level test file (`expectTypeOf`) asserting `InvoiceLineDto['vatAmountOC']` is `Money | undefined` and `TemplateDto` has `ipTemplateID`, `isMoreVATRate`.
- [ ] **Step 2: Run** `npm test -w @misaconnect/einvoice -- domain` → FAIL (modules missing).
- [ ] **Step 3: Implement** the four domain files (no imports outside `src/domain`).
- [ ] **Step 4: Run** → PASS; `npm run lint && npm run typecheck` → exit 0.
- [ ] **Step 5: Commit** `feat(ts-einvoice): domain types, statuses and MeInvoiceException`

---

### Task 2: Request JSON serializer

**Files:** Create `src/infrastructure/json.ts`; Test `test/unit/json.test.ts`.

**Interfaces — Produces:** `serializeRequest(value: unknown): string` — like `JSON.stringify` but: drops `undefined`/`null` object members; a value wrapped by `money(x: Money)` (exported from `json.ts`, returns branded `{ __money: string }`) is emitted as a bare JSON number literal; throws `MeInvoiceException('Validation', { rawErrorCode: 'InvalidMoney', field })` for strings not matching `^-?\d+(\.\d+)?$` or non-finite numbers. `formatDateOnly(v: string | Date): string` (`yyyy-MM-dd`; rejects other string shapes with `Validation`).

- [ ] **Step 1: Failing tests** — `serializeRequest({a: money('12345678901234567890.123456')})` === `'{"a":12345678901234567890.123456}'` (Review Focus 1); `money(1.5)` → `1.5`; `{a: null, b: undefined, c: 1}` → `'{"c":1}'`; arrays keep order and drop nothing; `money('1e5')` throws `InvalidMoney`; `money(NaN)` throws; `formatDateOnly(new Date(2026, 10, 3))` → `'2026-11-03'`; `formatDateOnly('2026-11-03')` passes through; `formatDateOnly('03/11/2026')` throws.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** with a recursive writer (do not post-process `JSON.stringify` output with regex).
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(ts-einvoice): request serializer with verbatim money`

---

### Task 3: Error mapping and delete disambiguation

**Files:** Create `src/application/error-mapper.ts`, `src/application/delete-disambiguator.ts`; Test `test/unit/error-mapper.test.ts`, `test/unit/delete-disambiguator.test.ts`.

**Interfaces — Produces:**
- `mapErrorCode(raw: string | undefined, message?: string): { category; rawCode: string; detail?: string; field?: string; component?: string }` — exact table of `MeInvoiceErrorMapper.cs` `ExactMap` (case-insensitive), then `PrefixMap` (`InvoiceDetail_`→Validation, `RequireError_`→Validation, `StockInTaxCode_NotInfo_`→OtherWorkflow; tail split on `_` → field, component), else `MisaUnknown`; empty/undefined → `MisaUnknown` with `rawCode ''`.
- **Deviation (spec §9.3):** `InvoiceQuantityTooLarge`, `XMLTooLong` → `BatchOrSize`.
- `disambiguateInvalidTransaction(message: string | undefined): 'NotFound' | 'NotDeletable' | undefined` — port phrase table of `InvalidTransactionDisambiguator.cs` (Vietnamese, NFC-normalised, case-insensitive).

- [ ] **Step 1: Failing tests** — table-driven test with **every** key of the .NET `ExactMap` (copy all 54 entries as the test table; expected categories as in .NET except the two §9.3 rows → `BatchOrSize`); `'unauthorize'` (lower-case) → `Authentication`; `'InvoiceDetail_Quantity_Line3'` → Validation, field `Quantity`, component `Line3`; `'RequireError_'` → Validation, no field; `'Whatever'` → `MisaUnknown`, rawCode kept. Disambiguator: one test per phrase in the .NET file, plus the same phrase in NFD form → same result; unmatched → `undefined`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** both files.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(ts-einvoice): MISA error-code mapping and delete disambiguation`

---

### Task 4: Validation, template resolution, status mapping

**Files:** Create `src/application/invoice-validator.ts`, `src/application/template-resolver.ts`, `src/application/status-mapper.ts`, `src/application/ports.ts` (validator/resolver/clock/refId/tokenCache/logger port types only; transport port added in Task 6); Test `test/unit/invoice-validator.test.ts`, `test/unit/template-resolver.test.ts`, `test/unit/status-mapper.test.ts`.

**Interfaces — Produces:**
- `defaultInvoiceValidator: InvoiceValidator` with `validate(invoice: InvoiceDto): ValidationFailure[]`, `validateBatch(invoices: InvoiceDto[]): ValidationFailure[]` — every rule of `InvoiceValidator.cs` (FR-006/021/023/024/025/027/028/029/030), `MaxLinesPerInvoice = 200`, batch 1–30 (`BatchSubmission.cs`; empty batch also fails `$batch.count`), RefId max 64, tolerance 0.5.
- `validatePagedLookup(req: PagedLookupRequestDto): ValidationFailure[]` (`start ≥ 0`, `length` 1–100, `fromDate ≤ toDate`, `publishStatus ∈ {0,4,6,7}` — `PagedLookupRequest.cs`), `validateAmendment(req): ValidationFailure[]` (all 5 `originalRef` fields, non-empty `changeReason` — `OriginalInvoiceReference.cs`, `ReplacementRequest.cs`).
- `resolveTemplate(templates: TemplateDto[], ref?: TemplateRefDto): { kind: 'Resolved'; template } | { kind: 'AmbiguousTemplate' | 'NoActiveTemplate' | 'UnknownTemplate' }` (port `TemplateResolver.cs`; `inferWithCode(invSeries)` from its 2nd char `C`/`K`).
- `mapInvoiceStatus(eInvoiceStatus?: number, publishStatus?: number): InvoiceStatus` (port `InvoiceStatusMapper.cs`).

- [ ] **Step 1: Failing tests** — one test per rule ID with the .NET message text and TS field path, e.g. org buyer without tax code → `{fieldPath:'buyer.taxCode', ruleId:'FR-024'}`; 201 lines → `FR-030`; `'Cà phê ☕'` passes, `'😀'` in `lines[0].unitName` → `FR-028` (Review Focus 5); discount sum off by 0.5 passes, by 0.51 fails `FR-025`; `vatRateName 'KHAC:0%'` fails `FR-029`; batch of 31 → `$batch.count`; batch failures prefixed `invoices[i].`; paged `length 101` fails; resolver: no ref + 2 active → `AmbiguousTemplate`, ref series match case-insensitive → `Resolved`; status mapper truth table from the .NET contract test `tests/dotnet/MisaConnect.EInvoice.UnitTests/Contracts/StatusMappingContractTests.cs`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** the four files.
- [ ] **Step 4: Run** → PASS; lint passes (no `node:` imports in application).
- [ ] **Step 5: Commit** `feat(ts-einvoice): invoice validation, template resolution, status mapping`

---

### Task 5: Options, defaults, logging

**Files:** Create `src/infrastructure/options.ts`, `src/infrastructure/defaults.ts`, `src/infrastructure/logging.ts`; Test `test/unit/options.test.ts`, `test/unit/logging.test.ts`.

**Interfaces — Produces:**
- `interface MisaEInvoiceOptions { environment?: 'sandbox'|'production'; baseUrl; taxCode; userName; password; appId; delete?: { includeRawErrorMessage?: boolean }; tokenCache?; refIdGenerator?; clock?; validator?; templateResolver?; logger?; fetch?: typeof fetch; correlationId?: () => string }`.
- `validateOptions(o): ResolvedOptions` — throws `MeInvoiceException('Configuration', { rawErrorCode: 'InvalidOptions', failures })` listing every problem (port `MisaEInvoiceOptionsValidator.cs`: required strings, absolute `https:` URL, host `testapi.meinvoice.vn` for sandbox / `api.meinvoice.vn` for production); default environment `sandbox`; normalises trailing `/`.
- `createInMemoryTokenCache(): TokenCache` (`get(key)`, `set(key, token)`, `delete(key)`, all async), `uuidRefIdGenerator` (`crypto.randomUUID()`), `systemClock` (`now(): Date`).
- `scrub(fields): Record<string, unknown>` — `***` for keys matching (case-insensitive) `authorization|bearer|taxcode|password|token|appid|app_id|app-id` (port `MeInvoiceLogScrubber.cs`); `noopLogger`; `fromPino(p): Logger` mapping `(msg, fields)` → `p.level(fields ?? {}, msg)`.

- [ ] **Step 1: Failing tests** — missing `password` + `http://` URL → one exception whose `failures` has both; production env with sandbox host → fails; `{environment:'production', baseUrl:'https://api.meinvoice.vn/api/integration/'}` resolves with `baseUrl` ending `integration/`; `scrub({Password:'x', taxCode:'y', endpoint:'z'})` → `{Password:'***', taxCode:'***', endpoint:'z'}`; `fromPino` calls `info({a:1}, 'm')` for `info('m', {a:1})`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(ts-einvoice): options validation, defaults, log scrubbing`

---

### Task 6: Transport — token, bearer/401, retry, envelope

**Files:** Create `src/infrastructure/transport.ts`, `src/infrastructure/envelope.ts`, `src/application/use-cases/ensure-access-token.ts`; Modify `src/application/ports.ts` (add `MeInvoiceTransport`); Test `test/unit/transport.test.ts`, `test/unit/ensure-access-token.test.ts`.

**Interfaces — Produces:**
- `wireTaxCode(taxCode, appId) = \`${taxCode}-${appId}\`` (port `WireTaxCode.cs`).
- `ensureAccessToken(deps): Promise<string>` — cache key = wire taxcode; reuse while `expiresAt > now + 1h`; lifetime 14 days − 1 h; **single-flight**: concurrent misses share one in-flight acquisition (Review Focus 2).
- `createTransport(resolved, deps): MeInvoiceTransport` with `request<T>(method: 'GET'|'POST'|'DELETE', path: string, query: Record<string,string>, body?: unknown, opts?): Promise<Envelope<T>>`:
  - token route `webapp/token` bypasses bearer; body `{taxcode, username, password}`; reads `access_token` from stringified `data`; empty → `Authentication 'EmptyToken'`.
  - headers `Authorization: Bearer <t>`, `taxcode: <wire>`, `Content-Type: application/json`; body via `serializeRequest`.
  - 401 → evict, re-acquire, retry once; second 401 → `Authentication 'UnAuthorize'`.
  - retry (outer): one retry on 429 / 5xx / network error / timeout after `1250 + random(0..500)` ms via injectable `delay(ms, signal)`; then `MisaThrottled` (429) / `MisaUnavailable` (5xx) / `TransportFailed` (`'Timeout'` | `'ConnectionFailed'`); per-request timeout 100 s (parity with .NET default).
  - caller `signal` aborts fetch **and** the backoff wait; rejects with the signal's `AbortError` (Review Focus 3).
  - envelope: `{success, data, error, errorCode}`; `data` may be stringified JSON or a raw array (`MeInvoiceClient.cs` ~L330); non-JSON body → `MisaUnavailable` if status ≥ 500 else `MisaUnknown 'InvalidResponse'`; empty body → `MisaUnknown 'EmptyResponse'` (Review Focus 4).
  - `extractErrorCode(envelope)`, `parseInsertErrors(envelope.error)` — port `ExtractRawErrorCode`, `ParseErrorEntries` (`errorCode` string or string[]; insert code inside stringified `error` array).

- [ ] **Step 1: Failing tests** (fake `fetch` returning scripted `Response`s; fake `delay` recording calls):
  - `posts wire taxcode and caches token`: two calls → one `/webapp/token`.
  - `single flight`: `Promise.all` of 10 requests on cold cache → exactly 1 token request.
  - `refreshes on 401 once` / `second 401 throws Authentication UnAuthorize`.
  - `retries 503 once then MisaUnavailable`; `429 twice → MisaThrottled`; `fetch TypeError twice → TransportFailed ConnectionFailed`; delay called with a value in [1250, 1750].
  - `abort during backoff rejects with AbortError and sends no second request`.
  - `HTML 502 body → MisaUnavailable`; `empty 200 → MisaUnknown EmptyResponse`.
  - envelope: `data` stringified object, `data` raw array, `errorCode: ['A','B']` → `'A'`, insert error string `'[{"RefID":"r1","ErrorCode":"InvalidInvoiceData"}]'` parsed.
  - token `expiresAt = now + 59 min` → re-acquire; `now + 61 min` → reuse.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(ts-einvoice): transport with token cache, 401 refresh and throttle retry`

---

### Task 7: Wire mapping

**Files:** Create `src/infrastructure/wire/invoice.ts`, `src/infrastructure/wire/lookup.ts`, `src/infrastructure/wire/templates.ts`; Test `test/unit/wire-mapping.test.ts`.

**Interfaces — Produces:**
- `toWireInvoice(dto: InvoiceDto, refId: string, template: TemplateDto): WireInvoice` — port `Client/Mapping/DtoMapper.cs` + `Infrastructure/MeInvoice/Mapping/InvoiceDtoMapper.cs` + `VatRateMapper.cs`; money fields wrapped with `money()`; dates via `formatDateOnly` / ISO.
- `toWireAmendment(dto, kind: 'replacement'|'adjustment', refId, template)` — `EInvoiceStatus "3"`/`"4"` + `Org*` fields + `ChangeReason` (port `AmendmentDtoMapper.cs`, `MeInvoiceClient.ToWire`).
- `fromWireTemplate(w): TemplateDto`, `fromWireSnapshot(w): InvoiceSnapshotDto` (port `InvoiceLookupMapper.cs`, `LookupDtoMapper.cs`, status via `mapInvoiceStatus`).

- [ ] **Step 1: Failing tests** — golden test: the sample invoice from `tests/dotnet/MisaConnect.EInvoice.TestSupport/Fixtures/SampleInvoices.cs` expressed as a TS `InvoiceDto`, serialized with `serializeRequest(toWireInvoice(...))`, deep-equals a checked-in golden JSON `test/golden/sample-invoice.wire.json` produced once from the .NET mapper (Step 1a below); adjustment sets `EInvoiceStatus: "4"` and all five `Org*`; snapshot mapping of a wire lookup row yields `status 'Adjusted'` for `EInvoiceStatus 4`.
  - **Step 1a:** generate the golden file by adding a temporary `[Fact]` in `tests/dotnet/MisaConnect.EInvoice.UnitTests` that serializes `SampleInvoices` through the .NET mapper with `MeInvoiceJsonOptions` and writes the JSON; run it once; copy the output; delete the temporary test (do not commit it).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS (golden equal).
- [ ] **Step 5: Commit** `feat(ts-einvoice): wire mapping with .NET golden parity`

---

### Task 8: Fake MISA server

**Files:** Create `test/fake/fake-misa-server.ts`, `test/fake/fake-misa-server.test.ts`.

**Interfaces — Produces:** `startFakeMisa(): Promise<{ baseUrl: string; state: FakeState; close(): Promise<void> }>` on `127.0.0.1:0` via `node:http`. Routes and scripted behaviours ported from `tests/dotnet/MisaConnect.EInvoice.IntegrationTests/MisaFake/FakeMisaServer.cs` (token, templates, delete, getlist, paging, paging/calculating, insert) **plus** `POST /webapp/preview` and `GET /webapp/viewrefid` (base64 PDF `%PDF-1.4` stub). `state` lets tests script: next status codes (`state.queueStatus(503)`), error codes per RefID, stored drafts, request log (method, path, query, headers, body text).

> The integration client points at the fake through `baseUrl` + `fetch`; options validation would reject `http://127.0.0.1`, so `createMisaEInvoiceClient` accepts an internal `__unsafeAllowAnyBaseUrl` flag **not exported from `index.ts`** and used only by tests. *(Plan ruling: the .NET tests override the HttpClient base address the same way.)*

- [ ] **Step 1: Failing test** — starts, `POST /webapp/token` returns envelope with stringified `{access_token}`; insert then getlist returns the stored draft; queued 503 is served once.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `test(ts-einvoice): in-process fake MISA server`

---

### Task 9: Client + read operations (`listTemplates`, `preview`, `getDraftPdf`)

**Files:** Create `src/client.ts`, `src/application/use-cases/list-templates.ts`, `preview.ts`, `get-draft-pdf.ts`; Modify `src/index.ts`; Test `test/integration/read-ops.test.ts`.

**Interfaces — Produces:** `interface IMisaEInvoiceClient` with the 10 method signatures of spec §6 (all declared now; unimplemented ones throw `Error('not implemented')` until their task); `createMisaEInvoiceClient(options: MisaEInvoiceOptions): IMisaEInvoiceClient`. Every call logs one `info` line via the call logger: `{ endpoint, outcome, errorCategory?, durationMs, refId?, templateId?, correlationId }` (refId batches as `"{first}+N-more"`; never `customerTaxCode`).

- `listTemplates(withCode = true)` → active templates only (`?invoiceWithCode=…&isTicket=false`, body `{TaxCode, UserName, Password}` per `MeInvoiceClient.ListTemplatesAsync`).
- `preview(invoice, withCode = true)` → validate (throws `Validation` with `failures`), resolve template, `POST webapp/preview`, base64 → `Uint8Array`.
- `getDraftPdf(refId, withCode = true)` → `GET webapp/viewrefid?refid=`; error → mapped exception, default code `RefIdNotFound`, `refId` set.

- [ ] **Step 1: Failing tests** against the fake — list returns only `isActive` templates and query has `isTicket=false`; preview of an invalid invoice throws `Validation` with **no** HTTP request in the fake log; preview returns bytes starting `%PDF`; getDraftPdf unknown id → `ResourceNotFound` with `refId`; a captured logger's lines contain no `password`, no buyer name, no `customerTaxCode` key.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(ts-einvoice): client, listTemplates, preview, getDraftPdf`

---

### Task 10: `saveDraft`

**Files:** Create `src/application/use-cases/save-draft.ts`; Test `test/integration/save-draft.test.ts`.

Behaviour: port `SaveDraftInvoices.cs` — validate batch (throws on any failure, no request); assign `refIdGenerator()` when `refId` absent; resolve template once; `POST webapp/insert` with array body; results **in input order**, one `SaveResultDto` per invoice; per-RefID error from `parseInsertErrors` → `outcome 'Error'` with mapped `errorCategory`/`rawErrorCode`; invoice missing from response → `rawErrorCode 'NoEchoedResult'`, `MisaUnknown`.

- [ ] **Step 1: Failing tests** — 3 invoices, fake rejects the 2nd with `DuplicateInvoiceRefID` → results `[Success, Error(DuplicateOrUniqueness), Success]` in order; fake drops the 3rd from its echo → `NoEchoedResult`; 31 invoices → throws `Validation` (`$batch.count`), fake log empty; generated refIds are UUIDs.
- [ ] **Step 2–4:** Run FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(ts-einvoice): saveDraft`

---

### Task 11: `deleteDraft`

**Files:** Create `src/application/use-cases/delete-draft.ts`; Test `test/integration/delete-draft.test.ts`.

Behaviour: port `DeleteDraftInvoice.cs` — never throws for MISA errors, returns `DeleteDraftOutcome`; `InvalidTransactionID` + disambiguator → `NotFound` / `NotDeletable`, unmatched → `MisaUnknown` + `warn` log with code `ARCH-DEL-001`; transport categories map to the same-named statuses; `message` populated **only** when `delete.includeRawErrorMessage` (spec §9.1 — honoured on every construction path).

- [ ] **Step 1: Failing tests** — success → `Deleted`; "không tồn tại" → `NotFound`; "đã phát hành" → `NotDeletable`; unmatched phrase → `MisaUnknown` + one `ARCH-DEL-001` warn; `message` undefined by default and equal to the MISA text with the flag on; queued 503×2 → `MisaUnavailable` status (no throw).
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(ts-einvoice): deleteDraft`

---

### Task 12: Lookups (`lookupByRefId`, `lookupStandard`, `lookupCalculating`)

**Files:** Create `src/application/use-cases/lookup-by-ref-ids.ts`, `lookup-paged.ts`; Test `test/integration/lookups.test.ts`.

Behaviour: port `LookupByRefIds.cs` (chunks of 50, per-RefID `LookupOutcomeDto`, terminal categories — `Authentication`, `Configuration` — copied to all remaining chunks without further requests, invalid input → `LookupBatchOutcomeDto { status: 'Validation', reason }`), `LookupStandard.cs` / `LookupCalculating.cs` (validate request, `POST webapp/paging[/calculating]`, `PagedLookupResultDto { items, start, length, returnedCount }`); `message` gated by `includeRawErrorMessage`.

- [ ] **Step 1: Failing tests** — 120 refIds → 3 requests of 50/50/20, outcomes in input order; auth failure on chunk 1 → all 120 `AuthFailed`, only 1 getlist request; unknown id → `NotFound`; paged `length 0` → throws `Validation` without request; calculating route hit with the right body; snapshot `status` from status mapper.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(ts-einvoice): lookups by RefId and paged`

---

### Task 13: Amendments (`issueReplacement`, `issueAdjustment`)

**Files:** Create `src/application/use-cases/issue-amendment.ts`; Test `test/integration/amendments.test.ts`.

Behaviour: port `IssueReplacementInvoice.cs` / `IssueAdjustmentInvoice.cs` — validate invoice + amendment fields; new RefId; `POST webapp/insert` via `toWireAmendment`; `AmendmentResultDto` (with `orgRefId`); `changeReason` logged only with `includeRawErrorMessage`; HTTP status cases of `AmendmentHttpStatusTests.cs`.

- [ ] **Step 1: Failing tests** — replacement body has `EInvoiceStatus "3"` and the five `Org*` fields; adjustment `"4"`; `InvoiceCannotReplace` → `Error` / `Replacement`; missing `orgInvNo` → throws `Validation`, no request; logs contain no `changeReason` unless flag on.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(ts-einvoice): replacement and adjustment issuance`

---

### Task 14: Sandbox tests

**Files:** Create `test/sandbox/sandbox.test.ts`, `vitest.sandbox.config.ts`; Modify `package.json` (script `test:sandbox`).

Behaviour: `describe.skipIf(!hasSandboxEnv)` using the four `MISACONNECT_SANDBOX_*` vars and the sandbox base URL `https://testapi.meinvoice.vn/api/integration`; flows: token, listTemplates, saveDraft → getDraftPdf → lookupByRefId → deleteDraft (cleans up its own draft in `afterAll`). Not part of `npm test`.

- [ ] **Step 1:** Write tests. **Step 2:** `npm run test:sandbox -w @misaconnect/einvoice` without env → all skipped, exit 0. With env (ask the owner to run `! npm run test:sandbox -w @misaconnect/einvoice` with credentials) → pass.
- [ ] **Step 3: Commit** `test(ts-einvoice): sandbox flows (skip without credentials)`

---

### Task 15: Docs, samples, release `0.1.0`

**Files:** Modify `src/typescript/einvoice/package.json` (`version 0.1.0`, remove `private`, add `repository`, `keywords`, `publishConfig: { access: 'public', provenance: true }`), `src/typescript/einvoice/README.md` (install, quick start, all 10 methods, Money/strings guidance for foreign currency, `fromPino`, name table for acronym properties), root `README.md` (TS row in supported-operations table); Create `src/typescript/einvoice/CHANGELOG.md`, `samples/typescript/express/` (`package.json` private, `server.ts` with 2 routes), `samples/typescript/nestjs/` (module + provider factory), `.github/workflows/release-ts.yml` (tag `einvoice-ts-v*` → `npm ci`, test, `npm publish -w @misaconnect/einvoice --provenance`, environment `npm-publish`, `id-token: write`).

- [ ] **Step 1:** Samples type-check: `npm run typecheck --workspaces` → exit 0. `npm pack -w @misaconnect/einvoice --dry-run` → only `dist/*`, `README.md`, `CHANGELOG.md`, `package.json`; `npm publish --dry-run -w @misaconnect/einvoice` → succeeds.
- [ ] **Step 2:** actionlint (docker `rhysd/actionlint:1.7.7`) on `release-ts.yml` → 0 findings.
- [ ] **Step 3: Commit** `release(ts-einvoice): 0.1.0 docs, samples, publish workflow`
- [ ] **Step 4: Stop and ask the owner** before pushing the tag (needs npm org `@misaconnect` and an `NPM_TOKEN`/trusted-publisher setup on the `npm-publish` environment).

---

## Self-review notes

- Spec §6 operations → Tasks 9–13; auth/retry/envelope → 6; errors → 3; validation/templates/status → 4; options/logging → 5, 9; §7 type mapping → 1, 2, 7; §9 defects → Global Constraints + Tasks 3, 9, 11; §10 release → 15; tests layers → 8, 14.
- `MeInvoiceTransport` defined in Task 6 and consumed by 9–13; `money()`/`serializeRequest` (Task 2) consumed by 6, 7; `mapErrorCode` (3) consumed by 6, 9–13.

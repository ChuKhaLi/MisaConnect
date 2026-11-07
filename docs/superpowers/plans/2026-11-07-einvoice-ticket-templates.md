# EInvoice ticket templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** List MISA e-ticket templates (`POST /webapp/templates/ticket`) from both SDKs; ship .NET 2.10.0 and TS einvoice 0.3.0.

**Architecture:** Mirror `ListActiveTemplates` (Domain record → Application request + use case + port method → Infrastructure HTTP adapter + logger + DI → Client facade), with paging validation in the style of `PagedLookupRequest`. TS mirrors `listTemplates` + `lookupPaged` validation.

**Tech Stack:** .NET 8/10, xunit.v3/MTP; TypeScript, vitest, pnpm.

**Spec:** `docs/superpowers/specs/2026-11-07-einvoice-ticket-templates-design.md`

## Global Constraints

- Route `webapp/templates/ticket`, POST, query always `invoiceWithCode={true|false}&start={n}&length={n}&invoiceCalcu={true|false}` (in that order), no `isTicket`.
- Body = same credential body as `ListTemplatesAsync` (`TemplateRequestDto(WireTaxCode.Compose(opts), UserName, Password)`); TS likewise.
- Validation before any HTTP: `Start ≥ 0` → field `Start`, message `"Start must be >= 0."`; `1 ≤ Length ≤ 100` → field `Length`, `"Length must be in [1, 100]."`; rule id `"FR-047"` (same rule family as paged lookup); category `Validation`.
- Defaults: `InvoiceWithCode = true`, `Start = 0`, `Length = 20`, `FromCashRegister = false`.
- No active-only filtering. `TicketType`, `TemplateType` are `int`. `ServiceName` nullable; missing bools → `false`, missing ints → `0`.
- Wire field names verbatim: `TicketTemplateID, TemplateName, InvSeries, Inactive, TicketType, TemplateType, ServiceName, IsInheritFromOldTemplate, IsTicketCode`.
- `IMeInvoiceClient.ListTicketTemplatesAsync` is a default interface method throwing `NotSupportedException`.
- No secrets/PII in logs; logger op name `"ListTicketTemplates"`.

## Review Focus

1. MISA returns `data: null`/empty → empty list, not an exception (both SDKs) — test in Task 2 and Task 4.
2. Envelope `success:false` → same exception category mapping as templates (e.g. token error → Auth) — test in Task 2 and Task 4.
3. A custom `IMeInvoiceClient` without the new method → `NotSupportedException` from the use case, not a crash at DI — test in Task 1.
4. `Length` = 100 accepted, 101 rejected; `Start` = 0 accepted, −1 rejected — tests in Task 1 and Task 4.
5. TS row with non-string `TicketTemplateID` skipped; other rows kept — test in Task 4.

---

### Task 1: .NET Domain + Application

**Files:**
- Create: `src/dotnet/MisaConnect.EInvoice.Domain/Templates/TicketTemplate.cs`
- Create: `src/dotnet/MisaConnect.EInvoice.Application/UseCases/TicketTemplateListRequest.cs`, `.../UseCases/ListTicketTemplates.cs`
- Modify: `src/dotnet/MisaConnect.EInvoice.Application/Abstractions/IMeInvoiceClient.cs`
- Test: `tests/dotnet/MisaConnect.EInvoice.UnitTests/UseCases/ListTicketTemplatesTests.cs`

**Interfaces — Produces:**
- `public sealed record TicketTemplate(string TicketTemplateID, string TemplateName, string InvSeries, bool IsActive, int TicketType, int TemplateType, string? ServiceName, bool IsInheritFromOldTemplate, bool IsTicketCode);` (namespace `MisaConnect.EInvoice.Domain.Templates`)
- `public sealed record TicketTemplateListRequest(bool InvoiceWithCode = true, int Start = 0, int Length = 20, bool FromCashRegister = false) { public ValidationFailure? Validate(); }`
- `Task<IReadOnlyList<TicketTemplate>> IMeInvoiceClient.ListTicketTemplatesAsync(TicketTemplateListRequest request, CancellationToken ct) => throw new NotSupportedException(...)` (DIM)
- `public sealed class ListTicketTemplates(IMeInvoiceClient, EnsureAccessToken)` with `Task<IReadOnlyList<TicketTemplate>> ExecuteAsync(TicketTemplateListRequest request, CancellationToken ct)`

- [ ] **Step 1: Failing tests** in `ListTicketTemplatesTests` (use the hand-rolled fakes used by `ListActiveTemplatesTests`):
  - `Validation_rejects_negative_start` (Start −1 → `MeInvoiceException` category Validation, Field `"Start"`, no client call, no token call)
  - `Validation_rejects_length_out_of_range` theory 0, 101 → Field `"Length"`
  - `Validation_accepts_bounds` theory (Start 0, Length 1) and (Start 0, Length 100) → client called with the same request
  - `Returns_rows_unfiltered_including_inactive` (fake returns one active + one inactive → both returned)
  - `Ensures_token_before_calling_client`
  - `Port_without_override_throws_NotSupported` (fake `IMeInvoiceClient` that does not implement the new member → `NotSupportedException`)
- [ ] **Step 2:** `dotnet test --project tests/dotnet/MisaConnect.EInvoice.UnitTests --filter "FullyQualifiedName~ListTicketTemplatesTests"` → FAIL (types missing).
- [ ] **Step 3:** Implement the Produces block.
- [ ] **Step 4:** Same filter → PASS.
- [ ] **Step 5:** Commit `feat(einvoice): TicketTemplate domain, request validation, ListTicketTemplates use case`.

### Task 2: .NET Infrastructure

**Files:**
- Modify: `src/dotnet/MisaConnect.EInvoice.Infrastructure/MeInvoice/MeInvoiceHttpRoutes.cs` (`TicketTemplates = "webapp/templates/ticket"`), `.../MeInvoice/Wire/TemplateDtos.cs` (`internal sealed record TicketTemplateDto` with wire names; nullable where optional), `.../MeInvoice/MeInvoiceClient.cs` (`ListTicketTemplatesAsync`), mapper next to `InvoiceDtoMapper.FromWire(TemplateDto…)`, `.../Logging/MeInvoiceCallLogger.cs` (`Wrap("ListTicketTemplates", null, null, …)`), `.../DependencyInjection/ServiceCollectionExtensions.cs` (`AddScoped<ListTicketTemplates>()`)
- Modify: `tests/dotnet/MisaConnect.EInvoice.IntegrationTests/MisaFake/FakeMisaServer.cs` (`MapPost("/webapp/templates/ticket")` → capture + one active ticket row; no body assertion)
- Test: `tests/dotnet/MisaConnect.EInvoice.UnitTests/Infrastructure/MeInvoiceClientTicketTemplatesTests.cs`

**Interfaces — Consumes:** Task 1 types. **Produces:** `MeInvoiceClient.ListTicketTemplatesAsync(TicketTemplateListRequest, CancellationToken)`.

- [ ] **Step 1: Failing tests** (pattern of `MeInvoiceClientDeleteTests`, `CapturingHandler`):
  - `Posts_to_ticket_route_with_all_four_query_params` → method POST; URL contains `webapp/templates/ticket?invoiceWithCode=false&start=40&length=100&invoiceCalcu=true` for request `(false, 40, 100, true)`; body JSON has the tax code from `WireTaxCode.Compose`
  - `Maps_wire_rows` (two rows: full row → all nine fields; row missing `ServiceName`/`IsTicketCode`/`TicketType` → `null`/`false`/`0`; `Inactive:true` → `IsActive == false`)
  - `Null_data_returns_empty_list`
  - `Failure_envelope_maps_like_templates` (same envelope as an existing templates failure test → same exception category)
- [ ] **Step 2:** Run the class → FAIL.
- [ ] **Step 3:** Implement; logger + DI.
- [ ] **Step 4:** Class → PASS; full EInvoice unit project green.
- [ ] **Step 5:** Commit `feat(einvoice): MeInvoice adapter for /webapp/templates/ticket`.

### Task 3: .NET Client facade, operations registry, sample, integration + sandbox tests

**Files:**
- Modify: `src/dotnet/MisaConnect.EInvoice.Client/Dtos/Dtos.cs` (`TicketTemplateDto` same 9 fields, `TicketTemplateListRequestDto(bool WithCode = true, int Start = 0, int Length = 20, bool FromCashRegister = false)`), `Mapping/DtoMapper.cs`, `IMisaEInvoiceClient.cs` + `MisaEInvoiceClient.cs` (`Task<IReadOnlyList<TicketTemplateDto>> ListTicketTemplatesAsync(TicketTemplateListRequestDto? request = null, CancellationToken ct = default)`)
- Modify: `src/dotnet/MisaConnect.EInvoice.Application/Operations/MeInvoiceOperations.cs` (entry `"list-ticket-templates"`, `typeof(ListTicketTemplates)`, `"GET /api/templates/ticket"`, `"ListTicketTemplatesAsync"`); `tests/dotnet/MisaConnect.EInvoice.UnitTests/Operations/MeInvoiceOperationsTests.cs` count 10 → 11
- Modify: `samples/dotnet/MisaConnect.Samples.Api/Endpoints/TemplateEndpoints.cs` (`MapGet("/api/templates/ticket", …withCode, start, length, fromCashRegister)`)
- Create: `tests/dotnet/MisaConnect.EInvoice.IntegrationTests/Client/EInvoiceClientTicketTemplatesTests.cs` (fake server: route hit with expected query, one row mapped; validation error surfaces without a request reaching the fake)
- Create: `tests/dotnet/MisaConnect.EInvoice.IntegrationTests/Sandbox/ListTicketTemplatesTests.cs` (`[SandboxFact] Live_ticket_template_list_succeeds`: call with defaults; assert every row has non-empty `TicketTemplateID`; empty list passes)

- [ ] **Step 1:** Write the integration tests + update the operations count → run → FAIL.
- [ ] **Step 2:** Implement facade, DTOs, mapper, registry entry, sample endpoint.
- [ ] **Step 3:** EInvoice unit + integration projects green (incl. `OperationCoverageTests`); build 0 warnings.
- [ ] **Step 4:** Commit `feat(einvoice): IMisaEInvoiceClient.ListTicketTemplatesAsync`.

### Task 4: TypeScript `listTicketTemplates`

**Files:**
- Modify: `src/typescript/einvoice/src/domain/dtos.ts` (`TicketTemplateDto`, `TicketTemplateListRequestDto`), `src/application/ports.ts` (`listTicketTemplates(request: Required<TicketTemplateListRequestDto>, opts?)` on `MeInvoiceGateway`), `src/application/invoice-validator.ts` (`validateTicketTemplateList`), `src/infrastructure/wire/templates.ts` (`WireTicketTemplate`, `fromWireTicketTemplate`), `src/infrastructure/gateway.ts` (route `webapp/templates/ticket`), `src/client.ts` + `src/client-internal.ts` (`listTicketTemplates(request?, opts?)`, op name `'ListTicketTemplates'`)
- Create: `src/typescript/einvoice/src/application/use-cases/list-ticket-templates.ts`
- Modify: `test/fake/fake-misa-server.ts` (route), tests: `test/unit/` (validation + wire mapping), `test/integration/read-ops.test.ts` (fake round-trip), `test/sandbox/sandbox.test.ts` (live call), `test/unit/public-types.test.ts` if it enumerates exports

**Interfaces — Produces:** `listTicketTemplates(request?: TicketTemplateListRequestDto, opts?: CallOptions): Promise<TicketTemplateDto[]>`; `TicketTemplateDto { ticketTemplateID; templateName; invSeries; isActive; ticketType; templateType; serviceName?; isInheritFromOldTemplate; isTicketCode }`; `TicketTemplateListRequestDto { withCode?; start?; length?; fromCashRegister? }`.

- [ ] **Step 1: Failing tests:** validation (`start: -1` → `MeInvoiceException` category `Validation`, `field: 'start'`; `length` 0/101 → `field: 'length'`; 100 accepted; no request reaches the fake); query string `invoiceWithCode=…&start=…&length=…&invoiceCalcu=…`; mapping incl. missing optional fields; row without string `TicketTemplateID` skipped; `data` null → `[]`; failure envelope → same category as `listTemplates`.
- [ ] **Step 2:** `pnpm --filter @misaconnect/einvoice run test` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same command → PASS; `pnpm run lint`, `format:check`, `typecheck` → 0.
- [ ] **Step 5:** Commit `feat(ts-einvoice): listTicketTemplates`.

### Task 5: Docs, versions, changelogs

**Files:** `README.md` (supported-operations rows, .NET + TS), `src/typescript/einvoice/README.md` (method section), `docs/einvoice/*` (operation list if present), `CHANGELOG.md` (`[2.10.0] - 2026-11-07`: Added — EInvoice `ListTicketTemplatesAsync`; ESign: no change; Repo subsection = current `[Unreleased]` pnpm notes), `src/typescript/einvoice/CHANGELOG.md` `[0.3.0]`, `src/typescript/einvoice/package.json` version `0.3.0`, both Client csproj `<Version>2.10.0</Version>`.

- [ ] **Step 1:** Edits.
- [ ] **Step 2:** All four .NET test projects, `pnpm test`, `bash scripts/package-smoke.sh` → green.
- [ ] **Step 3:** Commit `docs+release: 2.10.0 / einvoice-ts 0.3.0 — ticket templates`.

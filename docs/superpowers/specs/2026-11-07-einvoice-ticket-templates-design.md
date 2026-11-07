# EInvoice: list ticket templates (`/webapp/templates/ticket`) — design (2026-11-07)

Owner, 2026-11-07: users need tickets; "implement theo docs của Misa là được".

## Why

MISA's eInvoice API ("Tài liệu API Tạo hóa đơn - V2", §3.2 "Lấy danh sách mẫu vé điện tử" and
§8.2 "Ticket (mẫu vé)") lists one endpoint neither SDK covers: the list of e-ticket templates a
company can issue. Every other endpoint in that document is implemented. Scope is exactly what
the document describes — **listing ticket templates**. Nothing about creating, previewing or
looking up tickets is documented, so none of that is in scope.

## What MISA documents (verbatim source: `docs/misa-api-reference/Tài liệu API Tạo hóa đơn - V2.md`)

- `POST {BaseUrl}/webapp/templates/ticket`
- Query params: `invoiceWithCode` (true/false), `start` (default 0), `length` (max 100,
  default 20), `invoiceCalcu` (true/false — ticket templates from a cash register, "MTT").
- Headers: `Content-Type: application/json`, `Authorization: Bearer {token}`, `TaxCode: {taxcode}`.
- Response: the standard envelope; `data` is a JSON string holding an array of Ticket objects.
- Ticket fields: `TicketTemplateID` (string), `TemplateName` (string), `InvSeries` (string),
  `Inactive` (bool), `TicketType` (int), `TemplateType` (int), `ServiceName` (string),
  `IsInheritFromOldTemplate` (bool), `IsTicketCode` (bool).

## Goals

1. **.NET (`MisaConnect.EInvoice`)**, layered like `ListActiveTemplates`:
   - Domain: `TicketTemplate` record with the nine fields above (domain names may be idiomatic,
     e.g. `IsActive = !Inactive`, as `Template` does).
   - Application: request type `TicketTemplateListRequest` (`InvoiceWithCode`, `Start`,
     `Length`, `FromCashRegister` → `invoiceCalcu`) with `Validate()` in the style of
     `PagedLookupRequest`: `Start ≥ 0`, `1 ≤ Length ≤ 100`; defaults `true / 0 / 20 / false`.
     A failure throws `MeInvoiceException(Validation, …)` before any network call, like
     `LookupStandard`.
   - Use case `ListTicketTemplates` (ensure token → port call). Port: `IMeInvoiceClient`
     gains `ListTicketTemplatesAsync(TicketTemplateListRequest, CancellationToken)` as a
     **default interface method** that throws `NotSupportedException`, so existing custom
     implementations of the port still compile (same approach as ESign's
     `ITokenCache.RemoveIfMatchesAsync`).
   - Infrastructure: `MeInvoiceHttpRoutes.TicketTemplates = "webapp/templates/ticket"`, wire DTO
     with MISA's field names verbatim, envelope handling and error mapping exactly as
     `ListTemplatesAsync`; `MeInvoiceCallLogger` decorates it; registered in DI.
   - Client: `IMisaEInvoiceClient.ListTicketTemplatesAsync(TicketTemplateListRequestDto? request = null, CancellationToken ct = default)`
     returning `IReadOnlyList<TicketTemplateDto>`; `MeInvoiceOperations` gets a
     `list-ticket-templates` entry.
2. **TypeScript (`@misaconnect/einvoice`)**: `client.listTicketTemplates(request?, opts?)` with the
   same request fields (camelCase: `withCode`, `start`, `length`, `fromCashRegister`), same
   validation (thrown as the package's validation error before any request), same result shape
   (`TicketTemplateDto`), gateway method, wire mapper with malformed-row skipping as
   `listTemplates` does.
3. **No active-only filter.** Unlike `ListTemplates`, results are returned as MISA sends them
   (each row carries `isActive`): the endpoint is paged, so filtering would make a page shorter
   than `length` and break `start` arithmetic.
4. **Tests:** unit tests (validation bounds 0/1/100/101, −1; query string; wire mapping incl.
   missing optional fields; envelope failure → same exception category as templates);
   integration test against the in-repo fake servers (`.NET MisaFake`, TS fake); one
   `[SandboxFact]` / TS sandbox test that calls the real endpoint and asserts only that it
   succeeds and every row has a non-empty `TicketTemplateID` (the sandbox account may have zero
   ticket templates — an empty list passes).
5. **Docs + release:** README supported-operations table, `docs/einvoice/*` operation page if
   other operations have one, sample endpoint/console line only if samples list every
   operation. `CHANGELOG.md` `[2.10.0]` (both NuGet packages in lock-step; ESign: no change);
   TS `src/typescript/einvoice/CHANGELOG.md` `[0.3.0]`, `package.json` 0.3.0.

## Non-goals

- Issuing / previewing / looking up tickets; using ticket templates in `TemplateResolver`.
- Any ESign change.

## Calls made by the agent (owner may override)

- **Request body.** MISA documents headers and query params only for this endpoint, while the
  sibling `/webapp/templates` call sends `{taxcode, username, password}` as body (and the
  current code does). Send the same credential body as `ListTemplatesAsync`. The sandbox test
  (goal 4) is the check; if MISA rejects it, switch to an empty JSON object `{}`.
- **No active-only filter** (goal 3).
- **Names:** `ListTicketTemplatesAsync` / `listTicketTemplates`, `FromCashRegister` /
  `fromCashRegister` for `invoiceCalcu`.
- **Versions:** .NET 2.10.0 (new public surface → minor), TS 0.3.0. Version bumps
  (`<Version>` in both Client csproj files, TS `package.json`) are part of this change; the
  current root `[Unreleased]` pnpm notes move under `[2.10.0]` as a "Repo" subsection.
- **Wire details:** `TaxCode` header and body tax code use `WireTaxCode.Compose(opts)` exactly as
  `ListTemplatesAsync`. Query string always carries all four documented params explicitly
  (`invoiceWithCode`, `start`, `length`, `invoiceCalcu`); no `isTicket` param (not documented for
  this endpoint). `invoiceWithCode` defaults to `true` on our side, as `ListTemplatesAsync` does.
- **Types:** `TicketType`, `TemplateType` stay `int` (MISA documents no enum).
- **Malformed rows:** each SDK mirrors its own `listTemplates` behaviour — TS skips rows without
  a string `TicketTemplateID`; .NET deserializes like `ListTemplatesAsync`. Absent optional
  fields (`ServiceName`, `IsTicketCode`, `IsInheritFromOldTemplate`, `TicketType`) map to
  `null`/`false`/`0` defaults — `ServiceName` nullable.
- **Fake servers** do not assert the request body (so they do not lock in the body guess); they
  assert route, method and query string.
- **Release gate:** the sandbox run (Proof of done) must show the ticket test **passing** before
  the `v2.10.0` / `einvoice-ts-v0.3.0` tags. The EInvoice sandbox is reachable from CI
  (`integration.yml` lowers the MTU). If the sandbox test skips or fails, the owner decides
  before tagging.

## Proof of done

- All .NET and TS unit + integration tests green; `dotnet format` / `pnpm run lint`,
  `format:check` clean; `scripts/package-smoke.sh` OK.
- `integration.yml` (filter `FullyQualifiedName~Sandbox`) and `ts-sandbox.yml` run on the branch
  or `main`, and the ticket-template sandbox tests pass (not skip) — this proves the request
  shape against the real MISA sandbox.

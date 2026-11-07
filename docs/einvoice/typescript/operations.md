---
title: Operations
parent: TypeScript
grand_parent: EInvoice
nav_order: 3
---

# Operations
{: .no_toc }

1. TOC
{:toc}

`createMisaEInvoiceClient(options)` returns an `IMisaEInvoiceClient`. Every method takes an
optional last argument `opts?: CallOptions` (`{ signal?: AbortSignal; correlationId?: string }`).
`withCode` defaults to `true` on the methods where it is optional (see
[With-code and without-code invoices](../index#with-code-and-without-code-invoices)). Endpoints
below are relative to `baseUrl` (`https://testapi.meinvoice.vn/api/integration` in the sandbox);
every call signs in first (token cached, see [Configuration](./configuration#token-cache)) and
sends `invoiceWithCode` as a query parameter.

| Method | Returns | Purpose |
| --- | --- | --- |
| [`listTemplates(withCode?)`](#listtemplates) | `TemplateDto[]` | Active invoice templates. |
| [`listTicketTemplates(request?)`](#listtickettemplates) | `TicketTemplateDto[]` | One page of e-ticket templates. |
| [`preview(invoice, withCode?)`](#preview) | `PdfDocumentDto` | Render an invoice PDF without saving it. |
| [`saveDraft(invoices, withCode?)`](#savedraft) | `SaveResultDto[]` | Save drafts; one result per invoice. |
| [`getDraftPdf(refId, withCode?)`](#getdraftpdf) | `PdfDocumentDto` | PDF of a saved draft. |
| [`deleteDraft(refId, invoiceWithCode)`](#deletedraft) | `DeleteDraftOutcome` | Delete a draft; outcome-returning. |
| [`lookupByRefId(refIds, withCode)`](#lookupbyrefid) | `LookupBatchOutcomeDto` | Look up invoices by RefId; per-RefId outcomes. |
| [`lookupStandard(request, withCode)`](#lookupstandard) | `PagedLookupResultDto` | Paged lookup by date range. |
| [`lookupCalculating(request, withCode)`](#lookupcalculating) | `PagedLookupResultDto` | Paged lookup (calculating-machine invoices). |
| [`issueReplacement(request)`](#issuereplacement) | `AmendmentResultDto` | Issue a replacement invoice. |
| [`issueAdjustment(request)`](#issueadjustment) | `AmendmentResultDto` | Issue an adjustment invoice. |

The examples below declare `client` (and `invoice`) instead of building them; see
[Getting started](./getting-started#quick-start) and [A complete invoice](#a-complete-invoice).

## listTemplates

```ts
import type { CallOptions, TemplateDto } from '@misaconnect/einvoice';

export type ListTemplates = (withCode?: boolean, opts?: CallOptions) => Promise<TemplateDto[]>;
```

- **MISA endpoint:** `POST webapp/templates?invoiceWithCode=…&isTicket=false`.
- **Inputs:** `withCode` (default `true`).
- **Result:** the **active** templates only (MISA returns active and inactive; the SDK filters on
  `isActive`). Each `TemplateDto` has `ipTemplateID`, `invSeries`, `templateName`,
  `invTemplateNo?`, `templateType`, `usesTaxAuthorityCode`, `isActive`, `isMoreVATRate`. See
  [Templates and series](../index#templates-and-series).
- **Errors:** throws `MeInvoiceException` (`Authentication`, `Configuration`, `MisaThrottled`,
  `MisaUnavailable`, `TransportFailed`, `MisaUnknown` with `InvalidResponse` for a non-array
  payload).

```ts
import type { IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;

const templates = await client.listTemplates(true);
for (const t of templates) console.log(t.invSeries, t.ipTemplateID, t.templateName);
```

## listTicketTemplates

```ts
import type {
  CallOptions,
  TicketTemplateDto,
  TicketTemplateListRequestDto,
} from '@misaconnect/einvoice';

export type ListTicketTemplates = (
  request?: TicketTemplateListRequestDto,
  opts?: CallOptions,
) => Promise<TicketTemplateDto[]>;
```

- **MISA endpoint:** `POST webapp/templates/ticket?invoiceWithCode=…&start=…&length=…&invoiceCalcu=…`
  (e-ticket templates, "mẫu vé điện tử").
- **Inputs:** `request` is `{ withCode?, start?, length?, fromCashRegister? }` with defaults
  `true`, `0`, `20`, `false`; `length` is 1–100 and `fromCashRegister` is MISA's `invoiceCalcu`
  (cash-register / MTT templates). See [Paging](../index#paging).
- **Result:** one page of `TicketTemplateDto` (`ticketTemplateID`, `templateName`, `invSeries`,
  `isActive`, `ticketType`, `templateType`, `serviceName?`, `isInheritFromOldTemplate`,
  `isTicketCode`). Rows come back as MISA sends them, active and inactive (`isActive`), because the
  endpoint is paged.
- **Errors:** a `start` below 0 or a `length` outside 1–100 throws `MeInvoiceException` with
  category `Validation` (`ValidationFailed`) before any request; MISA and transport failures throw
  as for `listTemplates`.

```ts
import type { IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;

const page = await client.listTicketTemplates({ start: 0, length: 100 });
const usable = page.filter((t) => t.isActive);
console.log(usable.map((t) => t.invSeries));
```

## preview

```ts
import type { CallOptions, InvoiceDto, PdfDocumentDto } from '@misaconnect/einvoice';

export type Preview = (
  invoice: InvoiceDto,
  withCode?: boolean,
  opts?: CallOptions,
) => Promise<PdfDocumentDto>;
```

- **MISA endpoint:** `POST webapp/templates` (to resolve the template), then
  `POST webapp/preview?invoiceWithCode=…`.
- **Inputs:** one `InvoiceDto` ([A complete invoice](#a-complete-invoice)). A missing `refId` is
  generated by the `refIdGenerator`. The invoice is validated first, then its `template` is
  resolved against the active templates (omit it when exactly one is active).
- **Result:** `PdfDocumentDto` `{ content: Uint8Array; contentType: 'application/pdf' }`. Nothing
  is saved.
- **Errors:** `Validation` / `ValidationFailed` (with `failures`) for an invalid invoice, with no
  HTTP call; `InvalidMoney` / `InvalidDate` / `InvalidNumber` / `InvalidVatRate` for values that
  cannot be put on the wire; template errors `NoActiveTemplate` / `UnknownTemplate`
  (`TemplateState`) or `AmbiguousTemplate` (`Configuration`); MISA and transport failures.

```ts
import type { IMisaEInvoiceClient, InvoiceDto } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;
declare const invoice: InvoiceDto;

const pdf = await client.preview(invoice); // pdf.content is a Uint8Array
console.log(pdf.contentType, pdf.content.byteLength);
```

## saveDraft

```ts
import type { CallOptions, InvoiceDto, SaveResultDto } from '@misaconnect/einvoice';

export type SaveDraft = (
  invoices: readonly InvoiceDto[],
  withCode?: boolean,
  opts?: CallOptions,
) => Promise<SaveResultDto[]>;
```

- **MISA endpoint:** `POST webapp/templates`, then one `POST webapp/insert?invoiceWithCode=…` for
  all valid invoices.
- **Inputs:** 1–30 invoices. Each gets its `refId` (yours, or generated); pass your own as an
  idempotency key (see [RefId and drafts](../index#refid-and-drafts) and
  [Retries and duplicate drafts](#retries-and-duplicate-drafts)).
- **Result:** one `SaveResultDto` per invoice, in input order: `{ refId, outcome: 'Success' |
  'Error', errorCategory?, rawErrorCode?, errorMessage?, failures? }`. An invoice that fails
  validation, cannot be put on the wire or has an unresolvable template fails only itself (it is
  not sent); the valid ones still go out in one insert call.
- **Errors:** only the batch throws: an empty batch (`Validation` / `ValidationFailed`) or more than
  30 invoices (`BatchOrSize`, `rawErrorCode: 'InvoiceQuantityTooLarge'`), both with a
  `$batch.count` failure. Details in [Errors](./errors#outcome-returning-operations).

```ts
import type { IMisaEInvoiceClient, InvoiceDto } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;
declare const invoice: InvoiceDto;

const results = await client.saveDraft([invoice]); // one result per invoice, in input order
for (const r of results) {
  if (r.outcome === 'Success') console.log('saved', r.refId);
  else console.warn(r.refId, r.errorCategory, r.rawErrorCode, r.failures ?? r.errorMessage);
}
```

## getDraftPdf

```ts
import type { CallOptions, PdfDocumentDto } from '@misaconnect/einvoice';

export type GetDraftPdf = (
  refId: string,
  withCode?: boolean,
  opts?: CallOptions,
) => Promise<PdfDocumentDto>;
```

- **MISA endpoint:** `GET webapp/viewrefid?invoiceWithCode=…&refid=…`.
- **Inputs:** the draft's `refId` (non-empty).
- **Result:** `PdfDocumentDto` with the stored draft's PDF.
- **Errors:** `Validation` / `RefIdRequired` for an empty RefId; `ResourceNotFound` for an unknown
  RefId (MISA answers success with empty data, which the SDK treats as not found); `MisaUnknown` /
  `InvalidPdfContent` when the payload is not a PDF; MISA and transport failures.

```ts
import { writeFile } from 'node:fs/promises';
import type { IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;

const pdf = await client.getDraftPdf('order-2026-0001');
await writeFile('order-2026-0001.pdf', pdf.content);
```

## deleteDraft

```ts
import type { CallOptions, DeleteDraftOutcome } from '@misaconnect/einvoice';

export type DeleteDraft = (
  refId: string,
  invoiceWithCode: boolean,
  opts?: CallOptions,
) => Promise<DeleteDraftOutcome>;
```

- **MISA endpoint:** `DELETE webapp/delete?invoiceWithCode=…&refid=…`.
- **Inputs:** `refId` (1–64 characters) and `invoiceWithCode` (required here).
- **Result:** a `DeleteDraftOutcome` `{ status, rawErrorCode?, message?, field?, refId? }` with
  `status` one of `Deleted`, `NotFound`, `NotDeletable`, `AuthFailed`, `Configuration`,
  `MisaThrottled`, `MisaUnavailable`, `TransportFailed`, `MisaUnknown`. MISA and transport failures
  are outcomes, not exceptions. `message` is present only with
  [`delete.includeRawErrorMessage`](./configuration#deleteincluderawerrormessage).
- **Errors:** throws only `Validation` / `RefIdInvalid` for an empty or over-long RefId (plus a
  caller abort, or an error from your `TokenCache`).

```ts
import type { IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;

const del = await client.deleteDraft('order-2026-0001', true);
if (del.status !== 'Deleted') console.warn(del.status, del.rawErrorCode);
```

## lookupByRefId

```ts
import type { CallOptions, LookupBatchOutcomeDto } from '@misaconnect/einvoice';

export type LookupByRefId = (
  refIds: readonly string[],
  withCode: boolean,
  opts?: CallOptions,
) => Promise<LookupBatchOutcomeDto>;
```

- **MISA endpoint:** `POST webapp/getlist?invoiceWithCode=…`, in chunks of 50 RefIds sent one
  after another.
- **Inputs:** RefIds, each 1–64 characters.
- **Result:** `LookupBatchOutcomeDto` `{ status: 'Completed' | 'Validation', outcomes?, reason? }`.
  Each `LookupOutcomeDto` (input order) has `refId`, `status` (`Found`, `NotFound`, `Validation`,
  `AuthFailed`, `Configuration`, `MisaThrottled`, `MisaUnavailable`, `TransportFailed`,
  `MisaUnknown`), `snapshot?` (an `InvoiceSnapshotDto` with `status`, `invNo`, totals, ...),
  `errorCode?` and `message?` (gated by `delete.includeRawErrorMessage`). An `AuthFailed` or
  `Configuration` failure in one chunk is copied to the remaining chunks without further requests.
- **Errors:** an empty list is a `Validation` batch outcome (a value); an invalid individual RefId
  throws `Validation` / `RefIdInvalid`.

```ts
import type { IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;

const found = await client.lookupByRefId(['order-2026-0001'], true);
for (const o of found.outcomes ?? []) console.log(o.refId, o.status, o.snapshot?.status);
```

## lookupStandard

```ts
import type {
  CallOptions,
  PagedLookupRequestDto,
  PagedLookupResultDto,
} from '@misaconnect/einvoice';

export type LookupStandard = (
  request: PagedLookupRequestDto,
  withCode: boolean,
  opts?: CallOptions,
) => Promise<PagedLookupResultDto>;
```

- **MISA endpoint:** `POST webapp/paging?invoiceWithCode=…`.
- **Inputs:** `PagedLookupRequestDto` `{ start, length, sort?, fromDate, toDate, publishStatus? }`:
  `start >= 0`, `length` 1–100, `fromDate <= toDate` (`yyyy-MM-dd` or `Date`), `publishStatus` one
  of `0`, `4`, `6`, `7` when set; a blank `sort` becomes `InvDate`. See [Paging](../index#paging).
- **Result:** `PagedLookupResultDto` `{ items: InvoiceSnapshotDto[]; start; length; returnedCount }`.
- **Errors:** the first request violation throws `Validation` / `ValidationFailed` with the rule id
  (`FR-047`) in `failures[0].ruleId`, before any request; an impossible date throws `InvalidDate`;
  any MISA error throws.

```ts
import type { IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;

const page = await client.lookupStandard(
  { start: 0, length: 50, fromDate: '2026-11-01', toDate: '2026-11-30' },
  true,
);
console.log(page.returnedCount, page.items.map((i) => i.invNo));
```

## lookupCalculating

```ts
import type {
  CallOptions,
  PagedLookupRequestDto,
  PagedLookupResultDto,
} from '@misaconnect/einvoice';

export type LookupCalculating = (
  request: PagedLookupRequestDto,
  withCode: boolean,
  opts?: CallOptions,
) => Promise<PagedLookupResultDto>;
```

- **MISA endpoint:** `POST webapp/paging/calculating?invoiceWithCode=…` (invoices from calculating
  machines / cash registers).
- **Inputs, result, errors:** as for [`lookupStandard`](#lookupstandard).

```ts
import type { IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;

const page = await client.lookupCalculating(
  { start: 0, length: 100, fromDate: new Date(2026, 10, 1), toDate: new Date(2026, 10, 30) },
  false,
);
console.log(page.items.length);
```

## issueReplacement

```ts
import type { AmendmentResultDto, CallOptions, ReplacementRequestDto } from '@misaconnect/einvoice';

export type IssueReplacement = (
  request: ReplacementRequestDto,
  opts?: CallOptions,
) => Promise<AmendmentResultDto>;
```

- **MISA endpoint:** `POST webapp/templates`, then `POST webapp/insert?invoiceWithCode=…` with one
  invoice marked as a replacement (`EInvoiceStatus "3"`).
- **Inputs:** `ReplacementRequestDto` `{ invoice, originalRef, changeReason, invoiceWithCode? }`
  (`invoiceWithCode` defaults to `true`). `originalRef` is
  `{ orgRefID, orgInvNo, orgInvTemplateNo, orgInvSeries, orgInvDate }`, all required. The new
  invoice gets its own `refId` (yours, or generated).
- **Result:** `AmendmentResultDto` `{ refId, outcome, errorCategory?, rawErrorCode?,
  errorMessage?, failures?, orgRefId? }`. A MISA rejection of the insert is an `Error` result, not
  an exception.
- **Errors:** request or invoice validation throws `Validation` / `ValidationFailed` (rule ids
  `FR-058`, `FR-059`, ...) before any request; template resolution failures throw; transport and
  sign-in failures throw. `changeReason` is logged only with `delete.includeRawErrorMessage`.

```ts
import type { IMisaEInvoiceClient, InvoiceDto } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;
declare const invoice: InvoiceDto;

const replaced = await client.issueReplacement({
  invoice: { ...invoice, refId: 'order-2026-0001-r1' },
  originalRef: {
    orgRefID: 'order-2026-0001',
    orgInvNo: '00000012',
    orgInvTemplateNo: '1',
    orgInvSeries: 'C26TAA',
    orgInvDate: '2026-11-03',
  },
  changeReason: 'Sai địa chỉ người mua',
});
if (replaced.outcome === 'Error') console.warn(replaced.errorCategory, replaced.rawErrorCode);
```

## issueAdjustment

```ts
import type { AdjustmentRequestDto, AmendmentResultDto, CallOptions } from '@misaconnect/einvoice';

export type IssueAdjustment = (
  request: AdjustmentRequestDto,
  opts?: CallOptions,
) => Promise<AmendmentResultDto>;
```

- **MISA endpoint:** as [`issueReplacement`](#issuereplacement), with the invoice marked as an
  adjustment (`EInvoiceStatus "4"`).
- **Inputs, result, errors:** as for `issueReplacement`; `AdjustmentRequestDto` has the same shape.

```ts
import type { AdjustmentRequestDto, IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;
declare const request: AdjustmentRequestDto;

const adjusted = await client.issueAdjustment(request);
console.log(adjusted.refId, adjusted.outcome, adjusted.orgRefId);
```

## Cancellation and correlation

```ts
import type { IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;

const ac = new AbortController();
const templates = await client.listTemplates(true, { signal: ac.signal, correlationId: 'req-42' });
console.log(templates.length);
```

Aborting rejects with the native `AbortError` (or the reason you passed to `abort()`): never
wrapped, never retried, also during retry backoff. The call's log line then has
`outcome: 'Cancelled'` and no `errorCategory`. `correlationId` overrides the
[`correlationId` option](./configuration#options-misaeinvoiceoptions) for this call's log line.

## A complete invoice

A minimal VND invoice for an organisation buyer with one goods line. This exact object is
type-checked and saved against the fake MISA server in the package tests
(`src/typescript/einvoice/test/fixtures/readme-invoice.ts`).

```ts
import type { InvoiceDto } from '@misaconnect/einvoice';

export const invoice: InvoiceDto = {
  refId: 'order-2026-0001', // your idempotency key; omit to get a generated UUID
  template: { ipTemplateID: 'ipt-active-1', invSeries: 'C26TAA' }, // omit if you have one active template
  invDate: '2026-11-03',
  createdDate: '2026-11-03T09:00:00+07:00',
  modifiedDate: '2026-11-03T09:00:00+07:00',
  currency: 'VND',
  exchangeRate: 1,
  paymentMethod: 'TM/CK',
  buyerType: 1, // 1 = organisation (buyer.taxCode required)
  buyer: {
    name: 'Công ty TNHH Ví Dụ',
    taxCode: '0312345678',
    address: '1 Lê Lợi, Quận 1, TP. Hồ Chí Minh',
    email: 'ketoan@example.com',
  },
  lines: [
    {
      inventoryItemType: 0, // 0 = goods
      sortOrder: 1,
      description: 'Bàn phím cơ',
      unitName: 'cái',
      quantity: 2,
      unitPrice: 500000,
      amountOC: 1000000,
      amount: 1000000,
      amountWithoutVATOC: 1000000,
      amountWithoutVAT: 1000000,
      vatRateName: '8%',
      vatAmountOC: 80000,
      vatAmount: 80000,
    },
  ],
  totals: {
    totalSaleAmountOC: 1000000,
    totalSaleAmount: 1000000,
    totalDiscountAmountOC: 0,
    totalDiscountAmount: 0,
    totalAmountWithoutVATOC: 1000000,
    totalAmountWithoutVAT: 1000000,
    totalVATAmountOC: 80000,
    totalVATAmount: 80000,
    totalAmountOC: 1080000,
    totalAmount: 1080000,
    totalAmountInWords: 'Một triệu không trăm tám mươi nghìn đồng',
  },
};
```

## Retries and duplicate drafts

Every request, inserts included, is retried once after a timeout, network error, 429 or 5xx (as in
.NET). If the first insert attempt was in fact saved, MISA answers the retry with
`DuplicateInvoiceRefID` for that draft. Pass explicit `refId`s, and on `DuplicateInvoiceRefID` (or
any unclear failure) look the draft up with [`lookupByRefId`](#lookupbyrefid) before creating it
again.

```ts
import type { IMisaEInvoiceClient, InvoiceDto } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;
declare const invoice: InvoiceDto & { refId: string };

const [saved] = await client.saveDraft([invoice]);
if (saved?.outcome === 'Error' && saved.rawErrorCode === 'DuplicateInvoiceRefID') {
  const { outcomes } = await client.lookupByRefId([invoice.refId], true);
  console.log('already saved?', outcomes?.[0]?.status === 'Found');
}
```

## Money

`Money = number | string`. A string must be a plain decimal (`^-?\d+(\.\d+)?$`) and is written to
the JSON body **verbatim as a number literal**, so no precision is lost. Amounts must be within the
.NET decimal range (|x| ≤ 79,228,162,514,264,337,593,543,950,335, ≤ 28 fractional digits). A string
beyond it is rejected with `InvalidMoney`, as is a number above `decimal.MaxValue` (e.g.
`Number.MAX_VALUE`). A number with more than 28 fractional digits is rounded half-to-even to 28, as
.NET's decimal parser rounds the same JSON literal: `0.1 + 0.2 - 0.3` (`5.551115123125783e-17`) →
`0.0000000000000000555111512313`, `5e-324` → `0`. The validator's discount-total check uses the
same rounded value. Surrounding whitespace is rejected rather than trimmed: .NET money fields are
typed `decimal`, so there is no .NET string form whose parsing rules to copy, and rejecting is the
safer default. A `number` is written as plain decimal text, never in exponent form (`1e21` →
`1000000000000000000000`, `1e-7` → `0.0000001`). JavaScript `number` is a binary double: for
foreign-currency amounts, exchange rates and anything that must be exact, compute with a decimal
library (decimal.js, big.js, ...) and pass `value.toFixed()` strings. Amounts in responses are
`number`.

## Property names

Property names lowercase only the leading word or acronym of the .NET name and keep later
acronyms:

| .NET | TypeScript |
| --- | --- |
| `IPTemplateID` | `ipTemplateID` |
| `RefId` | `refId` |
| `OrgRefID` | `orgRefID` |
| `InvSeries` | `invSeries` |
| `AmountOC` | `amountOC` |
| `VATAmountOC` | `vatAmountOC` |
| `TotalAmountWithoutVATOC` | `totalAmountWithoutVATOC` |
| `IsMoreVATRate` | `isMoreVATRate` |

Dates: `yyyy-MM-dd` strings (a `Date` is also accepted and formatted from its local calendar
fields). Date-times are ISO-8601 strings **with an explicit offset** (`Z` or `+hh:mm` /
`-hh:mm`).

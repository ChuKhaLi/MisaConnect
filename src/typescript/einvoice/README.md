# @misaconnect/einvoice

TypeScript SDK for **MISA meInvoice** — a port of the .NET `MisaConnect.EInvoice` package. Zero runtime dependencies; uses the platform `fetch`. ESM and CommonJS builds, full type declarations.

Requires **Node.js >= 20**.

## Install

```
npm install @misaconnect/einvoice
```

## Quick start

```ts
import { createMisaEInvoiceClient, isMeInvoiceException } from '@misaconnect/einvoice';

const client = createMisaEInvoiceClient({
  environment: 'sandbox', // or 'production'
  baseUrl: 'https://testapi.meinvoice.vn/api/integration',
  taxCode: process.env.MISACONNECT_SANDBOX_TAXCODE!,
  userName: process.env.MISACONNECT_SANDBOX_USERNAME!,
  password: process.env.MISACONNECT_SANDBOX_PASSWORD!,
  appId: process.env.MISACONNECT_SANDBOX_APPID!,
});

try {
  const templates = await client.listTemplates();
  console.log(templates.map((t) => `${t.invSeries} (${t.ipTemplateID})`));
} catch (e) {
  if (isMeInvoiceException(e)) console.error(e.category, e.rawErrorCode, e.message);
  else throw e;
}
```

The quick start, the samples (`samples/typescript/`) and the sandbox tests all read the same `MISACONNECT_SANDBOX_*` variables (the names the .NET SDK uses).

`baseUrl` must be an `https` URL whose host matches the chosen `environment` (`testapi.meinvoice.vn` for sandbox, `api.meinvoice.vn` for production). Invalid options throw a `MeInvoiceException` with category `Configuration`.

### Options (`MisaEInvoiceOptions`)

| Option                                                                   | Notes                                                                          |
| ------------------------------------------------------------------------ | ------------------------------------------------------------------------------ |
| `environment`                                                            | `'sandbox'` (default) or `'production'`.                                       |
| `baseUrl`, `taxCode`, `userName`, `password`, `appId`                    | Required.                                                                      |
| `delete.includeRawErrorMessage`                                          | Opt in to raw MISA error text (default `false`); see below.                    |
| `logger`                                                                 | `{ debug, info, warn, error }(message, fields?)`. Default is a no-op.          |
| `correlationId`                                                          | `() => string` producing the id logged per call (default `crypto.randomUUID`). |
| `fetch`                                                                  | Custom `fetch`. Default: the global `fetch`, looked up on every call.          |
| `tokenCache`, `refIdGenerator`, `clock`, `validator`, `templateResolver` | Swappable collaborators (same ports as the .NET SDK); see below.               |

`delete.includeRawErrorMessage` gates exactly these: the `message` on `deleteDraft` outcomes and on `lookupByRefId` per-RefId outcomes, the MISA-text excerpt in the `ARCH-DEL-001` warning, and logging of an amendment's `changeReason` (with credential-looking JSON fragments such as `"password":"..."` masked, as .NET `MeInvoiceLogScrubber` does). `saveDraft` and `issueReplacement` / `issueAdjustment` results always carry MISA's `errorMessage` (as in .NET), and `MeInvoiceException.message` is not gated.

Because the default `fetch` is resolved per call, a mock installed after the client is created (MSW, nock, `vi.stubGlobal`) is honoured.

### Swappable collaborators

`InvoiceValidator` has two methods, `validate` and `validateBatch`. `validateBatch` is part of the port for parity with .NET; `saveDraft` itself validates each invoice with `validate` so that one bad invoice does not fail the others.

The port types are exported: `TokenCache`, `AccessToken`, `Clock`, `RefIdGenerator`, `InvoiceValidator`, `TemplateResolver` and `TemplateResolution`. So are the defaults `defaultInvoiceValidator` and `resolveTemplate`, so you can wrap them instead of reimplementing them.

```ts
import type { TokenCache } from '@misaconnect/einvoice';

// Share tokens across processes (e.g. Redis). `expiresAt` is a `Date`: revive it when reading.
const tokenCache: TokenCache = {
  async get(key) {
    const raw = await redis.get(`misa:token:${key}`);
    if (raw === null) return undefined;
    const { value, expiresAt } = JSON.parse(raw) as { value: string; expiresAt: string };
    return { value, expiresAt: new Date(expiresAt) };
  },
  async set(key, token) {
    await redis.set(`misa:token:${key}`, JSON.stringify(token));
  },
  async delete(key) {
    await redis.del(`misa:token:${key}`);
  },
};
```

If a cache returns an ISO string or an epoch number instead of a `Date`, it is converted; an unparseable value counts as expired, so a new token is acquired.

## Operations

Every method takes an optional last argument `opts?: CallOptions` (`{ signal?: AbortSignal; correlationId?: string }`). `withCode` defaults to `true` on the methods that have it.

| Method                                 | Returns                 | Purpose                                        |
| -------------------------------------- | ----------------------- | ---------------------------------------------- |
| `listTemplates(withCode?)`             | `TemplateDto[]`         | Active invoice templates.                      |
| `preview(invoice, withCode?)`          | `PdfDocumentDto`        | Render an invoice PDF without saving it.       |
| `saveDraft(invoices, withCode?)`       | `SaveResultDto[]`       | Save drafts; one result per invoice.           |
| `getDraftPdf(refId, withCode?)`        | `PdfDocumentDto`        | PDF of a saved draft.                          |
| `deleteDraft(refId, invoiceWithCode)`  | `DeleteDraftOutcome`    | Delete a draft; outcome-returning.             |
| `lookupByRefId(refIds, withCode)`      | `LookupBatchOutcomeDto` | Look up invoices by RefId; per-RefId outcomes. |
| `lookupStandard(request, withCode)`    | `PagedLookupResultDto`  | Paged lookup by date range.                    |
| `lookupCalculating(request, withCode)` | `PagedLookupResultDto`  | Paged lookup (calculating-machine invoices).   |
| `issueReplacement(request)`            | `AmendmentResultDto`    | Issue a replacement invoice.                   |
| `issueAdjustment(request)`             | `AmendmentResultDto`    | Issue an adjustment invoice.                   |

```ts
const results = await client.saveDraft([invoice]); // one result per invoice, in input order
for (const r of results) {
  if (r.outcome === 'Success') console.log('saved', r.refId);
  else console.warn(r.refId, r.errorCategory, r.rawErrorCode, r.failures ?? r.errorMessage);
}
const result = results[0];

const pdf = await client.preview(invoice); // pdf.content is a Uint8Array

const del = await client.deleteDraft(result!.refId, true);
if (del.status !== 'Deleted') console.warn(del.status, del.rawErrorCode);

const found = await client.lookupByRefId([result!.refId], true);
for (const o of found.outcomes ?? []) console.log(o.refId, o.status, o.snapshot?.status);

const page = await client.lookupStandard(
  { start: 0, length: 50, fromDate: '2026-11-01', toDate: '2026-11-30' },
  true,
);
```

### A complete invoice

A minimal VND invoice for an organisation buyer with one goods line. This exact object is type-checked and saved against the fake MISA server in the package tests.

```ts
import type { InvoiceDto } from '@misaconnect/einvoice';

const invoice: InvoiceDto = {
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

### Retries and duplicate drafts

Every request, inserts included, is retried once after a timeout, network error, 429 or 5xx (as in .NET). If the first insert attempt was in fact saved, MISA answers the retry with `DuplicateInvoiceRefID` for that draft. Pass explicit `refId`s, and on `DuplicateInvoiceRefID` (or any unclear failure) look the draft up with `lookupByRefId` before creating it again.

## Error handling

Operations that cannot proceed throw `MeInvoiceException`, which carries `category` (a `MeInvoiceErrorCategory`: `Configuration`, `Authentication`, `Validation`, `BatchOrSize`, `MisaThrottled`, `MisaUnavailable`, `TransportFailed`, `MisaUnknown`, ...), plus `rawErrorCode`, `field`, `refId` and `failures` (`{ fieldPath, message, ruleId? }[]`) when known.

`field` and `component` are also populated (beyond .NET) when the MISA error code names them, e.g. `InvoiceDetail_Quantity_Line3` gives `field: 'Quantity'`, `component: 'Line3'`.

Check errors with `isMeInvoiceException(err)` rather than `err instanceof MeInvoiceException`. If both the ESM and the CommonJS build of this package load in one process (the dual-package hazard, e.g. a dependency `require`s it while your code `import`s it), an exception from one copy is not an `instanceof` the other copy's class; the guard falls back to `err.name === 'MeInvoiceException'`.

A `Validation` error raised from your own input (an invoice, an amendment or paged-lookup request, an empty batch) has `rawErrorCode: 'ValidationFailed'`; the individual problems are in `failures`, with the rule id in `failures[].ruleId` (e.g. `FR-047`). A few checks raise their own code directly: `RefIdInvalid` / `RefIdRequired` for a RefID argument, and `InvalidMoney` / `InvalidDate` / `InvalidNumber` / `InvalidVatRate` for a bad value. Every operation except `saveDraft` raises these directly (not wrapped into `ValidationFailed`): `preview`, `issueReplacement`, `issueAdjustment` and the dates of a lookup request. Only `saveDraft` wraps them into the affected invoice's `ValidationFailed` result.

Some operations report expected business results as **values, not exceptions**:

- `deleteDraft` returns a `DeleteDraftOutcome` with a `status` (`Deleted`, `NotFound`, `NotDeletable`, ...).
- `lookupByRefId` returns a `LookupBatchOutcomeDto`: batch `status` is `Completed` or `Validation`; each entry has its own `LookupStatus`. The asymmetry: an empty list comes back as a `Validation` outcome (a value), while an invalid individual RefId (empty or longer than 64 characters) throws `Validation` / `RefIdInvalid`.
- `saveDraft` returns one `SaveResultDto` per invoice, in input order (`outcome: 'Success' | 'Error'` with `errorCategory`, `rawErrorCode`, and `errorMessage` or `failures`). An invoice that fails validation gets `errorCategory: 'Validation'`, `rawErrorCode: 'ValidationFailed'` and `failures` with paths like `invoices[1].lines[0].unitPrice`. That includes values that cannot be put on the wire: a malformed money string, a non-finite number (`NaN`, `Infinity`), an impossible date, a date-time without offset. Such an invoice is not sent, and the valid invoices still go out in one insert call. An unresolvable template likewise fails only its own invoice. Only the batch itself throws: an empty batch (`Validation`) or more than 30 invoices (`BatchOrSize`, `rawErrorCode: 'InvoiceQuantityTooLarge'`), both with a `$batch.count` failure. `issueReplacement` / `issueAdjustment` return an `AmendmentResultDto` the same way.

## Cancellation and correlation

```ts
const ac = new AbortController();
const templates = await client.listTemplates(true, { signal: ac.signal, correlationId: 'req-42' });
```

Aborting rejects with the native `AbortError` (or the reason you passed to `abort()`): never wrapped, never retried, also during retry backoff. The call's log line then has `outcome: 'Cancelled'` and no `errorCategory`.

## Logging

```ts
import pino from 'pino';
import { createMisaEInvoiceClient, fromPino } from '@misaconnect/einvoice';

const client = createMisaEInvoiceClient({ /* ... */ logger: fromPino(pino()) });
```

Any object with `debug/info/warn/error(message, fields?)` works. An exception thrown by your logger is swallowed and never changes a call's result. Tokens, credentials, tax-code headers, buyer data, line content, amounts and the customer tax code are never logged.

## Money

`Money = number | string`. A string must be a plain decimal (`^-?\d+(\.\d+)?$`) and is written to the JSON body **verbatim as a number literal**, so no precision is lost. Surrounding whitespace is rejected rather than trimmed: .NET money fields are typed `decimal`, so there is no .NET string form whose parsing rules to copy, and rejecting is the safer default. A `number` is written as plain decimal text, never in exponent form (`1e21` → `1000000000000000000000`, `1e-7` → `0.0000001`). JavaScript `number` is a binary double: for foreign-currency amounts, exchange rates and anything that must be exact, compute with a decimal library (decimal.js, big.js, ...) and pass `value.toFixed()` strings. Amounts in responses are `number`.

## Property names

Property names lowercase only the leading word or acronym of the .NET name and keep later acronyms:

| .NET                      | TypeScript                |
| ------------------------- | ------------------------- |
| `IPTemplateID`            | `ipTemplateID`            |
| `RefId`                   | `refId`                   |
| `OrgRefID`                | `orgRefID`                |
| `InvSeries`               | `invSeries`               |
| `AmountOC`                | `amountOC`                |
| `VATAmountOC`             | `vatAmountOC`             |
| `TotalAmountWithoutVATOC` | `totalAmountWithoutVATOC` |
| `IsMoreVATRate`           | `isMoreVATRate`           |

Dates: `yyyy-MM-dd` strings (a `Date` is also accepted and formatted from its local calendar fields). Date-times are ISO-8601 strings **with an explicit offset** (`Z` or `+hh:mm` / `-hh:mm`).

## Differences from the .NET SDK

- `preview` validates before resolving the template, so an invalid invoice makes no HTTP call. `saveDraft` does the same per invoice: a batch with no valid invoice makes no HTTP call, and an invoice that is both invalid and has an unresolvable template reports `ValidationFailed` (.NET reports the template error).
- Untyped-JS `null` / missing inputs (a `null` invoice, line, buyer, request, date) throw or report `Validation` / `ValidationFailed` instead of a `TypeError`; a `null` optional field such as `vatRateName` or `sort` counts as absent.
- A getlist response with `success:false` and no error code maps to `MisaUnknown` (.NET reports `NotFound` for every RefID).
- An empty RefId list yields a `Validation` batch outcome.
- Paged-lookup request violations throw `rawErrorCode: 'ValidationFailed'` with the rule id in `failures[0].ruleId` (.NET puts the rule id, e.g. `FR-047`, in `rawErrorCode`). `preview` validation failures likewise carry `ValidationFailed`.
- Batch-size codes `InvoiceQuantityTooLarge` / `XMLTooLong` map to `BatchOrSize`. That includes the client-side check for a `saveDraft` batch of more than 30 invoices, where .NET throws `Validation`. An empty batch throws `Validation` / `ValidationFailed` (.NET reuses `InvoiceQuantityTooLarge`).
- `saveDraft` also turns wire-mapping failures (malformed money string, impossible date, date-time without offset) into that invoice's own `ValidationFailed` result. .NET's typed DTOs cannot hold such values.
- The customer tax code is never logged.
- Each call logs one completion line; failures are logged at `info` with `outcome: 'Error'` and `errorCategory`, a caller abort with `outcome: 'Cancelled'`.
- Money strings must be plain decimals (`^-?\d+(\.\d+)?$`); the discount-total consistency check skips amounts with more than 28 fractional or 29 integer digits.
- `KHAC:<rate>%` VAT names take a plain decimal rate (`KHAC:3.5%`); thousands separators, which .NET's `NumberStyles.Number` accepts (`KHAC:1,000%`), are rejected (FR-029), as is a rate <= 0 (the message says the rate must be positive).
- Date-times need an explicit offset (`Z` or `+hh:mm` / `-hh:mm`).

## Sandbox tests

Sandbox tests skip cleanly unless all four variables are set (`MISACONNECT_SANDBOX_TAXCODE`, `MISACONNECT_SANDBOX_USERNAME`, `MISACONNECT_SANDBOX_PASSWORD`, `MISACONNECT_SANDBOX_APPID`):

```
npm run test:sandbox -w @misaconnect/einvoice
```

## License

MIT. Design: [docs/superpowers/specs/2026-11-02-typescript-sdk-design.md](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/superpowers/specs/2026-11-02-typescript-sdk-design.md).

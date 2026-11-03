# @misaconnect/einvoice

TypeScript SDK for **MISA meInvoice** — a port of the .NET `MisaConnect.EInvoice` package. Zero runtime dependencies; uses the platform `fetch`. ESM and CommonJS builds, full type declarations.

Requires **Node.js >= 20**.

## Install

```
npm install @misaconnect/einvoice
```

## Quick start

```ts
import { createMisaEInvoiceClient, MeInvoiceException } from '@misaconnect/einvoice';

const client = createMisaEInvoiceClient({
  environment: 'sandbox', // or 'production'
  baseUrl: 'https://testapi.meinvoice.vn/api/integration',
  taxCode: process.env.MISA_TAXCODE!,
  userName: process.env.MISA_USERNAME!,
  password: process.env.MISA_PASSWORD!,
  appId: process.env.MISA_APPID!,
});

try {
  const templates = await client.listTemplates();
  console.log(templates.map((t) => `${t.invSeries} (${t.ipTemplateID})`));
} catch (e) {
  if (e instanceof MeInvoiceException) console.error(e.category, e.rawErrorCode, e.message);
  else throw e;
}
```

`baseUrl` must be an `https` URL whose host matches the chosen `environment` (`testapi.meinvoice.vn` for sandbox, `api.meinvoice.vn` for production). Invalid options throw a `MeInvoiceException` with category `Configuration`.

### Options (`MisaEInvoiceOptions`)

| Option                                                                   | Notes                                                                          |
| ------------------------------------------------------------------------ | ------------------------------------------------------------------------------ |
| `environment`                                                            | `'sandbox'` (default) or `'production'`.                                       |
| `baseUrl`, `taxCode`, `userName`, `password`, `appId`                    | Required.                                                                      |
| `delete.includeRawErrorMessage`                                          | Opt in to raw MISA error messages in results and logs (default `false`).       |
| `logger`                                                                 | `{ debug, info, warn, error }(message, fields?)`. Default is a no-op.          |
| `correlationId`                                                          | `() => string` producing the id logged per call (default `crypto.randomUUID`). |
| `fetch`                                                                  | Custom `fetch` implementation.                                                 |
| `tokenCache`, `refIdGenerator`, `clock`, `validator`, `templateResolver` | Swappable collaborators (same ports as the .NET SDK).                          |

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
const [result] = await client.saveDraft([invoice]);
if (result?.outcome === 'Success') console.log('saved', result.refId);
else console.warn(result?.errorCategory, result?.failures);

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

## Error handling

Operations that cannot proceed throw `MeInvoiceException`, which carries `category` (a `MeInvoiceErrorCategory`: `Configuration`, `Authentication`, `Validation`, `BatchOrSize`, `MisaThrottled`, `MisaUnavailable`, `TransportFailed`, `MisaUnknown`, ...), plus `rawErrorCode`, `field`, `refId` and `failures` (`{ fieldPath, message, ruleId? }[]`) when known.

Some operations report expected business results as **values, not exceptions**:

- `deleteDraft` returns a `DeleteDraftOutcome` with a `status` (`Deleted`, `NotFound`, `NotDeletable`, ...).
- `lookupByRefId` returns a `LookupBatchOutcomeDto`: batch `status` is `Completed` or `Validation`; each entry has its own `LookupStatus`.
- `saveDraft` returns one `SaveResultDto` per invoice (`outcome: 'Success' | 'Error'` with `errorCategory` / `failures`). `issueReplacement` / `issueAdjustment` return an `AmendmentResultDto` the same way.

## Cancellation and correlation

```ts
const ac = new AbortController();
const templates = await client.listTemplates(true, { signal: ac.signal, correlationId: 'req-42' });
```

Aborting rejects with the native `AbortError` (never wrapped, never retried), including during retry backoff.

## Logging

```ts
import pino from 'pino';
import { createMisaEInvoiceClient, fromPino } from '@misaconnect/einvoice';

const client = createMisaEInvoiceClient({ /* ... */ logger: fromPino(pino()) });
```

Any object with `debug/info/warn/error(message, fields?)` works. Tokens, credentials, tax-code headers, buyer data, line content, amounts and the customer tax code are never logged.

## Money

`Money = number | string`. A string must be a plain decimal (`^-?\d+(\.\d+)?$`) and is written to the JSON body **verbatim as a number literal**, so no precision is lost. JavaScript `number` is a binary double: for foreign-currency amounts, exchange rates and anything that must be exact, compute with a decimal library (decimal.js, big.js, ...) and pass `value.toFixed()` strings. Amounts in responses are `number`.

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

- `preview` validates before resolving the template, so an invalid invoice makes no HTTP call.
- A getlist response with `success:false` and no error code maps to `MisaUnknown` (.NET reports `NotFound` for every RefID).
- An empty RefId list yields a `Validation` batch outcome.
- Batch-size codes `InvoiceQuantityTooLarge` / `XMLTooLong` map to `BatchOrSize`.
- The customer tax code is never logged.
- Each call logs one completion line; failures are logged at `info` with `outcome: 'Error'` and `errorCategory`.
- `changeReason` is logged verbatim (no JSON-fragment redaction) and only when `delete.includeRawErrorMessage` is on.
- Money strings must be plain decimals (`^-?\d+(\.\d+)?$`); the discount-total consistency check skips amounts with more than 28 fractional or 29 integer digits.
- Date-times need an explicit offset (`Z` or `+hh:mm` / `-hh:mm`).

## Sandbox tests

Sandbox tests skip cleanly unless all four variables are set (`MISACONNECT_SANDBOX_TAXCODE`, `MISACONNECT_SANDBOX_USERNAME`, `MISACONNECT_SANDBOX_PASSWORD`, `MISACONNECT_SANDBOX_APPID`):

```
npm run test:sandbox -w @misaconnect/einvoice
```

## License

MIT. Design: [docs/superpowers/specs/2026-11-02-typescript-sdk-design.md](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/superpowers/specs/2026-11-02-typescript-sdk-design.md).

---
title: Errors
parent: TypeScript
grand_parent: EInvoice
nav_order: 4
---

# Errors
{: .no_toc }

1. TOC
{:toc}

## MeInvoiceException

Operations that cannot proceed throw `MeInvoiceException`, which carries `category` (a
`MeInvoiceErrorCategory`: `Configuration`, `Authentication`, `Validation`, `BatchOrSize`,
`MisaThrottled`, `MisaUnavailable`, `TransportFailed`, `MisaUnknown`, ...), plus `rawErrorCode`,
`field`, `refId` and `failures` (`{ fieldPath, message, ruleId? }[]`) when known. The original
error, if any, is on the standard `cause`.

`field` and `component` are also populated (beyond .NET) when the MISA error code names them, e.g.
`InvoiceDetail_Quantity_Line3` gives `field: 'Quantity'`, `component: 'Line3'`.

Check errors with `isMeInvoiceException(err)` rather than `err instanceof MeInvoiceException`. If
both the ESM and the CommonJS build of this package load in one process (the dual-package hazard,
e.g. a dependency `require`s it while your code `import`s it), an exception from one copy is not an
`instanceof` the other copy's class; the guard falls back to `err.name === 'MeInvoiceException'`.

```ts
import { isMeInvoiceException, type IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;

try {
  await client.getDraftPdf('order-2026-0001');
} catch (err) {
  if (!isMeInvoiceException(err)) throw err; // AbortError, TokenCache errors, bugs
  switch (err.category) {
    case 'ResourceNotFound':
      console.warn('no such draft', err.refId);
      break;
    case 'MisaThrottled':
    case 'MisaUnavailable':
    case 'TransportFailed':
      console.warn('try again later', err.rawErrorCode);
      break;
    default:
      console.error(err.category, err.rawErrorCode, err.message, err.failures);
  }
}
```

Not everything a call rejects with is a `MeInvoiceException`: a caller abort rejects with the
native `AbortError` (see [Cancellation and correlation](./operations#cancellation-and-correlation)),
and an error from your own `TokenCache` propagates unchanged (see
[Token cache](./configuration#token-cache)).

## Categories

`MeInvoiceErrorCategory` mirrors the .NET enum, in the same order, and is exported both as a type
and as a frozen object of string constants: `Configuration`, `Authentication`, `TemplateState`,
`DeclarationState`, `Validation`, `BatchOrSize`, `DuplicateOrUniqueness`, `ResourceNotFound`,
`MisaThrottled`, `MisaUnavailable`, `Signing`, `Replacement`, `OtherWorkflow`, `MisaUnknown`,
`NotDeletable`, `TransportFailed`. What each category means, and which are worth retrying, is
described under [Error categories](../index#error-categories).

<!-- snippet: partial -->
```ts
import { MeInvoiceErrorCategory } from '@misaconnect/einvoice';

if (err.category === MeInvoiceErrorCategory.Authentication) {
  // sign-in failed: MISA answers INVALID_USER for a wrong userName, password, taxCode or appId
}
```

Sign-in failures: `INVALID_USER` maps to `Authentication` and `INVALID_TAXCODE` to
`Configuration` (a combined `<taxCode>-<appId>` in `taxCode` is the usual cause; see
[Tax code and AppID](../index#tax-code-and-appid)). Invalid options throw `Configuration` /
`InvalidOptions` from `createMisaEInvoiceClient`.

## Validation errors

A `Validation` error raised from your own input (an invoice, an amendment or paged-lookup request,
an empty batch) has `rawErrorCode: 'ValidationFailed'`; the individual problems are in `failures`,
with the rule id in `failures[].ruleId` (e.g. `FR-047`). A few checks raise their own code
directly: `RefIdInvalid` / `RefIdRequired` for a RefID argument, and `InvalidMoney` /
`InvalidDate` / `InvalidNumber` / `InvalidVatRate` for a bad value. Every operation except
`saveDraft` raises these directly (not wrapped into `ValidationFailed`): `preview`,
`issueReplacement`, `issueAdjustment` and the dates of a lookup request. Only `saveDraft` wraps
them into the affected invoice's `ValidationFailed` result.

## Outcome-returning operations

Some operations report expected business results as **values, not exceptions**:

- `deleteDraft` returns a `DeleteDraftOutcome` with a `status` (`Deleted`, `NotFound`,
  `NotDeletable`, ...).
- `lookupByRefId` returns a `LookupBatchOutcomeDto`: batch `status` is `Completed` or
  `Validation`; each entry has its own `LookupStatus`. The asymmetry: an empty list comes back as a
  `Validation` outcome (a value), while an invalid individual RefId (empty or longer than 64
  characters) throws `Validation` / `RefIdInvalid`.
- `saveDraft` returns one `SaveResultDto` per invoice, in input order (`outcome: 'Success' |
  'Error'` with `errorCategory`, `rawErrorCode`, and `errorMessage` or `failures`). An invoice that
  fails validation gets `errorCategory: 'Validation'`, `rawErrorCode: 'ValidationFailed'` and
  `failures` with paths like `invoices[1].lines[0].unitPrice`. That includes values that cannot be
  put on the wire: a malformed money string, a non-finite number (`NaN`, `Infinity`), an impossible
  date, a date-time without offset. Such an invoice is not sent, and the valid invoices still go
  out in one insert call. An unresolvable template likewise fails only its own invoice. Only the
  batch itself throws: an empty batch (`Validation`) or more than 30 invoices (`BatchOrSize`,
  `rawErrorCode: 'InvoiceQuantityTooLarge'`), both with a `$batch.count` failure.
- `issueReplacement` / `issueAdjustment` return an `AmendmentResultDto` the same way.

The status enums (`DeleteDraftStatus`, `LookupStatus`, `LookupBatchStatus`, `SaveOutcomeDto`,
`InvoiceStatus`) are exported like `MeInvoiceErrorCategory`: as types and as frozen constant
objects.

```ts
import { DeleteDraftStatus, type IMisaEInvoiceClient } from '@misaconnect/einvoice';
declare const client: IMisaEInvoiceClient;

const outcome = await client.deleteDraft('order-2026-0001', true);
switch (outcome.status) {
  case DeleteDraftStatus.Deleted:
  case DeleteDraftStatus.NotFound:
    break; // gone either way
  case DeleteDraftStatus.NotDeletable:
    console.warn('MISA refused to delete it', outcome.rawErrorCode);
    break;
  default:
    console.error('delete failed', outcome.status, outcome.rawErrorCode);
}
```

Raw MISA error text on `deleteDraft` and `lookupByRefId` outcomes appears only with
[`delete.includeRawErrorMessage`](./configuration#deleteincluderawerrormessage).

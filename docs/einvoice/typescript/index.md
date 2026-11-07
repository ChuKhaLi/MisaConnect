---
title: TypeScript
parent: EInvoice
nav_order: 2
has_children: true
permalink: /einvoice/typescript/
---

# EInvoice for TypeScript

```bash
npm install @misaconnect/einvoice    # or: pnpm add @misaconnect/einvoice / yarn add @misaconnect/einvoice
```

[`@misaconnect/einvoice`](https://www.npmjs.com/package/@misaconnect/einvoice) is the TypeScript
SDK for **MISA meInvoice**, a port of the .NET `MisaConnect.EInvoice` package with the same
operations. It has zero runtime dependencies: HTTP goes through Node's built-in `node:https` (or a
`fetch` you supply). It ships ESM and CommonJS builds with full type declarations and requires
**Node.js >= 22**.

One function, `createMisaEInvoiceClient(options)`, returns an `IMisaEInvoiceClient` with eleven
methods: list templates and ticket templates, preview, save / fetch / delete drafts, three lookups,
and issue replacement or adjustment invoices. The MISA concepts behind them (tax code + AppID,
with-code invoices, templates, RefId, paging, error categories) are explained once on the
[EInvoice page](../index#concepts).

- [Getting started](./getting-started): install, first call, environment variables.
- [Configuration](./configuration): options, swappable collaborators, logging, network / TLS.
- [Operations](./operations): every client method, a complete invoice, money and property names.
- [Errors](./errors): `MeInvoiceException`, categories, operations that return outcomes.
- [Sandbox setup](./sandbox-setup): running the live sandbox tests.

Package changelog:
[`src/typescript/einvoice/CHANGELOG.md`](https://github.com/ChuKhaLi/MisaConnect/blob/main/src/typescript/einvoice/CHANGELOG.md).

## Differences from the .NET SDK

- `preview` validates before resolving the template, so an invalid invoice makes no HTTP call.
  `saveDraft` does the same per invoice: a batch with no valid invoice makes no HTTP call, and an
  invoice that is both invalid and has an unresolvable template reports `ValidationFailed` (.NET
  reports the template error).
- Untyped-JS `null` / missing inputs (a `null` invoice, line, buyer, request, date) throw or report
  `Validation` / `ValidationFailed` instead of a `TypeError`; a `null` optional field such as
  `vatRateName` or `sort` counts as absent.
- A getlist response with `success:false` and no error code maps to `MisaUnknown` (.NET reports
  `NotFound` for every RefID).
- An empty RefId list yields a `Validation` batch outcome.
- Paged-lookup request violations throw `rawErrorCode: 'ValidationFailed'` with the rule id in
  `failures[0].ruleId` (.NET puts the rule id, e.g. `FR-047`, in `rawErrorCode`). `preview`
  validation failures likewise carry `ValidationFailed`.
- Batch-size codes `InvoiceQuantityTooLarge` / `XMLTooLong`, and the client-side check for a
  `saveDraft` batch of more than 30 invoices, map to `BatchOrSize`. An empty batch throws
  `Validation` / `ValidationFailed`. The .NET SDK does the same from its next release;
  `MisaConnect.EInvoice` 2.3.0 and earlier report `Validation` for all three, with
  `InvoiceQuantityTooLarge` also on an empty batch.
- `saveDraft` also turns wire-mapping failures (malformed money string, impossible date, date-time
  without offset) into that invoice's own `ValidationFailed` result. .NET's typed DTOs cannot hold
  such values.
- Token-endpoint sign-in failures `INVALID_USER` and `INVALID_TAXCODE` map to `Authentication` and
  `Configuration` (.NET reports `MisaUnknown`). MISA answers `INVALID_USER` for a wrong `userName`,
  `password`, `taxCode` or `appId` alike, so an `Authentication` error at sign-in means any of the
  four may be wrong.
- The customer tax code is never logged.
- Each call logs one completion line; failures are logged at `info` with `outcome: 'Error'` and
  `errorCategory`, a caller abort with `outcome: 'Cancelled'`.
- Money strings must be plain decimals (`^-?\d+(\.\d+)?$`); the discount-total consistency check
  skips amount strings with more than 28 fractional digits and any amount beyond
  `decimal.MaxValue`.
- `KHAC:<rate>%` VAT names take a plain decimal rate (`KHAC:3.5%`); thousands separators, which
  .NET's `NumberStyles.Number` accepts (`KHAC:1,000%`), are rejected (FR-029), as is a rate <= 0
  (the message says the rate must be positive).
- Date-times need an explicit offset (`Z` or `+hh:mm` / `-hh:mm`).
- `field` and `component` on `MeInvoiceException` are also populated when the MISA error code names
  them (see [Errors](./errors#meinvoiceexception)).

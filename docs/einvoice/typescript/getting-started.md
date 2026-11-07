---
title: Getting started
parent: TypeScript
grand_parent: EInvoice
nav_order: 1
---

# Getting started

## Install

Requires **Node.js >= 22**. The package ships ESM (`import`) and CommonJS (`require`) builds with
type declarations, and has no runtime dependencies.

```bash
npm install @misaconnect/einvoice
# or
pnpm add @misaconnect/einvoice
# or
yarn add @misaconnect/einvoice
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

The quick start, the samples (`samples/typescript/`) and the [sandbox tests](./sandbox-setup) all
read the same `MISACONNECT_SANDBOX_*` variables (the names the .NET SDK uses):

```bash
export MISACONNECT_SANDBOX_TAXCODE=0000000000   # bare tax code, without the AppID
export MISACONNECT_SANDBOX_USERNAME=your-misa-user
export MISACONNECT_SANDBOX_PASSWORD=your-misa-password
export MISACONNECT_SANDBOX_APPID=your-misa-app-id
```

## Tax code and AppID

`taxCode` is the bare tax code, without the AppID: when MISA gives you a "Mã số thuế" such as
`6868686868-267`, that is `<taxCode>-<appId>`, so use `taxCode: '6868686868'` and `appId: '267'`.
The SDK sends `<taxCode>-<appId>` itself; passing the combined value makes MISA answer
`INVALID_TAXCODE` (category `Configuration`). See
[Tax code and AppID](../index#tax-code-and-appid).

## Base URL and environment

`baseUrl` must be an `https` URL whose host matches the chosen `environment`
(`testapi.meinvoice.vn` for sandbox, `api.meinvoice.vn` for production). Invalid options throw a
`MeInvoiceException` with category `Configuration` from `createMisaEInvoiceClient`, before any
request. To go to production, set `environment: 'production'`,
`baseUrl: 'https://api.meinvoice.vn/api/integration'` and your production credentials.

## A first draft

With exactly one active template, `saveDraft` picks it for you; otherwise pass `template` on the
invoice (see [Templates and series](../index#templates-and-series)). A full invoice object is on
the [Operations](./operations#a-complete-invoice) page.

<!-- snippet: partial -->
```ts
const [result] = await client.saveDraft([invoice]);
if (result?.outcome === 'Success') {
  const pdf = await client.getDraftPdf(result.refId); // pdf.content is a Uint8Array
  console.log(pdf.contentType, pdf.content.length);
  await client.deleteDraft(result.refId, true);
}
```

Next: [Configuration](./configuration) for all options, [Operations](./operations) for every
method, [Errors](./errors) for what can go wrong.

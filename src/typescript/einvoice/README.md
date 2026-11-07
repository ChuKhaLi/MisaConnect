# @misaconnect/einvoice

TypeScript SDK for **MISA meInvoice** (hóa đơn điện tử): invoice and e-ticket templates, preview, drafts, lookups, replacement and adjustment invoices. Same operations as the .NET `MisaConnect.EInvoice` package. Zero runtime dependencies, ESM and CommonJS, full type declarations. Requires **Node.js >= 22**.

📖 **Full documentation:** <https://chukhali.github.io/MisaConnect/einvoice/typescript/>

## Install

```bash
npm install @misaconnect/einvoice    # or pnpm add / yarn add
```

## Quick start

```ts
import { createMisaEInvoiceClient, isMeInvoiceException } from '@misaconnect/einvoice';

const client = createMisaEInvoiceClient({
  environment: 'sandbox', // or 'production'
  baseUrl: 'https://testapi.meinvoice.vn/api/integration',
  taxCode: process.env.MISACONNECT_SANDBOX_TAXCODE!, // without the AppID
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

## Operations

| Method                                 | Purpose                              |
| -------------------------------------- | ------------------------------------ |
| `listTemplates(withCode?)`             | Active invoice templates.            |
| `listTicketTemplates(request?)`        | One page of e-ticket templates.      |
| `preview(invoice, withCode?)`          | PDF preview without saving.          |
| `saveDraft(invoices, withCode?)`       | Save drafts; one result per invoice. |
| `getDraftPdf(refId, withCode?)`        | PDF of a saved draft.                |
| `deleteDraft(refId, invoiceWithCode)`  | Delete a draft (outcome-returning).  |
| `lookupByRefId(refIds, withCode)`      | Look up invoices by RefId.           |
| `lookupStandard` / `lookupCalculating` | Paged lookup by date range.          |
| `issueReplacement` / `issueAdjustment` | Replacement / adjustment invoices.   |

Every method takes an optional last `opts` (`{ signal?, correlationId? }`).

More: [Operations](https://chukhali.github.io/MisaConnect/einvoice/typescript/operations) · [Configuration](https://chukhali.github.io/MisaConnect/einvoice/typescript/configuration) · [Errors](https://chukhali.github.io/MisaConnect/einvoice/typescript/errors) · [Concepts](https://chukhali.github.io/MisaConnect/einvoice/#concepts) · [Sandbox tests](https://chukhali.github.io/MisaConnect/einvoice/typescript/sandbox-setup) · [CHANGELOG](https://github.com/ChuKhaLi/MisaConnect/blob/main/src/typescript/einvoice/CHANGELOG.md).

Calling MISA from outside Vietnam? Read [this first](https://chukhali.github.io/MisaConnect/einvoice/#calling-misa-from-outside-vietnam).

## License

MIT. Community project, not affiliated with MISA JSC.

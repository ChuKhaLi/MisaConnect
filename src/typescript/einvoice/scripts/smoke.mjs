// Runtime smoke test without dev tooling, so it runs on the oldest supported Node (20).
import { MeInvoiceErrorCategory } from '@misaconnect/einvoice';
if (MeInvoiceErrorCategory.Validation !== 'Validation')
  throw new Error(`ESM import broken: ${MeInvoiceErrorCategory.Validation}`);
console.log(`ESM ok on Node ${process.version}`);

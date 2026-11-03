// Runtime smoke test without dev tooling, so it runs on the oldest supported Node (20).
const { MeInvoiceErrorCategory } = require('@misaconnect/einvoice');
if (MeInvoiceErrorCategory.Validation !== 'Validation')
  throw new Error(`CJS require broken: ${MeInvoiceErrorCategory.Validation}`);
console.log(`CJS ok on Node ${process.version}`);

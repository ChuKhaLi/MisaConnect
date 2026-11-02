// Runtime smoke test without dev tooling, so it runs on the oldest supported Node (20).
const { PACKAGE_NAME } = require('@misaconnect/einvoice');
if (PACKAGE_NAME !== '@misaconnect/einvoice')
  throw new Error(`CJS require broken: ${PACKAGE_NAME}`);
console.log(`CJS ok on Node ${process.version}`);

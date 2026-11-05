// Runtime smoke test without dev tooling, so it runs on the oldest supported Node (20).
const { VERSION } = require('@misaconnect/esign');
if (typeof VERSION !== 'string' || VERSION.length === 0)
  throw new Error(`CJS require broken: ${VERSION}`);
console.log(`CJS ok (esign ${VERSION}) on Node ${process.version}`);

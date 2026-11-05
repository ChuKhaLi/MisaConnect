// Runtime smoke test without dev tooling, so it runs on the oldest supported Node (20).
import { VERSION } from '@misaconnect/esign';
if (typeof VERSION !== 'string' || VERSION.length === 0)
  throw new Error(`ESM import broken: ${VERSION}`);
console.log(`ESM ok (esign ${VERSION}) on Node ${process.version}`);

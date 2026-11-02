// Runtime smoke test without dev tooling, so it runs on the oldest supported Node (20).
import { PACKAGE_NAME } from '@misaconnect/einvoice';
if (PACKAGE_NAME !== '@misaconnect/einvoice') throw new Error(`ESM import broken: ${PACKAGE_NAME}`);
console.log(`ESM ok on Node ${process.version}`);

import { createRequire } from 'node:module';
import { describe, it, expect } from 'vitest';

describe('package exports', () => {
  it('loads via ESM import', async () => {
    const mod = await import('@misaconnect/einvoice');
    expect(mod.PACKAGE_NAME).toBe('@misaconnect/einvoice');
  });

  it('loads via CommonJS require', () => {
    const mod = createRequire(import.meta.url)('@misaconnect/einvoice') as {
      PACKAGE_NAME: string;
    };
    expect(mod.PACKAGE_NAME).toBe('@misaconnect/einvoice');
  });
});

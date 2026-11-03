import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMisaEInvoiceClient, type IMisaEInvoiceClient } from '../../src/client.js';
import type { Logger } from '../../src/application/ports.js';
import type { MisaEInvoiceOptions } from '../../src/infrastructure/options.js';
import { startFakeMisa, type FakeMisa } from '../fake/fake-misa-server.js';

let fake: FakeMisa;

function client(over: Partial<MisaEInvoiceOptions> = {}): IMisaEInvoiceClient {
  return createMisaEInvoiceClient({
    environment: 'sandbox',
    baseUrl: fake.baseUrl,
    taxCode: '0101243150',
    userName: 'user@example.com',
    password: 'pa55-secret',
    appId: 'APP-1',
    __unsafeAllowAnyBaseUrl: true,
    ...over,
  } as MisaEInvoiceOptions);
}

beforeEach(async () => {
  fake = await startFakeMisa();
});
afterEach(async () => {
  vi.unstubAllGlobals();
  await fake.close();
});

describe('default fetch is resolved per call', () => {
  it('uses a globalThis.fetch installed after the client was created', async () => {
    const c = client();
    const original = globalThis.fetch;
    const stub = vi.fn((input: string | URL | Request, init?: RequestInit) =>
      original(input, init),
    );
    vi.stubGlobal('fetch', stub);
    await c.listTemplates();
    expect(stub).toHaveBeenCalled();
  });
});

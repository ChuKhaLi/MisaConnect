import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMisaEInvoiceClient, type IMisaEInvoiceClient } from '../../src/client.js';
import type { Logger } from '../../src/application/ports.js';
import type { MisaEInvoiceOptions } from '../../src/infrastructure/options.js';
import { startFakeMisa, type FakeMisa } from '../fake/fake-misa-server.js';
import { invoice } from '../fixtures/readme-invoice.js';

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

const throwingLogger = (levels: readonly (keyof Logger)[]): Logger => {
  const at = (level: keyof Logger) => (): void => {
    if (levels.includes(level)) throw new Error(`logger ${level} exploded`);
  };
  return { debug: at('debug'), info: at('info'), warn: at('warn'), error: at('error') };
};

describe('a throwing logger never changes the operation result', () => {
  it('saveDraft still resolves with Success when logger.info throws', async () => {
    const results = await client({ logger: throwingLogger(['info']) }).saveDraft([invoice]);
    expect(results).toEqual([{ refId: 'order-2026-0001', outcome: 'Success' }]);
    expect(fake.state.requests.filter((r) => r.path === '/webapp/insert')).toHaveLength(1);
  });

  it('a failing call rejects with its own error, not the logger error', async () => {
    const err = await client({ logger: throwingLogger(['info']) })
      .getDraftPdf('missing-ref')
      .catch((e: unknown) => e);
    expect(err).toMatchObject({ name: 'MeInvoiceException', category: 'ResourceNotFound' });
  });

  it('deleteDraft still returns NotFound when the ARCH-DEL-001 warn throws', async () => {
    fake.state.setDeleteError('r1', 'InvalidTransactionID', 'Lỗi không xác định khác');
    const outcome = await client({ logger: throwingLogger(['warn']) }).deleteDraft('r1', true);
    expect(outcome.status).toBe('NotFound');
  });
});

describe('caller abort is logged as Cancelled', () => {
  const capture = (): { logger: Logger; fields: Record<string, unknown>[] } => {
    const fields: Record<string, unknown>[] = [];
    const at = (_m: string, f?: Record<string, unknown>): void => {
      if (f !== undefined) fields.push(f);
    };
    return { logger: { debug: at, info: at, warn: at, error: at }, fields };
  };

  it('an aborted signal logs outcome Cancelled without errorCategory', async () => {
    const { logger, fields } = capture();
    const ac = new AbortController();
    ac.abort();
    const err = await client({ logger })
      .listTemplates(true, { signal: ac.signal })
      .catch((e: unknown) => e);
    expect((err as Error).name).toBe('AbortError');
    expect(fields).toHaveLength(1);
    expect(fields[0]).toMatchObject({ endpoint: 'ListTemplates', outcome: 'Cancelled' });
    expect(fields[0]).not.toHaveProperty('errorCategory');
  });

  it('an abort with a custom reason is also Cancelled', async () => {
    const { logger, fields } = capture();
    const ac = new AbortController();
    const reason = new Error('shutting down');
    ac.abort(reason);
    const err = await client({ logger })
      .listTemplates(true, { signal: ac.signal })
      .catch((e: unknown) => e);
    expect(err).toBe(reason);
    expect(fields[0]).toMatchObject({ outcome: 'Cancelled' });
    expect(fields[0]).not.toHaveProperty('errorCategory');
  });
});

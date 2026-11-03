import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMisaEInvoiceClient, type IMisaEInvoiceClient } from '../../src/client.js';
import { createMisaEInvoiceClientInternal } from '../../src/client-internal.js';
import type { Logger } from '../../src/application/ports.js';
import { MeInvoiceException } from '../../src/domain/errors.js';
import type { MisaEInvoiceOptions } from '../../src/infrastructure/options.js';
import { startFakeMisa, type FakeMisa } from '../fake/fake-misa-server.js';
import { invoice } from '../fixtures/readme-invoice.js';

let fake: FakeMisa;

function client(over: Partial<MisaEInvoiceOptions> = {}): IMisaEInvoiceClient {
  return createMisaEInvoiceClientInternal(
    {
      environment: 'sandbox',
      baseUrl: fake.baseUrl,
      taxCode: '0101243150',
      userName: 'user@example.com',
      password: 'pa55-secret',
      appId: 'APP-1',
      ...over,
    } as MisaEInvoiceOptions,
    { allowAnyBaseUrl: true },
  );
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

describe('public factory has no base-URL escape hatch', () => {
  it('ignores __unsafeAllowAnyBaseUrl and rejects a non-MISA http baseUrl', () => {
    const attempt = (): unknown =>
      createMisaEInvoiceClient({
        environment: 'sandbox',
        baseUrl: 'http://127.0.0.1:1',
        taxCode: '0101243150',
        userName: 'user@example.com',
        password: 'pa55-secret',
        appId: 'APP-1',
        __unsafeAllowAnyBaseUrl: true,
      } as unknown as MisaEInvoiceOptions);
    expect(attempt).toThrow(
      expect.objectContaining({ category: 'Configuration', rawErrorCode: 'InvalidOptions' }),
    );
  });
});

describe('untyped-JS null inputs never surface a raw TypeError', () => {
  const any = <T>(v: unknown): T => v as T;
  const rejection = async (p: Promise<unknown>): Promise<unknown> =>
    p.then(
      () => undefined,
      (e: unknown) => e,
    );
  const isValidation = (err: unknown, field?: string): void => {
    expect(err).toBeInstanceOf(MeInvoiceException);
    expect(err).toMatchObject({ category: 'Validation', rawErrorCode: 'ValidationFailed' });
    if (field !== undefined) {
      expect((err as MeInvoiceException).failures?.map((f) => f.fieldPath)).toContain(field);
    }
  };

  it('saveDraft(null) throws Validation', async () => {
    isValidation(await rejection(client().saveDraft(any(null))), 'invoices');
  });

  it('saveDraft([null]) reports a per-invoice ValidationFailed at invoices[0]', async () => {
    const [r] = await client().saveDraft(any([null]));
    expect(r).toMatchObject({ outcome: 'Error', rawErrorCode: 'ValidationFailed' });
    expect(r?.failures?.map((f) => f.fieldPath)).toEqual(['invoices[0]']);
  });

  it('saveDraft with a null line reports invoices[0].lines[0]', async () => {
    const [r] = await client().saveDraft([{ ...invoice, lines: any([null]) }]);
    expect(r?.failures?.map((f) => f.fieldPath)).toContain('invoices[0].lines[0]');
  });

  it('saveDraft with a null buyer or missing dates is a per-invoice ValidationFailed, not sent', async () => {
    const results = await client().saveDraft([
      { ...invoice, refId: 'a', buyerType: 2, buyer: any(null) },
      { ...invoice, refId: 'b', invDate: any(undefined), createdDate: any(null) },
    ]);
    expect(results[0]).toMatchObject({ outcome: 'Error', rawErrorCode: 'ValidationFailed' });
    expect(results[0]?.failures?.map((f) => f.fieldPath)).toContain('invoices[0].buyer');
    expect(results[1]).toMatchObject({ outcome: 'Error', rawErrorCode: 'ValidationFailed' });
    expect(results[1]?.failures?.map((f) => f.fieldPath)).toEqual(['invoices[1].invDate']);
    expect(fake.state.requests.filter((r) => r.path === '/webapp/insert')).toHaveLength(0);
  });

  it('a null vatRateName is treated as absent', async () => {
    const line = { ...invoice.lines[0], vatRateName: any(null) };
    const results = await client().saveDraft([{ ...invoice, lines: [any(line)] }]);
    expect(results[0]).toMatchObject({ outcome: 'Success' });
  });

  it('preview(null) throws Validation', async () => {
    isValidation(await rejection(client().preview(any(null))), 'invoice');
  });

  it('issueReplacement(null) / issueAdjustment(null) throw Validation', async () => {
    isValidation(await rejection(client().issueReplacement(any(null))), '$request');
    isValidation(await rejection(client().issueAdjustment(any(null))), '$request');
  });

  it('lookupStandard(null) / lookupCalculating(null) throw Validation', async () => {
    isValidation(await rejection(client().lookupStandard(any(null), true)), 'request');
    isValidation(await rejection(client().lookupCalculating(any(null), true)), 'request');
  });

  it('a null sort defaults to InvDate', async () => {
    const res = await client().lookupStandard(
      { start: 0, length: 10, sort: any(null), fromDate: '2026-01-01', toDate: '2026-12-31' },
      true,
    );
    expect(res.items).toEqual([]);
    const sent = fake.state.requests.find((r) => r.path === '/webapp/paging');
    expect((JSON.parse(sent?.bodyText ?? '{}') as Record<string, unknown>)['Sort']).toBe('InvDate');
  });

  it('null template rows and rows without a string InvSeries are skipped', async () => {
    const good = fake.state.templates[0];
    fake.state.templates = any([
      null,
      { ...good, IPTemplateID: 'no-series', InvSeries: null },
      good,
    ]);
    const templates = await client().listTemplates();
    expect(templates.map((t) => t.ipTemplateID)).toEqual([good?.IPTemplateID]);
  });
});

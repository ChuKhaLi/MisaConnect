import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createMisaEInvoiceClient, type IMisaEInvoiceClient } from '../../src/client.js';
import type { Logger } from '../../src/application/ports.js';
import type { InvoiceDto } from '../../src/domain/dtos.js';
import { createMeInvoiceGateway } from '../../src/infrastructure/gateway.js';
import type { ResolvedOptions } from '../../src/infrastructure/options.js';
import type { MeInvoiceGateway, MeInvoiceTransport } from '../../src/application/ports.js';
import { defaultInvoiceValidator } from '../../src/application/invoice-validator.js';
import { resolveTemplate } from '../../src/application/template-resolver.js';
import { saveDraftInvoices } from '../../src/application/use-cases/save-draft.js';
import { MeInvoiceException } from '../../src/domain/errors.js';
import type { MisaEInvoiceOptions } from '../../src/infrastructure/options.js';
import { startFakeMisa, type FakeMisa } from '../fake/fake-misa-server.js';

const BUYER = 'Nguyen Van Buyer';
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

let fake: FakeMisa;
let lines: { fields: Record<string, unknown> | undefined }[];

function capture(): Logger {
  const at = (_m: string, fields?: Record<string, unknown>): void => {
    lines.push({ fields });
  };
  return { debug: at, info: at, warn: at, error: at };
}

function client(over: Partial<MisaEInvoiceOptions> = {}): IMisaEInvoiceClient {
  return createMisaEInvoiceClient({
    environment: 'sandbox',
    baseUrl: fake.baseUrl,
    taxCode: '0101243150',
    userName: 'user@example.com',
    password: 'pa55-secret',
    appId: 'APP-1',
    logger: capture(),
    __unsafeAllowAnyBaseUrl: true,
    ...over,
  } as MisaEInvoiceOptions);
}

const invoice = (over: Partial<InvoiceDto> = {}): InvoiceDto => ({
  template: { ipTemplateID: 'ipt-active-1', invSeries: 'C26TAA' },
  invDate: '2026-05-12',
  createdDate: '2026-05-12T10:30:00+07:00',
  modifiedDate: '2026-05-12T10:30:00+07:00',
  currency: 'VND',
  exchangeRate: 1,
  paymentMethod: 'TM/CK',
  buyerType: 2,
  buyer: { name: BUYER, taxCode: '0312345678', address: '1 Le Loi' },
  lines: [
    {
      inventoryItemType: 0,
      sortOrder: 1,
      description: 'Item',
      unitName: 'cái',
      quantity: 1,
      unitPrice: 100,
      amountOC: 100,
      amount: 100,
      amountWithoutVATOC: 100,
      amountWithoutVAT: 100,
    },
  ],
  totals: {
    totalSaleAmountOC: 100,
    totalSaleAmount: 100,
    totalDiscountAmountOC: 0,
    totalDiscountAmount: 0,
    totalAmountWithoutVATOC: 100,
    totalAmountWithoutVAT: 100,
    totalVATAmountOC: 0,
    totalVATAmount: 0,
    totalAmountOC: 100,
    totalAmount: 100,
    totalAmountInWords: 'Một trăm đồng',
  },
  ...over,
});

const nonToken = () => fake.state.requests.filter((r) => r.path !== '/webapp/token');

beforeEach(async () => {
  fake = await startFakeMisa();
  lines = [];
});
afterEach(async () => {
  await fake.close();
});

describe('saveDraft', () => {
  it('returns results in input order with per-RefID errors mapped', async () => {
    fake.state.failInsertFor('r2', 'DuplicateInvoiceRefID', 'dup');
    const results = await client().saveDraft([
      invoice({ refId: 'r1' }),
      invoice({ refId: 'r2' }),
      invoice({ refId: 'r3' }),
    ]);
    expect(results.map((r) => [r.refId, r.outcome])).toEqual([
      ['r1', 'Success'],
      ['r2', 'Error'],
      ['r3', 'Success'],
    ]);
    expect(results[1]).toMatchObject({
      errorCategory: 'DuplicateOrUniqueness',
      rawErrorCode: 'DuplicateInvoiceRefID',
    });

    const reqs = nonToken().filter((r) => r.path === '/webapp/insert');
    expect(reqs).toHaveLength(1);
    expect(reqs[0]?.method).toBe('POST');
    expect(new URLSearchParams(reqs[0]?.query).get('invoiceWithCode')).toBe('true');
    const body = JSON.parse(reqs[0]?.bodyText ?? '') as { RefID: string }[];
    expect(body.map((b) => b.RefID)).toEqual(['r1', 'r2', 'r3']);
  });

  it('a RefID missing from the echo → MisaUnknown NoEchoedResult', async () => {
    fake.state.dropEchoFor('r3');
    const results = await client().saveDraft([invoice({ refId: 'r1' }), invoice({ refId: 'r3' })]);
    expect(results[0]?.outcome).toBe('Success');
    expect(results[1]).toMatchObject({
      refId: 'r3',
      outcome: 'Error',
      errorCategory: 'MisaUnknown',
      rawErrorCode: 'NoEchoedResult',
    });
  });

  it('31 invoices throws BatchOrSize InvoiceQuantityTooLarge ($batch.count) and sends nothing', async () => {
    const many = Array.from({ length: 31 }, (_, i) => invoice({ refId: `r${i}` }));
    let err: unknown;
    try {
      await client().saveDraft(many);
    } catch (e) {
      err = e;
    }
    expect(err).toBeInstanceOf(MeInvoiceException);
    const ex = err as MeInvoiceException;
    expect(ex.category).toBe('BatchOrSize');
    expect(ex.rawErrorCode).toBe('InvoiceQuantityTooLarge');
    expect(ex.failures).toEqual([
      {
        fieldPath: '$batch.count',
        message: 'Batch size 31 exceeds the MISA limit of 30 invoices per request.',
        ruleId: 'FR-030',
      },
    ]);
    expect(fake.state.requests).toHaveLength(0);
  });

  it('an invalid invoice becomes its own ValidationFailed result; the others are submitted', async () => {
    const results = await client().saveDraft([
      invoice({ refId: 'r1' }),
      invoice({ refId: 'r2', lines: [] }),
      invoice({ refId: 'r3' }),
    ]);
    expect(results.map((r) => [r.refId, r.outcome])).toEqual([
      ['r1', 'Success'],
      ['r2', 'Error'],
      ['r3', 'Success'],
    ]);
    expect(results[1]).toMatchObject({
      errorCategory: 'Validation',
      rawErrorCode: 'ValidationFailed',
    });
    expect(results[1]?.failures?.length).toBeGreaterThan(0);
    expect(results[1]?.failures?.every((f) => f.fieldPath.startsWith('invoices[1].'))).toBe(true);
    const reqs = nonToken().filter((r) => r.path === '/webapp/insert');
    expect(reqs).toHaveLength(1);
    const body = JSON.parse(reqs[0]?.bodyText ?? '') as { RefID: string }[];
    expect(body.map((b) => b.RefID)).toEqual(['r1', 'r3']);
  });

  it('a bad money string on invoice 2 fails only invoice 2 with an indexed field path', async () => {
    const bad = invoice({ refId: 'r2' });
    bad.lines = bad.lines.map((l) => ({ ...l, unitPrice: '1e3' }));
    const results = await client().saveDraft([invoice({ refId: 'r1' }), bad]);
    expect(results[0]).toEqual({ refId: 'r1', outcome: 'Success' });
    expect(results[1]).toMatchObject({
      refId: 'r2',
      outcome: 'Error',
      errorCategory: 'Validation',
      rawErrorCode: 'ValidationFailed',
    });
    expect(results[1]?.failures?.map((f) => f.fieldPath)).toEqual([
      'invoices[1].lines[0].unitPrice',
    ]);
    const req = nonToken().find((r) => r.path === '/webapp/insert');
    const body = JSON.parse(req?.bodyText ?? '') as { RefID: string }[];
    expect(body.map((b) => b.RefID)).toEqual(['r1']);
  });

  it('a date-time without offset on the only invoice: ValidationFailed and no insert request', async () => {
    const bad = invoice({ refId: 'r1', createdDate: '2026-05-12T10:30:00' });
    const results = await client().saveDraft([bad]);
    expect(results[0]).toMatchObject({
      outcome: 'Error',
      rawErrorCode: 'ValidationFailed',
      failures: [expect.objectContaining({ fieldPath: 'invoices[0].createdDate' })],
    });
    expect(nonToken().some((r) => r.path === '/webapp/insert')).toBe(false);
  });

  it('a gateway that returns no outcome for an item yields MisaUnknown NoEchoedResult', async () => {
    const gateway = {
      listTemplates: () =>
        Promise.resolve([{ ipTemplateID: 'ipt-active-1', invSeries: 'C26TAA', isActive: true }]),
      insertInvoices: () => Promise.resolve([]),
    } as unknown as MeInvoiceGateway;
    const out = await saveDraftInvoices(
      {
        gateway,
        validator: defaultInvoiceValidator,
        templateResolver: resolveTemplate,
        refIdGenerator: () => 'gen',
      },
      [invoice({ refId: 'r1' })],
      true,
    );
    expect(out.results).toEqual([
      {
        refId: 'r1',
        outcome: 'Error',
        errorCategory: 'MisaUnknown',
        rawErrorCode: 'NoEchoedResult',
      },
    ]);
  });

  it('assigns UUID RefIDs to invoices without one', async () => {
    const results = await client().saveDraft([invoice(), invoice({ refId: 'keep' })]);
    expect(results[0]?.refId).toMatch(UUID);
    expect(results[1]?.refId).toBe('keep');
    const req = nonToken().find((r) => r.path === '/webapp/insert');
    const body = JSON.parse(req?.bodyText ?? '') as { RefID: string }[];
    expect(body[0]?.RefID).toBe(results[0]?.refId);
  });

  it('logs endpoint SaveDraft with a {first}+N-more refId label and no buyer data', async () => {
    await client().saveDraft([
      invoice({ refId: 'a' }),
      invoice({ refId: 'b' }),
      invoice({ refId: 'c' }),
    ]);
    expect(lines).toHaveLength(1);
    expect(lines[0]?.fields).toMatchObject({
      endpoint: 'SaveDraft',
      outcome: 'Success',
      refId: 'a+2-more',
      templateId: 'ipt-active-1',
    });
    const text = JSON.stringify(lines);
    expect(text).not.toContain(BUYER);
    expect(text).not.toContain('0312345678');
  });
});

describe('saveDraft per-invoice template resolution', () => {
  const tpl = (id: string): Partial<InvoiceDto> => ({
    template: { ipTemplateID: id, invSeries: '' },
  });

  it('sends one insert with each invoice under its own template', async () => {
    const results = await client().saveDraft([
      invoice({ refId: 'a', ...tpl('ipt-active-1') }),
      invoice({ refId: 'b', ...tpl('ipt-active-2') }),
    ]);
    expect(results.map((r) => r.outcome)).toEqual(['Success', 'Success']);
    const reqs = nonToken().filter((r) => r.path === '/webapp/insert');
    expect(reqs).toHaveLength(1);
    const body = JSON.parse(reqs[0]?.bodyText ?? '') as { InvoiceTemplateID: string }[];
    expect(body.map((b) => b.InvoiceTemplateID)).toEqual(['ipt-active-1', 'ipt-active-2']);
  });

  it('an unknown template fails only that item (TemplateState); the rest are sent', async () => {
    const results = await client().saveDraft([
      invoice({ refId: 'a', ...tpl('ipt-active-1') }),
      invoice({ refId: 'b', ...tpl('ipt-inactive-1') }),
      invoice({ refId: 'c', ...tpl('ipt-active-2') }),
    ]);
    expect(results.map((r) => [r.refId, r.outcome])).toEqual([
      ['a', 'Success'],
      ['b', 'Error'],
      ['c', 'Success'],
    ]);
    expect(results[1]).toMatchObject({
      errorCategory: 'TemplateState',
      rawErrorCode: 'UnknownTemplate',
    });
    const req = nonToken().find((r) => r.path === '/webapp/insert');
    const body = JSON.parse(req?.bodyText ?? '') as { RefID: string }[];
    expect(body.map((b) => b.RefID)).toEqual(['a', 'c']);
  });

  it('a missing template ref with two active templates is an Ambiguous (Configuration) item', async () => {
    const noTpl = invoice({ refId: 'a' });
    delete noTpl.template;
    const results = await client().saveDraft([noTpl, invoice({ refId: 'b' })]);
    expect(results[0]).toMatchObject({
      outcome: 'Error',
      errorCategory: 'Configuration',
      rawErrorCode: 'AmbiguousTemplate',
    });
    expect(results[1]?.outcome).toBe('Success');
  });

  it('all unresolvable: no insert request, every item Error, no templateId logged', async () => {
    const results = await client().saveDraft([
      invoice({ refId: 'a', ...tpl('nope-1') }),
      invoice({ refId: 'b', ...tpl('nope-2') }),
    ]);
    expect(results.map((r) => r.outcome)).toEqual(['Error', 'Error']);
    expect(nonToken().some((r) => r.path === '/webapp/insert')).toBe(false);
    expect(lines[0]?.fields).not.toHaveProperty('templateId');
  });

  it('empty batch throws Validation with $batch.count and sends nothing', async () => {
    const err = await client()
      .saveDraft([])
      .catch((e: unknown) => e);
    expect(err).toBeInstanceOf(MeInvoiceException);
    expect((err as MeInvoiceException).failures?.some((f) => f.fieldPath === '$batch.count')).toBe(
      true,
    );
    expect(fake.state.requests).toHaveLength(0);
  });

  it('results follow input order even when MISA echoes in reverse', async () => {
    const transport: MeInvoiceTransport = {
      request: () =>
        Promise.resolve({
          success: true,
          data: [{ RefID: 'b' }, { RefID: 'a' }],
          error: undefined,
          errorCode: undefined,
        }) as never,
    };
    const gw = createMeInvoiceGateway(transport, {} as ResolvedOptions);
    const out = await gw.insertInvoices(
      [
        { invoice: invoice(), refId: 'a', template: { ipTemplateID: 't' } as never },
        { invoice: invoice(), refId: 'b', template: { ipTemplateID: 't' } as never },
      ],
      true,
    );
    expect(out.map((o) => o.refId)).toEqual(['a', 'b']);
  });

  it('a failed item errorMessage never reaches the log lines', async () => {
    fake.state.failInsertFor('b', 'InvalidInvoiceData', 'SECRET-MISA-TEXT');
    const results = await client().saveDraft([invoice({ refId: 'a' }), invoice({ refId: 'b' })]);
    expect(results[1]?.errorMessage).toBe('SECRET-MISA-TEXT');
    expect(JSON.stringify(lines)).not.toContain('SECRET-MISA-TEXT');
  });
});

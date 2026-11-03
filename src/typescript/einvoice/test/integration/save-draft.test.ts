import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createMisaEInvoiceClient, type IMisaEInvoiceClient } from '../../src/client.js';
import type { Logger } from '../../src/application/ports.js';
import type { InvoiceDto } from '../../src/domain/dtos.js';
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

  it('31 invoices throws Validation ($batch.count) and sends nothing', async () => {
    const many = Array.from({ length: 31 }, (_, i) => invoice({ refId: `r${i}` }));
    let err: unknown;
    try {
      await client().saveDraft(many);
    } catch (e) {
      err = e;
    }
    expect(err).toBeInstanceOf(MeInvoiceException);
    const ex = err as MeInvoiceException;
    expect(ex.category).toBe('Validation');
    expect(ex.failures?.some((f) => f.fieldPath === '$batch.count')).toBe(true);
    expect(fake.state.requests).toHaveLength(0);
  });

  it('an invalid invoice in the batch throws Validation with indexed paths, no HTTP', async () => {
    const err = await client()
      .saveDraft([invoice({ refId: 'r1' }), invoice({ refId: 'r2', lines: [] })])
      .catch((e: unknown) => e);
    expect(err).toBeInstanceOf(MeInvoiceException);
    expect(
      (err as MeInvoiceException).failures?.some((f) => f.fieldPath.startsWith('invoices[1]')),
    ).toBe(true);
    expect(fake.state.requests).toHaveLength(0);
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

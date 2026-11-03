import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createMisaEInvoiceClient, type IMisaEInvoiceClient } from '../../src/client.js';
import type { Logger } from '../../src/application/ports.js';
import type {
  AdjustmentRequestDto,
  InvoiceDto,
  ReplacementRequestDto,
} from '../../src/domain/dtos.js';
import { MeInvoiceException } from '../../src/domain/errors.js';
import type { MisaEInvoiceOptions } from '../../src/infrastructure/options.js';
import { startFakeMisa, type FakeMisa } from '../fake/fake-misa-server.js';

const REASON = 'Sua thong tin nguoi mua';
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

let fake: FakeMisa;
let lines: { message: string; fields: Record<string, unknown> | undefined }[];

function capture(): Logger {
  const at = (message: string, fields?: Record<string, unknown>): void => {
    lines.push({ message, fields });
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
  buyer: { name: 'Nguyen Van Buyer', taxCode: '0312345678', address: '1 Le Loi' },
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

const request = (
  over: Partial<ReplacementRequestDto & AdjustmentRequestDto> = {},
): ReplacementRequestDto & AdjustmentRequestDto => ({
  invoice: invoice({ refId: 'fresh-1' }),
  originalRef: {
    orgRefID: 'orig-001',
    orgInvNo: '00000123',
    orgInvTemplateNo: '1',
    orgInvSeries: 'C26TAA',
    orgInvDate: '2026-05-10',
  },
  changeReason: REASON,
  ...over,
});

const nonToken = () => fake.state.requests.filter((r) => r.path !== '/webapp/token');
const inserts = () => nonToken().filter((r) => r.path === '/webapp/insert');
const insertBody = (): Record<string, unknown> =>
  (JSON.parse(inserts()[0]?.bodyText ?? '[]') as Record<string, unknown>[])[0] ?? {};

beforeEach(async () => {
  fake = await startFakeMisa();
  lines = [];
});
afterEach(async () => {
  await fake.close();
});

describe('issueReplacement', () => {
  it('posts EInvoiceStatus 3 with the Org* fields and echoes orgRefId', async () => {
    const result = await client().issueReplacement(request());
    expect(result).toMatchObject({ refId: 'fresh-1', outcome: 'Success', orgRefId: 'orig-001' });
    expect(inserts()).toHaveLength(1);
    expect(new URLSearchParams(inserts()[0]?.query).get('invoiceWithCode')).toBe('true');
    expect(insertBody()).toMatchObject({
      RefID: 'fresh-1',
      EInvoiceStatus: '3',
      OrgRefID: 'orig-001',
      OrgInvNo: '00000123',
      OrgInvTemplateNo: '1',
      OrgInvSeries: 'C26TAA',
      OrgInvDate: '2026-05-10',
      ChangeReason: REASON,
    });
  });

  it('assigns a new RefId when absent and honours invoiceWithCode=false', async () => {
    const result = await client().issueReplacement(
      request({ invoice: invoice(), invoiceWithCode: false }),
    );
    expect(result.refId).toMatch(UUID);
    expect(new URLSearchParams(inserts()[0]?.query).get('invoiceWithCode')).toBe('false');
    expect(insertBody()['RefID']).toBe(result.refId);
  });

  it('InvoiceCannotReplace → Error / Replacement result (no throw)', async () => {
    fake.state.failInsertFor('fresh-1', 'InvoiceCannotReplace', 'nope');
    const result = await client().issueReplacement(request());
    expect(result).toMatchObject({
      refId: 'fresh-1',
      outcome: 'Error',
      errorCategory: 'Replacement',
      rawErrorCode: 'InvoiceCannotReplace',
      orgRefId: 'orig-001',
    });
  });

  it('missing orgInvNo throws Validation and sends no request', async () => {
    const bad = request({ originalRef: { ...request().originalRef, orgInvNo: ' ' } });
    const err = await client()
      .issueReplacement(bad)
      .catch((e: unknown) => e);
    expect(err).toBeInstanceOf(MeInvoiceException);
    expect((err as MeInvoiceException).category).toBe('Validation');
    expect((err as MeInvoiceException).failures?.map((f) => f.ruleId)).toContain('FR-059');
    expect(fake.state.requests).toHaveLength(0);
  });

  it('blank changeReason throws Validation FR-060; invalid invoice throws before HTTP', async () => {
    const c = client();
    await expect(c.issueReplacement(request({ changeReason: '   ' }))).rejects.toMatchObject({
      category: 'Validation',
      failures: expect.arrayContaining([expect.objectContaining({ ruleId: 'FR-060' })]),
    });
    await expect(
      c.issueReplacement(request({ invoice: invoice({ refId: 'x', lines: [] }) })),
    ).rejects.toMatchObject({ category: 'Validation' });
    expect(fake.state.requests).toHaveLength(0);
  });

  it('unknown template throws TemplateState', async () => {
    await expect(
      client().issueReplacement(
        request({
          invoice: invoice({ refId: 'a', template: { ipTemplateID: 'nope', invSeries: 'C26TAA' } }),
        }),
      ),
    ).rejects.toMatchObject({ category: 'TemplateState' });
    expect(inserts()).toHaveLength(0);
  });

  it('transport failure (non-JSON 502) throws MisaUnavailable', async () => {
    fake.state.queueStatus(502, 2);
    await expect(client().issueReplacement(request())).rejects.toMatchObject({
      category: 'MisaUnavailable',
    });
  });
});

describe('issueAdjustment', () => {
  it('posts EInvoiceStatus 4 and serialises negative deltas verbatim', async () => {
    const inv = invoice({
      refId: 'adj-1',
      totals: { ...invoice().totals, totalAmountOC: -55000, totalAmount: -55000 },
    });
    const result = await client().issueAdjustment(request({ invoice: inv }));
    expect(result).toMatchObject({ refId: 'adj-1', outcome: 'Success', orgRefId: 'orig-001' });
  });

  it('HasAdjustmentInvoice → Error / Replacement', async () => {
    fake.state.failInsertFor('fresh-1', 'HasAdjustmentInvoice', 'dup');
    const result = await client().issueAdjustment(request());
    expect(result).toMatchObject({
      outcome: 'Error',
      errorCategory: 'Replacement',
      rawErrorCode: 'HasAdjustmentInvoice',
    });
  });

  it('missing orgRefID throws Validation, no request', async () => {
    const bad = request({ originalRef: { ...request().originalRef, orgRefID: '' } });
    await expect(client().issueAdjustment(bad)).rejects.toMatchObject({ category: 'Validation' });
    expect(fake.state.requests).toHaveLength(0);
  });
});

describe('amendment logging', () => {
  const all = (): string => JSON.stringify(lines);

  it('omits changeReason unless delete.includeRawErrorMessage is on', async () => {
    await client().issueReplacement(request());
    expect(all()).not.toContain(REASON);
    expect(lines.at(-1)?.fields).toMatchObject({
      endpoint: 'IssueReplacement',
      outcome: 'Success',
      orgRefId: 'orig-001',
      eInvoiceStatus: '3',
    });
    expect(lines.at(-1)?.fields).not.toHaveProperty('changeReason');
    expect(all()).not.toContain('0312345678');
  });

  it('includes changeReason when the flag is on', async () => {
    await client({ delete: { includeRawErrorMessage: true } }).issueAdjustment(request());
    expect(lines.at(-1)?.fields).toMatchObject({
      endpoint: 'IssueAdjustment',
      eInvoiceStatus: '4',
      changeReason: REASON,
    });
  });

  it('a returned Error result logs Error with its category', async () => {
    fake.state.failInsertFor('fresh-1', 'InvoiceCannotReplace', 'nope');
    await client().issueReplacement(request());
    expect(lines.at(-1)?.fields).toMatchObject({
      outcome: 'Error',
      errorCategory: 'Replacement',
      refId: 'fresh-1',
    });
  });

  it('validation failure logs Error / Validation', async () => {
    await client()
      .issueReplacement(request({ changeReason: '' }))
      .catch(() => undefined);
    expect(lines.at(-1)?.fields).toMatchObject({ outcome: 'Error', errorCategory: 'Validation' });
  });
});

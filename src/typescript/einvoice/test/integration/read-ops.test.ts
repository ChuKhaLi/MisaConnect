import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createMisaEInvoiceClient, type IMisaEInvoiceClient } from '../../src/client.js';
import { createMisaEInvoiceClientInternal } from '../../src/client-internal.js';
import type { Logger } from '../../src/application/ports.js';
import type { InvoiceDto } from '../../src/domain/dtos.js';
import { MeInvoiceException } from '../../src/domain/errors.js';
import type { MisaEInvoiceOptions } from '../../src/infrastructure/options.js';
import { startFakeMisa, type FakeMisa } from '../fake/fake-misa-server.js';

const PASSWORD = 'pa55-secret';
const BUYER = 'Nguyen Van Buyer';

interface LogLine {
  level: string;
  message: string;
  fields: Record<string, unknown> | undefined;
}

let fake: FakeMisa;
let lines: LogLine[];

function capture(): Logger {
  const at =
    (level: string) =>
    (message: string, fields?: Record<string, unknown>): void => {
      lines.push({ level, message, fields });
    };
  return { debug: at('debug'), info: at('info'), warn: at('warn'), error: at('error') };
}

function client(over: Partial<MisaEInvoiceOptions> = {}): IMisaEInvoiceClient {
  return createMisaEInvoiceClientInternal(
    {
      environment: 'sandbox',
      baseUrl: fake.baseUrl,
      taxCode: '0101243150',
      userName: 'user@example.com',
      password: PASSWORD,
      appId: 'APP-1',
      logger: capture(),
      ...over,
    } as MisaEInvoiceOptions,
    { allowAnyBaseUrl: true },
  );
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

const ascii = (bytes: Uint8Array, n: number): string =>
  String.fromCharCode(...Array.from(bytes.subarray(0, n)));
const nonToken = () => fake.state.requests.filter((r) => r.path !== '/webapp/token');

async function caught(p: Promise<unknown>): Promise<MeInvoiceException> {
  try {
    await p;
  } catch (e) {
    if (e instanceof MeInvoiceException) return e;
    throw e;
  }
  throw new Error('expected MeInvoiceException');
}

beforeEach(async () => {
  fake = await startFakeMisa();
  lines = [];
});
afterEach(async () => {
  await fake.close();
});

describe('createMisaEInvoiceClient', () => {
  it('rejects a non-MISA baseUrl without the internal escape hatch', () => {
    expect(() =>
      createMisaEInvoiceClient({
        baseUrl: fake.baseUrl,
        taxCode: 't',
        userName: 'u',
        password: 'p',
        appId: 'a',
      }),
    ).toThrow(MeInvoiceException);
  });
});

describe('listTemplates', () => {
  it('returns only active templates and posts the templates body', async () => {
    const templates = await client().listTemplates();
    expect(templates.map((t) => t.ipTemplateID)).toEqual(['ipt-active-1', 'ipt-active-2']);
    expect(templates.every((t) => t.isActive)).toBe(true);
    expect(templates[1]?.isMoreVATRate).toBe(true);

    const [req] = nonToken();
    expect(req?.method).toBe('POST');
    expect(req?.path).toBe('/webapp/templates');
    expect(new URLSearchParams(req?.query).get('isTicket')).toBe('false');
    expect(new URLSearchParams(req?.query).get('invoiceWithCode')).toBe('true');
    expect(JSON.parse(req?.bodyText ?? '')).toEqual({
      TaxCode: '0101243150-APP-1',
      UserName: 'user@example.com',
      Password: PASSWORD,
    });
  });

  it('keeps a row with a null InvSeries (as blank), drops null/non-object rows and rows without a string IPTemplateID', async () => {
    const good = fake.state.templates[0];
    fake.state.templates.push(
      null as never,
      'junk' as never,
      { ...good, IPTemplateID: 7 } as never,
      { ...good, IPTemplateID: undefined } as never,
      { ...good, IPTemplateID: 'ipt-noseries', InvSeries: null } as never,
    );
    const templates = await client().listTemplates();
    expect(templates.map((t) => t.ipTemplateID)).toEqual([
      'ipt-active-1',
      'ipt-active-2',
      'ipt-noseries',
    ]);
    expect(templates[2]?.invSeries).toBe('');
  });

  it('sends invoiceWithCode=false when asked', async () => {
    await client().listTemplates(false);
    expect(new URLSearchParams(nonToken()[0]?.query).get('invoiceWithCode')).toBe('false');
  });
  it('an aborted signal rejects with the native AbortError and sends nothing', async () => {
    const ctrl = new AbortController();
    ctrl.abort();
    await expect(client().listTemplates(true, { signal: ctrl.signal })).rejects.toMatchObject({
      name: 'AbortError',
    });
    expect(fake.state.requests).toHaveLength(0);
  });
});

describe('preview', () => {
  it('invalid invoice throws Validation with failures and makes no HTTP request', async () => {
    const err = await caught(client().preview(invoice({ lines: [] })));
    expect(err.category).toBe('Validation');
    expect(err.failures?.length).toBeGreaterThan(0);
    expect(fake.state.requests).toHaveLength(0);
  });

  it('returns PDF bytes and posts the wire invoice with a generated RefID', async () => {
    const c = client({ refIdGenerator: () => 'gen-ref-1' });
    const pdf = await c.preview(invoice());
    expect(pdf.content).toBeInstanceOf(Uint8Array);
    expect(ascii(pdf.content, 4)).toBe('%PDF');
    expect(pdf.contentType).toBe('application/pdf');

    const req = nonToken().find((r) => r.path === '/webapp/preview');
    expect(req?.method).toBe('POST');
    expect(new URLSearchParams(req?.query).get('invoiceWithCode')).toBe('true');
    const body = JSON.parse(req?.bodyText ?? '') as Record<string, unknown>;
    expect(body['RefID']).toBe('gen-ref-1');
    expect(body['InvoiceTemplateID']).toBe('ipt-active-1');
    expect(body['InvSeries']).toBe('C26TAA');
  });

  it('two active templates and no template ref → AmbiguousTemplate (Configuration)', async () => {
    const noTemplate: InvoiceDto = invoice();
    delete noTemplate.template;
    const err = await caught(client().preview(noTemplate));
    expect(err.category).toBe('Configuration');
    expect(err.rawErrorCode).toBe('AmbiguousTemplate');
  });

  it('inactive template ref → UnknownTemplate (TemplateState)', async () => {
    const err = await caught(
      client().preview(invoice({ template: { ipTemplateID: 'ipt-inactive-1', invSeries: '' } })),
    );
    expect(err.category).toBe('TemplateState');
    expect(err.rawErrorCode).toBe('UnknownTemplate');
    expect(nonToken().some((r) => r.path === '/webapp/preview')).toBe(false);
  });
});

describe('getDraftPdf', () => {
  it('known RefID returns PDF bytes via GET viewrefid', async () => {
    fake.state.seedDraft({ RefID: 'r-known' });
    const pdf = await client().getDraftPdf('r-known');
    expect(ascii(pdf.content, 5)).toBe('%PDF-');
    const [req] = nonToken();
    expect(req?.method).toBe('GET');
    expect(req?.path).toBe('/webapp/viewrefid');
    expect(new URLSearchParams(req?.query).get('refid')).toBe('r-known');
    expect(new URLSearchParams(req?.query).get('invoiceWithCode')).toBe('true');
  });

  it('unknown RefID → ResourceNotFound RefIdNotFound with refId', async () => {
    const err = await caught(client().getDraftPdf('r-missing'));
    expect(err.category).toBe('ResourceNotFound');
    expect(err.rawErrorCode).toBe('RefIdNotFound');
    expect(err.refId).toBe('r-missing');
  });

  it('blank RefID → Validation RefIdRequired before any HTTP', async () => {
    const err = await caught(client().getDraftPdf('  '));
    expect(err.category).toBe('Validation');
    expect(err.rawErrorCode).toBe('RefIdRequired');
    expect(fake.state.requests).toHaveLength(0);
  });
});

describe('call logging', () => {
  it('logs one info line per call with the FR-014 shape and no secrets or buyer data', async () => {
    const c = client({ correlationId: () => 'cid-default' });
    fake.state.seedDraft({ RefID: 'r-known' });
    await c.listTemplates();
    await c.preview(invoice({ refId: 'r-prev' }), true, { correlationId: 'cid-explicit' });
    await c.getDraftPdf('r-known');
    await caught(c.getDraftPdf('r-missing'));
    await caught(c.preview(invoice({ lines: [] })));

    expect(lines.map((l) => [l.level, l.fields?.['endpoint'], l.fields?.['outcome']])).toEqual([
      ['info', 'ListTemplates', 'Success'],
      ['info', 'Preview', 'Success'],
      ['info', 'GetDraftPdfByRefId', 'Success'],
      ['info', 'GetDraftPdfByRefId', 'Error'],
      ['info', 'Preview', 'Error'],
    ]);
    expect(lines[1]?.fields).toMatchObject({
      refId: 'r-prev',
      templateId: 'ipt-active-1',
      correlationId: 'cid-explicit',
    });
    expect(lines[0]?.fields?.['correlationId']).toBe('cid-default');
    expect(lines[3]?.fields).toMatchObject({
      refId: 'r-missing',
      errorCategory: 'ResourceNotFound',
    });
    expect(lines[4]?.fields?.['errorCategory']).toBe('Validation');
    for (const l of lines) {
      expect(typeof l.fields?.['durationMs']).toBe('number');
      expect(l.fields).not.toHaveProperty('customerTaxCode');
      const text = JSON.stringify(l);
      expect(text).not.toContain(PASSWORD);
      expect(text).not.toContain(BUYER);
      expect(text).not.toContain('0101243150');
      expect(text).not.toContain('0312345678');
    }
  });
});

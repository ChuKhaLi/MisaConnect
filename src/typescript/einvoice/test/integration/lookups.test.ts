import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createMisaEInvoiceClient, type IMisaEInvoiceClient } from '../../src/client.js';
import type { Logger } from '../../src/application/ports.js';
import type { PagedLookupRequestDto } from '../../src/domain/dtos.js';
import { MeInvoiceException } from '../../src/domain/errors.js';
import type { MisaEInvoiceOptions } from '../../src/infrastructure/options.js';
import { startFakeMisa, type FakeMisa } from '../fake/fake-misa-server.js';

let fake: FakeMisa;
let logs: { level: string; message: string; fields: Record<string, unknown> | undefined }[];

function capture(): Logger {
  const at =
    (level: string) =>
    (message: string, fields?: Record<string, unknown>): void => {
      logs.push({ level, message, fields });
    };
  return { debug: at('debug'), info: at('info'), warn: at('warn'), error: at('error') };
}

function client(includeRawErrorMessage = false): IMisaEInvoiceClient {
  return createMisaEInvoiceClient({
    environment: 'sandbox',
    baseUrl: fake.baseUrl,
    taxCode: '0101243150',
    userName: 'user@example.com',
    password: 'pa55-secret',
    appId: 'APP-1',
    logger: capture(),
    delete: { includeRawErrorMessage },
    __unsafeAllowAnyBaseUrl: true,
  } as MisaEInvoiceOptions);
}

const nonToken = () => fake.state.requests.filter((r) => r.path !== '/webapp/token');
const onPath = (path: string) => fake.state.requests.filter((r) => r.path === path);
const infoLines = () => logs.filter((l) => l.level === 'info');
const ids = (n: number) => Array.from({ length: n }, (_, i) => `r-${i}`);

const range = (over: Partial<PagedLookupRequestDto> = {}): PagedLookupRequestDto => ({
  start: 0,
  length: 10,
  fromDate: '2026-05-01',
  toDate: '2026-05-31',
  ...over,
});

beforeEach(async () => {
  fake = await startFakeMisa();
  logs = [];
});
afterEach(async () => {
  await fake.close();
});

describe('lookupByRefId', () => {
  it('120 refIds → 3 getlist requests of 50/50/20; outcomes in input order', async () => {
    for (let i = 0; i < 120; i += 2) fake.state.seedDraft({ RefID: `r-${i}` });
    // Reverse input so order is not MISA's storage order.
    const input = [...ids(120)].reverse();
    const result = await client().lookupByRefId(input, true);

    const reqs = onPath('/webapp/getlist');
    expect(reqs.map((r) => (JSON.parse(r.bodyText) as string[]).length)).toEqual([50, 50, 20]);
    expect(reqs[0]?.method).toBe('POST');
    expect(reqs[0]?.query).toBe('invoiceWithCode=true');
    expect(result.status).toBe('Completed');
    expect(result.outcomes?.map((o) => o.refId)).toEqual(input);
    for (const o of result.outcomes ?? []) {
      const even = Number(o.refId.slice(2)) % 2 === 0;
      expect(o.status).toBe(even ? 'Found' : 'NotFound');
      if (even) {
        expect(o.snapshot?.refId).toBe(o.refId);
        expect(o.snapshot?.status).toBe('Draft');
      } else {
        expect(o.snapshot).toBeUndefined();
      }
    }
  });

  it('duplicates in input each get an outcome', async () => {
    fake.state.seedDraft({ RefID: 'r-a' });
    const result = await client().lookupByRefId(['r-a', 'r-b', 'r-a'], false);
    expect(result.outcomes?.map((o) => [o.refId, o.status])).toEqual([
      ['r-a', 'Found'],
      ['r-b', 'NotFound'],
      ['r-a', 'Found'],
    ]);
    expect(onPath('/webapp/getlist')[0]?.query).toBe('invoiceWithCode=false');
  });

  it('auth failure on chunk 1 → all 120 AuthFailed; no request for later chunks', async () => {
    fake.state.queue401(2);
    const result = await client().lookupByRefId(ids(120), true);
    expect(result.status).toBe('Completed');
    expect(result.outcomes).toHaveLength(120);
    expect(result.outcomes?.every((o) => o.status === 'AuthFailed')).toBe(true);
    // original attempt + the single post-refresh retry, both for chunk 1 (50 ids)
    const reqs = onPath('/webapp/getlist');
    expect(reqs).toHaveLength(2);
    expect(reqs.every((r) => (JSON.parse(r.bodyText) as string[]).length === 50)).toBe(true);
    expect(infoLines()[0]?.fields).toMatchObject({
      endpoint: 'LookupByRefId',
      outcome: 'Error',
      errorCategory: 'Authentication',
      refId: 'r-0+119-more',
    });
  });

  it('configuration envelope error on chunk 1 is terminal too', async () => {
    fake.state.failNextLookup('InvalidAppID', 'bad app');
    const result = await client().lookupByRefId(ids(120), true);
    expect(result.outcomes?.every((o) => o.status === 'Configuration')).toBe(true);
    expect(result.outcomes?.[0]?.errorCode).toBe('InvalidAppID');
    expect(onPath('/webapp/getlist')).toHaveLength(1);
  });

  it('non-terminal chunk failure does not stop later chunks', async () => {
    fake.state.seedDraft({ RefID: 'r-55' });
    fake.state.queueStatus(503, 2);
    const result = await client().lookupByRefId(ids(60), true);
    const out = result.outcomes ?? [];
    expect(out.slice(0, 50).every((o) => o.status === 'MisaUnavailable')).toBe(true);
    expect(out[55]?.status).toBe('Found');
    expect(out[51]?.status).toBe('NotFound');
    expect(infoLines()[0]?.fields).toMatchObject({
      outcome: 'Error',
      errorCategory: 'MisaUnavailable',
    });
  });

  it('message is gated by delete.includeRawErrorMessage', async () => {
    fake.state.failNextLookup('SomeUnmappedCode', 'raw misa text');
    const off = await client(false).lookupByRefId(['r1'], true);
    expect(off.outcomes?.[0]?.status).toBe('MisaUnknown');
    expect(off.outcomes?.[0]?.errorCode).toBe('SomeUnmappedCode');
    expect(off.outcomes?.[0]).not.toHaveProperty('message');

    fake.state.failNextLookup('SomeUnmappedCode', 'raw misa text');
    const on = await client(true).lookupByRefId(['r1'], true);
    expect(on.outcomes?.[0]?.message).toContain('raw misa text');
  });

  it('empty list → Validation batch outcome without HTTP; logged as Error/Validation', async () => {
    const result = await client().lookupByRefId([], true);
    expect(result.status).toBe('Validation');
    expect(result.reason).toBe('RefIds list cannot be empty.');
    expect(result.outcomes).toBeUndefined();
    expect(fake.state.requests).toHaveLength(0);
    expect(infoLines()[0]?.fields).toMatchObject({ outcome: 'Error', errorCategory: 'Validation' });
  });

  it('empty / over-long individual RefId → Validation RefIdInvalid exception, no HTTP', async () => {
    for (const bad of ['', 'x'.repeat(65)]) {
      const err = await client()
        .lookupByRefId(['ok', bad], true)
        .catch((e: unknown) => e);
      expect(err).toBeInstanceOf(MeInvoiceException);
      expect((err as MeInvoiceException).rawErrorCode).toBe('RefIdInvalid');
    }
    expect(fake.state.requests).toHaveLength(0);
  });

  it('Found/NotFound-only batch logs Success and no snapshot data', async () => {
    fake.state.seedDraft({ RefID: 'r-a', AccountObjectName: 'Nguyen Van A' });
    await client().lookupByRefId(['r-a', 'r-b'], true);
    expect(infoLines()[0]?.fields).toMatchObject({
      endpoint: 'LookupByRefId',
      outcome: 'Success',
    });
    expect(JSON.stringify(logs)).not.toContain('Nguyen Van A');
  });

  it('caller abort rejects with AbortError', async () => {
    const ac = new AbortController();
    ac.abort();
    const err = await client()
      .lookupByRefId(['r1'], true, { signal: ac.signal })
      .catch((e: unknown) => e);
    expect((err as Error).name).toBe('AbortError');
  });
});

describe('lookup data shapes', () => {
  it('stringified-array data is handled for getlist and paging', async () => {
    fake.state.seedDraft({ RefID: 'r-a' });
    fake.state.stringifyLookupData = true;
    const byRef = await client().lookupByRefId(['r-a'], true);
    expect(byRef.outcomes?.[0]).toMatchObject({ status: 'Found', snapshot: { refId: 'r-a' } });
    const paged = await client().lookupStandard(range(), true);
    expect(paged.items.map((i) => i.refId)).toEqual(['r-a']);
  });

  it('non-object rows are skipped without throwing', async () => {
    fake.state.rawLookupRows = [
      null,
      5,
      'x',
      [1],
      { RefID: 'r-a', EInvoiceStatus: '1', PublishStatus: '0' },
    ];
    const byRef = await client().lookupByRefId(['r-a'], true);
    expect(byRef.outcomes?.[0]?.status).toBe('Found');
    const paged = await client().lookupCalculating(range(), true);
    expect(paged.items.map((i) => i.refId)).toEqual(['r-a']);
    expect(paged.returnedCount).toBe(1);
  });
});

describe('lookupStandard / lookupCalculating', () => {
  it('standard posts to webapp/paging with the wire body; maps snapshots', async () => {
    fake.state.seedDraft({ RefID: 's1', InvDate: '2026-05-12' });
    fake.state.seedDraft({ RefID: 'c1', InvDate: '2026-05-12' }, { calculating: true });
    const result = await client().lookupStandard(
      range({ start: 0, length: 5, sort: 'InvDate', publishStatus: 0 }),
      true,
    );
    const [req] = nonToken();
    expect(req?.method).toBe('POST');
    expect(req?.path).toBe('/webapp/paging');
    expect(req?.query).toBe('invoiceWithCode=true');
    expect(JSON.parse(req?.bodyText ?? '')).toEqual({
      Start: 0,
      Length: 5,
      Sort: 'InvDate',
      FromDate: '2026-05-01',
      ToDate: '2026-05-31',
      PublishStatus: '0',
    });
    expect(result.items.map((i) => i.refId)).toEqual(['s1']);
    expect(result.items[0]?.status).toBe('Draft');
    expect(result).toMatchObject({ start: 0, length: 5, returnedCount: 1 });
  });

  it('calculating hits webapp/paging/calculating; Date inputs formatted yyyy-MM-dd', async () => {
    fake.state.seedDraft({ RefID: 's1' });
    fake.state.seedDraft({ RefID: 'c1' }, { calculating: true });
    const result = await client().lookupCalculating(
      range({ fromDate: new Date(2026, 4, 1), toDate: new Date(2026, 4, 31) }),
      false,
    );
    const [req] = nonToken();
    expect(req?.path).toBe('/webapp/paging/calculating');
    expect(req?.query).toBe('invoiceWithCode=false');
    const body = JSON.parse(req?.bodyText ?? '') as Record<string, unknown>;
    expect(body['FromDate']).toBe('2026-05-01');
    expect(body['ToDate']).toBe('2026-05-31');
    expect(body).not.toHaveProperty('PublishStatus');
    expect(body['Sort']).toBe('InvDate');
    expect(result.items.map((i) => i.refId)).toEqual(['c1']);
  });

  it.each([
    [{ length: 0 }, 'length'],
    [{ length: 101 }, 'length'],
    [{ start: -1 }, 'start'],
    [{ fromDate: '2026-06-01' }, 'fromDate'],
    [{ publishStatus: 3 }, 'publishStatus'],
  ])('invalid request %j → Validation on %s without HTTP', async (over, field) => {
    for (const op of ['lookupStandard', 'lookupCalculating'] as const) {
      const c = client();
      const err = await c[op](range(over), true).catch((e: unknown) => e);
      expect(err).toBeInstanceOf(MeInvoiceException);
      expect((err as MeInvoiceException).category).toBe('Validation');
      expect((err as MeInvoiceException).field).toBe(field);
      expect((err as MeInvoiceException).failures).toHaveLength(1);
    }
    expect(fake.state.requests).toHaveLength(0);
  });

  it('envelope error throws the mapped category', async () => {
    fake.state.failNextLookup('InvalidTaxCode', 'tax');
    const err = await client()
      .lookupStandard(range(), true)
      .catch((e: unknown) => e);
    expect((err as MeInvoiceException).category).toBe('Configuration');
    expect((err as MeInvoiceException).rawErrorCode).toBe('InvalidTaxCode');
    expect(infoLines()[0]?.fields).toMatchObject({
      endpoint: 'LookupStandard',
      outcome: 'Error',
      errorCategory: 'Configuration',
    });
  });

  it('success logs endpoint names LookupStandard / LookupCalculating', async () => {
    await client().lookupStandard(range(), true);
    await client().lookupCalculating(range(), true);
    expect(infoLines().map((l) => [l.fields?.['endpoint'], l.fields?.['outcome']])).toEqual([
      ['LookupStandard', 'Success'],
      ['LookupCalculating', 'Success'],
    ]);
  });

  it('caller abort rejects with AbortError', async () => {
    const ac = new AbortController();
    ac.abort();
    const err = await client()
      .lookupStandard(range(), true, { signal: ac.signal })
      .catch((e: unknown) => e);
    expect((err as Error).name).toBe('AbortError');
  });
});

describe('outcome-returning operations log the effective outcome', () => {
  it('deleteDraft failure status logs Error with category; NotFound/NotDeletable stay Success', async () => {
    fake.state.queueStatus(503, 2);
    await client().deleteDraft('r1', true);
    fake.state.setDeleteError('r2', 'InvalidTransactionID', 'Hóa đơn không tồn tại');
    await client().deleteDraft('r2', true);
    fake.state.setDeleteError('r3', 'InvalidTransactionID', 'Hóa đơn đã phát hành');
    await client().deleteDraft('r3', true);
    expect(infoLines().map((l) => [l.fields?.['outcome'], l.fields?.['errorCategory']])).toEqual([
      ['Error', 'MisaUnavailable'],
      ['Success', undefined],
      ['Success', undefined],
    ]);
  });
});

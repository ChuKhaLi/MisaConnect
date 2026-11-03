import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { IMisaEInvoiceClient } from '../../src/client.js';
import { createMisaEInvoiceClientInternal } from '../../src/client-internal.js';
import type { Logger } from '../../src/application/ports.js';
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

function client(
  over: Partial<MisaEInvoiceOptions> = {},
  includeRawErrorMessage = false,
): IMisaEInvoiceClient {
  return createMisaEInvoiceClientInternal(
    {
      environment: 'sandbox',
      baseUrl: fake.baseUrl,
      taxCode: '0101243150',
      userName: 'user@example.com',
      password: 'pa55-secret',
      appId: 'APP-1',
      logger: capture(),
      delete: { includeRawErrorMessage },
      ...over,
    } as MisaEInvoiceOptions,
    { allowAnyBaseUrl: true },
  );
}

const nonToken = () => fake.state.requests.filter((r) => r.path !== '/webapp/token');
const archWarnings = () =>
  logs.filter((l) => l.level === 'warn' && l.fields?.['eventId'] === 'ARCH-DEL-001');

beforeEach(async () => {
  fake = await startFakeMisa();
  logs = [];
});
afterEach(async () => {
  await fake.close();
});

describe('deleteDraft', () => {
  it('success → Deleted, DELETE with url-encoded refid', async () => {
    const outcome = await client().deleteDraft('ref a&b', true);
    expect(outcome).toEqual({ status: 'Deleted', refId: 'ref a&b' });
    const reqs = nonToken();
    expect(reqs).toHaveLength(1);
    expect(reqs[0]?.method).toBe('DELETE');
    expect(reqs[0]?.path).toBe('/webapp/delete');
    expect(reqs[0]?.query).toBe('invoiceWithCode=true&refid=ref%20a%26b');
    const info = logs.find((l) => l.level === 'info');
    expect(info?.fields).toMatchObject({
      endpoint: 'DeleteDraft',
      outcome: 'Success',
      refId: 'ref a&b',
    });
  });

  it('"không tồn tại" → NotFound, no drift warning', async () => {
    fake.state.setDeleteError('r1', 'InvalidTransactionID', 'Hóa đơn không tồn tại');
    const outcome = await client().deleteDraft('r1', true);
    expect(outcome).toEqual({
      status: 'NotFound',
      rawErrorCode: 'InvalidTransactionID',
      refId: 'r1',
    });
    expect(archWarnings()).toHaveLength(0);
  });

  it('"đã phát hành" → NotDeletable', async () => {
    fake.state.setDeleteError('r1', 'InvalidTransactionID', 'Hóa đơn đã phát hành');
    const outcome = await client().deleteDraft('r1', true);
    expect(outcome.status).toBe('NotDeletable');
    expect(outcome.rawErrorCode).toBe('InvalidTransactionID');
    expect(archWarnings()).toHaveLength(0);
  });

  it('unmatched phrase → NotFound (default) + exactly one ARCH-DEL-001 warn', async () => {
    fake.state.setDeleteError('r1', 'InvalidTransactionID', 'Lỗi không xác định khác');
    const outcome = await client().deleteDraft('r1', true);
    expect(outcome.status).toBe('NotFound');
    const w = archWarnings();
    expect(w).toHaveLength(1);
    expect(w[0]?.fields).toEqual({ eventId: 'ARCH-DEL-001', errorCode: 'InvalidTransactionID' });
    expect(w[0]?.message).toContain('ARCH-DEL-001');
    expect(JSON.stringify(logs)).not.toContain('Lỗi không xác định');
  });

  it('unmatched phrase with the flag on → excerpt (max 80 chars) in the warning', async () => {
    const long = 'x'.repeat(100);
    fake.state.setDeleteError('r1', 'InvalidTransactionID', long);
    await client({}, true).deleteDraft('r1', true);
    const w = archWarnings();
    expect(w).toHaveLength(1);
    expect(w[0]?.fields?.['errorMessageExcerpt']).toBe('x'.repeat(80));
  });

  it('message is undefined by default and the MISA text with the flag on', async () => {
    fake.state.setDeleteError('r1', 'InvalidTransactionID', 'Hóa đơn đã phát hành');
    const off = await client().deleteDraft('r1', true);
    expect(off.message).toBeUndefined();
    expect('message' in off).toBe(false);
    const on = await client({}, true).deleteDraft('r1', true);
    expect(on.message).toBe('Hóa đơn đã phát hành');
  });

  it('other MISA error codes are mapped (DuplicateInvoiceRefID → MisaUnknown, InvalidTokenCode → AuthFailed)', async () => {
    fake.state.setDeleteError('r1', 'SomethingNew', 'boom');
    const o = await client().deleteDraft('r1', true);
    expect(o).toEqual({ status: 'MisaUnknown', rawErrorCode: 'SomethingNew', refId: 'r1' });
    fake.state.setDeleteError('r2', 'InvalidAppID', 'bad app');
    const c = await client({}, true).deleteDraft('r2', true);
    expect(c).toMatchObject({
      status: 'Configuration',
      rawErrorCode: 'InvalidAppID',
      message: 'bad app',
    });
  });

  it('queued 503 x2 → MisaUnavailable status, no throw', async () => {
    fake.state.queueStatus(503, 2);
    const outcome = await client().deleteDraft('r1', true);
    expect(outcome.status).toBe('MisaUnavailable');
    expect(outcome.refId).toBe('r1');
    const info = logs.find((l) => l.level === 'info');
    expect(info?.fields).toMatchObject({
      endpoint: 'DeleteDraft',
      outcome: 'Error',
      errorCategory: 'MisaUnavailable',
    });
  });

  it('persistent 401 → AuthFailed status, no throw', async () => {
    fake.state.queue401(2);
    const outcome = await client().deleteDraft('r1', true);
    expect(outcome.status).toBe('AuthFailed');
  });

  it('empty / over-long refId → Validation RefIdInvalid exception, no HTTP call', async () => {
    for (const bad of ['', 'x'.repeat(65)]) {
      const err = await client()
        .deleteDraft(bad, true)
        .catch((e: unknown) => e);
      expect(err).toBeInstanceOf(MeInvoiceException);
      expect((err as MeInvoiceException).category).toBe('Validation');
      expect((err as MeInvoiceException).rawErrorCode).toBe('RefIdInvalid');
    }
    expect(fake.state.requests).toHaveLength(0);
  });

  it('caller abort rejects with AbortError (not swallowed into an outcome)', async () => {
    const ac = new AbortController();
    ac.abort();
    const err = await client()
      .deleteDraft('r1', true, { signal: ac.signal })
      .catch((e: unknown) => e);
    expect((err as Error).name).toBe('AbortError');
  });
});

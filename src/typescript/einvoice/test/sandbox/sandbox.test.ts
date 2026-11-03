import { connect } from 'node:net';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import {
  createMisaEInvoiceClient,
  MeInvoiceException,
  type IMisaEInvoiceClient,
  type InvoiceDto,
} from '../../src/index.js';

// Opt-in live-sandbox flows (port of the .NET Sandbox/* tests, minus amendments and the
// fixture pool). Skipped unless all four credentials are set AND the sandbox host answers.
const HOST = 'testapi.meinvoice.vn';
const PORT = 443;
const PROBE_TIMEOUT_MS = 1500;
const BASE_URL = 'https://testapi.meinvoice.vn/api/integration';

const taxCode = process.env['MISACONNECT_SANDBOX_TAXCODE']?.trim();
const userName = process.env['MISACONNECT_SANDBOX_USERNAME']?.trim();
const password = process.env['MISACONNECT_SANDBOX_PASSWORD']?.trim();
const appId = process.env['MISACONNECT_SANDBOX_APPID']?.trim();
const hasSandboxEnv = !!taxCode && !!userName && !!password && !!appId;

/**
 * One-shot TCP-connect probe with a short timeout; never throws. It dials the host directly
 * and therefore ignores HTTP(S) proxy settings: behind a proxy-only network it reports
 * "unreachable" and the suite skips even though fetch (via a proxy) could have succeeded.
 */
function isReachable(): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = connect({ host: HOST, port: PORT });
    const done = (ok: boolean): void => {
      socket.destroy();
      resolve(ok);
    };
    socket.setTimeout(PROBE_TIMEOUT_MS, () => done(false));
    socket.once('connect', () => done(true));
    socket.once('error', () => done(false));
  });
}

// Only probe the network when credentials exist, so the no-credentials path is instant.
const runnable = hasSandboxEnv && (await isReachable());

function createClient(overridePassword?: string): IMisaEInvoiceClient {
  return createMisaEInvoiceClient({
    environment: 'sandbox',
    baseUrl: BASE_URL,
    taxCode: taxCode ?? '',
    userName: userName ?? '',
    password: overridePassword ?? password ?? '',
    appId: appId ?? '',
  });
}

/** Mirrors the .NET sandbox SampleInvoiceFactory: template omitted (auto-resolved), 10% VAT. */
function sampleInvoice(refId?: string): InvoiceDto {
  const now = new Date().toISOString();
  return {
    ...(refId !== undefined ? { refId } : {}),
    invDate: '2026-05-11',
    createdDate: now,
    modifiedDate: now,
    currency: 'VND',
    exchangeRate: 1,
    paymentMethod: 'TM',
    buyerType: 1,
    buyer: { name: 'Buyer', taxCode: '0123456789' },
    lines: [
      {
        inventoryItemType: 0,
        sortOrder: 1,
        description: 'Goods',
        unitName: 'Cái',
        quantity: 1,
        unitPrice: 1_000_000,
        amountOC: 1_000_000,
        amount: 1_000_000,
        amountWithoutVATOC: 1_000_000,
        amountWithoutVAT: 1_000_000,
        vatRateName: '10%',
        vatAmountOC: 100_000,
        vatAmount: 100_000,
        sortOrderView: 1,
      },
    ],
    totals: {
      totalSaleAmountOC: 1_000_000,
      totalSaleAmount: 1_000_000,
      totalDiscountAmountOC: 0,
      totalDiscountAmount: 0,
      totalAmountWithoutVATOC: 1_000_000,
      totalAmountWithoutVAT: 1_000_000,
      totalVATAmountOC: 100_000,
      totalVATAmount: 100_000,
      totalAmountOC: 1_100_000,
      totalAmount: 1_100_000,
      totalAmountInWords: 'Một triệu một trăm nghìn',
    },
  };
}

describe.skipIf(!runnable)('MISA sandbox (live)', () => {
  // Created in beforeAll (which only runs when the suite is not skipped): option validation
  // rejects empty credentials, so it cannot be built at collection time.
  let client: IMisaEInvoiceClient;
  beforeAll(() => {
    client = createClient();
  });
  const created = new Set<string>();

  /** Saves one draft and tracks it for afterAll cleanup. */
  async function saveOne(): Promise<string> {
    const refId = crypto.randomUUID();
    created.add(refId);
    const [result] = await client.saveDraft([sampleInvoice(refId)]);
    expect(result?.outcome).toBe('Success');
    expect(result?.refId).toBe(refId);
    return refId;
  }

  afterAll(async () => {
    // Best effort: delete is idempotent, so already-deleted drafts are fine.
    await Promise.allSettled([...created].map((refId) => client.deleteDraft(refId, true)));
  });

  it('listTemplates is repeatable: two consecutive calls return the same templates', async () => {
    const first = await client.listTemplates();
    const second = await client.listTemplates();
    expect(first.length).toBeGreaterThan(0);
    expect(second.map((t) => t.ipTemplateID)).toEqual(first.map((t) => t.ipTemplateID));
  });

  it('wrong password surfaces a MeInvoiceException', async () => {
    const bad = createClient('wrong-password-on-purpose');
    const err = await bad.listTemplates().catch((e: unknown) => e);
    expect(err).toBeInstanceOf(MeInvoiceException);
    // MISA answers a JSON Authentication error; TransportFailed covers an unreachable/failing
    // gateway. MisaUnknown is deliberately not accepted: no documented sandbox response for a
    // wrong password maps to it (the .NET sandbox test only asserts the exception type), so
    // seeing it means the error mapping regressed.
    expect(['Authentication', 'TransportFailed']).toContain((err as MeInvoiceException).category);
  });

  it('listTemplates returns active templates with ids and series', async () => {
    const templates = await client.listTemplates();
    expect(templates.length).toBeGreaterThan(0);
    for (const t of templates) {
      expect(t.isActive).toBe(true);
      expect(t.ipTemplateID).not.toBe('');
      expect(t.invSeries).not.toBe('');
    }
  });

  it('saveDraft without a RefId echoes a service-generated RefId', async () => {
    const [result] = await client.saveDraft([sampleInvoice()]);
    if (result?.refId) created.add(result.refId);
    expect(result?.refId).toBeTruthy();
  });

  it('saveDraft batch of two succeeds', async () => {
    const a = crypto.randomUUID();
    const b = crypto.randomUUID();
    created.add(a).add(b);
    const results = await client.saveDraft([sampleInvoice(a), sampleInvoice(b)]);
    expect(results).toHaveLength(2);
    expect(results.every((r) => r.outcome === 'Success')).toBe(true);
  });

  it('saveDraft -> getDraftPdf -> lookupByRefId -> deleteDraft round trip', async () => {
    const refId = await saveOne();

    const pdf = await client.getDraftPdf(refId);
    expect(pdf.content.length).toBeGreaterThan(0);
    expect(pdf.content[0]).toBe(0x25); // '%'

    // MISA's getlist does not return Draft-state invoices in the sandbox tenant (.NET R-LU-23),
    // so only the call's shape is asserted here, not Found.
    const lookup = await client.lookupByRefId([refId], true);
    expect(lookup.status).toBe('Completed');
    expect(lookup.outcomes).toHaveLength(1);
    expect(lookup.outcomes?.[0]?.refId).toBe(refId);

    const deleted = await client.deleteDraft(refId, true);
    expect(deleted.status).toBe('Deleted');

    await expect(client.getDraftPdf(refId)).rejects.toMatchObject({
      category: 'ResourceNotFound',
    });
  });

  it('getDraftPdf of an unknown RefId surfaces ResourceNotFound', async () => {
    await expect(client.getDraftPdf(`non-existent-${crypto.randomUUID()}`)).rejects.toMatchObject({
      category: 'ResourceNotFound',
    });
  });

  it('lookupByRefId with all-unknown RefIds returns all NotFound', async () => {
    const refIds = Array.from({ length: 5 }, () => crypto.randomUUID());
    const outcome = await client.lookupByRefId(refIds, true);
    expect(outcome.status).toBe('Completed');
    expect(outcome.outcomes).toHaveLength(5);
    for (const o of outcome.outcomes ?? []) {
      expect(o.status).toBe('NotFound');
      expect(o.snapshot).toBeUndefined();
    }
  });

  it('deleteDraft is idempotent on re-delete and on an unknown RefId', async () => {
    const refId = await saveOne();
    const first = await client.deleteDraft(refId, true);
    expect(first.status).toBe('Deleted');
    const second = await client.deleteDraft(refId, true);
    expect(['Deleted', 'NotFound']).toContain(second.status);

    const unknown = await client.deleteDraft(crypto.randomUUID(), true);
    expect(['Deleted', 'NotFound']).toContain(unknown.status);
  });

  it('deleteDraft with the wrong invoiceWithCode flag is idempotent', async () => {
    const refId = await saveOne();
    const outcome = await client.deleteDraft(refId, false);
    expect(['Deleted', 'NotFound']).toContain(outcome.status);
  });
});

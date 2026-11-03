import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { startFakeMisa, type FakeMisa } from './fake-misa-server.js';

const AUTH = {
  Authorization: 'Bearer fake-token',
  taxcode: '0100-APP',
  'Content-Type': 'application/json',
};

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Env = { success: boolean; data: any; error: string | null; errorCode: string | null };

let fake: FakeMisa;
beforeEach(async () => {
  fake = await startFakeMisa();
});
afterEach(async () => {
  await fake.close();
});

const post = (path: string, body: unknown, headers: Record<string, string> = AUTH) =>
  fetch(`${fake.baseUrl}/${path}`, { method: 'POST', headers, body: JSON.stringify(body) });

describe('fake MISA server', () => {
  it('listens on loopback and serves a stringified access_token envelope', async () => {
    expect(fake.baseUrl).toMatch(/^http:\/\/127\.0\.0\.1:\d+$/);
    const res = await post('webapp/token', { appid: 'a' }, { 'Content-Type': 'application/json' });
    const env = (await res.json()) as Env;
    expect(env.success).toBe(true);
    expect(JSON.parse(env.data)).toEqual({ access_token: 'fake-token' });
  });

  it('insert then getlist returns the stored draft', async () => {
    const ins = (await (
      await post('webapp/insert?invoiceWithCode=true', [{ RefID: 'r-1', InvSeries: 'C26TAA' }])
    ).json()) as Env;
    expect(ins.success).toBe(true);
    expect(JSON.parse(ins.data)).toEqual([{ RefID: 'r-1' }]);
    const list = (await (await post('webapp/getlist', ['r-1', 'nope'])).json()) as Env;
    expect(list.success).toBe(true);
    expect(Array.isArray(list.data)).toBe(true);
    expect(list.data).toHaveLength(1);
    expect(list.data[0].RefID).toBe('r-1');
    expect(fake.state.drafts.has('r-1')).toBe(true);
  });

  it('serves a queued 503 once, then recovers', async () => {
    fake.state.queueStatus(503);
    const queued = await post('webapp/templates', {});
    expect(queued.status).toBe(503);
    const text = await queued.text();
    expect(text).toBe('queued status 503');
    expect(() => JSON.parse(text)).toThrow();
    expect((await post('webapp/templates', {})).status).toBe(200);
  });

  it('answers an insert without any RefID with 400 InvalidInvoiceData', async () => {
    const res = await post('webapp/insert', [{ InvSeries: 'C26TAA' }]);
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ success: false, errorCode: 'InvalidInvoiceData' });
    expect(fake.state.drafts.size).toBe(0);
  });

  it('serves an empty 200 body once when queued', async () => {
    fake.state.queueEmptyBody();
    const res = await post('webapp/templates', {});
    expect(res.status).toBe(200);
    expect(await res.text()).toBe('');
    expect((await post('webapp/templates', {})).status).toBe(200);
  });

  it('catch-all records the error in state.errors and answers 500 with its message', async () => {
    fake.state.rawLookupRows = [{ boom: 1n }];
    const res = await post('webapp/paging', {});
    expect(res.status).toBe(500);
    expect(await res.text()).toContain('BigInt');
    expect(fake.state.errors).toHaveLength(1);
    expect(fake.state.errors[0]).toBeInstanceOf(Error);
  });

  it('catch-all destroys the socket (no appended body) when headers were already sent', async () => {
    // Coerces to 200 for writeHead, then throws when the body text is built: a failure after
    // the status line was committed.
    const lateFailure = {
      valueOf: () => 200,
      toString: () => {
        throw new Error('failed after headers');
      },
    };
    fake.state.statusQueue.push(lateFailure as unknown as number);
    await expect(post('webapp/paging', {}).then((r) => r.text())).rejects.toThrow();
    expect(fake.state.errors).toHaveLength(1);
    expect((fake.state.errors[0] as Error).message).toBe('failed after headers');
  });

  it('close() is idempotent', async () => {
    const other = await startFakeMisa();
    await other.close();
    await expect(other.close()).resolves.toBeUndefined();
  });

  it('requires bearer + taxcode on non-token routes and honours queue401', async () => {
    expect(
      (await post('webapp/templates', {}, { 'Content-Type': 'application/json' })).status,
    ).toBe(401);
    fake.state.queue401(2);
    expect((await post('webapp/templates', {})).status).toBe(401);
    expect((await post('webapp/templates', {})).status).toBe(401);
    expect((await post('webapp/templates', {})).status).toBe(200);
  });

  it('lists active and inactive templates and logs requests', async () => {
    const env = (await (
      await post('webapp/templates?invoiceWithCode=true&isTicket=false', {})
    ).json()) as Env;
    const rows = JSON.parse(env.data) as { Inactive: boolean }[];
    expect(rows.filter((r) => !r.Inactive).length).toBeGreaterThanOrEqual(2);
    expect(rows.filter((r) => r.Inactive).length).toBeGreaterThanOrEqual(1);
    const last = fake.state.requests[fake.state.requests.length - 1];
    if (!last) throw new Error('no request logged');
    expect(last).toMatchObject({ method: 'POST', path: '/webapp/templates' });
    expect(last.query).toBe('invoiceWithCode=true&isTicket=false');
    expect(last.headers['authorization']).toBe('Bearer fake-token');
    expect(last.bodyText).toBe('{}');
  });

  it('scripts per-RefID insert errors and dropped echoes', async () => {
    fake.state.failInsertFor('b', 'DuplicateInvoiceRefID', 'dup');
    fake.state.dropEchoFor('c');
    const env = (await (
      await post('webapp/insert', [{ RefID: 'a' }, { RefID: 'b' }, { RefID: 'c' }])
    ).json()) as Env;
    expect(env.success).toBe(false);
    expect(JSON.parse(env.data)).toEqual([{ RefID: 'a' }]);
    expect(JSON.parse(env.error ?? '')).toEqual([
      { RefID: 'b', ErrorCode: 'DuplicateInvoiceRefID', ErrorMessage: 'dup' },
    ]);
    expect([...fake.state.drafts.keys()]).toEqual(['a', 'c']);
  });

  it('delete succeeds, removes the draft, or returns a scripted error', async () => {
    fake.state.seedDraft({ RefID: 'd1' });
    fake.state.seedDraft({ RefID: 'd2' });
    fake.state.setDeleteError('d2', 'InvalidTransactionID', 'Hóa đơn không tồn tại');
    const del = (id: string) =>
      fetch(`${fake.baseUrl}/webapp/delete?invoiceWithCode=true&refid=${id}`, {
        method: 'DELETE',
        headers: AUTH,
      });
    expect(await (await del('d1')).json()).toMatchObject({ success: true });
    expect(fake.state.drafts.has('d1')).toBe(false);
    expect(await (await del('d2')).json()).toMatchObject({
      success: false,
      errorCode: 'InvalidTransactionID',
      ErrorMessage: 'Hóa đơn không tồn tại',
    });
  });

  it('preview and viewrefid return a base64 PDF stub; unknown refid has empty data', async () => {
    const pv = (await (
      await post('webapp/preview?invoiceWithCode=true', { RefID: 'x' })
    ).json()) as Env;
    expect(Buffer.from(pv.data, 'base64').toString('latin1').startsWith('%PDF-1.4')).toBe(true);
    fake.state.seedDraft({ RefID: 'v1' });
    const ok = (await (
      await fetch(`${fake.baseUrl}/webapp/viewrefid?refid=v1`, { headers: AUTH })
    ).json()) as Env;
    expect(Buffer.from(ok.data, 'base64').toString('latin1').startsWith('%PDF-1.4')).toBe(true);
    const miss = (await (
      await fetch(`${fake.baseUrl}/webapp/viewrefid?refid=zz`, { headers: AUTH })
    ).json()) as Env;
    expect(miss.success).toBe(true);
    expect(miss.data).toBeFalsy();
  });

  it('pages seeded drafts per route group', async () => {
    for (const d of ['01', '02', '03'])
      fake.state.seedDraft({ RefID: `s${d}`, InvDate: `2026-05-${d}` });
    fake.state.seedDraft({ RefID: 'c01', InvDate: '2026-05-01' }, { calculating: true });
    const std = (await (await post('webapp/paging', { Start: 1, Length: 1 })).json()) as Env;
    expect(std.data.map((x: { RefID: string }) => x.RefID)).toEqual(['s02']);
    const calc = (await (
      await post('webapp/paging/calculating', { Start: 0, Length: 10 })
    ).json()) as { data: { RefID: string }[] };
    expect(calc.data.map((x) => x.RefID)).toEqual(['c01']);
  });

  it('filters paging by FromDate, ToDate and PublishStatus', async () => {
    fake.state.seedDraft({ RefID: 'p1', InvDate: '2026-05-01', PublishStatus: '0' });
    fake.state.seedDraft({ RefID: 'p2', InvDate: '2026-05-10', PublishStatus: '6' });
    fake.state.seedDraft({ RefID: 'p3', InvDate: '2026-05-20', PublishStatus: '6' });
    const ids = async (body: unknown): Promise<string[]> =>
      ((await (await post('webapp/paging', body)).json()) as Env).data.map(
        (x: { RefID: string }) => x.RefID,
      );
    expect(await ids({ FromDate: '2026-05-10' })).toEqual(['p2', 'p3']);
    expect(await ids({ ToDate: '2026-05-10' })).toEqual(['p1', 'p2']);
    expect(await ids({ PublishStatus: '6' })).toEqual(['p2', 'p3']);
    expect(await ids({ FromDate: '2026-05-05', ToDate: '2026-05-15', PublishStatus: '6' })).toEqual(
      ['p2'],
    );
  });
});

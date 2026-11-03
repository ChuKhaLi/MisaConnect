import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';

/** Test-only in-process stand-in for the MISA meInvoice integration API (not shipped). */

export interface FakeRequest {
  method: string;
  path: string;
  query: string;
  headers: Record<string, string>;
  bodyText: string;
}

/** Lookup snapshot row, PascalCase exactly as MISA returns it (port of .NET `SeededSnapshot`). */
export interface FakeSnapshot {
  RefID: string;
  InvoiceTemplateID?: string | null;
  InvSeries?: string | null;
  InvDate?: string | null;
  InvNo?: string | null;
  AccountObjectTaxCode?: string | null;
  AccountObjectName?: string | null;
  TotalSaleAmount?: number | null;
  TotalVATAmount?: number | null;
  TotalAmount?: number | null;
  TotalSaleAmountOC?: number | null;
  TotalVATAmountOC?: number | null;
  TotalAmountOC?: number | null;
  EInvoiceStatus?: string | null;
  PublishStatus?: string | null;
  OrgRefID?: string | null;
  CreatedDate?: string | null;
  ModifiedDate?: string | null;
}

export interface FakeTemplate {
  IPTemplateID: string;
  InvSeries: string;
  TemplateName: string;
  InvTemplateNo: string;
  TemplateType: number;
  Inactive: boolean;
  IsMoreVATRate: boolean;
}

export const FAKE_ACCESS_TOKEN = 'fake-token';

const PDF_STUB_BASE64 = Buffer.from('%PDF-1.4\n%fake\n%%EOF\n', 'latin1').toString('base64');

export class FakeState {
  /** Stored drafts keyed by RefID (filled by insert and seedDraft). */
  readonly drafts = new Map<string, FakeSnapshot>();
  /** Mutable template list served by `/webapp/templates`. */
  templates: FakeTemplate[] = [
    {
      IPTemplateID: 'ipt-active-1',
      InvSeries: 'C26TAA',
      TemplateName: 'Active template 1',
      InvTemplateNo: '1',
      TemplateType: 1,
      Inactive: false,
      IsMoreVATRate: false,
    },
    {
      IPTemplateID: 'ipt-active-2',
      InvSeries: 'C26TBB',
      TemplateName: 'Active template 2',
      InvTemplateNo: '1',
      TemplateType: 1,
      Inactive: false,
      IsMoreVATRate: true,
    },
    {
      IPTemplateID: 'ipt-inactive-1',
      InvSeries: 'C25TAA',
      TemplateName: 'Retired template',
      InvTemplateNo: '1',
      TemplateType: 1,
      Inactive: true,
      IsMoreVATRate: false,
    },
  ];
  /** Every request received, in order (including rejected ones). */
  readonly requests: FakeRequest[] = [];

  /** Errors thrown while handling a request (the server answers those with a 500). */
  readonly errors: unknown[] = [];
  /** Delay (ms) before every non-token request is handled; lets tests abort mid-flight. */
  responseDelayMs = 0;

  /** @internal */ readonly statusQueue: number[] = [];
  /** @internal */ emptyBodies = 0;
  /** @internal */ pending401 = 0;
  /** @internal */ readonly insertErrors = new Map<
    string,
    { code: string; message: string | null }
  >();
  /** @internal */ readonly droppedEchoes = new Set<string>();
  /** @internal */ readonly deleteErrors = new Map<string, { code: string; message: string }>();
  /** @internal */ readonly calculatingIds = new Set<string>();
  /** @internal */ stringifyLookupData = false;
  /** @internal */ rawLookupRows: unknown[] | undefined;
  /** @internal */ readonly lookupErrors: { code: string; message: string }[] = [];

  /** Next lookup request (getlist / paging) answers `success:false` with this error; queued FIFO. */
  failNextLookup(errorCode: string, message: string): void {
    this.lookupErrors.push({ code: errorCode, message });
  }

  /** Next `times` non-token requests are answered with `status` and a non-JSON body. */
  queueStatus(status: number, times = 1): void {
    for (let i = 0; i < times; i++) this.statusQueue.push(status);
  }

  /** Next `times` non-token requests are answered 200 with an empty body. */
  queueEmptyBody(times = 1): void {
    this.emptyBodies += times;
  }

  /** Next `times` non-token requests get 401 regardless of credentials. */
  queue401(times = 1): void {
    this.pending401 += times;
  }

  /** Insert answers a per-RefID error entry for this RefID instead of storing it. */
  failInsertFor(refId: string, errorCode: string, message?: string): void {
    this.insertErrors.set(refId, { code: errorCode, message: message ?? null });
  }

  /** Insert stores this RefID but leaves it out of the response (no echo). */
  dropEchoFor(refId: string): void {
    this.droppedEchoes.add(refId);
  }

  /** Delete answers `{ success:false, errorCode, ErrorMessage }` for this RefID. */
  setDeleteError(refId: string, errorCode: string, message: string): void {
    this.deleteErrors.set(refId, { code: errorCode, message });
  }

  /** Store a snapshot; `calculating: true` puts it behind `/webapp/paging/calculating` instead of `/webapp/paging`. */
  seedDraft(snapshot: FakeSnapshot, opts: { calculating?: boolean } = {}): void {
    this.drafts.set(snapshot.RefID, {
      InvDate: '2026-05-12',
      EInvoiceStatus: '1',
      PublishStatus: '0',
      CreatedDate: '2026-05-12T10:30:00',
      ModifiedDate: '2026-05-12T10:30:00',
      ...snapshot,
    });
    if (opts.calculating) this.calculatingIds.add(snapshot.RefID);
    else this.calculatingIds.delete(snapshot.RefID);
  }
}

export interface FakeMisa {
  baseUrl: string;
  state: FakeState;
  close(): Promise<void>;
}

function readBody(req: IncomingMessage): Promise<string> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    req.on('data', (c: Buffer) => chunks.push(c));
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

function send(res: ServerResponse, status: number, body: unknown): void {
  const text = JSON.stringify(body); // before writeHead: a serialisation error must still yield a 500
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(text);
}

/** MISA envelope: `data` is a stringified JSON value (or null). */
const envelope = (data: unknown) => ({
  success: true,
  data:
    data === null || data === undefined
      ? null
      : typeof data === 'string'
        ? data
        : JSON.stringify(data),
  error: null,
  errorCode: null,
});

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

/** Lookup envelope: `data` is a raw array. */
function lookupBody(state: FakeState, items: FakeSnapshot[]) {
  const rows: unknown[] = state.rawLookupRows ?? items;
  const data = state.stringifyLookupData ? JSON.stringify(rows) : rows;
  return { success: true, errorCode: null, descriptionErrorCode: null, errors: [], data };
}

function sendLookupError(state: FakeState, res: ServerResponse): boolean {
  const scripted = state.lookupErrors.shift();
  if (scripted === undefined) return false;
  send(res, 200, {
    success: false,
    data: null,
    error: scripted.message,
    errorCode: scripted.code,
  });
  return true;
}

function paged(state: FakeState, calculating: boolean, bodyText: string): FakeSnapshot[] {
  const b = (parseJson(bodyText) ?? {}) as Record<string, unknown>;
  const start = typeof b['Start'] === 'number' ? b['Start'] : 0;
  const length = typeof b['Length'] === 'number' ? b['Length'] : 100;
  const from = typeof b['FromDate'] === 'string' ? b['FromDate'] : '';
  const to = typeof b['ToDate'] === 'string' ? b['ToDate'] : '';
  const status = typeof b['PublishStatus'] === 'string' ? b['PublishStatus'] : '';
  return [...state.drafts.values()]
    .filter((s) => state.calculatingIds.has(s.RefID) === calculating)
    .filter((s) => !from || (s.InvDate ?? '') >= from)
    .filter((s) => !to || (s.InvDate ?? '') <= to)
    .filter((s) => !status || s.PublishStatus === status)
    .sort((a, c) => (a.InvDate ?? '').localeCompare(c.InvDate ?? ''))
    .slice(start, start + length);
}

const UNAUTHORIZED = {
  success: false,
  data: null,
  error: 'Unauthorized',
  errorCode: 'UnAuthorize',
};

function handle(state: FakeState, req: FakeRequest, res: ServerResponse): void {
  const params = new URLSearchParams(req.query);
  const route = `${req.method} ${req.path}`;

  if (route === 'POST /webapp/token') {
    send(res, 200, envelope({ access_token: FAKE_ACCESS_TOKEN }));
    return;
  }

  if (state.pending401 > 0) {
    state.pending401--;
    send(res, 401, UNAUTHORIZED);
    return;
  }
  if (state.emptyBodies > 0) {
    state.emptyBodies--;
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end();
    return;
  }
  const queued = state.statusQueue.shift();
  if (queued !== undefined) {
    res.writeHead(queued, { 'Content-Type': 'text/plain' });
    res.end(`queued status ${queued}`);
    return;
  }
  if (req.headers['authorization'] !== `Bearer ${FAKE_ACCESS_TOKEN}` || !req.headers['taxcode']) {
    send(res, 401, UNAUTHORIZED);
    return;
  }

  switch (route) {
    case 'POST /webapp/templates':
      send(res, 200, envelope(state.templates));
      return;
    case 'POST /webapp/preview':
      send(res, 200, envelope(PDF_STUB_BASE64));
      return;
    case 'GET /webapp/viewrefid': {
      // Unknown RefID: MISA answers success:true with empty data.
      const known = state.drafts.has(params.get('refid') ?? '');
      send(res, 200, envelope(known ? PDF_STUB_BASE64 : null));
      return;
    }
    case 'POST /webapp/insert': {
      const body = parseJson(req.bodyText);
      const items = Array.isArray(body) ? (body as Record<string, unknown>[]) : [];
      if (!items.some((i) => typeof i['RefID'] === 'string')) {
        send(res, 400, {
          success: false,
          data: null,
          error: 'missing RefID',
          errorCode: 'InvalidInvoiceData',
        });
        return;
      }
      const ok: { RefID: string }[] = [];
      const errors: { RefID: string; ErrorCode: string; ErrorMessage: string | null }[] = [];
      for (const item of items) {
        const refId = item['RefID'];
        if (typeof refId !== 'string') continue;
        const fail = state.insertErrors.get(refId);
        if (fail) {
          errors.push({ RefID: refId, ErrorCode: fail.code, ErrorMessage: fail.message });
          continue;
        }
        state.drafts.set(refId, {
          RefID: refId,
          InvoiceTemplateID: (item['InvoiceTemplateID'] as string | undefined) ?? null,
          InvSeries: (item['InvSeries'] as string | undefined) ?? null,
          InvDate: (item['InvDate'] as string | undefined) ?? '2026-05-12',
          EInvoiceStatus: (item['EInvoiceStatus'] as string | undefined) ?? '1',
          PublishStatus: '0',
          OrgRefID: (item['OrgRefID'] as string | undefined) ?? null,
        });
        if (!state.droppedEchoes.has(refId)) ok.push({ RefID: refId });
      }
      send(
        res,
        200,
        errors.length === 0
          ? envelope(ok)
          : {
              success: false,
              data: ok.length > 0 ? JSON.stringify(ok) : null,
              error: JSON.stringify(errors),
              errorCode: null,
            },
      );
      return;
    }
    case 'DELETE /webapp/delete': {
      const refId = params.get('refid') ?? '';
      const scripted = state.deleteErrors.get(refId);
      if (scripted) {
        send(res, 200, {
          success: false,
          errorCode: scripted.code,
          ErrorMessage: scripted.message,
        });
        return;
      }
      state.drafts.delete(refId);
      send(res, 200, { success: true, errorCode: null });
      return;
    }
    case 'POST /webapp/getlist': {
      if (sendLookupError(state, res)) return;
      const body = parseJson(req.bodyText);
      const wanted = new Set(
        Array.isArray(body) ? body.filter((x): x is string => typeof x === 'string') : [],
      );
      send(
        res,
        200,
        lookupBody(
          state,
          [...state.drafts.values()].filter((s) => wanted.has(s.RefID)),
        ),
      );
      return;
    }
    case 'POST /webapp/paging':
      if (sendLookupError(state, res)) return;
      send(res, 200, lookupBody(state, paged(state, false, req.bodyText)));
      return;
    case 'POST /webapp/paging/calculating':
      if (sendLookupError(state, res)) return;
      send(res, 200, lookupBody(state, paged(state, true, req.bodyText)));
      return;
    default:
      send(res, 404, {
        success: false,
        data: null,
        error: `no route ${route}`,
        errorCode: 'NotFound',
      });
  }
}

export async function startFakeMisa(): Promise<FakeMisa> {
  const state = new FakeState();
  const server: Server = createServer((req, res) => {
    void (async () => {
      const bodyText = await readBody(req);
      const url = new URL(req.url ?? '/', 'http://fake');
      const headers: Record<string, string> = {};
      for (const [k, v] of Object.entries(req.headers)) {
        headers[k.toLowerCase()] = Array.isArray(v) ? v.join(',') : (v ?? '');
      }
      const entry: FakeRequest = {
        method: req.method ?? 'GET',
        path: url.pathname,
        query: url.search.replace(/^\?/, ''),
        headers,
        bodyText,
      };
      state.requests.push(entry);
      if (state.responseDelayMs > 0 && entry.path !== '/webapp/token') {
        await new Promise((resolve) => setTimeout(resolve, state.responseDelayMs));
      }
      handle(state, entry, res);
    })().catch((e: unknown) => {
      state.errors.push(e);
      if (!res.headersSent) res.writeHead(500, { 'Content-Type': 'text/plain' });
      res.end(e instanceof Error ? e.message : String(e));
    });
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;
  let closing: Promise<void> | undefined;
  return {
    baseUrl: `http://127.0.0.1:${port}`,
    state,
    close: () =>
      (closing ??= new Promise<void>((resolve, reject) => {
        server.closeAllConnections();
        server.close((e) => (e ? reject(e) : resolve()));
      })),
  };
}

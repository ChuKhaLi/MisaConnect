import { describe, expect, it } from 'vitest';
import { MeInvoiceException } from '../../src/domain/errors.js';
import {
  extractErrorCode,
  parseInsertErrors,
  readEnvelope,
} from '../../src/infrastructure/envelope.js';
import { money } from '../../src/infrastructure/json.js';
import { validateOptions } from '../../src/infrastructure/options.js';
import {
  abortableDelay,
  createTransport,
  wireTaxCode,
  type TransportDeps,
} from '../../src/infrastructure/transport.js';

interface Call {
  url: URL;
  method: string;
  headers: Record<string, string>;
  body: string | undefined;
  signal: AbortSignal | undefined;
}

type Handler = (call: Call) => Response | Promise<Response>;

const BASE = 'https://testapi.meinvoice.vn/api/integration';
const NOW = new Date('2026-11-03T00:00:00Z');

function envelope(fields: Record<string, unknown>, status = 200): Response {
  return new Response(JSON.stringify(fields), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function tokenResponse(token: string): Response {
  return envelope({
    success: true,
    data: JSON.stringify({ access_token: token }),
    error: null,
    errorCode: null,
  });
}

const ok = (): Response =>
  envelope({ success: true, data: JSON.stringify({ ok: 1 }), error: null, errorCode: null });

/**
 * Scripted fake fetch: the token route answers with tokens `t1`, `t2`, … unless overridden;
 * every other request is answered by the next queued handler (the last one repeats).
 */
function setup(
  queue: Handler[],
  opts: { token?: Handler; deps?: TransportDeps } = {},
): {
  transport: ReturnType<typeof createTransport>;
  calls: Call[];
  tokenCalls: () => number;
  apiCalls: () => Call[];
  delays: number[];
} {
  const calls: Call[] = [];
  let tokenCount = 0;
  let next = 0;
  const fakeFetch = async (input: string | URL | Request, init?: RequestInit) => {
    const call: Call = {
      url: new URL(String(input)),
      method: init?.method ?? 'GET',
      headers: { ...(init?.headers as Record<string, string> | undefined) },
      body: init?.body === undefined || init.body === null ? undefined : String(init.body),
      signal: init?.signal ?? undefined,
    };
    calls.push(call);
    if (call.url.pathname.endsWith('/webapp/token')) {
      tokenCount++;
      return opts.token ? opts.token(call) : tokenResponse(`t${tokenCount}`);
    }
    const handler = queue[Math.min(next, queue.length - 1)];
    next++;
    if (handler === undefined) throw new Error('no handler');
    return handler(call);
  };
  const delays: number[] = [];
  const resolved = validateOptions({
    baseUrl: BASE,
    taxCode: '0100000000',
    userName: 'user',
    password: 'secret',
    appId: 'APP',
    fetch: fakeFetch as typeof fetch,
    clock: { now: () => NOW },
  });
  const transport = createTransport(resolved, {
    delay: (ms) => {
      delays.push(ms);
      return Promise.resolve();
    },
    random: () => 0.5,
    ...opts.deps,
  });
  return {
    transport,
    calls,
    tokenCalls: () => tokenCount,
    apiCalls: () => calls.filter((c) => !c.url.pathname.endsWith('/webapp/token')),
    delays,
  };
}

async function rejection(p: Promise<unknown>): Promise<unknown> {
  try {
    await p;
  } catch (e) {
    return e;
  }
  throw new Error('did not reject');
}

describe('wireTaxCode', () => {
  it('composes taxCode-appId', () => {
    expect(wireTaxCode('0100000000', 'APP')).toBe('0100000000-APP');
  });
});

describe('transport: token and headers', () => {
  it('posts wire taxcode and caches token', async () => {
    const s = setup([ok]);
    await s.transport.request('POST', 'webapp/templates', { invoiceWithCode: 'true' }, { a: 1 });
    await s.transport.request('GET', 'webapp/viewrefid', { refid: 'r 1' });
    expect(s.tokenCalls()).toBe(1);

    const token = s.calls[0];
    expect(token?.url.href).toBe(`${BASE}/webapp/token`);
    expect(token?.method).toBe('POST');
    expect(JSON.parse(token?.body ?? '')).toEqual({
      taxcode: '0100000000-APP',
      username: 'user',
      password: 'secret',
    });
    expect(token?.headers['Authorization']).toBeUndefined();

    const [post, get] = s.apiCalls();
    expect(post?.url.href).toBe(`${BASE}/webapp/templates?invoiceWithCode=true`);
    expect(post?.headers).toEqual({
      Authorization: 'Bearer t1',
      taxcode: '0100000000-APP',
      'Content-Type': 'application/json',
    });
    expect(post?.body).toBe('{"a":1}');
    expect(get?.url.search).toBe('?refid=r%201');
    expect(get?.headers['Authorization']).toBe('Bearer t1');
    expect(get?.body).toBeUndefined();
  });

  it('serializes the body with serializeRequest (money verbatim, nulls omitted)', async () => {
    const s = setup([ok]);
    await s.transport.request('POST', 'webapp/insert', {}, [
      { Amount: money('12345678901234567890.123456'), X: null },
    ]);
    expect(s.apiCalls()[0]?.body).toBe('[{"Amount":12345678901234567890.123456}]');
  });

  it('single flight: 10 concurrent requests on a cold cache acquire exactly one token', async () => {
    const s = setup([ok]);
    await Promise.all(
      Array.from({ length: 10 }, () => s.transport.request('POST', 'webapp/insert', {}, [])),
    );
    expect(s.tokenCalls()).toBe(1);
    expect(s.apiCalls()).toHaveLength(10);
  });

  it('empty access_token → Authentication EmptyToken', async () => {
    const s = setup([ok], {
      token: () =>
        envelope({ success: true, data: JSON.stringify({ access_token: '' }), error: null }),
    });
    const err = await rejection(s.transport.request('POST', 'webapp/insert', {}, []));
    expect(err).toBeInstanceOf(MeInvoiceException);
    expect(err).toMatchObject({ category: 'Authentication', rawErrorCode: 'EmptyToken' });
    expect(s.apiCalls()).toHaveLength(0);
  });

  it('token envelope failure → mapped error code', async () => {
    const s = setup([ok], {
      token: () =>
        envelope({ success: false, data: null, error: 'bad app', errorCode: 'InvalidAppID' }),
    });
    const err = await rejection(s.transport.request('POST', 'webapp/insert', {}, []));
    expect(err).toMatchObject({ category: 'Configuration', rawErrorCode: 'InvalidAppID' });
  });
});

describe('transport: 401 refresh', () => {
  it('refreshes on 401 once', async () => {
    const s = setup([() => new Response('', { status: 401 }), ok]);
    const env = await s.transport.request('POST', 'webapp/insert', {}, []);
    expect(env.success).toBe(true);
    expect(s.tokenCalls()).toBe(2);
    expect(s.apiCalls().map((c) => c.headers['Authorization'])).toEqual(['Bearer t1', 'Bearer t2']);
    expect(s.apiCalls()[1]?.body).toBe('[]');
  });

  it('second 401 throws Authentication UnAuthorize', async () => {
    const s = setup([() => new Response('', { status: 401 })]);
    const err = await rejection(s.transport.request('POST', 'webapp/insert', {}, []));
    expect(err).toBeInstanceOf(MeInvoiceException);
    expect(err).toMatchObject({ category: 'Authentication', rawErrorCode: 'UnAuthorize' });
    expect(s.apiCalls()).toHaveLength(2);
    expect(s.delays).toHaveLength(0);
  });

  it('concurrent 401s do not evict a token that was already refreshed', async () => {
    let n = 0;
    const s = setup([
      (c) => {
        n++;
        return c.headers['Authorization'] === 'Bearer t1'
          ? new Response('', { status: 401 })
          : ok();
      },
    ]);
    await Promise.all(
      Array.from({ length: 10 }, () => s.transport.request('POST', 'webapp/insert', {}, [])),
    );
    expect(n).toBe(20);
    expect(s.tokenCalls()).toBe(2);
  });
});

describe('transport: retry', () => {
  it('retries 503 once then MisaUnavailable', async () => {
    const s = setup([() => new Response('', { status: 503 })]);
    const err = await rejection(s.transport.request('POST', 'webapp/insert', {}, []));
    expect(err).toMatchObject({ category: 'MisaUnavailable', rawErrorCode: 'ServiceUnavailable' });
    expect(s.apiCalls()).toHaveLength(2);
    expect(s.delays).toHaveLength(1);
  });

  it('503 then success returns the envelope', async () => {
    const s = setup([() => new Response('', { status: 503 }), ok]);
    const env = await s.transport.request<{ ok: number }>('POST', 'webapp/insert', {}, []);
    expect(env.data).toEqual({ ok: 1 });
    expect(s.tokenCalls()).toBe(1);
  });

  it('429 twice → MisaThrottled', async () => {
    const s = setup([() => new Response('', { status: 429 })]);
    const err = await rejection(s.transport.request('POST', 'webapp/insert', {}, []));
    expect(err).toMatchObject({ category: 'MisaThrottled', rawErrorCode: 'TooManyRequests' });
    expect(s.apiCalls()).toHaveLength(2);
  });

  it('fetch TypeError twice → TransportFailed ConnectionFailed', async () => {
    const s = setup([
      () => {
        throw new TypeError('fetch failed');
      },
    ]);
    const err = await rejection(s.transport.request('POST', 'webapp/insert', {}, []));
    expect(err).toBeInstanceOf(MeInvoiceException);
    expect(err).toMatchObject({ category: 'TransportFailed', rawErrorCode: 'ConnectionFailed' });
    expect(s.apiCalls()).toHaveLength(2);
  });

  it('delay is called with a value in [1250, 1750]', async () => {
    for (const r of [0, 0.5, 0.999999]) {
      const s = setup([() => new Response('', { status: 503 }), ok], { deps: { random: () => r } });
      await s.transport.request('POST', 'webapp/insert', {}, []);
      expect(s.delays).toHaveLength(1);
      const ms = s.delays[0] ?? 0;
      expect(ms).toBeGreaterThanOrEqual(1250);
      expect(ms).toBeLessThanOrEqual(1750);
      expect(Number.isInteger(ms)).toBe(true);
    }
  });

  it('per-request timeout twice → TransportFailed Timeout', async () => {
    const hang: Handler = (c) =>
      new Promise((_, reject) => {
        c.signal?.addEventListener('abort', () => reject(c.signal?.reason), { once: true });
      });
    const s = setup([hang], { deps: { timeoutMs: 5 } });
    const err = await rejection(s.transport.request('POST', 'webapp/insert', {}, []));
    expect(err).toMatchObject({ category: 'TransportFailed', rawErrorCode: 'Timeout' });
    expect(s.apiCalls()).toHaveLength(2);
  });

  it('the token request is retried on 503', async () => {
    let t = 0;
    const s = setup([ok], {
      token: () => (++t === 1 ? new Response('', { status: 503 }) : tokenResponse('tok')),
    });
    await s.transport.request('POST', 'webapp/insert', {}, []);
    expect(s.tokenCalls()).toBe(2);
    expect(s.apiCalls()[0]?.headers['Authorization']).toBe('Bearer tok');
  });
});

describe('transport: abort', () => {
  it('abort during backoff rejects with AbortError and sends no second request', async () => {
    const ctrl = new AbortController();
    let waiting = false;
    const s = setup([() => new Response('', { status: 503 })], {
      deps: {
        delay: (_ms, signal) =>
          new Promise((_, reject) => {
            waiting = true;
            signal?.addEventListener('abort', () => reject(signal.reason), { once: true });
          }),
      },
    });
    const p = s.transport.request('POST', 'webapp/insert', {}, [], { signal: ctrl.signal });
    await vitestWaitFor(() => waiting);
    ctrl.abort();
    const err = await rejection(p);
    expect(err).toBeInstanceOf(DOMException);
    expect(err).toMatchObject({ name: 'AbortError' });
    expect(s.apiCalls()).toHaveLength(1);
  });

  it('abort during the default backoff rejects promptly', async () => {
    const ctrl = new AbortController();
    const s = setup([() => new Response('', { status: 503 })], {
      deps: { delay: abortableDelay },
    });
    const started = Date.now();
    const p = s.transport.request('POST', 'webapp/insert', {}, [], { signal: ctrl.signal });
    await vitestWaitFor(() => s.apiCalls().length === 1);
    ctrl.abort();
    const err = await rejection(p);
    expect(err).toMatchObject({ name: 'AbortError' });
    expect(Date.now() - started).toBeLessThan(1000);
    expect(s.apiCalls()).toHaveLength(1);
  });

  it('abort during fetch rejects with AbortError and is not retried', async () => {
    const ctrl = new AbortController();
    const s = setup([
      (c) =>
        new Promise((_, reject) => {
          c.signal?.addEventListener('abort', () => reject(c.signal?.reason), { once: true });
          ctrl.abort();
        }),
    ]);
    const err = await rejection(
      s.transport.request('POST', 'webapp/insert', {}, [], { signal: ctrl.signal }),
    );
    expect(err).toMatchObject({ name: 'AbortError' });
    expect(s.apiCalls()).toHaveLength(1);
    expect(s.delays).toHaveLength(0);
  });

  it('an already-aborted signal rejects without any request', async () => {
    const s = setup([ok]);
    const err = await rejection(
      s.transport.request('POST', 'webapp/insert', {}, [], { signal: AbortSignal.abort() }),
    );
    expect(err).toMatchObject({ name: 'AbortError' });
    expect(s.calls).toHaveLength(0);
  });
});

describe('transport: bad bodies', () => {
  it('HTML 502 body → MisaUnavailable, never SyntaxError', async () => {
    const html = (): Response =>
      new Response('<html><body>502 Bad Gateway</body></html>', {
        status: 502,
        headers: { 'Content-Type': 'text/html' },
      });
    const s = setup([html]);
    const err = await rejection(s.transport.request('POST', 'webapp/insert', {}, []));
    expect(err).toBeInstanceOf(MeInvoiceException);
    expect(err).toMatchObject({ category: 'MisaUnavailable' });
  });

  it('empty 200 → MisaUnknown EmptyResponse', async () => {
    const s = setup([() => new Response('', { status: 200 })]);
    const err = await rejection(s.transport.request('POST', 'webapp/insert', {}, []));
    expect(err).toMatchObject({ category: 'MisaUnknown', rawErrorCode: 'EmptyResponse' });
  });

  it('HTML 200 → MisaUnknown InvalidResponse', async () => {
    const s = setup([() => new Response('<html></html>', { status: 200 })]);
    const err = await rejection(s.transport.request('POST', 'webapp/insert', {}, []));
    expect(err).toMatchObject({ category: 'MisaUnknown', rawErrorCode: 'InvalidResponse' });
  });

  it('4xx with an envelope is returned for the caller to interpret', async () => {
    const s = setup([
      () => envelope({ success: false, data: null, errorCode: 'InvalidTaxCode' }, 400),
    ]);
    const env = await s.transport.request('POST', 'webapp/insert', {}, []);
    expect(env.success).toBe(false);
    expect(extractErrorCode(env)).toBe('InvalidTaxCode');
  });
});

describe('envelope', () => {
  it('decodes stringified-object data', () => {
    const env = readEnvelope<{ a: number }>(
      200,
      JSON.stringify({ success: true, data: '{"a":1}' }),
    );
    expect(env).toEqual({ success: true, data: { a: 1 }, error: undefined, errorCode: undefined });
  });

  it('decodes stringified-array and raw-array data', () => {
    expect(readEnvelope(200, JSON.stringify({ success: true, data: '[{"a":1}]' })).data).toEqual([
      { a: 1 },
    ]);
    expect(readEnvelope(200, JSON.stringify({ success: true, data: [{ a: 2 }] })).data).toEqual([
      { a: 2 },
    ]);
  });

  it('keeps non-JSON string data (base64 PDF) verbatim and maps null/empty to undefined', () => {
    expect(readEnvelope(200, JSON.stringify({ success: true, data: 'JVBERi0xLjQ=' })).data).toBe(
      'JVBERi0xLjQ=',
    );
    expect(readEnvelope(200, JSON.stringify({ success: true, data: null })).data).toBeUndefined();
    expect(readEnvelope(200, JSON.stringify({ success: true, data: '' })).data).toBeUndefined();
  });

  it('non-JSON body: MisaUnavailable when status >= 500, else MisaUnknown InvalidResponse', () => {
    expect(() => readEnvelope(502, '<html>Bad Gateway</html>')).toThrowError(
      expect.objectContaining({ category: 'MisaUnavailable', rawErrorCode: 'InvalidResponse' }),
    );
    expect(() => readEnvelope(404, '<html>Not Found</html>')).toThrowError(
      expect.objectContaining({ category: 'MisaUnknown', rawErrorCode: 'InvalidResponse' }),
    );
  });

  it('treats a JSON null / non-object body as empty / invalid', () => {
    expect(() => readEnvelope(200, 'null')).toThrowError(
      expect.objectContaining({ category: 'MisaUnknown', rawErrorCode: 'EmptyResponse' }),
    );
    expect(() => readEnvelope(200, '"x"')).toThrowError(
      expect.objectContaining({ category: 'MisaUnknown', rawErrorCode: 'InvalidResponse' }),
    );
  });

  it('extractErrorCode: string, array first element, empty array, absent', () => {
    expect(
      extractErrorCode({ success: false, data: undefined, error: undefined, errorCode: 'X' }),
    ).toBe('X');
    expect(
      extractErrorCode({
        success: false,
        data: undefined,
        error: undefined,
        errorCode: ['A', 'B'],
      }),
    ).toBe('A');
    expect(
      extractErrorCode({ success: false, data: undefined, error: undefined, errorCode: [] }),
    ).toBeUndefined();
    expect(
      extractErrorCode({ success: false, data: undefined, error: undefined, errorCode: undefined }),
    ).toBeUndefined();
  });

  it('parseInsertErrors: stringified array, raw array, plain string, empty', () => {
    expect(parseInsertErrors('[{"RefID":"r1","ErrorCode":"InvalidInvoiceData"}]')).toEqual([
      { refID: 'r1', errorCode: 'InvalidInvoiceData' },
    ]);
    expect(
      parseInsertErrors([{ RefID: 'r2', ErrorCode: 'DuplicateInvoiceRefID', ErrorMessage: 'dup' }]),
    ).toEqual([{ refID: 'r2', errorCode: 'DuplicateInvoiceRefID', errorMessage: 'dup' }]);
    expect(parseInsertErrors('InvoiceData is null')).toEqual([
      { errorMessage: 'InvoiceData is null' },
    ]);
    expect(parseInsertErrors(undefined)).toEqual([]);
    expect(parseInsertErrors('')).toEqual([]);
  });
});

async function vitestWaitFor(cond: () => boolean): Promise<void> {
  for (let i = 0; i < 200 && !cond(); i++) await new Promise((r) => setTimeout(r, 1));
  if (!cond()) throw new Error('condition not met');
}

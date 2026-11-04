/* eslint-disable @typescript-eslint/no-non-null-assertion */
import { describe, expect, it } from 'vitest';
import type { MisaCredentials } from '../../src/domain/dtos.js';
import { ESignGeneralException, ESignTransportException } from '../../src/domain/errors.js';
import type { ResolvedESignOptions } from '../../src/infrastructure/options.js';
import { createTransport } from '../../src/infrastructure/transport.js';

const creds = {
  clientId: 'cid',
  clientKey: 'KEY-SECRET-123',
  userName: 'u',
  password: 'p',
} as MisaCredentials;
const BEARER = 'BEARER-SECRET-456';

type Step = Response | Error;
const json = (status: number, body: unknown, headers: Record<string, string> = {}): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json', ...headers },
  });

function setup(steps: Step[], over: Partial<ResolvedESignOptions> = {}) {
  const calls: { url: string; init: RequestInit }[] = [];
  const delays: number[] = [];
  const logs: { level: string; msg: string; fields?: Record<string, unknown> }[] = [];
  const log =
    (level: string) =>
    (msg: string, fields?: Record<string, unknown>): void =>
      void logs.push({ level, msg, ...(fields ? { fields } : {}) });
  const fetchFn = ((url: string, init: RequestInit) => {
    calls.push({ url, init });
    const s = steps[Math.min(calls.length - 1, steps.length - 1)]!;
    if (s instanceof Error) return Promise.reject(s);
    return Promise.resolve(s.clone());
  }) as unknown as typeof fetch;
  const resolved = {
    origin: 'https://h.vn/',
    host: 'h.vn',
    authUnderWebdev: false,
    transportRetry: { maxAttempts: 3, baseDelayMs: 100, maxDelayMs: 500 },
    fetch: fetchFn,
    delay: (ms: number) => {
      delays.push(ms);
      return Promise.resolve();
    },
    logger: { debug: log('debug'), info: log('info'), warn: log('warn'), error: log('error') },
    ...over,
  } as unknown as ResolvedESignOptions;
  const transport = createTransport(resolved, { random: () => 1 });
  return { transport, calls, delays, logs };
}

const base = { method: 'POST' as const, credentials: creds, correlationId: 'corr-1' };
const net = (code: string): Error => Object.assign(new Error('net'), { code });

describe('transport', () => {
  it('sends the MISA headers and no Authorization without bearer', async () => {
    const { transport, calls } = setup([json(200, { ok: 1 })]);
    const r = await transport.send({
      ...base,
      route: 'certificates',
      body: { a: 1 },
      expectJson: true,
    });
    const h = calls[0]!.init.headers as Record<string, string>;
    expect(h['x-clientId']).toBe('cid');
    expect(h['x-clientKey']).toBe('KEY-SECRET-123');
    expect(h['X-Correlation-Id']).toBe('corr-1');
    expect(h['Content-Type']).toBe('application/json');
    expect(h['AuthorizationRM']).toBeUndefined();
    expect(Object.keys(h).some((k) => k.toLowerCase() === 'authorization')).toBe(false);
    expect(calls[0]!.init.body).toBe('{"a":1}');
    expect(r.status).toBe(200);
    expect(r.json).toEqual({ ok: 1 });
  });

  it('adds AuthorizationRM only with bearer; no content type without body', async () => {
    const { transport, calls } = setup([json(200, {})]);
    await transport.send({ ...base, method: 'GET', route: 'certificates', bearer: BEARER });
    const h = calls[0]!.init.headers as Record<string, string>;
    expect(h['AuthorizationRM']).toBe(`Bearer ${BEARER}`);
    expect(h['Content-Type']).toBeUndefined();
  });

  it('retries 503,503,200', async () => {
    const { transport, calls, delays } = setup([
      json(503, {}),
      json(503, {}),
      json(200, { ok: 1 }),
    ]);
    const r = await transport.send({ ...base, route: 'certificates' });
    expect(calls).toHaveLength(3);
    expect(r.status).toBe(200);
    expect(delays).toEqual([100, 200]);
  });

  it('exhausts 503x3 into ESignTransportException', async () => {
    const { transport, calls } = setup([json(503, {})]);
    const err = await transport.send({ ...base, route: 'certificates' }).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ESignTransportException);
    const e = err as ESignTransportException;
    expect(e.rawCode).toBe('ServiceUnavailable');
    expect(e.attemptCount).toBe(3);
    expect(calls).toHaveLength(3);
  });

  it('honours Retry-After clamped to maxDelayMs', async () => {
    const { transport, delays } = setup([
      json(429, {}, { 'retry-after': '1' }),
      json(200, { ok: 1 }),
    ]);
    await transport.send({ ...base, route: 'certificates' });
    expect(delays).toEqual([500]);
  });

  it('honours Retry-After seconds below the clamp', async () => {
    const { transport, delays } = setup([json(429, {}, { 'retry-after': '1' }), json(200, {})], {
      transportRetry: { maxAttempts: 3, baseDelayMs: 100, maxDelayMs: 5000 },
    });
    await transport.send({ ...base, route: 'certificates' });
    expect(delays).toEqual([1000]);
  });

  it('does not retry or throw on 401', async () => {
    const { transport, calls } = setup([json(401, { x: 1 })]);
    const r = await transport.send({ ...base, route: 'certificates' });
    expect(r.status).toBe(401);
    expect(calls).toHaveLength(1);
  });

  it('returns 4xx as-is', async () => {
    const { transport, calls } = setup([json(400, { x: 1 })]);
    const r = await transport.send({ ...base, route: 'hash' });
    expect(r.status).toBe(400);
    expect(calls).toHaveLength(1);
  });

  it('retries network errors on general routes', async () => {
    const { transport, calls } = setup([net('ECONNRESET'), json(200, {})]);
    await transport.send({ ...base, route: 'hash' });
    expect(calls).toHaveLength(2);
  });

  it('signHash + 503 throws immediately', async () => {
    const { transport, calls } = setup([json(503, {})]);
    const err = await transport.send({ ...base, route: 'signHash' }).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ESignTransportException);
    expect((err as ESignTransportException).attemptCount).toBe(1);
    expect(calls).toHaveLength(1);
  });

  it.each(['ECONNREFUSED', 'ENOTFOUND', 'EAI_AGAIN'])('signHash retries %s', async (code) => {
    const { transport, calls } = setup([net(code), json(200, {})]);
    await transport.send({ ...base, route: 'signHash' });
    expect(calls).toHaveLength(2);
  });

  it('signHash retries a code found on cause', async () => {
    const e = new TypeError('fetch failed', { cause: net('ECONNREFUSED') });
    const { transport, calls } = setup([e, json(200, {})]);
    await transport.send({ ...base, route: 'signHash' });
    expect(calls).toHaveLength(2);
  });

  it('signHash does not retry ECONNRESET', async () => {
    const { transport, calls } = setup([net('ECONNRESET'), json(200, {})]);
    const err = await transport.send({ ...base, route: 'signHash' }).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ESignTransportException);
    expect(calls).toHaveLength(1);
  });

  it('signHash retries 429', async () => {
    const { transport, calls } = setup([json(429, {}), json(200, {})]);
    await transport.send({ ...base, route: 'signHash' });
    expect(calls).toHaveLength(2);
  });

  it('caller abort rejects with signal.reason, not retried', async () => {
    const ctrl = new AbortController();
    const reason = new Error('stop');
    let count = 0;
    const fetchFn = ((_u: string, init: RequestInit) => {
      count++;
      return new Promise((_res, rej) => {
        init.signal!.addEventListener('abort', () => rej(init.signal!.reason));
      });
    }) as unknown as typeof fetch;
    const { transport } = setup([], { fetch: fetchFn });
    const p = transport.send({ ...base, route: 'hash', signal: ctrl.signal });
    ctrl.abort(reason);
    await expect(p).rejects.toBe(reason);
    expect(count).toBe(1);
  });

  it('per-attempt timeout is retried then wrapped when exhausted', async () => {
    const fetchFn = ((_u: string, init: RequestInit) =>
      new Promise((_res, rej) => {
        init.signal!.addEventListener('abort', () => rej(init.signal!.reason));
      })) as unknown as typeof fetch;
    const t = createTransport(
      {
        origin: 'https://h.vn/',
        authUnderWebdev: false,
        transportRetry: { maxAttempts: 2, baseDelayMs: 1, maxDelayMs: 1 },
        fetch: fetchFn,
        delay: () => Promise.resolve(),
      } as unknown as ResolvedESignOptions,
      { timeoutMs: 5 },
    );
    const err = await t.send({ ...base, route: 'hash' }).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ESignTransportException);
    expect((err as ESignTransportException).attemptCount).toBe(2);
  });

  it('rejects HTML 200 with UnexpectedContentType', async () => {
    const html = new Response('<html><body>oops</body></html>', {
      status: 200,
      headers: { 'content-type': 'text/html' },
    });
    const { transport } = setup([html]);
    const err = await transport
      .send({ ...base, route: 'certificates', expectJson: true })
      .catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ESignGeneralException);
    const e = err as ESignGeneralException;
    expect(e.category).toBe('MisaUnknown');
    expect(e.rawCode).toBe('UnexpectedContentType');
    expect(e.detail).toContain('<html');
    expect(e.detail).toContain('Certificates/by-userId');
    expect(e.detail).toContain('text/html');
  });

  it('flags unparseable JSON without leaking the body', async () => {
    const bad = new Response('{bad SECRETBODY', {
      status: 200,
      headers: { 'content-type': 'application/json' },
    });
    const { transport } = setup([bad]);
    const err = await transport
      .send({ ...base, route: 'certificates', expectJson: true })
      .catch((e: unknown) => e);
    const e = err as ESignGeneralException;
    expect(e.rawCode).toBe('UnparseableResponse');
    expect(e.message).not.toContain('SECRETBODY');
  });

  it('logs started/completed and never leaks secrets', async () => {
    const { transport, logs } = setup([json(503, {}), json(200, { ok: 1 })]);
    await transport.send({ ...base, route: 'certificates', bearer: BEARER });
    expect(logs.map((l) => l.msg)).toEqual([
      'MISA eSign call started',
      'MISA eSign call completed',
    ]);
    expect(logs[1]!.fields).toMatchObject({
      outcome: 'Success',
      status: 200,
      correlationId: 'corr-1',
    });
    const all = JSON.stringify(logs);
    expect(all).not.toContain('KEY-SECRET-123');
    expect(all).not.toContain(BEARER);
  });

  it('logs failures at warn with category and rawCode', async () => {
    const { transport, logs } = setup([json(503, {})]);
    await transport.send({ ...base, route: 'hash' }).catch(() => undefined);
    const w = logs.find((l) => l.level === 'warn')!;
    expect(w.fields).toMatchObject({ errorCategory: 'Transport', rawCode: 'ServiceUnavailable' });
    expect(logs.at(-1)!.fields).toMatchObject({ outcome: 'Error' });
  });
});

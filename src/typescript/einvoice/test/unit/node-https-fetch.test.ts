import http from 'node:http';
import https from 'node:https';
import { createServer as createNetServer, Socket, type AddressInfo } from 'node:net';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  CLASSICAL_ECDH_CURVES,
  createNodeHttpsFetch,
  DEFAULT_IDLE_TIMEOUT_MS,
  type NodeHttpsFetch,
  type NodeHttpsFetchOptions,
} from '../../src/infrastructure/node-https-fetch.js';

interface Seen {
  method: string;
  url: string;
  headers: http.IncomingHttpHeaders;
  body: string;
}

const servers: { close: () => Promise<void> }[] = [];
const clients: NodeHttpsFetch[] = [];

afterEach(async () => {
  for (const c of clients.splice(0)) c.close();
  for (const s of servers.splice(0)) await s.close();
});

function client(opts?: Parameters<typeof createNodeHttpsFetch>[0]): NodeHttpsFetch {
  const f = createNodeHttpsFetch(opts);
  clients.push(f);
  return f;
}

async function startHttp(
  handler: (req: http.IncomingMessage, res: http.ServerResponse, seen: Seen) => void,
): Promise<{ url: string; seen: Seen[] }> {
  const seen: Seen[] = [];
  const server = http.createServer((req, res) => {
    const chunks: Buffer[] = [];
    req.on('data', (c: Buffer) => chunks.push(c));
    req.on('end', () => {
      const s: Seen = {
        method: req.method ?? '',
        url: req.url ?? '',
        headers: req.headers,
        body: Buffer.concat(chunks).toString('utf8'),
      };
      seen.push(s);
      handler(req, res, s);
    });
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;
  servers.push({
    close: () =>
      new Promise<void>((resolve) => {
        server.closeAllConnections();
        server.close(() => resolve());
      }),
  });
  return { url: `http://127.0.0.1:${port}`, seen };
}

interface Created {
  protocol: string;
  options: https.AgentOptions;
  agent: http.Agent;
}

/**
 * Agent factory whose sockets fail with ECONNREFUSED without touching the network
 * (deterministic, unlike dialling a port that was free a moment ago).
 */
function refusingFactory(
  created: Created[] = [],
): NonNullable<NodeHttpsFetchOptions['agentFactory']> {
  return (protocol, options) => {
    const agent = protocol === 'https:' ? new https.Agent(options) : new http.Agent(options);
    (agent as unknown as { createConnection: () => Socket }).createConnection = () => {
      const socket = new Socket();
      const err = Object.assign(new Error('connect ECONNREFUSED 127.0.0.1:1'), {
        code: 'ECONNREFUSED',
      });
      process.nextTick(() => socket.destroy(err));
      return socket;
    };
    created.push({ protocol, options, agent });
    return agent;
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

describe('createNodeHttpsFetch over http', () => {
  it('returns a WHATWG Response with status, headers and body text', async () => {
    const srv = await startHttp((_req, res) => {
      res.writeHead(201, 'Created', { 'Content-Type': 'application/json', 'X-Thing': 'a' });
      res.end('{"ok":"chào"}');
    });
    const res = await client()(`${srv.url}/api/x?q=1`);
    expect(res).toBeInstanceOf(Response);
    expect(res.status).toBe(201);
    expect(res.statusText).toBe('Created');
    expect(res.ok).toBe(true);
    expect(res.headers.get('content-type')).toBe('application/json');
    expect(res.headers.get('x-thing')).toBe('a');
    expect(await res.text()).toBe('{"ok":"chào"}');
    expect(srv.seen[0]).toMatchObject({ method: 'GET', url: '/api/x?q=1' });
  });

  it('sends method, headers and a string body with its byte length', async () => {
    const srv = await startHttp((_req, res) => res.end('done'));
    const body = JSON.stringify({ name: 'Công ty' });
    const res = await client()(new URL(`${srv.url}/webapp/insert`), {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        taxcode: '0101-APP',
        Authorization: 'Bearer t',
      },
      body,
    });
    expect(await res.text()).toBe('done');
    const s = srv.seen[0];
    expect(s?.method).toBe('POST');
    expect(s?.body).toBe(body);
    expect(s?.headers['content-type']).toBe('application/json');
    expect(s?.headers['taxcode']).toBe('0101-APP');
    expect(s?.headers['authorization']).toBe('Bearer t');
    expect(s?.headers['content-length']).toBe(String(Buffer.byteLength(body)));
  });

  it('returns non-2xx responses (no throw) and handles null-body statuses', async () => {
    const srv = await startHttp((req, res) => {
      res.statusCode = req.url === '/empty' ? 204 : 503;
      res.end(req.url === '/empty' ? undefined : '<html>down</html>');
    });
    const f = client();
    const down = await f(`${srv.url}/down`);
    expect(down.status).toBe(503);
    expect(await down.text()).toBe('<html>down</html>');
    const empty = await f(`${srv.url}/empty`);
    expect(empty.status).toBe(204);
    expect(await empty.text()).toBe('');
  });

  it('reuses one keep-alive connection across calls', async () => {
    const sockets = new Set<unknown>();
    const srv = await startHttp((req, res) => {
      sockets.add(req.socket);
      res.end('x');
    });
    const f = client();
    await (await f(`${srv.url}/a`)).text();
    await (await f(`${srv.url}/b`)).text();
    expect(srv.seen).toHaveLength(2);
    expect(sockets.size).toBe(1);
  });

  it('an already-aborted signal rejects with its reason and sends nothing', async () => {
    const srv = await startHttp((_req, res) => res.end('x'));
    const reason = new DOMException('This operation was aborted', 'AbortError');
    const err = await rejection(client()(srv.url, { signal: AbortSignal.abort(reason) }));
    expect(err).toBe(reason);
    expect(srv.seen).toHaveLength(0);
  });

  it('abort during the request rejects with the signal reason (AbortError by default)', async () => {
    const srv = await startHttp(() => {
      /* never answers */
    });
    const ctrl = new AbortController();
    const p = client()(srv.url, { method: 'POST', body: '{}', signal: ctrl.signal });
    await vi.waitFor(() => expect(srv.seen).toHaveLength(1));
    ctrl.abort();
    const err = await rejection(p);
    expect(err).toBeInstanceOf(DOMException);
    expect(err).toMatchObject({ name: 'AbortError' });
  });

  it('abort while the body is streaming rejects with the signal reason', async () => {
    const srv = await startHttp((_req, res) => {
      res.writeHead(200);
      res.write('partial');
    });
    const ctrl = new AbortController();
    const p = client()(srv.url, { signal: ctrl.signal });
    await vi.waitFor(() => expect(srv.seen).toHaveLength(1));
    await new Promise((r) => setTimeout(r, 20));
    const reason = new DOMException('The operation timed out.', 'TimeoutError');
    ctrl.abort(reason);
    expect(await rejection(p)).toBe(reason);
  });

  it('connection refused rejects with TypeError("fetch failed") carrying the socket error', async () => {
    const err = await rejection(client({ agentFactory: refusingFactory() })('http://127.0.0.1:1/'));
    expect(err).toBeInstanceOf(TypeError);
    expect((err as TypeError).message).toBe('fetch failed');
    expect((err as TypeError).cause).toMatchObject({ code: 'ECONNREFUSED' });
  });

  it('a connection dropped mid-response rejects with a TypeError carrying the cause', async () => {
    const srv = await startHttp((_req, res) => {
      res.writeHead(200, { 'Content-Length': '100' });
      res.write('short');
      setTimeout(() => res.socket?.destroy(), 10);
    });
    const err = await rejection(client()(srv.url));
    expect(err).toBeInstanceOf(TypeError);
    expect((err as TypeError).cause).toBeDefined();
  });

  it('rejects an unsupported URL scheme with TypeError("fetch failed")', async () => {
    const err = await rejection(client()('ftp://example.invalid/x'));
    expect(err).toBeInstanceOf(TypeError);
    expect((err as TypeError).message).toBe('fetch failed');
  });

  it('synchronous failures (bad header value, bad method) reject with TypeError("fetch failed")', async () => {
    const srv = await startHttp((_req, res) => res.end('x'));
    const f = client();
    const badHeader = await rejection(f(srv.url, { headers: { 'x-bad': 'a\u0000b' } }));
    expect(badHeader).toBeInstanceOf(TypeError);
    expect((badHeader as TypeError).message).toBe('fetch failed');
    expect((badHeader as TypeError).cause).toBeInstanceOf(Error);
    const badMethod = await rejection(f(srv.url, { method: 'BAD METHOD' }));
    expect(badMethod).toBeInstanceOf(TypeError);
    expect((badMethod as TypeError).message).toBe('fetch failed');
    expect((badMethod as TypeError).cause).toMatchObject({ code: 'ERR_INVALID_HTTP_TOKEN' });
    expect(srv.seen).toHaveLength(0);
  });

  it('a compressed response rejects with TypeError("fetch failed") instead of returning raw bytes', async () => {
    const srv = await startHttp((_req, res) => {
      res.writeHead(200, { 'Content-Encoding': 'gzip', 'Content-Type': 'application/json' });
      res.end(Buffer.from([0x1f, 0x8b, 0x08, 0x00]));
    });
    const err = await rejection(client()(srv.url));
    expect(err).toBeInstanceOf(TypeError);
    expect((err as TypeError).message).toBe('fetch failed');
    expect(((err as TypeError).cause as Error).message).toMatch(
      /unsupported content-encoding 'gzip'/,
    );
  });

  it('an identity content-encoding is accepted', async () => {
    const srv = await startHttp((_req, res) => {
      res.writeHead(200, { 'Content-Encoding': 'identity' });
      res.end('plain');
    });
    expect(await (await client()(srv.url)).text()).toBe('plain');
  });
});

describe('createNodeHttpsFetch keep-alive agents', () => {
  const agentOf = async (f: NodeHttpsFetch, url: string): Promise<unknown> => {
    const spy = vi.spyOn(http, 'request');
    try {
      await (await f(url)).text();
      const options = spy.mock.calls[0]?.[1] as http.RequestOptions | undefined;
      return options?.agent;
    } finally {
      spy.mockRestore();
    }
  };

  it('two default clients share one module-level agent with a 4 s idle timeout', async () => {
    const srv = await startHttp((_req, res) => res.end('x'));
    const a = await agentOf(client(), srv.url);
    const b = await agentOf(client(), srv.url);
    expect(a).toBeInstanceOf(http.Agent);
    expect(b).toBe(a);
    expect(DEFAULT_IDLE_TIMEOUT_MS).toBe(4_000);
    expect((a as http.Agent & { options: http.AgentOptions }).options).toMatchObject({
      keepAlive: true,
      timeout: 4_000,
    });
  });

  it('close() on a default client leaves the shared agent usable for other clients', async () => {
    const srv = await startHttp((_req, res) => res.end('x'));
    const first = client();
    await (await first(srv.url)).text();
    first.close();
    expect(await (await client()(srv.url)).text()).toBe('x');
  });

  it('closes an idle keep-alive socket after the idle timeout', async () => {
    const closed: boolean[] = [];
    const srv = await startHttp((req, res) => {
      const i = closed.push(false) - 1;
      req.socket.once('close', () => (closed[i] = true));
      res.end('x');
    });
    const f = client({ idleTimeoutMs: 150 });
    await (await f(srv.url)).text();
    await new Promise((r) => setTimeout(r, 30));
    expect(closed).toEqual([false]); // kept alive right after the response
    await vi.waitFor(() => expect(closed).toEqual([true]), { timeout: 2_000, interval: 20 });
  });

  it('the idle timeout does not cut off a slow in-flight response', async () => {
    const srv = await startHttp((_req, res) => {
      setTimeout(() => res.end('slow'), 400);
    });
    expect(await (await client({ idleTimeoutMs: 100 })(srv.url)).text()).toBe('slow');
  });
});

describe('createNodeHttpsFetch TLS settings', () => {
  it('uses classical ECDH groups only by default', () => {
    expect(CLASSICAL_ECDH_CURVES).toBe('X25519:P-256:P-384');
  });

  it('creates one lazy keep-alive https agent with ecdhCurve X25519:P-256:P-384', async () => {
    const created: Created[] = [];
    const f = client({ agentFactory: refusingFactory(created) });
    expect(created).toHaveLength(0);
    await rejection(f('https://misa.invalid/a'));
    await rejection(f('https://misa.invalid/b'));
    expect(created).toHaveLength(1);
    expect(created[0]?.protocol).toBe('https:');
    expect(created[0]?.options).toEqual({
      keepAlive: true,
      timeout: 4_000,
      ecdhCurve: 'X25519:P-256:P-384',
    });
    expect((created[0]?.agent as https.Agent).options.ecdhCurve).toBe('X25519:P-256:P-384');
  });

  it('honours an http/https module patch installed after creation (MSW, nock)', async () => {
    const srv = await startHttp((_req, res) => res.end('x'));
    const f = client();
    const spy = vi.spyOn(http, 'request');
    try {
      await (await f(srv.url)).text();
      expect(spy).toHaveBeenCalled();
    } finally {
      spy.mockRestore();
    }
  });

  it('the ClientHello offers only X25519, P-256 and P-384 (no hybrid post-quantum group)', async () => {
    const hello = await captureClientHello(client());
    // IANA: x25519 = 0x001d, secp256r1 = 0x0017, secp384r1 = 0x0018.
    expect(hello.supportedGroups).toEqual([0x001d, 0x0017, 0x0018]);
    expect(hello.keyShareGroups.every((g) => [0x001d, 0x0017, 0x0018].includes(g))).toBe(true);
    expect(hello.supportedGroups).not.toContain(0x11ec); // X25519MLKEM768
  });

  // Control for the parser above: on OpenSSL >= 3.5 Node's own default offers the hybrid group.
  const [major = 0, minor = 0] = process.versions.openssl.split('.').map(Number);
  it.runIf(major > 3 || (major === 3 && minor >= 5))(
    "control: Node's default TLS client offers X25519MLKEM768 on OpenSSL >= 3.5",
    async () => {
      const plain = ((input: string) =>
        new Promise((resolve, reject) => {
          const req = https.request(input, { agent: false }, resolve);
          req.on('error', reject);
          req.end();
        })) as unknown as NodeHttpsFetch;
      const hello = await captureClientHello(plain);
      expect(hello.supportedGroups).toContain(0x11ec);
    },
  );
});

/** Points the client at a raw TCP server and parses the TLS ClientHello it receives. */
async function captureClientHello(
  f: NodeHttpsFetch,
): Promise<{ supportedGroups: number[]; keyShareGroups: number[] }> {
  let resolveHello!: (b: Buffer) => void;
  const helloP = new Promise<Buffer>((r) => (resolveHello = r));
  const sockets: Socket[] = [];
  const server = createNetServer((socket) => {
    sockets.push(socket);
    let buf = Buffer.alloc(0);
    socket.on('data', (d: Buffer) => {
      buf = Buffer.concat([buf, d]);
      if (buf.length >= 5 && buf.length >= 5 + buf.readUInt16BE(3)) {
        resolveHello(buf);
        socket.destroy();
      }
    });
    socket.on('error', () => undefined);
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;
  try {
    const call = f(`https://127.0.0.1:${port}/`).catch(() => undefined);
    const record = await helloP;
    await call;
    return parseClientHello(record);
  } finally {
    for (const s of sockets) s.destroy();
    await new Promise<void>((resolve) => server.close(() => resolve()));
  }
}

function parseClientHello(record: Buffer): { supportedGroups: number[]; keyShareGroups: number[] } {
  expect(record[0]).toBe(0x16); // handshake record
  let p = 5;
  expect(record[p]).toBe(0x01); // ClientHello
  p += 4 + 2 + 32; // handshake header, legacy_version, random
  p += 1 + (record[p] ?? 0); // session id
  p += 2 + record.readUInt16BE(p); // cipher suites
  p += 1 + (record[p] ?? 0); // compression methods
  const extEnd = p + 2 + record.readUInt16BE(p);
  p += 2;
  const supportedGroups: number[] = [];
  const keyShareGroups: number[] = [];
  while (p + 4 <= extEnd) {
    const type = record.readUInt16BE(p);
    const len = record.readUInt16BE(p + 2);
    const data = record.subarray(p + 4, p + 4 + len);
    if (type === 0x000a) {
      for (let i = 2; i < 2 + data.readUInt16BE(0); i += 2)
        supportedGroups.push(data.readUInt16BE(i));
    } else if (type === 0x0033) {
      let q = 2;
      while (q < 2 + data.readUInt16BE(0)) {
        keyShareGroups.push(data.readUInt16BE(q));
        q += 4 + data.readUInt16BE(q + 2);
      }
    }
    p += 4 + len;
  }
  return { supportedGroups, keyShareGroups };
}

import http from 'node:http';
import https from 'node:https';

/**
 * Classical ECDH groups only. OpenSSL >= 3.5 (Node >= 22.20 / 24) also offers the hybrid
 * post-quantum X25519MLKEM768 by default; the much larger ClientHello is reset by a
 * middlebox in front of MISA for traffic from outside Vietnam (ECONNRESET).
 */
export const CLASSICAL_ECDH_CURVES = 'X25519:P-256:P-384';

export interface NodeHttpsFetchOptions {
  /** TLS key-exchange groups for https URLs. Default {@link CLASSICAL_ECDH_CURVES}. */
  ecdhCurve?: string;
  /** Creates the keep-alive agent for a protocol (tests). Called at most once per protocol. */
  agentFactory?: (protocol: 'http:' | 'https:', options: https.AgentOptions) => http.Agent;
}

/** A fetch-compatible function owning its keep-alive agents; `close()` destroys them. */
export type NodeHttpsFetch = typeof fetch & { close(): void };

const defaultAgentFactory = (protocol: 'http:' | 'https:', options: https.AgentOptions) =>
  protocol === 'https:' ? new https.Agent(options) : new http.Agent(options);

// Statuses whose Response must have a null body (WHATWG fetch).
const NULL_BODY_STATUSES = new Set([101, 103, 204, 205, 304]);

const fetchFailed = (cause: unknown): TypeError => new TypeError('fetch failed', { cause });

const abortReason = (signal: AbortSignal): unknown =>
  signal.reason ?? new DOMException('This operation was aborted', 'AbortError');

/**
 * Minimal `fetch` over `node:https` / `node:http`: method, headers, string / byte body and
 * `signal`; resolves with a fully buffered WHATWG `Response`. Failures mirror undici: an abort
 * rejects with `signal.reason`, any socket/TLS error with `TypeError('fetch failed', { cause })`.
 * Redirects are not followed and bodies are not decompressed (no `Accept-Encoding` is sent).
 * `request` is looked up on the module at call time, so http/https interceptors installed
 * later (MSW, nock) apply.
 */
export function createNodeHttpsFetch(opts: NodeHttpsFetchOptions = {}): NodeHttpsFetch {
  const ecdhCurve = opts.ecdhCurve ?? CLASSICAL_ECDH_CURVES;
  const agentFactory = opts.agentFactory ?? defaultAgentFactory;
  const agents = new Map<'http:' | 'https:', http.Agent>();

  const agentFor = (protocol: 'http:' | 'https:'): http.Agent => {
    let agent = agents.get(protocol);
    if (agent === undefined) {
      agent = agentFactory(
        protocol,
        protocol === 'https:' ? { keepAlive: true, ecdhCurve } : { keepAlive: true },
      );
      agents.set(protocol, agent);
    }
    return agent;
  };

  const nodeFetch = (input: string | URL | Request, init: RequestInit = {}): Promise<Response> =>
    new Promise<Response>((resolve, reject) => {
      const signal = init.signal ?? undefined;
      if (signal?.aborted) {
        reject(abortReason(signal));
        return;
      }
      let url: URL;
      let body: Buffer | undefined;
      try {
        if (typeof input !== 'string' && !(input instanceof URL)) {
          throw new TypeError('Request objects are not supported; pass a URL string.');
        }
        url = new URL(String(input));
        if (url.protocol !== 'https:' && url.protocol !== 'http:') {
          throw new TypeError(`URL scheme '${url.protocol}' is not supported.`);
        }
        body = toBuffer(init.body);
      } catch (err) {
        reject(fetchFailed(err));
        return;
      }
      const protocol = url.protocol as 'http:' | 'https:';

      const headers: Record<string, string> = {};
      new Headers(init.headers).forEach((value, name) => {
        headers[name] = value;
      });
      headers['accept'] ??= '*/*';
      if (body !== undefined) headers['content-length'] = String(body.length);

      let settled = false;
      const settle = (fn: () => void): void => {
        if (settled) return;
        settled = true;
        signal?.removeEventListener('abort', onAbort);
        fn();
      };
      const onAbort = (): void => {
        const reason = signal === undefined ? undefined : abortReason(signal);
        settle(() => reject(reason));
        req.destroy();
      };

      const mod = protocol === 'https:' ? https : http;
      const req = mod.request(
        url,
        { method: init.method ?? 'GET', headers, agent: agentFor(protocol) },
        (res) => {
          const chunks: Buffer[] = [];
          res.on('data', (c: Buffer) => chunks.push(c));
          res.on('error', (err) =>
            settle(() => reject(new TypeError('terminated', { cause: err }))),
          );
          res.on('aborted', () =>
            settle(() => reject(new TypeError('terminated', { cause: new Error('aborted') }))),
          );
          res.on('end', () => {
            if (!res.complete) {
              settle(() => reject(new TypeError('terminated', { cause: new Error('aborted') })));
              return;
            }
            settle(() => {
              try {
                resolve(toResponse(res, Buffer.concat(chunks)));
              } catch (err) {
                reject(fetchFailed(err));
              }
            });
          });
        },
      );
      req.on('error', (err) => settle(() => reject(fetchFailed(err))));
      signal?.addEventListener('abort', onAbort, { once: true });
      req.end(body);
    });

  return Object.assign(nodeFetch as typeof fetch, {
    close: (): void => {
      for (const agent of agents.values()) agent.destroy();
      agents.clear();
    },
  });
}

function toBuffer(body: RequestInit['body']): Buffer | undefined {
  if (body === undefined || body === null) return undefined;
  if (typeof body === 'string') return Buffer.from(body, 'utf8');
  if (body instanceof Uint8Array) return Buffer.from(body);
  if (body instanceof ArrayBuffer) return Buffer.from(new Uint8Array(body));
  throw new TypeError('Only string and byte request bodies are supported.');
}

function toResponse(res: http.IncomingMessage, bytes: Buffer): Response {
  const headers = new Headers();
  const raw = res.rawHeaders;
  for (let i = 0; i + 1 < raw.length; i += 2) {
    const name = raw[i];
    const value = raw[i + 1];
    if (name !== undefined && value !== undefined) headers.append(name, value);
  }
  const status = res.statusCode ?? 0;
  return new Response(NULL_BODY_STATUSES.has(status) ? null : new Uint8Array(bytes), {
    status,
    statusText: res.statusMessage ?? '',
    headers,
  });
}

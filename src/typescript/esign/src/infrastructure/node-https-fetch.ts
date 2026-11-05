// Duplicated from @misaconnect/einvoice (src/infrastructure/node-https-fetch.ts) — keep in sync.
import http from 'node:http';
import https from 'node:https';

/**
 * Classical ECDH groups only. Node >= 22 built with OpenSSL 3.5 (22.20+ / 24) also offers the
 * hybrid post-quantum X25519MLKEM768 by default; the much larger ClientHello is reset by a
 * middlebox in front of MISA for traffic from outside Vietnam (ECONNRESET).
 */
export const CLASSICAL_ECDH_CURVES = 'X25519:P-256:P-384';

/**
 * Per-address connect attempt budget for Node's happy-eyeballs (`autoSelectFamily`). Node's
 * default is 250 ms, but MISA publishes an AAAA record that IPv4-only hosts cannot reach, so
 * every connection goes through happy-eyeballs, and the TCP handshake to Vietnam from abroad
 * takes ~210-250 ms (spikes to ~900 ms): connections failed with ETIMEDOUT at ~250 ms.
 */
export const CONNECT_ATTEMPT_TIMEOUT_MS = 2_000;

/**
 * Idle keep-alive sockets are closed after this long (undici's default keep-alive is 4 s;
 * IIS in front of MISA would otherwise hold them for ~120 s).
 */
export const DEFAULT_IDLE_TIMEOUT_MS = 4_000;

type Protocol = 'http:' | 'https:';

export interface NodeHttpsFetchOptions {
  /** TLS key-exchange groups for https URLs. Default {@link CLASSICAL_ECDH_CURVES}. */
  ecdhCurve?: string;
  /** Idle keep-alive socket lifetime. Default {@link DEFAULT_IDLE_TIMEOUT_MS}. */
  idleTimeoutMs?: number;
  /**
   * Creates a private keep-alive agent for a protocol (tests). Called at most once per protocol
   * per client. Without it, agents are module-level and shared by every client with the same
   * settings.
   */
  agentFactory?: (protocol: Protocol, options: https.AgentOptions) => http.Agent;
}

/**
 * A fetch-compatible function; `close()` destroys the agents it owns (only those made by an
 * injected `agentFactory`; the shared module-level agents are left alone).
 */
export type NodeHttpsFetch = typeof fetch & { close(): void };

const defaultAgentFactory = (protocol: Protocol, options: https.AgentOptions): http.Agent =>
  protocol === 'https:' ? new https.Agent(options) : new http.Agent(options);

// Module-level (not global) agents, created lazily and shared by every default client so that
// creating a client per request does not leak keep-alive sockets. Keyed by their settings.
const sharedAgents = new Map<string, http.Agent>();

// Statuses whose Response must have a null body (WHATWG fetch). 1xx never reach here: Node
// handles informational responses itself (and `Response` rejects statuses below 200).
const NULL_BODY_STATUSES = new Set([204, 205, 304]);

const fetchFailed = (cause: unknown): TypeError => new TypeError('fetch failed', { cause });

const terminated = (cause: unknown): TypeError => new TypeError('terminated', { cause });

const abortReason = (signal: AbortSignal): unknown =>
  signal.reason ?? new DOMException('This operation was aborted', 'AbortError');

/**
 * Minimal `fetch` over `node:https` / `node:http`: method, headers, string / byte body and
 * `signal`; resolves with a fully buffered WHATWG `Response`. Failures mirror undici: an abort
 * rejects with `signal.reason`, any other failure (socket/TLS error, synchronous throw) with
 * `TypeError('fetch failed', { cause })`. Redirects are not followed and bodies are not
 * decompressed: no `Accept-Encoding` is sent and a non-identity `Content-Encoding` is rejected.
 * `request` is looked up on the module at call time, so http/https interceptors installed
 * later (MSW, nock) apply.
 *
 * The agents' `timeout` closes idle keep-alive sockets: Node destroys a socket that times out
 * while in the agent's free list; on an in-flight request it only emits `'timeout'` (no
 * listener here), so slow responses are not cut off.
 */
export function createNodeHttpsFetch(opts: NodeHttpsFetchOptions = {}): NodeHttpsFetch {
  const ecdhCurve = opts.ecdhCurve ?? CLASSICAL_ECDH_CURVES;
  const timeout = opts.idleTimeoutMs ?? DEFAULT_IDLE_TIMEOUT_MS;
  const factory = opts.agentFactory;
  const owned = new Map<Protocol, http.Agent>();

  const agentOptions = (protocol: Protocol): https.AgentOptions =>
    protocol === 'https:'
      ? {
          keepAlive: true,
          timeout,
          ecdhCurve,
          autoSelectFamilyAttemptTimeout: CONNECT_ATTEMPT_TIMEOUT_MS,
        }
      : { keepAlive: true, timeout, autoSelectFamilyAttemptTimeout: CONNECT_ATTEMPT_TIMEOUT_MS };

  const agentFor = (protocol: Protocol): http.Agent => {
    if (factory !== undefined) {
      let agent = owned.get(protocol);
      if (agent === undefined) {
        agent = factory(protocol, agentOptions(protocol));
        owned.set(protocol, agent);
      }
      return agent;
    }
    const key = protocol === 'https:' ? `https:|${timeout}|${ecdhCurve}` : `http:|${timeout}`;
    let agent = sharedAgents.get(key);
    if (agent === undefined) {
      agent = defaultAgentFactory(protocol, agentOptions(protocol));
      sharedAgents.set(key, agent);
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

      let settled = false;
      let req: http.ClientRequest | undefined;
      const settle = (fn: () => void): void => {
        if (settled) return;
        settled = true;
        signal?.removeEventListener('abort', onAbort);
        fn();
      };
      const onAbort = (): void => {
        const reason = signal === undefined ? undefined : abortReason(signal);
        settle(() => reject(reason));
        req?.destroy();
      };

      const onResponse = (res: http.IncomingMessage): void => {
        const encoding = (res.headers['content-encoding'] ?? '').trim().toLowerCase();
        if (encoding !== '' && encoding !== 'identity') {
          settle(() =>
            reject(fetchFailed(new Error(`unsupported content-encoding '${encoding}'`))),
          );
          res.destroy();
          return;
        }
        const chunks: Buffer[] = [];
        res.on('data', (c: Buffer) => chunks.push(c));
        res.on('error', (err) => settle(() => reject(terminated(err))));
        res.on('aborted', () => settle(() => reject(terminated(new Error('aborted')))));
        res.on('end', () => {
          if (!res.complete) {
            settle(() => reject(terminated(new Error('aborted'))));
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
      };

      let body: Buffer | undefined;
      try {
        if (typeof input !== 'string' && !(input instanceof URL)) {
          throw new TypeError('Request objects are not supported; pass a URL string.');
        }
        const url = new URL(String(input));
        if (url.protocol !== 'https:' && url.protocol !== 'http:') {
          throw new TypeError(`URL scheme '${url.protocol}' is not supported.`);
        }
        const protocol: Protocol = url.protocol;
        body = toBuffer(init.body);

        const headers: Record<string, string> = {};
        new Headers(init.headers).forEach((value, name) => {
          headers[name] = value;
        });
        headers['accept'] ??= '*/*';
        if (body !== undefined) headers['content-length'] = String(body.length);

        const mod = protocol === 'https:' ? https : http;
        req = mod.request(
          url,
          { method: init.method ?? 'GET', headers, agent: agentFor(protocol) },
          onResponse,
        );
      } catch (err) {
        reject(fetchFailed(err));
        return;
      }

      req.on('error', (err) => settle(() => reject(fetchFailed(err))));
      signal?.addEventListener('abort', onAbort, { once: true });
      req.end(body);
    });

  return Object.assign(nodeFetch as typeof fetch, {
    close: (): void => {
      for (const agent of owned.values()) agent.destroy();
      owned.clear();
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

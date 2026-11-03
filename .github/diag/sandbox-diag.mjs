// TEMPORARY diagnostic for ECONNRESET on authenticated sandbox calls from GitHub runners.
// Prints only status codes, byte counts, timings, TLS group and error codes — never bodies,
// tokens or credentials. Remove once the root cause is recorded.
/* global process, Buffer, console, setTimeout */
import https from 'node:https';

const HOST = 'testapi.meinvoice.vn';
const BASE = '/api/integration';
const env = (k) => process.env[k]?.trim() ?? '';
const wire = `${env('MISACONNECT_SANDBOX_TAXCODE')}-${env('MISACONNECT_SANDBOX_APPID')}`;
const user = env('MISACONNECT_SANDBOX_USERNAME');
const pass = env('MISACONNECT_SANDBOX_PASSWORD');
const CLASSICAL = 'X25519:P-256:P-384';

function call(label, { path, body, headers = {}, agent, ecdhCurve = CLASSICAL }) {
  const payload = JSON.stringify(body);
  const started = Date.now();
  return new Promise((resolve) => {
    const req = https.request(
      {
        host: HOST,
        path: BASE + path,
        method: 'POST',
        agent: agent ?? false,
        ...(agent ? {} : { ecdhCurve }),
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(payload),
          ...headers,
        },
        timeout: 30_000,
      },
      (res) => {
        const chunks = [];
        res.on('data', (c) => chunks.push(c));
        res.on('end', () => {
          const buf = Buffer.concat(chunks);
          let note;
          try {
            const j = JSON.parse(buf.toString('utf8'));
            note = j.success ? 'success' : `success=false error=${JSON.stringify(j.error)?.slice(0, 40)}`;
          } catch {
            note = 'non-JSON';
          }
          console.log(
            `${label}: HTTP ${res.statusCode}, ${buf.length} B, ${Date.now() - started} ms, reused=${req.reusedSocket}, ${note}`,
          );
          resolve(buf);
        });
        res.on('error', (e) => {
          console.log(`${label}: RESPONSE ERROR ${e.code ?? e.message} after ${Date.now() - started} ms`);
          resolve(undefined);
        });
      },
    );
    req.on('socket', (s) =>
      s.once('secureConnect', () => {
        const k = s.getEphemeralKeyInfo?.();
        console.log(`${label}:   tls ${s.getProtocol()} group=${k?.name ?? '?'}`);
      }),
    );
    req.on('timeout', () => req.destroy(Object.assign(new Error('timeout'), { code: 'TIMEOUT' })));
    req.on('error', (e) => {
      console.log(`${label}: ERROR ${e.code ?? e.message} after ${Date.now() - started} ms, reused=${req.reusedSocket}`);
      resolve(undefined);
    });
    req.end(payload);
  });
}

const tokenOf = (buf) => {
  try {
    const j = JSON.parse(buf.toString('utf8'));
    return JSON.parse(j.data).access_token;
  } catch {
    return undefined;
  }
};

const creds = (pw) => ({ taxcode: wire, username: user, password: pw });
const tplBody = { TaxCode: wire, UserName: user, Password: pass };
const tplPath = '/webapp/templates?invoiceWithCode=true&isTicket=false';

console.log(`node ${process.version}, openssl ${process.versions.openssl}`);

// 1. Control: small error response on a fresh connection.
await call('1 token wrong-pw  fresh', { path: '/webapp/token', body: creds('wrong-on-purpose') });
// 2. Good token on a fresh connection (larger response).
const t = tokenOf((await call('2 token good      fresh', { path: '/webapp/token', body: creds(pass) })) ?? Buffer.alloc(0));
console.log(`  token obtained: ${t ? `yes (${t.length} chars)` : 'no'}`);
// 3. Templates on a fresh connection.
if (t) {
  const auth = { Authorization: `Bearer ${t}`, taxcode: wire };
  await call('3 templates       fresh', { path: tplPath, body: tplBody, headers: auth });
  // 4. Keep-alive agent like the SDK: two calls back to back, then one after 2 s idle.
  const agent = new https.Agent({ keepAlive: true, timeout: 4000, ecdhCurve: CLASSICAL });
  await call('4a templates      keepalive', { path: tplPath, body: tplBody, headers: auth, agent });
  await call('4b templates      keepalive', { path: tplPath, body: tplBody, headers: auth, agent });
  await new Promise((r) => setTimeout(r, 2000));
  await call('4c templates      keepalive+2s', { path: tplPath, body: tplBody, headers: auth, agent });
  agent.destroy();
}
// 6. Request-size isolation: the same small wrong-password token call, padded with a dummy
//    header to ~1 KB, ~2 KB, ~4 KB. If only the large ones fail, the network drops large requests.
for (const size of [1000, 2000, 4000]) {
  await call(`6 token wrong-pw  pad ${size}`, {
    path: '/webapp/token',
    body: creds('wrong-on-purpose'),
    headers: { 'X-Diag-Pad': 'x'.repeat(size) },
  });
}
// 7. Templates without the bearer header (small request; body still carries the credentials).
await call('7 templates       no-bearer', { path: tplPath, body: tplBody, headers: { taxcode: wire } });
// 5. Default (post-quantum) curves on a fresh connection, for comparison.
await call('5 token good      fresh PQ', { path: '/webapp/token', body: creds(pass), ecdhCurve: undefined });

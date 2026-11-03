// Credential-free diagnostics: which Node TLS client settings can reach the MISA sandbox?
import https from 'node:https';
import tls from 'node:tls';
const host = 'testapi.meinvoice.vn';
const path = '/api/integration/';
console.log('node', process.version, 'openssl', process.versions.openssl);
console.log('default ecdhCurve', tls.DEFAULT_ECDH_CURVE, 'min/max', tls.DEFAULT_MIN_VERSION, tls.DEFAULT_MAX_VERSION);
function get(label, opts) {
  return new Promise((resolve) => {
    const req = https.request({ host, path, method: 'GET', timeout: 15000, ...opts }, (res) => {
      res.resume();
      console.log(label.padEnd(34), 'HTTP', res.statusCode, 'tls', res.socket.getProtocol?.(), 'curve', res.socket.getEphemeralKeyInfo?.()?.name);
      resolve();
    });
    req.on('timeout', () => req.destroy(new Error('timeout')));
    req.on('error', (e) => { console.log(label.padEnd(34), 'ERR', e.code ?? e.message); resolve(); });
    req.end();
  });
}
await get('https default', {});
await get('https maxVersion TLSv1.2', { maxVersion: 'TLSv1.2' });
await get('https ecdhCurve X25519:P-256:P-384', { ecdhCurve: 'X25519:P-256:P-384' });
await get('https ecdhCurve X25519', { ecdhCurve: 'X25519' });
await get('https ecdhCurve P-256', { ecdhCurve: 'P-256' });
try { const r = await fetch(`https://${host}${path}`, { signal: AbortSignal.timeout(15000) }); console.log('fetch default'.padEnd(34), 'HTTP', r.status); }
catch (e) { console.log('fetch default'.padEnd(34), 'ERR', e.cause?.code ?? e.message); }

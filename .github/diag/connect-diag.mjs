/* global console, process */
// TEMPORARY: why does Node time out connecting to MISA from GitHub runners while curl works?
import net from 'node:net';
import dns from 'node:dns/promises';

const HOST = 'testapi.meinvoice.vn';
console.log(`node ${process.version} autoSelectFamily=${net.getDefaultAutoSelectFamily()} attemptTimeout=${net.getDefaultAutoSelectFamilyAttemptTimeout()}`);
for (const family of [4, 6]) {
  try {
    console.log(`dns family ${family}:`, (await dns.lookup(HOST, { family, all: true })).map((a) => a.address));
  } catch (e) {
    console.log(`dns family ${family}: ${e.code}`);
  }
}
function attempt(label, opts) {
  return new Promise((resolve) => {
    const t0 = Date.now();
    const s = net.connect({ host: HOST, port: 443, timeout: 15000, ...opts });
    const done = (r) => {
      console.log(`${label}: ${r} after ${Date.now() - t0} ms${s.remoteAddress ? ` (${s.remoteAddress})` : ''}`);
      s.destroy();
      resolve();
    };
    s.once('connect', () => done('connected'));
    s.once('timeout', () => done('socket timeout'));
    s.once('error', (e) => done(`error ${e.code}${e.errors ? ' [' + e.errors.map((x) => x.code + ' ' + (x.address ?? '')).join(', ') + ']' : ''}`));
  });
}
for (let i = 0; i < 3; i++) {
  await attempt(`default #${i}`, {});
  await attempt(`autoSelectFamily=false #${i}`, { autoSelectFamily: false });
  await attempt(`attemptTimeout=2000 #${i}`, { autoSelectFamilyAttemptTimeout: 2000 });
  await attempt(`family=4 #${i}`, { family: 4 });
  await attempt(`family=6 #${i}`, { family: 6 });
}

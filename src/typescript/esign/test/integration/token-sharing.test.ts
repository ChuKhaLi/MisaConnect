import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { createMisaESignClientInternal } from '../../src/client-internal.js';
import type { IMisaESignClient } from '../../src/client.js';
import type { MisaCredentials } from '../../src/domain/dtos.js';
import { createInMemoryTokenCache } from '../../src/infrastructure/defaults.js';
import type { MisaESignOptions } from '../../src/infrastructure/options.js';
import { startFakeESignServer, type FakeESignServer } from '../fake/fake-esign-server.js';

// Two client instances sharing one token cache stand in for two hosts sharing Redis.
const creds: MisaCredentials = {
  clientId: 'cid',
  clientKey: 'ck',
  userName: 'alice',
  password: 'pw',
};
const SECRET = 'a-shared-fingerprint-secret-of-32+chars!';
const NOW = new Date('2026-11-05T08:00:00.000Z');

let fake: FakeESignServer;
beforeAll(async () => {
  fake = await startFakeESignServer();
});
afterAll(async () => {
  await fake.close();
});
beforeEach(() => {
  fake.requests.length = 0;
  fake.resetScenario();
});

function makeClient(over: Partial<MisaESignOptions>): IMisaESignClient {
  return createMisaESignClientInternal(
    {
      baseUrl: fake.baseUrl,
      credentials: creds,
      polling: { intervalMs: 10, totalTimeoutMs: 5000 },
      ...over,
    },
    { clock: { now: () => NOW }, delay: () => Promise.resolve() },
  );
}

const pdf = {
  pdf: new Uint8Array([37, 80, 68, 70]),
  documentName: 'contract',
  signerName: 'Alice',
  location: 'Hanoi',
  reason: 'Approve',
  contact: 'alice@example.test',
  logoImageBase64: 'TE9HTw==',
  dataToBeDisplayed: 'Sign the contract',
};
const logins = (): number => fake.requests.filter((r) => r.path.endsWith('/login-api')).length;

describe('tokenFingerprintSecret', () => {
  it('two clients with the same secret share cached tokens (one login)', async () => {
    const tokenCache = createInMemoryTokenCache();
    await makeClient({ tokenCache, tokenFingerprintSecret: SECRET }).signPdf(pdf);
    await makeClient({ tokenCache, tokenFingerprintSecret: SECRET }).signPdf(pdf);
    expect(logins()).toBe(1);
  });

  it('without a secret each client logs in itself (per-instance salt)', async () => {
    const tokenCache = createInMemoryTokenCache();
    await makeClient({ tokenCache }).signPdf(pdf);
    await makeClient({ tokenCache }).signPdf(pdf);
    expect(logins()).toBe(2);
  });

  it('different secrets do not share tokens', async () => {
    const tokenCache = createInMemoryTokenCache();
    await makeClient({ tokenCache, tokenFingerprintSecret: SECRET }).signPdf(pdf);
    await makeClient({ tokenCache, tokenFingerprintSecret: `${SECRET}-other` }).signPdf(pdf);
    expect(logins()).toBe(2);
  });

  it('a wrong password with the shared secret still cannot reuse the token', async () => {
    const tokenCache = createInMemoryTokenCache();
    await makeClient({ tokenCache, tokenFingerprintSecret: SECRET }).signPdf(pdf);
    await makeClient({ tokenCache, tokenFingerprintSecret: SECRET }).signPdf(pdf, {
      credentials: { ...creds, password: 'wrong' },
    });
    expect(logins()).toBe(2);
    const second = fake.requests.filter((r) => r.path.endsWith('/login-api'))[1];
    expect(JSON.parse(second?.bodyText ?? '{}')).toMatchObject({ password: 'wrong' });
  });
});

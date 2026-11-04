import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import {
  buildSyntheticWebhookBody,
  postWebhook,
  startFakeESignServer,
  type FakeESignServer,
} from './fake-esign-server.js';

let fake: FakeESignServer;
let echo: FakeESignServer;

beforeAll(async () => {
  fake = await startFakeESignServer();
  echo = await startFakeESignServer();
});
afterAll(async () => {
  await fake.close();
  await echo.close();
});
beforeEach(() => {
  fake.requests.length = 0;
  fake.resetScenario();
});

const post = (path: string, body: unknown) =>
  fetch(fake.baseUrl + path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });

describe('fake eSign server', () => {
  it.each(['', '/webdev'])('login works at "%s"', async (prefix) => {
    const res = await post(`${prefix}/api/auth/api/v1/auth/login-api`, {});
    const json = (await res.json()) as { data: { remoteSigningAccessToken: string } };
    expect(res.status).toBe(200);
    expect(json.data.remoteSigningAccessToken).toBe('rs-token');
  });

  it('serves two-factor, refresh and resend', async () => {
    const tf = await post('/api/auth/api/v1/auth/two-factor-auth', {});
    expect(((await tf.json()) as { data: { accessToken: string } }).data.accessToken).toBe(
      'raw-token-2fa',
    );
    const rf = await post('/webdev/api/auth/api/v1/auth/refreshtoken', {});
    expect(((await rf.json()) as { accessToken: string }).accessToken).toBe('raw-token-2');
    const rs = await post('/webdev/api/auth/api/v1/auth/resend-otp-auth', {});
    expect(rs.status).toBe(200);
  });

  it('returns certificates and records AuthorizationRM', async () => {
    const res = await fetch(
      `${fake.baseUrl}/external/esrm/service/general/api/v1/Certificates/by-userId`,
      { headers: { AuthorizationRM: 'Bearer abc' } },
    );
    const json = (await res.json()) as { certiticateChain: string[]; keyStatus: string }[];
    expect(json[0]?.certiticateChain).toHaveLength(3);
    expect(json[0]?.keyStatus).toBe('ACTIVE');
    expect(fake.lastCertificatesBearer).toBe('abc');
  });

  it('hash, sign, status (pending then success) and attachment defaults', async () => {
    const h = await post('/external/esrm/service/document/api/v1/documents/hash', {
      xmlDocs: [{ documentId: 'doc-1' }],
    });
    expect(Object.keys((await h.json()) as object)).toEqual(['xmlDocs']);

    const s = await post('/external/esrm/service/signing/api/v1/Signing/hash', {});
    expect(await s.json()).toEqual({ transactionId: 'tx-fake-1' });

    const url = `${fake.baseUrl}/external/esrm/service/signing/api/v1/Signing/status/tx-fake-1`;
    expect(((await (await fetch(url)).json()) as { status: string }).status).toBe('PENDING');
    const ok = (await (await fetch(url)).json()) as {
      status: string;
      signatures: { documentId: string }[];
    };
    expect(ok.status).toBe('SUCCESS');
    expect(ok.signatures[0]?.documentId).toBe('doc-1');

    const a = await post('/external/esrm/service/document/api/v1/documents/attachment', {
      pdfDocs: [{ documentId: 'doc-1' }],
    });
    const att = (await a.json()) as { pdfDocs: { document: string }[] };
    expect(Buffer.from(att.pdfDocs[0]?.document ?? '', 'base64').toString()).toBe('SIGNED-PDF');
    const x = await post('/external/esrm/service/document/api/v1/documents/attachment', {
      xmlDocs: [{ documentId: 'doc-1' }],
    });
    expect(((await x.json()) as { xmlDocs: { document: string }[] }).xmlDocs[0]?.document).toBe(
      '<signed/>',
    );
  });

  it('rejects present-but-empty doc arrays with validationFailures', async () => {
    const res = await post('/external/esrm/service/document/api/v1/documents/hash', {
      pdfDocs: [],
    });
    const json = (await res.json()) as { validationFailures: { property: string }[] };
    expect(res.status).toBe(400);
    expect(json.validationFailures[0]?.property).toBe('PdfDocs');
  });

  it('honours scenarios', async () => {
    fake.scenario.signHashStatus = 503;
    expect((await post('/external/esrm/service/signing/api/v1/Signing/hash', {})).status).toBe(503);
    fake.scenario.loginAlways401 = true;
    expect((await post('/api/auth/api/v1/auth/login-api', {})).status).toBe(401);
    fake.scenario.hashRejectionForFormat = {
      format: 'pdf',
      statusCode: 422,
      errorCode: 'X',
      devMsg: 'd',
      userMsg: 'u',
    };
    const r = await post('/external/esrm/service/document/api/v1/documents/hash', {
      pdfDocs: [{ documentId: 'doc-1' }],
    });
    expect(r.status).toBe(422);
  });

  it('postWebhook posts a synthetic envelope', async () => {
    const body = buildSyntheticWebhookBody({ clientId: 'c', transactionId: 'tx' });
    expect(body).toMatchObject({ clientId: 'c', transactionId: 'tx', status: 'SUCCESS' });
    const res = await postWebhook(echo.baseUrl + '/hook', body);
    expect(res.status).toBe(404);
    expect(echo.requests.at(-1)?.bodyText).toContain('"transactionId":"tx"');
  });

  const SIGN = '/external/esrm/service/signing/api/v1/Signing/hash';
  const statusOf = async (tx: string) =>
    (
      (await (
        await fetch(`${fake.baseUrl}/external/esrm/service/signing/api/v1/Signing/status/${tx}`)
      ).json()) as { status: string }
    ).status;

  it('each sign flow starts at poll 1', async () => {
    for (let i = 0; i < 2; i++) {
      const tx = ((await (await post(SIGN, {})).json()) as { transactionId: string }).transactionId;
      expect(await statusOf(tx)).toBe('PENDING');
      expect(await statusOf(tx)).toBe('SUCCESS');
    }
  });

  it('once-only scenarios re-arm when set again and on reset', async () => {
    const login = () => post('/api/auth/api/v1/auth/login-api', {});
    const isChallenge = async () =>
      ((await (await login()).json()) as { status: { errorCode?: string } }).status.errorCode ===
      '122';
    fake.scenario.loginRequires2FAOnFirstCall = true;
    expect(await isChallenge()).toBe(true);
    expect(await isChallenge()).toBe(false);
    fake.scenario.loginRequires2FAOnFirstCall = true;
    expect(await isChallenge()).toBe(true);
    fake.resetScenario();
    expect(await isChallenge()).toBe(false);

    const certs = () =>
      fetch(`${fake.baseUrl}/external/esrm/service/general/api/v1/Certificates/by-userId`);
    fake.scenario.certsForce401Once = true;
    expect((await certs()).status).toBe(401);
    expect((await certs()).status).toBe(200);
    fake.scenario.certsForce401Once = true;
    expect((await certs()).status).toBe(401);
  });

  it('covers cancelled, empty certs and resend modes', async () => {
    fake.scenario.statusCancelled = true;
    expect(await statusOf('tx-c')).toBe('CANCELLED');
    fake.scenario.certsEmptyArray200 = true;
    const certs = await fetch(
      `${fake.baseUrl}/external/esrm/service/general/api/v1/Certificates/by-userId`,
    );
    expect(await certs.json()).toEqual([]);
    const resend = '/webdev/api/auth/api/v1/auth/resend-otp-auth';
    fake.scenario.resendMode = 'typedFailure200';
    const t = (await (await post(resend, {})).json()) as { status: { errorCode: string } };
    expect(t.status.errorCode).toBe('RateLimited');
    fake.scenario.resendMode = 'transport500';
    expect((await post(resend, {})).status).toBe(500);
  });
});

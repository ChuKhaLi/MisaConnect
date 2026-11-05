import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { createMisaESignClientInternal } from '../../src/client-internal.js';
import type { CallOptions, IMisaESignClient } from '../../src/client.js';
import { SignXmlRequest, type MisaCredentials } from '../../src/domain/dtos.js';
import {
  ESignGeneralException,
  ESignTransportException,
  NoActiveCertificateException,
  SignTerminalStateException,
} from '../../src/domain/errors.js';
import type { MisaESignOptions } from '../../src/infrastructure/options.js';
import {
  SIGNED_EXCEL_TEXT,
  SIGNED_PDF_TEXT,
  SIGNED_WORD_TEXT,
  SIGNED_XML_TEXT,
  startFakeESignServer,
  type FakeESignServer,
  type FakeFormat,
  type FakeRequest,
} from '../fake/fake-esign-server.js';

const creds: MisaCredentials = {
  clientId: 'cid',
  clientKey: 'ck',
  userName: 'alice',
  password: 'pw',
};
const NOW = new Date('2026-11-04T08:00:00.000Z');

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

function makeClient(over: Partial<MisaESignOptions> = {}): IMisaESignClient {
  let n = 0;
  return createMisaESignClientInternal(
    {
      baseUrl: fake.baseUrl,
      credentials: creds,
      polling: { intervalMs: 10, totalTimeoutMs: 5000 },
      ...over,
    },
    {
      clock: { now: () => NOW },
      delay: (_ms, signal) => {
        signal?.throwIfAborted();
        return Promise.resolve();
      },
      newId: () => `id-${++n}`,
    },
  );
}

const visible = {
  documentName: 'contract',
  signerName: 'Alice',
  location: 'Hanoi',
  reason: 'Approve',
  contact: 'alice@example.test',
  logoImageBase64: 'TE9HTw==',
  dataToBeDisplayed: 'Sign the contract',
};
const bytes = new Uint8Array([37, 80, 68, 70]);
const text = (b: Uint8Array): string => new TextDecoder().decode(b);

interface FormatCase {
  format: 'Pdf' | 'Xml' | 'Word' | 'Excel';
  fake: FakeFormat;
  expected: string;
  sign(c: IMisaESignClient, opts?: CallOptions): Promise<{ signed: Uint8Array; r: object }>;
}

const CASES: FormatCase[] = [
  {
    format: 'Pdf',
    fake: 'pdf',
    expected: SIGNED_PDF_TEXT,
    sign: async (c, o) => {
      const r = await c.signPdf({ ...visible, pdf: bytes }, o);
      return { signed: r.signedPdf, r };
    },
  },
  {
    format: 'Xml',
    fake: 'xml',
    expected: SIGNED_XML_TEXT,
    sign: async (c, o) => {
      const r = await c.signXml(
        SignXmlRequest.fromString('<inv>Hóa đơn</inv>', {
          documentName: 'invoice',
          dataToBeDisplayed: 'Sign the invoice',
          signatureContext: {
            signatureName: 'sig',
            hashAlgorithm: 'SHA256',
            signatureDescription: {
              signedBy: 'Alice',
              location: 'Hanoi',
              reason: 'Approve',
              contact: 'alice@example.test',
            },
          },
        }),
        o,
      );
      return { signed: r.signedXml, r };
    },
  },
  {
    format: 'Word',
    fake: 'word',
    expected: SIGNED_WORD_TEXT,
    sign: async (c, o) => {
      const r = await c.signWord({ ...visible, word: bytes }, o);
      return { signed: r.signedWord, r };
    },
  },
  {
    format: 'Excel',
    fake: 'excel',
    expected: SIGNED_EXCEL_TEXT,
    sign: async (c, o) => {
      const r = await c.signExcel({ ...visible, excel: bytes }, o);
      return { signed: r.signedExcel, r };
    },
  },
];

const PDF = CASES[0] as FormatCase;
const XML = CASES[1] as FormatCase;

/** Short name of a fake-server request path. */
function label(r: FakeRequest): string {
  const p = r.path;
  if (p.endsWith('/login-api')) return 'login';
  if (p.endsWith('/refreshtoken')) return 'refresh';
  if (p.endsWith('/two-factor-auth')) return 'twoFactor';
  if (p.endsWith('/Certificates/by-userId')) return 'certificates';
  if (p.endsWith('/documents/hash')) return 'hash';
  if (p.endsWith('/Signing/hash')) return 'signHash';
  if (p.includes('/Signing/status/')) return 'status';
  if (p.endsWith('/documents/attachment')) return 'attachment';
  return p;
}
const labels = (): string[] => fake.requests.map(label);
const isEsrm = (r: FakeRequest): boolean => r.path.startsWith('/external/esrm/');

async function rejection(p: Promise<unknown>): Promise<unknown> {
  try {
    await p;
  } catch (e) {
    return e;
  }
  throw new Error('expected rejection');
}

const HAPPY_ORDER = ['login', 'certificates', 'hash', 'signHash', 'status', 'status', 'attachment'];

describe.each(CASES)('$format', (fc) => {
  it('happy path returns the signed bytes and the certificate alias', async () => {
    const { signed, r } = await fc.sign(makeClient());
    expect(text(signed)).toBe(fc.expected);
    expect(r).toMatchObject({
      transactionId: 'tx-fake-1',
      certificateKeyAlias: 'key-alias-1',
      completedAtUtc: NOW.toISOString(),
      format: fc.format,
    });
  });

  it('sends login → certificates → hash → Signing/hash → status×2 → attachment', async () => {
    await fc.sign(makeClient());
    expect(labels()).toEqual(HAPPY_ORDER);
    expect(fake.requests[0]?.path.startsWith('/webdev/')).toBe(true);
  });

  it('puts AuthorizationRM on ESRM requests only and x-clientKey on all', async () => {
    await fc.sign(makeClient());
    for (const r of fake.requests) {
      expect(r.headers['x-clientkey']).toBe('ck');
      expect(r.headers['x-clientid']).toBe('cid');
      expect(r.headers['authorization']).toBeUndefined();
      if (isEsrm(r)) expect(r.headers['authorizationrm']).toBe('Bearer rs-token');
      else expect(r.headers['authorizationrm']).toBeUndefined();
    }
  });

  it('sends the Signing/hash body with the login user id and the digest', async () => {
    await fc.sign(makeClient());
    const sh = fake.requests.find((r) => label(r) === 'signHash');
    expect(JSON.parse(sh?.bodyText ?? '{}')).toEqual({
      DataToBeDisplayed: fc.format === 'Xml' ? 'Sign the invoice' : 'Sign the contract',
      UserId: 'user-id',
      CertAlias: 'key-alias-1',
      Documents: [
        {
          DocumentId: 'doc-1',
          FileToSign: 'DIGEST',
          DocumentName: fc.format === 'Xml' ? 'invoice' : 'contract',
        },
      ],
    });
    const att = fake.requests.find((r) => label(r) === 'attachment');
    const docs = (JSON.parse(att?.bodyText ?? '{}') as Record<string, { signature: string }[]>)[
      `${fc.fake}Docs`
    ];
    expect(docs?.[0]?.signature).toBe('SIGNATURE-BYTES');
  });

  it('HTML certificates → UnexpectedContentType', async () => {
    fake.scenario.certsReturnHtml200 = true;
    const e = (await rejection(fc.sign(makeClient()))) as ESignGeneralException;
    expect(e).toBeInstanceOf(ESignGeneralException);
    expect(e.category).toBe('MisaUnknown');
    expect(e.rawCode).toBe('UnexpectedContentType');
    expect(labels()).toEqual(['login', 'certificates']);
  });

  it('empty certificates → NoActiveCertificate', async () => {
    fake.scenario.certsEmptyArray200 = true;
    const e = (await rejection(fc.sign(makeClient()))) as NoActiveCertificateException;
    expect(e).toBeInstanceOf(NoActiveCertificateException);
    expect(e.category).toBe('NoActiveCertificate');
    expect(e.format).toBe(fc.format);
  });

  it('hash rejection → HashRejected with a synthesized code', async () => {
    fake.scenario.hashRejectionForFormat =
      fc.format === 'Xml'
        ? {
            format: 'xml',
            statusCode: 400,
            errorCode: '',
            devMsg: 'malformed xml input',
            userMsg: 'u-bad',
          }
        : { format: fc.fake, statusCode: 400, errorCode: 'BadDigest', devMsg: 'd', userMsg: 'u' };
    const e = (await rejection(fc.sign(makeClient()))) as ESignGeneralException;
    expect(e).toBeInstanceOf(ESignGeneralException);
    expect(e.category).toBe('HashRejected');
    expect(e.rawCode).toBe(fc.format === 'Xml' ? 'InvalidXmlInput' : 'InvalidHash');
    expect(e.format).toBe(fc.format);
    expect(labels()).toEqual(['login', 'certificates', 'hash']);
  });

  it('certificates 401 once → refresh, then the flow succeeds with the new token', async () => {
    fake.scenario.certsForce401Once = true;
    const { signed } = await fc.sign(makeClient());
    expect(text(signed)).toBe(fc.expected);
    expect(labels()).toEqual([
      'login',
      'certificates',
      'refresh',
      'certificates',
      'hash',
      'signHash',
      'status',
      'status',
      'attachment',
    ]);
    // Every ESRM call after the refresh carries the refreshed token (design §9.1).
    const after = fake.requests.slice(3).filter(isEsrm);
    for (const r of after) expect(r.headers['authorizationrm']).toBe('Bearer rs-token-2');
  });

  it('status FAILED → SignTerminalFailed', async () => {
    fake.scenario.statusAlwaysFailed = true;
    const e = (await rejection(fc.sign(makeClient()))) as SignTerminalStateException;
    expect(e).toBeInstanceOf(SignTerminalStateException);
    expect(e.category).toBe('SignTerminalFailed');
    expect(e.rawCode).toBe('ResultBad');
    expect(e.transactionId).toBe('tx-fake-1');
    expect(labels()).not.toContain('attachment');
  });

  it('Signing/hash 503 → exactly one Signing/hash request and ESignTransportException', async () => {
    fake.scenario.signHashStatus = 503;
    const e = (await rejection(fc.sign(makeClient()))) as ESignTransportException;
    expect(e).toBeInstanceOf(ESignTransportException);
    expect(e.lastStatusCode).toBe(503);
    expect(e.rawCode).toBe('ServiceUnavailable');
    expect(labels().filter((l) => l === 'signHash')).toHaveLength(1);
    expect(labels()).not.toContain('status');
  });

  it('opts.credentials override uses the override client id header', async () => {
    const other: MisaCredentials = {
      clientId: 'cid-2',
      clientKey: 'ck-2',
      userName: 'bob',
      password: 'pw-2',
    };
    const { signed } = await fc.sign(makeClient(), { credentials: other });
    expect(text(signed)).toBe(fc.expected);
    expect(fake.requests.length).toBe(HAPPY_ORDER.length);
    for (const r of fake.requests) {
      expect(r.headers['x-clientid']).toBe('cid-2');
      expect(r.headers['x-clientkey']).toBe('ck-2');
    }
    expect(JSON.parse(fake.requests[0]?.bodyText ?? '{}')).toEqual({
      userName: 'bob',
      password: 'pw-2',
    });
  });
});

describe('sign flow — shared behaviour', () => {
  it('reuses the cached token across calls on one client', async () => {
    const client = makeClient();
    await PDF.sign(client);
    await XML.sign(client);
    expect(labels().filter((l) => l === 'login')).toHaveLength(1);
  });

  it('uses opts.correlationId on every request', async () => {
    await PDF.sign(makeClient(), { correlationId: 'corr-xyz' });
    for (const r of fake.requests) expect(r.headers['x-correlation-id']).toBe('corr-xyz');
  });

  it('invalid request → Validation before any HTTP call', async () => {
    const e = (await rejection(
      makeClient().signPdf({ ...visible, pdf: new Uint8Array(0) }),
    )) as ESignGeneralException;
    expect(e.category).toBe('Validation');
    expect(e.rawCode).toBe('InvalidSignPdfRequest');
    expect(fake.requests).toHaveLength(0);
  });

  it('empty JSON certificates body → NoActiveCertificate', async () => {
    fake.scenario.certsEmptyBody200 = true;
    const e = await rejection(PDF.sign(makeClient()));
    expect(e).toBeInstanceOf(NoActiveCertificateException);
    expect(labels()).toEqual(['login', 'certificates']);
  });

  it('certificates without a content type → UnexpectedContentType', async () => {
    fake.scenario.certsNoContentType200 = true;
    const e = (await rejection(PDF.sign(makeClient()))) as ESignGeneralException;
    expect(e).toBeInstanceOf(ESignGeneralException);
    expect(e.category).toBe('MisaUnknown');
    expect(e.rawCode).toBe('UnexpectedContentType');
  });

  it('Signing/hash 200 with an empty JSON body → MisaUnknown EmptyResponse', async () => {
    fake.scenario.signHashEmptyBody200 = true;
    const e = (await rejection(PDF.sign(makeClient()))) as ESignGeneralException;
    expect(e).toBeInstanceOf(ESignGeneralException);
    expect(e.category).toBe('MisaUnknown');
    expect(e.rawCode).toBe('EmptyResponse');
    expect(e.detail).toContain('Signing/hash');
    expect(labels().filter((l) => l === 'signHash')).toHaveLength(1);
    expect(labels()).not.toContain('status');
  });

  it('Signing/hash per-attempt timeout → one request and ESignTransportException', async () => {
    // A fetch that never answers Signing/hash; every other call goes to the fake server.
    const realFetch = globalThis.fetch;
    let signHashCalls = 0;
    const fetchFn = ((url: string, init: RequestInit) => {
      if (url.endsWith('/Signing/hash')) {
        signHashCalls++;
        return new Promise((_res, rej) => {
          init.signal?.addEventListener('abort', () => rej(init.signal?.reason as Error));
        });
      }
      return realFetch(url, init);
    }) as typeof fetch;
    const client = createMisaESignClientInternal(
      { baseUrl: fake.baseUrl, credentials: creds, fetch: fetchFn },
      { delay: () => Promise.resolve(), transport: { timeoutMs: 50 } },
    );
    const e = (await rejection(PDF.sign(client))) as ESignTransportException;
    expect(e).toBeInstanceOf(ESignTransportException);
    expect(e.attemptCount).toBe(1);
    expect(signHashCalls).toBe(1);
    expect(labels()).not.toContain('status');
  });

  it('two users on one client: each logs in with its own credentials', async () => {
    const bob: MisaCredentials = {
      clientId: 'cid-b',
      clientKey: 'ck-b',
      userName: 'bob',
      password: 'pw-b',
    };
    const client = makeClient();
    await PDF.sign(client);
    const firstCount = fake.requests.length;
    await PDF.sign(client, { credentials: bob });
    const logins = fake.requests.filter((r) => label(r) === 'login');
    expect(logins.map((r) => JSON.parse(r.bodyText) as unknown)).toEqual([
      { userName: 'alice', password: 'pw' },
      { userName: 'bob', password: 'pw-b' },
    ]);
    const second = fake.requests.slice(firstCount);
    expect(second.map(label)).toEqual(HAPPY_ORDER);
    for (const r of second) {
      expect(r.headers['x-clientid']).toBe('cid-b');
      expect(r.headers['x-clientkey']).toBe('ck-b');
    }
    for (const r of fake.requests.slice(0, firstCount)) {
      expect(r.headers['x-clientid']).toBe('cid');
    }
  });

  it('non-array JSON certificates → UnparseableResponse without echoing the body', async () => {
    fake.scenario.certsJsonNonArray200 = true;
    const e = (await rejection(PDF.sign(makeClient()))) as ESignGeneralException;
    expect(e.category).toBe('MisaUnknown');
    expect(e.rawCode).toBe('UnparseableResponse');
    expect(e.detail).toContain('Certificates/by-userId');
    expect(e.detail).not.toContain('leak@example.test');
  });

  it('webhook mode → sign* throws WebhookModeOnly without any request', async () => {
    const client = makeClient({ webhook: { mode: 'webhook' } });
    for (const fc of CASES) {
      const e = (await rejection(fc.sign(client))) as ESignGeneralException;
      expect(e).toBeInstanceOf(ESignGeneralException);
      expect(e.category).toBe('Validation');
      expect(e.rawCode).toBe('WebhookModeOnly');
    }
    expect(fake.requests).toHaveLength(0);
  });

  it('polling mode → beginSign* throws PollingModeOnly', async () => {
    const client = makeClient({ webhook: { mode: 'polling' } });
    const calls = [
      () => client.beginSignPdf({ ...visible, pdf: bytes }),
      () => client.beginSignWord({ ...visible, word: bytes }),
      () => client.beginSignExcel({ ...visible, excel: bytes }),
      () =>
        client.beginSignXml(
          SignXmlRequest.fromString('<a/>', {
            documentName: 'd',
            dataToBeDisplayed: 'D',
            signatureContext: {
              signatureName: 's',
              hashAlgorithm: 'SHA256',
              signatureDescription: { signedBy: 'A', location: 'L', reason: 'R', contact: 'C' },
            },
          }),
        ),
    ];
    for (const call of calls) {
      const e = (await rejection(call())) as ESignGeneralException;
      expect(e.category).toBe('Validation');
      expect(e.rawCode).toBe('PollingModeOnly');
    }
    expect(fake.requests).toHaveLength(0);
  });

  it('polling mode still signs', async () => {
    const { signed } = await PDF.sign(makeClient({ webhook: { mode: 'polling' } }));
    expect(text(signed)).toBe(SIGNED_PDF_TEXT);
  });

  it('caller abort rejects with the signal reason', async () => {
    const ctrl = new AbortController();
    const reason = new Error('stop');
    ctrl.abort(reason);
    await expect(PDF.sign(makeClient(), { signal: ctrl.signal })).rejects.toBe(reason);
    expect(fake.requests).toHaveLength(0);
  });
});

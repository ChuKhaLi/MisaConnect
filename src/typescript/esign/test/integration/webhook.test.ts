import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import type {
  MisaCredentialsProvider,
  OtpProvider,
  SigningSession,
  SigningSessionStore,
} from '../../src/application/ports.js';
import { createMisaESignClientInternal } from '../../src/client-internal.js';
import type { CallOptions, IMisaESignClient } from '../../src/client.js';
import {
  SignXmlRequest,
  type BeginResultDto,
  type MisaCredentials,
  type WebhookOutcomeDto,
} from '../../src/domain/dtos.js';
import { AuthenticationFailedException, ESignGeneralException } from '../../src/domain/errors.js';
import { createInMemorySigningSessionStore } from '../../src/infrastructure/in-memory-session-store.js';
import type { MisaESignOptions } from '../../src/infrastructure/options.js';
import {
  SIGNED_EXCEL_TEXT,
  SIGNED_PDF_TEXT,
  SIGNED_WORD_TEXT,
  SIGNED_XML_TEXT,
  buildSyntheticWebhookBody,
  postWebhook,
  startFakeESignServer,
  type FakeESignServer,
  type FakeFormat,
  type SyntheticWebhookOptions,
} from '../fake/fake-esign-server.js';

const credsA: MisaCredentials = {
  clientId: 'cid',
  clientKey: 'ck-secret',
  userName: 'alice',
  password: 'pw-secret',
};
const credsB: MisaCredentials = {
  clientId: 'cid',
  clientKey: 'ck-b-secret',
  userName: 'B',
  password: 'pw-b-secret',
};
const NOW = new Date('2026-11-04T08:00:00.000Z');
const TX = 'tx-fake-1';

const ACK_OK = { errorCode: '0', devMsg: 'Webhook accepted.', userMsg: 'Webhook accepted.' };
const ACK_FINALIZE_FAILED = {
  errorCode: 'webhook.finalize_failed',
  devMsg: 'Finalize attempt failed; MISA may retry.',
  userMsg: 'Signing finalization failed; please wait for retry.',
};

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

interface Harness {
  client: IMisaESignClient;
  delivered: WebhookOutcomeDto[];
}

function makeClient(over: Partial<MisaESignOptions> = {}): Harness {
  const delivered: WebhookOutcomeDto[] = [];
  let n = 0;
  const client = createMisaESignClientInternal(
    {
      baseUrl: fake.baseUrl,
      credentials: credsA,
      webhookDeliveryHook: (o) => {
        delivered.push(o);
      },
      ...over,
    },
    {
      clock: { now: () => NOW },
      delay: () => Promise.resolve(),
      newId: () => `id-${++n}`,
    },
  );
  return { client, delivered };
}

const visible = {
  documentName: 'contract',
  signerName: 'Alice',
  location: 'Hanoi',
  reason: 'Approve',
  contact: 'alice@example.test',
  logoImageBase64: 'TE9HTw==',
  dataToBeDisplayed: 'Sign the contract',
  documentId: 'doc-1',
};
const bytes = new Uint8Array([37, 80, 68, 70]);
const pdf = { ...visible, pdf: bytes };
const text = (b: Uint8Array): string => new TextDecoder().decode(b);

const countPath = (fragment: string): number =>
  fake.requests.filter((r) => r.path.includes(fragment)).length;
const attachments = (): number => countPath('documents/attachment');

async function catchErr(p: Promise<unknown>): Promise<unknown> {
  try {
    await p;
  } catch (err) {
    return err;
  }
  throw new Error('expected a rejection');
}

function success(over: Partial<SyntheticWebhookOptions> = {}): Record<string, unknown> {
  return buildSyntheticWebhookBody({ clientId: 'cid', transactionId: TX, ...over });
}

interface FormatCase {
  format: 'Pdf' | 'Xml' | 'Word' | 'Excel';
  fake: FakeFormat;
  expected: string;
  begin(c: IMisaESignClient, opts?: CallOptions): Promise<BeginResultDto>;
}

const CASES: FormatCase[] = [
  {
    format: 'Pdf',
    fake: 'pdf',
    expected: SIGNED_PDF_TEXT,
    begin: (c, o) => c.beginSignPdf(pdf, o),
  },
  {
    format: 'Xml',
    fake: 'xml',
    expected: SIGNED_XML_TEXT,
    begin: (c, o) =>
      c.beginSignXml(
        SignXmlRequest.fromString('<doc/>', {
          documentName: 'invoice.xml',
          dataToBeDisplayed: 'Sign the invoice',
          documentId: 'doc-1',
          signatureContext: {
            signatureName: 'Alice',
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
      ),
  },
  {
    format: 'Word',
    fake: 'word',
    expected: SIGNED_WORD_TEXT,
    begin: (c, o) => c.beginSignWord({ ...visible, word: bytes }, o),
  },
  {
    format: 'Excel',
    fake: 'excel',
    expected: SIGNED_EXCEL_TEXT,
    begin: (c, o) => c.beginSignExcel({ ...visible, excel: bytes }, o),
  },
];

/** A local endpoint that hands the posted JSON to `handleWebhook` and answers with the ACK. */
async function startReceiver(
  client: IMisaESignClient,
): Promise<{ url: string; close(): Promise<void> }> {
  const server: Server = createServer((req, res) => {
    const chunks: Buffer[] = [];
    req.on('data', (c: Buffer) => chunks.push(c));
    req.on('end', () => {
      void (async () => {
        const body: unknown = JSON.parse(Buffer.concat(chunks).toString('utf8'));
        const result = await client.handleWebhook(body);
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(result.ack));
      })();
    });
  });
  await new Promise<void>((r) => server.listen(0, '127.0.0.1', r));
  const { port } = server.address() as AddressInfo;
  return {
    url: `http://127.0.0.1:${port}/webhook`,
    close: () =>
      new Promise<void>((resolve) => {
        server.closeAllConnections();
        server.close(() => resolve());
      }),
  };
}

describe('beginSign* + handleWebhook happy path', () => {
  it.each(CASES)('$format: begin returns the transaction without polling', async (c) => {
    const { client } = makeClient();
    await expect(c.begin(client)).resolves.toEqual({ transactionId: TX, format: c.format });
    expect(countPath('Signing/hash')).toBe(1);
    expect(countPath('Signing/status')).toBe(0);
    expect(attachments()).toBe(0);
  });

  it.each(CASES)('$format: SUCCESS webhook finalizes with ACK "0"', async (c) => {
    const { client, delivered } = makeClient();
    await c.begin(client);
    const r = await client.handleWebhook(success());
    expect(r.ack).toEqual(ACK_OK);
    expect(r.outcome.kind).toBe('SuccessWithSignedBytes');
    if (r.outcome.kind !== 'SuccessWithSignedBytes') return;
    expect(r.outcome.transactionId).toBe(TX);
    expect(r.outcome.format).toBe(c.format);
    expect(text(r.outcome.signedBytes)).toBe(c.expected);
    expect(delivered).toEqual([r.outcome]);
    expect(attachments()).toBe(1);
    const attach = fake.requests.find((q) => q.path.includes('documents/attachment'));
    expect(attach?.bodyText).toContain('SIGNATURE-BYTES');
  });

  it('the fake posts SUCCESS to a webhook endpoint; the hook receives the signed bytes', async () => {
    const { client, delivered } = makeClient();
    await client.beginSignPdf(pdf);
    const receiver = await startReceiver(client);
    try {
      const res = await postWebhook(receiver.url, success());
      expect(res.status).toBe(200);
      expect(res.json).toEqual(ACK_OK);
    } finally {
      await receiver.close();
    }
    expect(delivered).toHaveLength(1);
    const o = delivered[0];
    expect(o?.kind).toBe('SuccessWithSignedBytes');
    if (o?.kind === 'SuccessWithSignedBytes') expect(text(o.signedBytes)).toBe(SIGNED_PDF_TEXT);
  });
});

describe('handleWebhook terminal and validation outcomes', () => {
  it.each(['FAILED', 'CANCELLED', 'canceled'])(
    '%s → TerminalWithoutFinalize, ACK "0", no attachment',
    async (status) => {
      const { client, delivered } = makeClient();
      await client.beginSignPdf(pdf);
      const r = await client.handleWebhook(success({ status, errorCode: 'E42', signatures: [] }), {
        correlationId: 'corr-t',
      });
      expect(r.ack).toEqual(ACK_OK);
      expect(r.outcome).toEqual({
        kind: 'TerminalWithoutFinalize',
        transactionId: TX,
        format: 'Pdf',
        status: status === 'FAILED' ? 'Failed' : 'Cancelled',
        misaErrorCode: 'E42',
        correlationId: 'corr-t',
      });
      expect(delivered).toEqual([r.outcome]);
      expect(attachments()).toBe(0);
    },
  );

  it('unknown transaction → webhook.unknown_transaction', async () => {
    const { client, delivered } = makeClient();
    const r = await client.handleWebhook(success({ transactionId: 'tx-nope' }), {
      correlationId: 'corr-u',
    });
    expect(r.ack).toEqual({
      errorCode: 'webhook.unknown_transaction',
      devMsg: 'No session recorded for that transactionId.',
      userMsg: 'Webhook transaction not recognized.',
    });
    expect(r.outcome).toEqual({
      kind: 'FailureWithError',
      transactionId: 'tx-nope',
      format: 'Unknown',
      categoryName: 'UnknownTransaction',
      correlationId: 'corr-u',
    });
    expect(delivered).toEqual([r.outcome]);
  });

  it('an envelope clientId with no session for the transaction → webhook.unknown_transaction', async () => {
    const { client } = makeClient();
    await client.beginSignPdf(pdf);
    const r = await client.handleWebhook(success({ clientId: 'someone-else' }));
    expect(r.ack.errorCode).toBe('webhook.unknown_transaction');
    expect(attachments()).toBe(0);
  });

  it('a session whose stored clientId differs from the envelope → webhook.client_id_mismatch', async () => {
    // A store that ignores the clientId key (e.g. a custom store keyed by transaction only).
    const inner = createInMemorySigningSessionStore({ now: () => NOW });
    const loose: SigningSessionStore = {
      ...inner,
      register: (s) => inner.register(s),
      get: (_clientId, tx) => inner.get('cid', tx),
    };
    const { client, delivered } = makeClient({ signingSessionStore: loose });
    await client.beginSignPdf(pdf);
    const r = await client.handleWebhook(success({ clientId: 'intruder' }), {
      correlationId: 'corr-c',
    });
    expect(r.ack).toEqual({
      errorCode: 'webhook.client_id_mismatch',
      devMsg: 'clientId did not match configured value.',
      userMsg: 'Webhook clientId mismatch.',
    });
    expect(r.outcome).toEqual({
      kind: 'FailureWithError',
      transactionId: TX,
      format: 'Unknown',
      categoryName: 'ClientIdMismatch',
      correlationId: 'corr-c',
    });
    expect(delivered).toEqual([r.outcome]);
    expect(attachments()).toBe(0);
  });

  it('foreign documentId → webhook.document_id_mismatch', async () => {
    const { client } = makeClient();
    await client.beginSignPdf(pdf);
    const r = await client.handleWebhook(
      success({ signatures: [{ documentId: 'doc-evil', signature: 'SIG' }] }),
    );
    expect(r.ack).toEqual({
      errorCode: 'webhook.document_id_mismatch',
      devMsg: 'signatures[].documentId did not match session.',
      userMsg: 'Webhook document mismatch.',
    });
    expect(r.outcome).toMatchObject({
      kind: 'FailureWithError',
      transactionId: TX,
      format: 'Pdf',
      categoryName: 'DocumentIdMismatch',
    });
    expect(attachments()).toBe(0);
  });

  it('SUCCESS with empty signatures → webhook.incomplete_success', async () => {
    const { client } = makeClient();
    await client.beginSignPdf(pdf);
    const r = await client.handleWebhook(success({ signatures: [] }));
    expect(r.ack).toEqual({
      errorCode: 'webhook.incomplete_success',
      devMsg: 'status=SUCCESS but signatures[] was empty or invalid.',
      userMsg: 'Webhook payload was incomplete.',
    });
    expect(r.outcome).toMatchObject({
      kind: 'FailureWithError',
      format: 'Pdf',
      categoryName: 'IncompleteSuccessEnvelope',
    });
    expect(attachments()).toBe(0);
  });

  it('garbage body "x" → webhook.malformed, never throws', async () => {
    const { client, delivered } = makeClient();
    const r = await client.handleWebhook('x', { correlationId: 'corr-m' });
    expect(r.ack).toEqual({
      errorCode: 'webhook.malformed',
      devMsg: 'Inbound envelope failed shape validation.',
      userMsg: 'Webhook payload was malformed.',
    });
    expect(r.outcome).toEqual({
      kind: 'FailureWithError',
      format: 'Unknown',
      categoryName: 'MalformedEnvelope',
      correlationId: 'corr-m',
    });
    expect(delivered).toEqual([r.outcome]);
  });

  it('a malformed envelope with a string transactionId reports it', async () => {
    const { client } = makeClient();
    const r = await client.handleWebhook(success({ status: 'PENDING' }));
    expect(r.ack.errorCode).toBe('webhook.malformed');
    expect(r.outcome).toMatchObject({ kind: 'FailureWithError', transactionId: TX });
  });

  it('a hook error on a validation-failure outcome is swallowed', async () => {
    const { client } = makeClient({
      webhookDeliveryHook: () => {
        throw new Error('hook down');
      },
    });
    const r = await client.handleWebhook('x');
    expect(r.ack.errorCode).toBe('webhook.malformed');
  });

  it('a hook error on success propagates (as .NET), and the success stays cached', async () => {
    let fail = true;
    const delivered: WebhookOutcomeDto[] = [];
    const { client } = makeClient({
      webhookDeliveryHook: (o) => {
        delivered.push(o);
        if (fail) throw new Error('hook down');
      },
    });
    await client.beginSignPdf(pdf);
    await expect(client.handleWebhook(success())).rejects.toThrow('hook down');
    fail = false;
    const r = await client.handleWebhook(success());
    expect(r.ack).toEqual(ACK_OK);
    expect(delivered).toHaveLength(1);
    expect(attachments()).toBe(1);
  });
});

describe('finalize', () => {
  it('attachment rejection → webhook.finalize_failed, hook not called; a retry succeeds', async () => {
    const { client, delivered } = makeClient();
    await client.beginSignPdf(pdf);
    fake.scenario.attachmentRejectionForFormat = {
      format: 'pdf',
      statusCode: 400,
      errorCode: 'AttachBad',
      devMsg: 'bad',
      userMsg: 'bad',
    };
    const r = await client.handleWebhook(success(), { correlationId: 'corr-f' });
    expect(r.ack).toEqual(ACK_FINALIZE_FAILED);
    expect(r.outcome.kind).toBe('FailureWithError');
    if (r.outcome.kind !== 'FailureWithError') return;
    expect(r.outcome).toMatchObject({
      transactionId: TX,
      format: 'Pdf',
      categoryName: 'None',
      correlationId: 'corr-f',
    });
    expect(r.outcome.misaErrorCode).toEqual(expect.any(String));
    expect(delivered).toHaveLength(0);

    fake.resetScenario();
    const retry = await client.handleWebhook(success());
    expect(retry.ack).toEqual(ACK_OK);
    expect(retry.outcome.kind).toBe('SuccessWithSignedBytes');
    expect(delivered).toEqual([retry.outcome]);
    expect(attachments()).toBe(2);
  });

  it('replay after success → cached ACK, hook not called again, no attachment call', async () => {
    const { client, delivered } = makeClient();
    await client.beginSignPdf(pdf);
    const first = await client.handleWebhook(success({ messageId: 'm1' }));
    const replay = await client.handleWebhook(success({ messageId: 'm2' }));
    expect(replay.ack).toEqual(first.ack);
    expect(replay.outcome.kind).toBe('SuccessWithSignedBytes');
    if (replay.outcome.kind === 'SuccessWithSignedBytes') {
      expect(text(replay.outcome.signedBytes)).toBe(SIGNED_PDF_TEXT);
    }
    expect(delivered).toHaveLength(1);
    expect(attachments()).toBe(1);
  });

  it('concurrent duplicate webhook finalizes once', async () => {
    const { client, delivered } = makeClient();
    await client.beginSignPdf(pdf);
    const [a, b] = await Promise.all([
      client.handleWebhook(success({ messageId: 'm1' })),
      client.handleWebhook(success({ messageId: 'm2' })),
    ]);
    expect(a.ack).toEqual(ACK_OK);
    expect(b.ack).toEqual(ACK_OK);
    expect(attachments()).toBe(1);
    expect(delivered).toHaveLength(1);
  });

  it('dynamic provider: begin with opts.credentials for B, finalize asks the provider for B', async () => {
    const asked: { clientId?: string; userName?: string }[] = [];
    const provider: MisaCredentialsProvider = (ctx) => {
      asked.push({
        ...(ctx.clientId === undefined ? {} : { clientId: ctx.clientId }),
        ...(ctx.userName === undefined ? {} : { userName: ctx.userName }),
      });
      return ctx.userName === 'B' ? credsB : credsA;
    };
    const { client, delivered } = makeClient({ credentials: provider });
    await client.beginSignPdf(pdf, { credentials: credsB });
    expect(asked).toEqual([]); // the per-call credentials win at begin
    const r = await client.handleWebhook(success());
    expect(r.ack).toEqual(ACK_OK);
    expect(asked).toEqual([{ clientId: 'cid', userName: 'B' }]);
    expect(delivered).toHaveLength(1);
    const attach = fake.requests.find((q) => q.path.includes('documents/attachment'));
    expect(attach?.headers['x-clientkey']).toBe('ck-b-secret');
  });

  it('a provider returning another principal fails the finalize; hook not called', async () => {
    const provider: MisaCredentialsProvider = () => credsA;
    const { client, delivered } = makeClient({ credentials: provider });
    await client.beginSignPdf(pdf, { credentials: credsB });
    const r = await client.handleWebhook(success());
    expect(r.ack).toEqual(ACK_FINALIZE_FAILED);
    expect(r.outcome).toMatchObject({ kind: 'FailureWithError', categoryName: 'None' });
    expect(delivered).toHaveLength(0);
    expect(attachments()).toBe(0);
  });

  it('static credentials finalize a transaction begun with the same credentials', async () => {
    const { client } = makeClient();
    await client.beginSignPdf(pdf);
    expect((await client.handleWebhook(success())).ack).toEqual(ACK_OK);
  });
});

describe('sessions', () => {
  it('store only clientId and userName (no secrets) and stay JSON', async () => {
    const registered: SigningSession[] = [];
    const inner = createInMemorySigningSessionStore({ now: () => NOW });
    const store: SigningSessionStore = {
      ...inner,
      register: (s) => {
        registered.push(s);
        return inner.register(s);
      },
    };
    const { client } = makeClient({
      signingSessionStore: store,
      webhook: { mode: 'webhook', sessionTtlMs: 5000 },
    });
    await client.beginSignPdf(pdf);
    const s = registered[0] as SigningSession;
    expect(s).toMatchObject({
      clientId: 'cid',
      userName: 'alice',
      transactionId: TX,
      format: 'Pdf',
      recordedDocumentIds: ['doc-1'],
      createdAt: NOW.toISOString(),
      ttlMs: 5000,
      observedMessageIds: [],
    });
    expect(s.hash).toMatchObject({ format: 'Pdf', digest: 'DIGEST' });
    const json = JSON.stringify(s);
    expect(json).not.toContain('ck-secret');
    expect(json).not.toContain('pw-secret');
    expect(JSON.parse(json)).toEqual(s);

    await client.handleWebhook(success({ messageId: 'm1' }));
    const after = await inner.get('cid', TX);
    expect(after?.observedMessageIds).toEqual(['m1']);
    expect(typeof after?.cachedSuccess?.signed).toBe('string');
    expect(JSON.parse(JSON.stringify(after))).toEqual(after);
  });
});

describe('modes and 2FA', () => {
  it.each(CASES)("$format: beginSign* throws PollingModeOnly in 'polling' mode", async (c) => {
    const { client } = makeClient({ webhook: { mode: 'polling' } });
    const err = await catchErr(c.begin(client));
    expect(err).toBeInstanceOf(ESignGeneralException);
    expect((err as ESignGeneralException).category).toBe('Validation');
    expect((err as ESignGeneralException).rawCode).toBe('PollingModeOnly');
    expect(fake.requests).toHaveLength(0);
  });

  it('beginSign* answers a 2FA challenge transparently with an otpProvider', async () => {
    fake.scenario.loginRequires2FAOnFirstCall = true;
    const otpProvider: OtpProvider = {
      provide: () => Promise.resolve({ code: '123456', otpType: 'SmsOrEmail', remember: true }),
    };
    const { client } = makeClient({ otpProvider });
    await expect(client.beginSignPdf(pdf)).resolves.toEqual({ transactionId: TX, format: 'Pdf' });
    expect(countPath('two-factor-auth')).toBe(1);
  });

  it('beginSign* records an explicit 2FA challenge; signInWithOtp then lets it succeed', async () => {
    fake.scenario.loginRequires2FAOnFirstCall = true;
    const { client } = makeClient();
    const err = await catchErr(client.beginSignPdf(pdf));
    expect(err).toBeInstanceOf(AuthenticationFailedException);
    expect((err as AuthenticationFailedException).requires2FA).toBe(true);
    await client.signInWithOtp('123456', 'SmsOrEmail', false);
    await expect(client.beginSignPdf(pdf)).resolves.toEqual({ transactionId: TX, format: 'Pdf' });
  });
});

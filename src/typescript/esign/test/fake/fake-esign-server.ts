import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';

/** Test-only in-process stand-in for the MISA eSign API (port of the .NET FakeMisaESignServer; not shipped). */

export type FakeFormat = 'pdf' | 'xml' | 'word' | 'excel';

export interface FakeRequest {
  method: string;
  path: string;
  headers: Record<string, string>;
  bodyText: string;
}

export interface FakeRejection {
  format: FakeFormat;
  statusCode: number;
  errorCode: string;
  devMsg: string;
  userMsg: string;
}

export type ResendMode =
  'success200' | 'typedFailure200' | 'typedFailure4xx' | 'noEnvelope4xx' | 'transport500';

export interface FakeScenario {
  loginAlways401: boolean;
  loginRequires2FAOnFirstCall: boolean;
  nextTwoFactorErrorCode: string | undefined;
  resendMode: ResendMode;
  certsForce401Once: boolean;
  certsReturnHtml200: boolean;
  certsNoContentType200: boolean;
  certsJsonNonArray200: boolean;
  certsEmptyArray200: boolean;
  /** 200 with an `application/json` content type and an empty body. */
  certsEmptyBody200: boolean;
  certsContentTypeOverride: string | undefined;
  hashRejectionForFormat: FakeRejection | undefined;
  pendingsBeforeSuccess: number;
  statusAlwaysFailed: boolean;
  statusCancelled: boolean;
  statusPending: boolean;
  multiArrayResponse: boolean;
  attachmentMissingFormatArrayFor: FakeFormat | undefined;
  attachmentRejectionForFormat: FakeRejection | undefined;
  /** When set (e.g. 503), Signing/hash answers that status with an empty JSON body. */
  signHashStatus: number | undefined;
  /** Signing/hash answers 200 with an `application/json` content type and an empty body. */
  signHashEmptyBody200: boolean;
}

export const SIGNED_PDF_TEXT = 'SIGNED-PDF';
export const SIGNED_WORD_TEXT = 'SIGNED-WORD';
export const SIGNED_EXCEL_TEXT = 'SIGNED-EXCEL';
export const SIGNED_XML_TEXT = '<signed/>';

export interface FakeESignServer {
  baseUrl: string;
  requests: FakeRequest[];
  scenario: FakeScenario;
  /** `AuthorizationRM` header (without "Bearer ") last seen on the certificates endpoint. */
  lastCertificatesBearer: string | undefined;
  /** Restores every scenario default and clears once-only consumed flags and status poll counts. */
  resetScenario(): void;
  close(): Promise<void>;
}

function defaultScenario(): FakeScenario {
  return {
    loginAlways401: false,
    loginRequires2FAOnFirstCall: false,
    nextTwoFactorErrorCode: undefined,
    resendMode: 'success200',
    certsForce401Once: false,
    certsReturnHtml200: false,
    certsNoContentType200: false,
    certsJsonNonArray200: false,
    certsEmptyArray200: false,
    certsEmptyBody200: false,
    certsContentTypeOverride: undefined,
    hashRejectionForFormat: undefined,
    pendingsBeforeSuccess: 1,
    statusAlwaysFailed: false,
    statusCancelled: false,
    statusPending: false,
    multiArrayResponse: false,
    attachmentMissingFormatArrayFor: undefined,
    attachmentRejectionForFormat: undefined,
    signHashStatus: undefined,
    signHashEmptyBody200: false,
  };
}

const DOC_ARRAYS = ['pdfDocs', 'xmlDocs', 'wordDocs', 'excelDocs'] as const;
const FORMAT_BY_ARRAY: Record<(typeof DOC_ARRAYS)[number], FakeFormat> = {
  pdfDocs: 'pdf',
  xmlDocs: 'xml',
  wordDocs: 'word',
  excelDocs: 'excel',
};
const ARRAY_BY_FORMAT: Record<FakeFormat, (typeof DOC_ARRAYS)[number]> = {
  pdf: 'pdfDocs',
  xml: 'xmlDocs',
  word: 'wordDocs',
  excel: 'excelDocs',
};

function parseJson(text: string): Record<string, unknown> | undefined {
  try {
    const v: unknown = JSON.parse(text);
    return v !== null && typeof v === 'object' ? (v as Record<string, unknown>) : undefined;
  } catch {
    return undefined;
  }
}

function detectFormat(body: string): FakeFormat {
  const root = parseJson(body);
  if (root) {
    for (const name of DOC_ARRAYS) {
      const arr = root[name];
      if (Array.isArray(arr) && arr.length > 0) return FORMAT_BY_ARRAY[name];
    }
  }
  return 'pdf';
}

function presentEmptyDocArrays(body: string): string[] {
  const root = parseJson(body);
  if (!root) return [];
  return DOC_ARRAYS.filter((n) => Array.isArray(root[n]) && (root[n] as unknown[]).length === 0);
}

/** Mirrors the captured MISA 400: generic errorCode plus per-property validationFailures. */
function emptyArrayRejectionBody(names: string[]): unknown {
  const msg = 'Dữ liệu truyền lên không chính xác, vui lòng xem trong trường ValidationFailures.';
  return {
    error: null,
    errorCode: 'e400',
    devMsg: msg,
    userMsg: msg,
    validationFailures: names.map((n) => ({
      property: n.charAt(0).toUpperCase() + n.slice(1),
      failureReason: 'Phải có ít nhất 1 tài liệu',
    })),
  };
}

const b64 = (s: string): string => Buffer.from(s, 'utf8').toString('base64');

const HASH_DOCS = {
  pdfDocs: [
    {
      documentId: 'doc-1',
      documentBytes: 'DOCBYTES',
      documentHash: 'DOCHASH',
      sh: 'SH',
      signatureName: 'SigName',
      digest: 'DIGEST',
    },
  ],
  xmlDocs: [
    { documentId: 'doc-1', document: '<doc/>', signatureId: 'sig-x', digest: 'DIGEST', sh: 'SH' },
  ],
  wordDocs: [
    {
      documentId: 'doc-1',
      documentBytes: 'WORDBYTES',
      signatureId: 'sig-w',
      digest: 'DIGEST',
      mainDom: 'WORDMAIN',
    },
  ],
  excelDocs: [
    {
      documentId: 'doc-1',
      documentBytes: 'EXCELBYTES',
      signatureId: 'sig-e',
      digest: 'DIGEST',
      mainDom: 'EXCELMAIN',
    },
  ],
};

function hashResponse(format: FakeFormat, sc: FakeScenario): unknown {
  if (sc.multiArrayResponse) return HASH_DOCS;
  const key = ARRAY_BY_FORMAT[format];
  return { [key]: HASH_DOCS[key] };
}

function attachmentDocs(): Record<(typeof DOC_ARRAYS)[number], unknown[]> {
  return {
    pdfDocs: [{ documentId: 'doc-1', document: b64(SIGNED_PDF_TEXT) }],
    xmlDocs: [{ documentId: 'doc-1', document: SIGNED_XML_TEXT }],
    wordDocs: [{ documentId: 'doc-1', document: b64(SIGNED_WORD_TEXT) }],
    excelDocs: [{ documentId: 'doc-1', document: b64(SIGNED_EXCEL_TEXT) }],
  };
}

function attachmentResponse(format: FakeFormat, sc: FakeScenario): unknown {
  const key = ARRAY_BY_FORMAT[format];
  if (sc.attachmentMissingFormatArrayFor === format) return { [key]: [] };
  const all = attachmentDocs();
  if (sc.multiArrayResponse) return all;
  return { [key]: all[key] };
}

const LOGIN_OK = {
  status: { code: 200, error: false },
  data: {
    accessToken: 'raw-token',
    remoteSigningAccessToken: 'rs-token',
    refreshToken: 'refresh-token',
    expiresIn: 3600,
    tokenType: 'Bearer',
    user: { id: 'user-id', username: 'alice' },
  },
};

const TWO_FACTOR_OK = {
  status: { code: 200, error: false },
  data: {
    accessToken: 'raw-token-2fa',
    remoteSigningAccessToken: 'rs-token-2fa',
    refreshToken: 'refresh-token-2fa',
    expiresIn: 3600,
    tokenType: 'Bearer',
    user: { id: 'user-id', username: 'alice' },
  },
};

const REFRESH_OK = {
  accessToken: 'raw-token-2',
  remoteSigningAccessToken: 'rs-token-2',
  refreshToken: 'refresh-token-2',
  expiresIn: 3600,
};

const CERTIFICATES = [
  {
    userId: 'user-id',
    keyAlias: 'key-alias-1',
    appName: 'misa',
    keyStatus: 'ACTIVE',
    certStatus: 'ACTIVE',
    certificate: 'BASE64CERT',
    certiticateChain: ['SIGN', 'INTERMEDIATE', 'ROOT'],
    effectiveDate: '2026-01-01T00:00:00Z',
    expirationDate: '2027-01-01T00:00:00Z',
    emailName: 'alice@example.com',
    isAutoSign: false,
  },
];

function sendJson(res: ServerResponse, status: number, body: unknown): void {
  const text = typeof body === 'string' ? body : JSON.stringify(body);
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(text);
}

function readBody(req: IncomingMessage): Promise<string> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    req.on('data', (c: Buffer) => chunks.push(c));
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

function rejectionBody(rej: FakeRejection): unknown {
  return { errorCode: rej.errorCode, devMsg: rej.devMsg, userMsg: rej.userMsg };
}

const AUTH = '/api/auth/api/v1/auth';
const ESRM = '/external/esrm/service';

export async function startFakeESignServer(): Promise<FakeESignServer> {
  const scenario = defaultScenario();
  const requests: FakeRequest[] = [];
  const statusCalls = new Map<string, number>();
  let login122Consumed = false;
  let certs401Consumed = false;
  // Setting a once-only scenario flag (again) re-arms it.
  let login2fa = false;
  let certs401 = false;
  Object.defineProperties(scenario, {
    loginRequires2FAOnFirstCall: {
      enumerable: true,
      get: () => login2fa,
      set: (v: boolean) => {
        login2fa = v;
        login122Consumed = false;
      },
    },
    certsForce401Once: {
      enumerable: true,
      get: () => certs401,
      set: (v: boolean) => {
        certs401 = v;
        certs401Consumed = false;
      },
    },
  });

  const fake: FakeESignServer = {
    baseUrl: '',
    requests,
    scenario,
    lastCertificatesBearer: undefined,
    resetScenario: () => {
      Object.assign(scenario, defaultScenario());
      login122Consumed = false;
      certs401Consumed = false;
      statusCalls.clear();
      fake.lastCertificatesBearer = undefined;
    },
    close: () => Promise.resolve(),
  };

  function handle(req: FakeRequest, res: ServerResponse): void {
    // Auth routes are served at the host root and under /webdev.
    const path = req.path.startsWith('/webdev/') ? req.path.slice('/webdev'.length) : req.path;
    const underWebdev = path !== req.path;
    const route = `${req.method} ${path}`;

    if (route === `POST ${AUTH}/login-api`) {
      if (scenario.loginAlways401) {
        sendJson(res, 401, { error: true, errorCode: 'InvalidPassword' });
        return;
      }
      if (scenario.loginRequires2FAOnFirstCall && !login122Consumed) {
        login122Consumed = true;
        sendJson(res, 200, { status: { code: 200, error: true, errorCode: '122' } });
        return;
      }
      sendJson(res, 200, LOGIN_OK);
      return;
    }
    if (route === `POST ${AUTH}/two-factor-auth`) {
      const code = scenario.nextTwoFactorErrorCode;
      if (code) {
        sendJson(res, 400, { status: { code: 400, error: true, errorCode: code } });
        return;
      }
      sendJson(res, 200, TWO_FACTOR_OK);
      return;
    }
    if (underWebdev && route === `POST ${AUTH}/refreshtoken`) {
      sendJson(res, 200, REFRESH_OK);
      return;
    }
    if (underWebdev && route === `POST ${AUTH}/resend-otp-auth`) {
      switch (scenario.resendMode) {
        case 'success200':
          sendJson(res, 200, {
            status: { code: 200, error: false },
            data: { user: { username: 'alice' } },
          });
          return;
        case 'typedFailure200':
          sendJson(res, 200, {
            status: {
              code: 200,
              error: true,
              errorCode: 'RateLimited',
              devMsg: 'too soon',
              userMsg: 'please wait',
            },
          });
          return;
        case 'typedFailure4xx':
          sendJson(res, 400, {
            status: {
              code: 400,
              error: true,
              errorCode: 'BadRequest',
              devMsg: 'bad',
              userMsg: 'u-bad',
            },
          });
          return;
        case 'noEnvelope4xx':
          sendJson(res, 400, '');
          return;
        case 'transport500':
          res.writeHead(500);
          res.end();
          return;
      }
    }

    if (route === `GET ${ESRM}/general/api/v1/Certificates/by-userId`) {
      const auth = req.headers['authorizationrm'] ?? '';
      fake.lastCertificatesBearer = /^bearer /i.test(auth) ? auth.slice('bearer '.length) : auth;
      if (scenario.certsForce401Once && !certs401Consumed) {
        certs401Consumed = true;
        sendJson(res, 401, { error: true, errorCode: 'TokenExpired' });
        return;
      }
      if (scenario.certsReturnHtml200) {
        res.writeHead(200, { 'Content-Type': 'text/html' });
        res.end('<!DOCTYPE html><html><head><title>MISA</title></head><body>app</body></html>');
        return;
      }
      if (scenario.certsNoContentType200) {
        res.writeHead(200);
        res.end('[]');
        return;
      }
      if (scenario.certsJsonNonArray200) {
        sendJson(res, 200, { error: 'oops', emailName: 'leak@example.test' });
        return;
      }
      if (scenario.certsEmptyBody200) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end();
        return;
      }
      res.writeHead(200, {
        'Content-Type': scenario.certsContentTypeOverride ?? 'application/json',
      });
      res.end(JSON.stringify(scenario.certsEmptyArray200 ? [] : CERTIFICATES));
      return;
    }

    if (route === `POST ${ESRM}/document/api/v1/documents/hash`) {
      const empty = presentEmptyDocArrays(req.bodyText);
      if (empty.length > 0) {
        sendJson(res, 400, emptyArrayRejectionBody(empty));
        return;
      }
      const format = detectFormat(req.bodyText);
      const rej = scenario.hashRejectionForFormat;
      if (rej && rej.format === format) {
        sendJson(res, rej.statusCode, rejectionBody(rej));
        return;
      }
      sendJson(res, 200, hashResponse(format, scenario));
      return;
    }

    if (route === `POST ${ESRM}/signing/api/v1/Signing/hash`) {
      if (scenario.signHashStatus !== undefined) {
        sendJson(res, scenario.signHashStatus, '');
        return;
      }
      if (scenario.signHashEmptyBody200) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end();
        return;
      }
      statusCalls.delete('tx-fake-1'); // every new sign flow starts at poll 1
      sendJson(res, 200, { transactionId: 'tx-fake-1' });
      return;
    }

    const statusPrefix = `${ESRM}/signing/api/v1/Signing/status/`;
    if (req.method === 'GET' && path.startsWith(statusPrefix)) {
      const tx = decodeURIComponent(path.slice(statusPrefix.length));
      const calls = (statusCalls.get(tx) ?? 0) + 1;
      statusCalls.set(tx, calls);
      if (scenario.statusAlwaysFailed) {
        sendJson(res, 200, {
          status: 'FAILED',
          errorCode: 'ResultBad',
          errorDescription: 'sign failed',
          transactionId: tx,
        });
      } else if (scenario.statusCancelled) {
        sendJson(res, 200, { status: 'CANCELLED', transactionId: tx });
      } else if (scenario.statusPending || calls <= scenario.pendingsBeforeSuccess) {
        sendJson(res, 200, { status: 'PENDING', transactionId: tx });
      } else {
        sendJson(res, 200, {
          status: 'SUCCESS',
          transactionId: tx,
          signatures: [{ documentId: 'doc-1', signature: 'SIGNATURE-BYTES' }],
        });
      }
      return;
    }

    if (route === `POST ${ESRM}/document/api/v1/documents/attachment`) {
      const empty = presentEmptyDocArrays(req.bodyText);
      if (empty.length > 0) {
        sendJson(res, 400, emptyArrayRejectionBody(empty));
        return;
      }
      const format = detectFormat(req.bodyText);
      const rej = scenario.attachmentRejectionForFormat;
      if (rej && rej.format === format) {
        sendJson(res, rej.statusCode, rejectionBody(rej));
        return;
      }
      sendJson(res, 200, attachmentResponse(format, scenario));
      return;
    }

    sendJson(res, 404, { error: true, errorCode: 'NotFound', devMsg: `no route ${route}` });
  }

  const server: Server = createServer((req, res) => {
    void (async () => {
      const bodyText = await readBody(req);
      const url = new URL(req.url ?? '/', 'http://fake');
      const headers: Record<string, string> = {};
      for (const [k, v] of Object.entries(req.headers)) {
        headers[k.toLowerCase()] = Array.isArray(v) ? v.join(',') : (v ?? '');
      }
      const entry: FakeRequest = {
        method: req.method ?? 'GET',
        path: url.pathname,
        headers,
        bodyText,
      };
      requests.push(entry);
      handle(entry, res);
    })().catch((e: unknown) => {
      if (res.headersSent) {
        res.destroy();
        return;
      }
      res.writeHead(500, { 'Content-Type': 'text/plain' });
      res.end(e instanceof Error ? e.message : String(e));
    });
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;
  fake.baseUrl = `http://127.0.0.1:${port}`;
  let closing: Promise<void> | undefined;
  fake.close = () =>
    (closing ??= new Promise<void>((resolve, reject) => {
      server.closeAllConnections();
      server.close((e) => (e ? reject(e) : resolve()));
    }));
  return fake;
}

export interface SyntheticWebhookOptions {
  clientId: string;
  transactionId: string;
  status?: string;
  messageId?: string;
  errorCode?: string | null;
  signatures?: { documentId: string; signature: string }[];
}

/** MISA-shaped webhook envelope (mirrors the .NET `BuildSyntheticWebhookBody`). */
export function buildSyntheticWebhookBody(opts: SyntheticWebhookOptions): Record<string, unknown> {
  const status = opts.status ?? 'SUCCESS';
  const signatures =
    opts.signatures ??
    (status === 'SUCCESS' ? [{ documentId: 'doc-1', signature: 'SIGNATURE-BYTES' }] : []);
  return {
    messageId: opts.messageId ?? crypto.randomUUID().replaceAll('-', ''),
    clientId: opts.clientId,
    extraData: null,
    status,
    errorCode: opts.errorCode ?? null,
    transactionId: opts.transactionId,
    signatures,
  };
}

/** POSTs a webhook envelope (object or raw string) to `targetUrl`. */
export async function postWebhook(
  targetUrl: string,
  body: unknown,
): Promise<{ status: number; json: unknown }> {
  const res = await fetch(targetUrl, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: typeof body === 'string' ? body : JSON.stringify(body),
  });
  const text = await res.text();
  let json: unknown;
  try {
    json = text ? (JSON.parse(text) as unknown) : undefined;
  } catch {
    json = undefined;
  }
  return { status: res.status, json };
}

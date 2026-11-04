import { connect } from 'node:net';
import { describe, expect, it } from 'vitest';
import { createMisaESignClient, SignXmlRequest, type IMisaESignClient } from '../../src/index.js';

// Opt-in live-sandbox flows (port of the .NET Sandbox/* tests). Skipped unless all five
// credentials are set AND the sandbox host answers. With MISACONNECT_ESIGN_SANDBOX_REQUIRED=1 a
// skip is a failure instead, so a green job always means the live tests actually ran.
const PROBE_TIMEOUT_MS = 5000;
const TEST_TIMEOUT_MS = 120_000;

const env = (name: string): string | undefined =>
  process.env[`MISACONNECT_ESIGN_SANDBOX_${name}`]?.trim() || undefined;
const baseUrl = env('BASE_URL');
const clientId = env('CLIENT_ID');
const clientKey = env('CLIENT_KEY');
const userName = env('USERNAME');
const password = env('PASSWORD');
const hasSandboxEnv = !!baseUrl && !!clientId && !!clientKey && !!userName && !!password;

function hostPort(): { host: string; port: number } | undefined {
  try {
    const u = new URL(baseUrl ?? '');
    return { host: u.hostname, port: u.port ? Number(u.port) : u.protocol === 'http:' ? 80 : 443 };
  } catch {
    return undefined;
  }
}

/**
 * One-shot TCP-connect probe with a short timeout; never throws. It dials the host directly and
 * therefore ignores HTTP(S) proxy settings.
 */
function isReachable(target: { host: string; port: number }): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = connect(target);
    const done = (ok: boolean): void => {
      socket.destroy();
      resolve(ok);
    };
    socket.setTimeout(PROBE_TIMEOUT_MS, () => done(false));
    socket.once('connect', () => done(true));
    socket.once('error', () => done(false));
  });
}

const target = hasSandboxEnv ? hostPort() : undefined;
// Only probe the network when credentials exist, so the no-credentials path is instant.
const reachable = target !== undefined && (await isReachable(target));
const runnable = hasSandboxEnv && reachable;
const required = process.env['MISACONNECT_ESIGN_SANDBOX_REQUIRED']?.trim() === '1';

describe.runIf(required && !runnable)('MISA eSign sandbox (required)', () => {
  it('has credentials and can reach the sandbox', () => {
    expect.fail(
      hasSandboxEnv
        ? `MISACONNECT_ESIGN_SANDBOX_REQUIRED=1 but ${baseUrl} did not accept a TCP connection within ${PROBE_TIMEOUT_MS} ms.`
        : 'MISACONNECT_ESIGN_SANDBOX_REQUIRED=1 but one of MISACONNECT_ESIGN_SANDBOX_BASE_URL / _CLIENT_ID / _CLIENT_KEY / _USERNAME / _PASSWORD is not set.',
    );
  });
});

function createClient(): IMisaESignClient {
  return createMisaESignClient({
    environment: 'sandbox',
    baseUrl: baseUrl ?? '',
    credentials: {
      clientId: clientId ?? '',
      clientKey: clientKey ?? '',
      userName: userName ?? '',
      password: password ?? '',
    },
    polling: { intervalMs: 2000, totalTimeoutMs: 90_000 },
  });
}

/** A minimal valid one-page PDF with a correct xref table, built in-test. */
function minimalPdf(): Uint8Array {
  const stream = 'BT /F1 18 Tf 72 720 Td (MisaConnect) Tj ET';
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
    `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
  ];
  let out = '%PDF-1.4\n';
  const offsets: number[] = [];
  objects.forEach((body, i) => {
    offsets.push(out.length);
    out += `${i + 1} 0 obj\n${body}\nendobj\n`;
  });
  const xref = out.length;
  out += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (const o of offsets) out += `${String(o).padStart(10, '0')} 00000 n \n`;
  out += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return new TextEncoder().encode(out);
}

describe.runIf(runnable)('MISA eSign sandbox', () => {
  it(
    'signs a PDF',
    async () => {
      const result = await createClient().signPdf({
        pdf: minimalPdf(),
        documentName: 'test.pdf',
        signerName: 'Alice',
        location: 'Hanoi',
        reason: 'Test',
        contact: 'alice@example.com',
        logoImageBase64: 'base64-logo',
        dataToBeDisplayed: '<p>Confirm sign</p>',
        page: 1,
        positionX: 100,
        positionY: 100,
        width: 200,
        height: 80,
        renderingMode: 1,
      });
      expect(result.format).toBe('Pdf');
      expect(result.transactionId).not.toBe('');
      expect(result.signedPdf.length).toBeGreaterThan(0);
      expect(new TextDecoder().decode(result.signedPdf.subarray(0, 5))).toBe('%PDF-');
    },
    TEST_TIMEOUT_MS,
  );

  it(
    'signs an XML document',
    async () => {
      const result = await createClient().signXml(
        SignXmlRequest.fromString('<root><child>hello</child></root>', {
          signatureContext: {
            signatureName: 'XmlSig1',
            hashAlgorithm: 'SHA256',
            signatureDescription: {
              signedBy: 'Alice',
              location: 'Hanoi',
              reason: 'Test',
              contact: 'alice@example.com',
              showSignedDate: true,
            },
          },
          documentName: 'test.xml',
          dataToBeDisplayed: '<p>Confirm sign</p>',
        }),
      );
      expect(result.format).toBe('Xml');
      expect(result.signedXml.length).toBeGreaterThan(0);
      expect(new TextDecoder().decode(result.signedXml).toLowerCase()).toContain('signature');
    },
    TEST_TIMEOUT_MS,
  );
});

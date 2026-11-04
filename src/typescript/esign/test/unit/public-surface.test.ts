import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

describe('public surface', () => {
  it('exports exactly the documented runtime values', async () => {
    const mod = await import('../../src/index.js');
    expect(Object.keys(mod).sort()).toMatchInlineSnapshot(`
      [
        "AuthenticationFailedException",
        "ClientIdMismatchException",
        "DocumentFormat",
        "DocumentIdMismatchException",
        "ESignErrorCategory",
        "ESignException",
        "ESignGeneralException",
        "ESignTransportException",
        "ExhaustedOtpAttemptsException",
        "ExpiredOtpException",
        "IncompleteSuccessEnvelopeException",
        "InvalidOtpException",
        "MalformedEnvelopeException",
        "NoActiveCertificateException",
        "OtpDeliveryChannel",
        "OtpRejectedException",
        "SignRejectedException",
        "SignStatus",
        "SignTerminalStateException",
        "SignTimeoutException",
        "SignXmlRequest",
        "UnknownTransactionException",
        "VERSION",
        "WebhookStatusDto",
        "WebhookValidationCategory",
        "WebhookValidationException",
        "createInMemorySigningSessionStore",
        "createInMemoryTokenCache",
        "createMisaESignClient",
        "fromPino",
        "httpStatusName",
        "isESignException",
        "otpChannelWireValue",
      ]
    `);
  });

  it('leaves no NotImplemented throw in src/', () => {
    const files: string[] = [];
    const walk = (dir: string): void => {
      for (const name of readdirSync(dir)) {
        const p = join(dir, name);
        if (statSync(p).isDirectory()) walk(p);
        else if (p.endsWith('.ts')) files.push(p);
      }
    };
    walk('src');
    const offenders = files.filter((f) =>
      /throw\s+new\s+\w*Error\(\s*['"`][^'"`]*not\s*implemented|NotImplementedError/i.test(
        readFileSync(f, 'utf8'),
      ),
    );
    expect(offenders).toEqual([]);
  });
});

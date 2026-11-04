import { describe, expect, it } from 'vitest';
import {
  AuthenticationFailedException,
  ClientIdMismatchException,
  DocumentIdMismatchException,
  ESignErrorCategory,
  ESignException,
  ESignGeneralException,
  ESignTransportException,
  ExhaustedOtpAttemptsException,
  ExpiredOtpException,
  IncompleteSuccessEnvelopeException,
  InvalidOtpException,
  MalformedEnvelopeException,
  NoActiveCertificateException,
  OtpRejectedException,
  SignRejectedException,
  SignTerminalStateException,
  SignTimeoutException,
  SignXmlRequest,
  UnknownTransactionException,
  WebhookValidationCategory,
  WebhookValidationException,
  httpStatusName,
  isESignException,
  otpChannelWireValue,
  type XmlSignatureContextDto,
} from '../../src/index.js';

const cid = 'corr-1';

describe('enums', () => {
  it('ESignErrorCategory has the 16 .NET members in order', () => {
    expect(Object.keys(ESignErrorCategory)).toEqual([
      'MisaUnknown',
      'Authentication',
      'NoActiveCertificate',
      'CertificateLookupFailed',
      'HashRejected',
      'SignRejected',
      'SignTerminalFailed',
      'SignTerminalCancelled',
      'SignTerminalUnknown',
      'SignTimeout',
      'AttachmentRejected',
      'StatusLookupFailed',
      'Transport',
      'Validation',
      'WebhookValidation',
      'WebhookFinalize',
    ]);
  });

  it('WebhookValidationCategory members', () => {
    expect(Object.keys(WebhookValidationCategory)).toEqual([
      'None',
      'MalformedEnvelope',
      'ClientIdMismatch',
      'UnknownTransaction',
      'IncompleteSuccessEnvelope',
      'DocumentIdMismatch',
    ]);
  });

  it('otpChannelWireValue', () => {
    expect(otpChannelWireValue('SmsOrEmail')).toBe(0);
    expect(otpChannelWireValue('Authenticator')).toBe(1);
  });
});

describe('ESignException', () => {
  it('throws TypeError on empty or blank correlationId', () => {
    for (const bad of ['', '   ']) {
      expect(
        () =>
          new ESignGeneralException({ category: 'MisaUnknown', detail: 'd', correlationId: bad }),
      ).toThrow(TypeError);
    }
  });

  it('general exception carries fields and defaults format to Unknown', () => {
    const inner = new Error('inner');
    const e = new ESignGeneralException({
      category: 'HashRejected',
      rawCode: 'X',
      detail: 'boom',
      correlationId: cid,
      cause: inner,
    });
    expect(e).toBeInstanceOf(ESignException);
    expect(e).toBeInstanceOf(Error);
    expect(e.category).toBe('HashRejected');
    expect(e.rawCode).toBe('X');
    expect(e.detail).toBe('boom');
    expect(e.message).toBe('boom');
    expect(e.correlationId).toBe(cid);
    expect(e.format).toBe('Unknown');
    expect(e.cause).toBe(inner);
  });

  it('authentication family', () => {
    const a = new AuthenticationFailedException({
      rawCode: '122',
      detail: 'd',
      correlationId: cid,
      requires2FA: true,
      username: 'u',
    });
    expect(a.category).toBe('Authentication');
    expect(a.requires2FA).toBe(true);
    expect(a.username).toBe('u');
    const plain = new AuthenticationFailedException({ detail: 'd', correlationId: cid });
    expect(plain.requires2FA).toBe(false);
    expect(plain.username).toBe('');

    const cases = [
      [InvalidOtpException, 'InvalidOtp'],
      [ExpiredOtpException, 'ExpiredOtp'],
      [ExhaustedOtpAttemptsException, 'ExhaustedOtpAttempts'],
      [OtpRejectedException, 'OtpRejected'],
    ] as const;
    for (const [Ctor, code] of cases) {
      const e = new Ctor({ detail: 'd', correlationId: cid });
      expect(e).toBeInstanceOf(AuthenticationFailedException);
      expect(e.category).toBe('Authentication');
      expect(e.rawCode).toBe(code);
      expect(e.requires2FA).toBe(false);
      expect(e.name).toBe(Ctor.name);
      expect(new Ctor({ rawCode: '1001', detail: 'd', correlationId: cid }).rawCode).toBe('1001');
    }
  });

  it('NoActiveCertificate / SignRejected / SignTimeout', () => {
    const n = new NoActiveCertificateException({ detail: 'd', correlationId: cid });
    expect(n.category).toBe('NoActiveCertificate');
    expect(n.rawCode).toBe('NoActiveCertificate');
    const s = new SignRejectedException({ rawCode: 'r', detail: 'd', correlationId: cid });
    expect(s.category).toBe('SignRejected');
    expect(s.requiresUserCertSetup).toBe(false);
    expect(
      new SignRejectedException({ detail: 'd', correlationId: cid, requiresUserCertSetup: true })
        .requiresUserCertSetup,
    ).toBe(true);
    const t = new SignTimeoutException({
      transactionId: 'tx',
      elapsedMs: 60000,
      detail: 'd',
      correlationId: cid,
    });
    expect(t.category).toBe('SignTimeout');
    expect(t.rawCode).toBe('SignTimeout');
    expect(t.transactionId).toBe('tx');
    expect(t.elapsedMs).toBe(60000);
  });

  it('SignTerminalStateException maps status to category', () => {
    const mk = (terminalStatus: 'FAILED' | 'CANCELLED' | 'UNKNOWN' | 'PENDING') =>
      new SignTerminalStateException({
        terminalStatus,
        transactionId: 'tx',
        detail: 'd',
        correlationId: cid,
      });
    expect(mk('FAILED').category).toBe('SignTerminalFailed');
    expect(mk('CANCELLED').category).toBe('SignTerminalCancelled');
    expect(mk('UNKNOWN').category).toBe('SignTerminalUnknown');
    expect(mk('PENDING').category).toBe('SignTerminalUnknown');
    expect(mk('FAILED').terminalStatus).toBe('FAILED');
    expect(mk('FAILED').transactionId).toBe('tx');
  });

  it('ESignTransportException raw code', () => {
    const a = new ESignTransportException({ attemptCount: 3, detail: 'd', correlationId: cid });
    expect(a.category).toBe('Transport');
    expect(a.rawCode).toBe('Transport');
    expect(a.lastStatusCode).toBeUndefined();
    expect(a.attemptCount).toBe(3);
    const b = new ESignTransportException({
      lastStatusCode: 503,
      attemptCount: 3,
      detail: 'd',
      correlationId: cid,
    });
    expect(b.rawCode).toBe('ServiceUnavailable');
    expect(b.lastStatusCode).toBe(503);
  });

  it('webhook validation exceptions', () => {
    const list: [WebhookValidationException, string, string | undefined, string][] = [
      [
        new MalformedEnvelopeException({ correlationId: cid, detail: 'd' }),
        'MalformedEnvelope',
        undefined,
        'Unknown',
      ],
      [
        new ClientIdMismatchException({ correlationId: cid, detail: 'd' }),
        'ClientIdMismatch',
        undefined,
        'Unknown',
      ],
      [
        new UnknownTransactionException({ correlationId: cid, transactionId: 'tx', detail: 'd' }),
        'UnknownTransaction',
        'tx',
        'Unknown',
      ],
      [
        new IncompleteSuccessEnvelopeException({
          correlationId: cid,
          transactionId: 'tx',
          format: 'Pdf',
          detail: 'd',
        }),
        'IncompleteSuccessEnvelope',
        'tx',
        'Pdf',
      ],
      [
        new DocumentIdMismatchException({
          correlationId: cid,
          transactionId: 'tx',
          format: 'Xml',
          detail: 'd',
        }),
        'DocumentIdMismatch',
        'tx',
        'Xml',
      ],
    ];
    for (const [e, cat, tx, fmt] of list) {
      expect(e).toBeInstanceOf(WebhookValidationException);
      expect(e.category).toBe('WebhookValidation');
      expect(e.rawCode).toBeUndefined();
      expect(e.webhookCategory).toBe(cat);
      expect(e.matchedTransactionId).toBe(tx);
      expect(e.format).toBe(fmt);
    }
  });

  it('name equals class name and isESignException is true for every subclass', () => {
    const all: ESignException[] = [
      new ESignGeneralException({ category: 'MisaUnknown', detail: 'd', correlationId: cid }),
      new AuthenticationFailedException({ detail: 'd', correlationId: cid }),
      new InvalidOtpException({ detail: 'd', correlationId: cid }),
      new ExpiredOtpException({ detail: 'd', correlationId: cid }),
      new ExhaustedOtpAttemptsException({ detail: 'd', correlationId: cid }),
      new OtpRejectedException({ detail: 'd', correlationId: cid }),
      new NoActiveCertificateException({ detail: 'd', correlationId: cid }),
      new SignRejectedException({ detail: 'd', correlationId: cid }),
      new SignTerminalStateException({
        terminalStatus: 'FAILED',
        transactionId: 't',
        detail: 'd',
        correlationId: cid,
      }),
      new SignTimeoutException({
        transactionId: 't',
        elapsedMs: 1,
        detail: 'd',
        correlationId: cid,
      }),
      new ESignTransportException({ attemptCount: 1, detail: 'd', correlationId: cid }),
      new MalformedEnvelopeException({ correlationId: cid, detail: 'd' }),
      new ClientIdMismatchException({ correlationId: cid, detail: 'd' }),
      new UnknownTransactionException({ correlationId: cid, transactionId: 't', detail: 'd' }),
      new IncompleteSuccessEnvelopeException({
        correlationId: cid,
        transactionId: 't',
        format: 'Pdf',
        detail: 'd',
      }),
      new DocumentIdMismatchException({
        correlationId: cid,
        transactionId: 't',
        format: 'Pdf',
        detail: 'd',
      }),
    ];
    for (const e of all) {
      expect(isESignException(e)).toBe(true);
      expect(e.name).toBe(e.constructor.name);
    }
    expect(all[0]?.name).toBe('ESignGeneralException');
  });

  it('isESignException accepts a branded object from another copy, rejects others', () => {
    const foreign = Object.assign(new Error('x'), {
      [Symbol.for('misaconnect.esign.ESignException')]: true,
    });
    expect(isESignException(foreign)).toBe(true);
    expect(isESignException(new Error('x'))).toBe(false);
    expect(isESignException(null)).toBe(false);
    expect(isESignException('x')).toBe(false);
  });
});

describe('httpStatusName', () => {
  it('uses .NET HttpStatusCode names', () => {
    expect(httpStatusName(100)).toBe('Continue');
    expect(httpStatusName(200)).toBe('OK');
    expect(httpStatusName(302)).toBe('Found');
    expect(httpStatusName(400)).toBe('BadRequest');
    expect(httpStatusName(401)).toBe('Unauthorized');
    expect(httpStatusName(404)).toBe('NotFound');
    expect(httpStatusName(408)).toBe('RequestTimeout');
    expect(httpStatusName(429)).toBe('TooManyRequests');
    expect(httpStatusName(500)).toBe('InternalServerError');
    expect(httpStatusName(502)).toBe('BadGateway');
    expect(httpStatusName(503)).toBe('ServiceUnavailable');
    expect(httpStatusName(504)).toBe('GatewayTimeout');
    expect(httpStatusName(511)).toBe('NetworkAuthenticationRequired');
    expect(httpStatusName(799)).toBe('799');
  });
});

describe('SignXmlRequest', () => {
  const ctx: XmlSignatureContextDto = {
    signatureName: 's',
    hashAlgorithm: 'SHA256',
    signatureDescription: { signedBy: 'a', location: 'b', reason: 'c', contact: 'd' },
  };
  it('fromString / fromUtf8Bytes set exactly one source', () => {
    const a = SignXmlRequest.fromString('<a/>', {
      signatureContext: ctx,
      documentName: 'n',
      dataToBeDisplayed: 'x',
    });
    expect(a.xml).toBe('<a/>');
    expect(a.xmlUtf8Bytes).toBeUndefined();
    const bytes = new TextEncoder().encode('<a/>');
    const b = SignXmlRequest.fromUtf8Bytes(bytes, {
      signatureContext: ctx,
      documentName: 'n',
      dataToBeDisplayed: 'x',
      documentId: 'id',
    });
    expect(b.xml).toBeUndefined();
    expect(b.xmlUtf8Bytes).toBe(bytes);
    expect(b.documentId).toBe('id');
  });
});

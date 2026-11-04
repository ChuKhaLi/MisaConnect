import { describe, expect, it } from 'vitest';
import { mapESignError } from '../../src/application/errors/esign-error-mapper.js';
import {
  isTwoFactorRequired,
  mapTwoFactorError,
} from '../../src/application/errors/otp-error-mapper.js';
import type { DocumentFormat } from '../../src/domain/enums.js';
import {
  AuthenticationFailedException,
  ESignGeneralException,
  ESignTransportException,
  ExhaustedOtpAttemptsException,
  ExpiredOtpException,
  InvalidOtpException,
  OtpRejectedException,
  SignRejectedException,
} from '../../src/domain/errors.js';
import type { RouteKey } from '../../src/infrastructure/routes.js';
import {
  parseResponseError,
  type ResponseError,
} from '../../src/infrastructure/wire/response-error.js';

const CID = 'cid-1';
const HASH_PATH = 'external/esrm/service/document/api/v1/documents/hash';
const CERT_PATH = 'external/esrm/service/general/api/v1/Certificates/by-userId';

interface MapOpts {
  status?: number;
  format?: DocumentFormat;
  includeRaw?: boolean;
  userName?: string;
}
const map = (route: RouteKey, envelope: ResponseError | undefined, o: MapOpts = {}) =>
  mapESignError({
    route,
    status: o.status ?? 400,
    envelope,
    format: o.format ?? 'Pdf',
    correlationId: CID,
    includeRaw: o.includeRaw ?? false,
    ...(o.userName === undefined ? {} : { userName: o.userName }),
  });

describe('parseResponseError', () => {
  it('parses flat', () => {
    expect(parseResponseError({ error: true, errorCode: 'X', devMsg: 'd', userMsg: 'u' })).toEqual({
      error: true,
      errorCode: 'X',
      devMsg: 'd',
      userMsg: 'u',
    });
  });
  it('parses nested status', () => {
    expect(parseResponseError({ status: { error: true, errorCode: '122', code: 401 } })).toEqual({
      error: true,
      errorCode: '122',
    });
  });
  it('coerces error string and errorCode number', () => {
    expect(parseResponseError({ error: 'true', errorCode: 122 })).toEqual({
      error: true,
      errorCode: '122',
    });
    expect(parseResponseError({ error: 'false' })).toEqual({ error: false });
    expect(parseResponseError({ error: null, errorCode: true })).toEqual({ errorCode: 'true' });
  });
  it('reads validationFailures with both spellings', () => {
    expect(
      parseResponseError({
        validationFailures: [
          { propertyName: 'A', errorMessage: 'bad' },
          { property: 'B', failureReason: 'worse' },
        ],
      }),
    ).toEqual({
      validationFailures: [
        { propertyName: 'A', errorMessage: 'bad' },
        { propertyName: 'B', errorMessage: 'worse' },
      ],
    });
  });
  it.each([[null], ['x'], [5], [[]], [undefined]])('rejects non-object %s', (v) => {
    expect(parseResponseError(v)).toBeUndefined();
  });
});

describe('isTwoFactorRequired', () => {
  it.each([
    [{ error: true, errorCode: '122' }, true],
    [{ status: { errorCode: '122' } }, true],
    [{ errorCode: 122 }, true],
    [{ errorCode: '1001' }, false],
    [{}, false],
    [undefined, false],
  ])('%j -> %s', (env, expected) => {
    expect(isTwoFactorRequired(env)).toBe(expected);
  });
});

describe('mapESignError routing', () => {
  it('401 on non-auth route -> Authentication', () => {
    const e = map('hash', undefined, { status: 401 });
    expect(e).toBeInstanceOf(AuthenticationFailedException);
    expect(e.rawCode).toBe('401');
    expect((e as AuthenticationFailedException).requires2FA).toBe(false);
  });
  it('401 on login is not special-cased', () => {
    const e = map('login', { errorCode: 'BadPwd' }, { status: 401 });
    expect(e).toBeInstanceOf(AuthenticationFailedException);
    expect(e.rawCode).toBe('BadPwd');
  });
  it.each([429, 500, 503])('%i -> Transport', (status) => {
    const e = map('hash', undefined, { status });
    expect(e).toBeInstanceOf(ESignTransportException);
    expect((e as ESignTransportException).lastStatusCode).toBe(status);
    expect((e as ESignTransportException).attemptCount).toBe(1);
  });
  it('login 122 -> requires2FA with username', () => {
    const e = map(
      'login',
      { errorCode: '122' },
      { userName: 'bob' },
    ) as AuthenticationFailedException;
    expect(e.requires2FA).toBe(true);
    expect(e.username).toBe('bob');
  });
  it('login other -> no 2FA, no username', () => {
    const e = map(
      'login',
      { errorCode: '5' },
      { userName: 'bob' },
    ) as AuthenticationFailedException;
    expect(e.requires2FA).toBe(false);
    expect(e.username).toBe('');
  });
  it('refresh -> Authentication', () => {
    expect(map('refresh', { errorCode: 'r' })).toBeInstanceOf(AuthenticationFailedException);
  });
  it.each([
    ['certificates', 'CertificateLookupFailed'],
    ['signStatus', 'StatusLookupFailed'],
    ['twoFactor', 'MisaUnknown'],
  ] as const)('%s -> %s with raw or EmptyErrorCode', (route, category) => {
    const e = map(route, { errorCode: 'Q' });
    expect(e).toBeInstanceOf(ESignGeneralException);
    expect(e.category).toBe(category);
    expect(e.rawCode).toBe('Q');
    expect(map(route, undefined).rawCode).toBe('EmptyErrorCode');
  });
  it('carries format', () => {
    expect(map('certificates', undefined, { format: 'Word' }).format).toBe('Word');
  });
});

describe('Signing/hash RequiresUserCertSetup', () => {
  it.each([
    [{ devMsg: 'Account not connected' }, true],
    [{ devMsg: 'user NOT SET UP yet' }, true],
    [{ devMsg: 'no Remote Signing Account' }, true],
    [{ userMsg: 'Tài khoản chưa kết nối' }, true],
    [{ userMsg: 'chưa thiết lập' }, true],
    [{ devMsg: 'chưa thiết lập' }, false],
    [{ userMsg: 'not connected' }, false],
    [{ devMsg: 'other' }, false],
  ] as [ResponseError, boolean][])('%j -> %s', (env, expected) => {
    const e = map('signHash', { errorCode: 'S', ...env });
    expect(e).toBeInstanceOf(SignRejectedException);
    expect((e as SignRejectedException).requiresUserCertSetup).toBe(expected);
    expect(e.rawCode).toBe('S');
  });
});

describe('hash synthesis', () => {
  it.each<[string, ResponseError | undefined, DocumentFormat, string]>([
    ['empty', undefined, 'Pdf', 'EmptyErrorCode'],
    ['empty envelope', {}, 'Pdf', 'EmptyErrorCode'],
    ['xml malformed', { devMsg: 'XML is malformed' }, 'Xml', 'InvalidXmlInput'],
    ['xml invalid', { userMsg: 'xml invalid' }, 'Xml', 'InvalidXmlInput'],
    ['xml không hợp lệ', { userMsg: 'Tệp XML không hợp lệ' }, 'Xml', 'InvalidXmlInput'],
    ['xml rule gated by format', { devMsg: 'xml invalid' }, 'Pdf', 'EmptyErrorCode'],
    ['unsupported', { devMsg: 'Unsupported thing' }, 'Pdf', 'UnsupportedDocumentVariant'],
    ['variant', { errorCode: 'X', devMsg: 'variant?' }, 'Word', 'UnsupportedDocumentVariant'],
    [
      'format not supported',
      { userMsg: 'Format not supported' },
      'Pdf',
      'UnsupportedDocumentVariant',
    ],
    ['cert', { errorCode: 'CertBad' }, 'Pdf', 'InvalidCertificate'],
    ['certificate via devMsg', { devMsg: 'Certificate expired' }, 'Pdf', 'InvalidCertificate'],
    ['hash', { errorCode: 'HashMismatch' }, 'Pdf', 'InvalidHash'],
    ['digest', { errorCode: 'digest' }, 'Pdf', 'InvalidHash'],
    ['file', { errorCode: 'FileEmpty' }, 'Pdf', 'InvalidDocument'],
    ['document', { errorCode: 'Document' }, 'Pdf', 'InvalidDocument'],
    ['signature', { errorCode: 'SignatureInfo' }, 'Pdf', 'InvalidSignatureInfo'],
    ['raw fallthrough', { errorCode: 'ZZ' }, 'Pdf', 'ZZ'],
    [
      'first non-null is devMsg (no raw)',
      { devMsg: 'bad Cert', userMsg: 'Hash' },
      'Pdf',
      'InvalidCertificate',
    ],
    ['no raw, no keyword', { devMsg: 'zzz' }, 'Pdf', 'EmptyErrorCode'],
  ])('%s', (_n, env, format, expected) => {
    const e = map('hash', env, { format });
    expect(e.category).toBe('HashRejected');
    expect(e.rawCode).toBe(expected);
  });
});

describe('attachment synthesis', () => {
  it.each<[string, ResponseError | undefined, DocumentFormat, string]>([
    ['empty', undefined, 'Pdf', 'EmptyErrorCode'],
    ['mainDom word', { devMsg: 'mainDom missing' }, 'Word', 'MissingMainDom'],
    ['main dom excel', { devMsg: 'Main DOM absent' }, 'Excel', 'MissingMainDom'],
    ['missing main excel', { userMsg: 'missing main' }, 'Excel', 'MissingMainDom'],
    ['mainDom gated (xml)', { devMsg: 'mainDom missing' }, 'Xml', 'EmptyErrorCode'],
    ['signatureId xml', { devMsg: 'signatureId required' }, 'Xml', 'MissingSignatureId'],
    ['signature id word', { devMsg: 'signature id required' }, 'Word', 'MissingSignatureId'],
    ['missing signature excel', { userMsg: 'Missing signature' }, 'Excel', 'MissingSignatureId'],
    ['signatureId gated (pdf)', { errorCode: 'signatureId' }, 'Pdf', 'InvalidSignature'],
    ['unsupported', { devMsg: 'unsupported' }, 'Pdf', 'UnsupportedDocumentVariant'],
    ['variant', { devMsg: 'variant' }, 'Xml', 'UnsupportedDocumentVariant'],
    [
      'format not supported',
      { devMsg: 'format not supported' },
      'Pdf',
      'UnsupportedDocumentVariant',
    ],
    ['signature', { errorCode: 'BadSignature' }, 'Pdf', 'InvalidSignature'],
    ['cert', { errorCode: 'Cert' }, 'Pdf', 'InvalidCertificate'],
    ['doc', { errorCode: 'DocX' }, 'Pdf', 'InvalidHashInputs'],
    ['hash', { errorCode: 'Hash' }, 'Pdf', 'InvalidHashInputs'],
    ['raw', { errorCode: 'QQ' }, 'Pdf', 'QQ'],
  ])('%s', (_n, env, format, expected) => {
    const e = map('attachment', env, { format });
    expect(e.category).toBe('AttachmentRejected');
    expect(e.rawCode).toBe(expected);
  });
});

describe('detail text', () => {
  it('summary only without includeRaw', () => {
    const e = map('hash', { errorCode: 'E', userMsg: 'u', devMsg: 'd' });
    expect(e.detail).toBe(`MISA returned errorCode=E on ${HASH_PATH}.`);
  });
  it('<none> when no code', () => {
    expect(map('certificates', undefined).detail).toBe(
      `MISA returned errorCode=<none> on ${CERT_PATH}.`,
    );
  });
  it('appends raw parts with includeRaw', () => {
    const e = map(
      'certificates',
      {
        errorCode: 'E',
        userMsg: 'u',
        devMsg: 'd',
        validationFailures: [
          { propertyName: 'P', errorMessage: 'bad' },
          { propertyName: 'Q' },
          { errorMessage: 'only reason' },
          {},
        ],
      },
      { includeRaw: true },
    );
    expect(e.detail).toBe(
      `MISA returned errorCode=E on ${CERT_PATH}.` +
        ' | userMsg=u | devMsg=d | validationFailures: [P] bad; [Q]; only reason',
    );
  });
  it('skips blank messages and missing envelope', () => {
    const summary = `MISA returned errorCode=<none> on ${CERT_PATH}.`;
    expect(map('certificates', { userMsg: ' ' }, { includeRaw: true }).detail).toBe(summary);
    expect(map('certificates', undefined, { includeRaw: true }).detail).toBe(summary);
  });
});

describe('mapTwoFactorError', () => {
  const two = (envelope: ResponseError | undefined, status = 400, includeRaw = false) =>
    mapTwoFactorError({ status, envelope, correlationId: CID, userName: 'bob', includeRaw });

  it.each([429, 500, 503])('%i -> Transport', (s) => {
    expect(two({ errorCode: '1001' }, s)).toBeInstanceOf(ESignTransportException);
  });
  it('122 -> requires2FA', () => {
    const e = two({ errorCode: '122' }) as AuthenticationFailedException;
    expect(e.requires2FA).toBe(true);
    expect(e.username).toBe('bob');
  });
  it.each([
    ['1001', InvalidOtpException],
    ['1002', ExpiredOtpException],
    ['1003', ExhaustedOtpAttemptsException],
  ] as const)('code %s', (code, cls) => {
    const e = two({ errorCode: code });
    expect(e).toBeInstanceOf(cls);
    expect(e.rawCode).toBe(code);
  });
  const keywords: [string, new (...a: never[]) => unknown][] = [
    ...[
      'invalid otp',
      'wrong otp',
      'otp incorrect',
      'otp wrong',
      'sai mã',
      'không đúng',
      'otp không hợp lệ',
    ].map((k): [string, new (...a: never[]) => unknown] => [k, InvalidOtpException]),
    ...['expired', 'hết hạn', 'quá hạn', 'timed out'].map(
      (k): [string, new (...a: never[]) => unknown] => [k, ExpiredOtpException],
    ),
    ...['max attempts', 'exceeded', 'vượt quá số lần', 'too many attempts', 'locked'].map(
      (k): [string, new (...a: never[]) => unknown] => [k, ExhaustedOtpAttemptsException],
    ),
  ];
  it.each(keywords)('keyword "%s"', (kw, cls) => {
    expect(two({ devMsg: `Xx ${kw.toUpperCase()} yy` })).toBeInstanceOf(cls);
    expect(two({ userMsg: kw })).toBeInstanceOf(cls);
  });
  it('unknown -> OtpRejected', () => {
    const e = two({ errorCode: 'Z', devMsg: 'nothing' });
    expect(e).toBeInstanceOf(OtpRejectedException);
    expect(e.rawCode).toBe('Z');
    expect(two(undefined)).toBeInstanceOf(OtpRejectedException);
  });
  it('detail with and without includeRaw', () => {
    const env = { errorCode: 'Z', userMsg: 'u', devMsg: 'd' };
    expect(two(env).detail).toBe(
      'MISA returned errorCode=Z on api/auth/api/v1/auth/two-factor-auth.',
    );
    expect(two(env, 400, true).detail).toBe(
      'MISA returned errorCode=Z on api/auth/api/v1/auth/two-factor-auth. | userMsg=u | devMsg=d',
    );
  });
});

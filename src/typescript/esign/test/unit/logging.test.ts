import { describe, expect, it } from 'vitest';
import { fromPino, noopLogger, safeLogger, scrubText } from '../../src/infrastructure/logging.js';

const FIELDS = [
  'accessToken',
  'remoteSigningAccessToken',
  'refreshToken',
  'authorizationRM',
  'authorization',
  'password',
  'code',
  'certificate',
  'certiticateChain',
  'certificateChain',
  'fileToSign',
  'documentBytes',
  'documentHash',
  'sh',
  'digest',
  'signature',
  'signatureId',
  'mainDom',
  'document',
  'fontData',
  'signatureImage',
  'logoImage',
  'email',
  'emailName',
  'phoneNumber',
  'firstName',
  'lastName',
  'extraData',
  'secret',
  'deviceId',
  'deviceName',
];

describe('scrubText', () => {
  it.each(FIELDS)('masks JSON field %s', (f) => {
    const out = scrubText(`{"${f}":"s3cr3t value"}`);
    expect(out).toBe(`{"${f}":"***"}`);
  });

  it('masks extraData objects', () => {
    expect(scrubText('{"extraData":{"a":"b","c":1}}')).toBe('{"extraData":"***"}');
  });

  it('masks bearer and authorization headers', () => {
    expect(scrubText('Bearer abcdefgh12345')).toBe('Bearer ***');
    expect(scrubText('AuthorizationRM: Bearer abcdefgh12345')).not.toContain('abcdefgh');
    expect(scrubText('Authorization=tokentoken123')).toBe('Authorization: ***');
  });

  it('masks 40-char base64 runs but keeps short text', () => {
    const b64 = 'QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIz';
    expect(b64).toHaveLength(40);
    expect(scrubText(`x ${b64} y`)).toBe('x *** y');
    expect(scrubText('hello world')).toBe('hello world');
  });

  it('passes empty through', () => {
    expect(scrubText('')).toBe('');
  });
});

describe('loggers', () => {
  it('safeLogger swallows throwing loggers', () => {
    const l = safeLogger({
      debug: () => {
        throw new Error('x');
      },
      info: () => undefined,
      warn: () => undefined,
      error: () => undefined,
    });
    expect(() => l.debug('m')).not.toThrow();
  });
  it('fromPino swaps argument order', () => {
    const calls: unknown[][] = [];
    const rec = (...a: unknown[]): void => void calls.push(a);
    const l = fromPino({ debug: rec, info: rec, warn: rec, error: rec });
    l.info('msg', { a: 1 });
    expect(calls[0]).toEqual([{ a: 1 }, 'msg']);
  });
  it('noopLogger does nothing', () => {
    expect(noopLogger.info('x')).toBeUndefined();
  });
});

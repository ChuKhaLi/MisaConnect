import { describe, expect, it } from 'vitest';
import type { ResolvedESignOptions } from '../../src/infrastructure/options.js';
import { endpointLabel, resolveUrl, Routes } from '../../src/infrastructure/routes.js';

const resolved = (o: Partial<ResolvedESignOptions> = {}): ResolvedESignOptions =>
  ({
    origin: 'https://h.vn/',
    host: 'h.vn',
    authUnderWebdev: true,
    ...o,
  }) as ResolvedESignOptions;

describe('Routes', () => {
  it('has the 9 canonical paths', () => {
    expect(Routes).toEqual({
      login: 'api/auth/api/v1/auth/login-api',
      twoFactor: 'api/auth/api/v1/auth/two-factor-auth',
      refresh: 'webdev/api/auth/api/v1/auth/refreshtoken',
      resendOtp: 'webdev/api/auth/api/v1/auth/resend-otp-auth',
      certificates: 'external/esrm/service/general/api/v1/Certificates/by-userId',
      hash: 'external/esrm/service/document/api/v1/documents/hash',
      signHash: 'external/esrm/service/signing/api/v1/Signing/hash',
      signStatus: 'external/esrm/service/signing/api/v1/Signing/status',
      attachment: 'external/esrm/service/document/api/v1/documents/attachment',
    });
  });
});

describe('resolveUrl', () => {
  it('puts sandbox login under webdev', () => {
    expect(resolveUrl(resolved(), 'login')).toBe(
      'https://h.vn/webdev/api/auth/api/v1/auth/login-api',
    );
    expect(resolveUrl(resolved(), 'twoFactor')).toBe(
      'https://h.vn/webdev/api/auth/api/v1/auth/two-factor-auth',
    );
  });
  it('keeps production login at the root', () => {
    expect(resolveUrl(resolved({ authUnderWebdev: false }), 'login')).toBe(
      'https://h.vn/api/auth/api/v1/auth/login-api',
    );
  });
  it('refresh/resend always carry exactly one webdev/', () => {
    for (const authUnderWebdev of [true, false]) {
      const r = resolved({ authUnderWebdev });
      expect(resolveUrl(r, 'refresh')).toBe(
        'https://h.vn/webdev/api/auth/api/v1/auth/refreshtoken',
      );
      expect(resolveUrl(r, 'resendOtp')).toBe(
        'https://h.vn/webdev/api/auth/api/v1/auth/resend-otp-auth',
      );
    }
  });
  it('keeps ESRM at the root', () => {
    expect(resolveUrl(resolved(), 'hash')).toBe(
      'https://h.vn/external/esrm/service/document/api/v1/documents/hash',
    );
  });
  it('appends an encoded suffix', () => {
    expect(resolveUrl(resolved(), 'signStatus', 'a/b c')).toBe(
      `https://h.vn/external/esrm/service/signing/api/v1/Signing/status/${encodeURIComponent('a/b c')}`,
    );
  });
});

describe('endpointLabel', () => {
  it.each([
    ['login', 'login-api'],
    ['twoFactor', 'two-factor-auth'],
    ['refresh', 'refreshtoken'],
    ['resendOtp', 'resend-otp-auth'],
    ['certificates', 'Certificates/by-userId'],
    ['hash', 'documents/hash'],
    ['signHash', 'Signing/hash'],
    ['signStatus', 'Signing/status'],
    ['attachment', 'documents/attachment'],
  ] as const)('%s -> %s', (route, label) => {
    expect(endpointLabel(route)).toBe(label);
  });
});

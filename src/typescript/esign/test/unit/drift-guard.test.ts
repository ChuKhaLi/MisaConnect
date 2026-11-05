import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  EXHAUSTED_OTP,
  EXPIRED_OTP,
  INVALID_OTP,
} from '../../src/application/errors/otp-error-mapper.js';
import { WEBHOOK_ACKS } from '../../src/application/webhook-envelope.js';
import { ESignErrorCategory } from '../../src/domain/enums.js';
import { Routes } from '../../src/infrastructure/routes.js';

// Drift guards: parse the .NET source of truth and compare with the TS port. Each block skips
// when the .NET file is not present (e.g. a package-only checkout).
const dotnet = (rel: string): string =>
  fileURLToPath(new URL(`../../../../dotnet/${rel}`, import.meta.url));
const read = (path: string): string => (existsSync(path) ? readFileSync(path, 'utf8') : '');

const routesCs = dotnet('MisaConnect.ESign.Infrastructure/ESign/ESignHttpRoutes.cs');
const categoryCs = dotnet('MisaConnect.ESign.Domain/Errors/ESignErrorCategory.cs');
const ackCs = dotnet('MisaConnect.ESign.Infrastructure/ESign/Webhook/MisaWebhookAckCodes.cs');
const otpCs = dotnet('MisaConnect.ESign.Application/Errors/OtpErrorMapper.cs');

describe.skipIf(!existsSync(routesCs))('drift guard against ESignHttpRoutes.cs', () => {
  const consts = [...read(routesCs).matchAll(/public const string (\w+)\s*=\s*"([^"]+)"/g)].map(
    (m) => [m[1] as string, m[2] as string] as const,
  );
  // .NET constant name -> TS route key.
  const keyOf: Record<string, keyof typeof Routes> = {
    AuthLoginApi: 'login',
    AuthRefreshToken: 'refresh',
    AuthTwoFactor: 'twoFactor',
    AuthResendOtp: 'resendOtp',
    CertificatesByUserId: 'certificates',
    DocumentsHash: 'hash',
    SigningHash: 'signHash',
    SigningStatus: 'signStatus',
    DocumentsAttachment: 'attachment',
  };

  it('covers every .NET route constant', () => {
    expect(consts.map(([n]) => n).sort()).toEqual(Object.keys(keyOf).sort());
    expect(Object.keys(Routes).sort()).toEqual(Object.values(keyOf).sort());
  });

  it.each(consts)('route %s equals the .NET value', (name, value) => {
    const key = keyOf[name];
    expect(key).toBeDefined();
    expect(Routes[key as keyof typeof Routes]).toBe(value);
  });
});

describe.skipIf(!existsSync(categoryCs))('drift guard against ESignErrorCategory.cs', () => {
  it('has the same members in the same order', () => {
    const members = [...read(categoryCs).matchAll(/^\s*(\w+)\s*=\s*(\d+),?\s*$/gm)].map(
      (m) => [m[1] as string, Number(m[2])] as const,
    );
    expect(members.length).toBeGreaterThan(0);
    expect(members.map(([, v]) => v)).toEqual(members.map((_, i) => i));
    expect(Object.keys(ESignErrorCategory)).toEqual(members.map(([n]) => n));
    expect(Object.values(ESignErrorCategory)).toEqual(members.map(([n]) => n));
  });
});

describe.skipIf(!existsSync(ackCs))('drift guard against MisaWebhookAckCodes.cs', () => {
  const consts = Object.fromEntries(
    [...read(ackCs).matchAll(/public const string (\w+)\s*=\s*"([^"]+)"/g)].map((m) => [
      m[1] as string,
      m[2] as string,
    ]),
  );

  it('has the same ACK codes', () => {
    expect(Object.keys(consts)).toHaveLength(7);
    expect(WEBHOOK_ACKS.success.errorCode).toBe(consts['Success']);
    expect(WEBHOOK_ACKS.MalformedEnvelope.errorCode).toBe(consts['MalformedEnvelope']);
    expect(WEBHOOK_ACKS.ClientIdMismatch.errorCode).toBe(consts['ClientIdMismatch']);
    expect(WEBHOOK_ACKS.UnknownTransaction.errorCode).toBe(consts['UnknownTransaction']);
    expect(WEBHOOK_ACKS.IncompleteSuccessEnvelope.errorCode).toBe(
      consts['IncompleteSuccessEnvelope'],
    );
    expect(WEBHOOK_ACKS.DocumentIdMismatch.errorCode).toBe(consts['DocumentIdMismatch']);
    expect(WEBHOOK_ACKS.finalizeFailed.errorCode).toBe(consts['FinalizeFailed']);
  });
});

describe.skipIf(!existsSync(otpCs))('drift guard against OtpErrorMapper.cs', () => {
  const source = read(otpCs);
  const keywords = (name: string): string[] => {
    const block = new RegExp(`${name}\\s*=\\s*new\\[\\]\\s*\\{([^}]*)\\}`).exec(source)?.[1] ?? '';
    return [...block.matchAll(/"([^"]*)"/g)].map((m) => m[1] as string);
  };

  it('has the same invalid-OTP keywords, in order', () => {
    expect(INVALID_OTP).toEqual(keywords('InvalidOtpKeywords'));
  });
  it('has the same expired-OTP keywords, in order', () => {
    expect(EXPIRED_OTP).toEqual(keywords('ExpiredOtpKeywords'));
  });
  it('has the same exhausted-OTP keywords, in order', () => {
    expect(EXHAUSTED_OTP).toEqual(keywords('ExhaustedOtpKeywords'));
  });
});

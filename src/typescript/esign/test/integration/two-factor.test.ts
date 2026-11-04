import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { createMisaESignClientInternal } from '../../src/client-internal.js';
import type { IMisaESignClient } from '../../src/client.js';
import type { MisaCredentials, SignPdfRequestDto } from '../../src/domain/dtos.js';
import {
  AuthenticationFailedException,
  ESignGeneralException,
  ESignTransportException,
  ExpiredOtpException,
} from '../../src/domain/errors.js';
import type { OtpProvider } from '../../src/application/ports.js';
import type { MisaESignOptions } from '../../src/infrastructure/options.js';
import { startFakeESignServer, type FakeESignServer } from '../fake/fake-esign-server.js';

const creds: MisaCredentials = {
  clientId: 'cid',
  clientKey: 'ck',
  userName: 'alice',
  password: 'pw',
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
      clock: { now: () => new Date('2026-11-04T08:00:00.000Z') },
      delay: (_ms, signal) => {
        signal?.throwIfAborted();
        return Promise.resolve();
      },
      newId: () => `id-${++n}`,
    },
  );
}

const pdf = {
  documentName: 'contract',
  signerName: 'Alice',
  location: 'Hanoi',
  reason: 'Approve',
  contact: 'alice@example.test',
  logoImageBase64: 'TE9HTw==',
  dataToBeDisplayed: 'Sign the contract',
  pdf: new Uint8Array([37, 80, 68, 70]),
} as unknown as SignPdfRequestDto;

const twoFactorRequests = () => fake.requests.filter((r) => r.path.endsWith('/two-factor-auth'));
const loginRequests = () => fake.requests.filter((r) => r.path.endsWith('/login-api'));
const resendRequests = () => fake.requests.filter((r) => r.path.endsWith('/resend-otp-auth'));

const body = (reqs: { bodyText: string }[]): unknown => JSON.parse(reqs[0]?.bodyText ?? 'null');

interface Challenge {
  userName: string;
  correlationId: string;
}

function provider(code: string, calls: Challenge[] = [], onProvide?: () => void): OtpProvider {
  return {
    provide: (c) => {
      calls.push(c);
      onProvide?.();
      return Promise.resolve({ code, otpType: 'SmsOrEmail', remember: true });
    },
  };
}

async function catchErr(p: Promise<unknown>): Promise<unknown> {
  try {
    await p;
  } catch (e) {
    return e;
  }
  return undefined;
}

describe('transparent 2FA (otpProvider set)', () => {
  it('provides a code, exchanges it and completes one sign call', async () => {
    fake.scenario.loginRequires2FAOnFirstCall = true;
    const calls: Challenge[] = [];
    const client = makeClient({ otpProvider: provider('123456', calls) });
    const r = await client.signPdf(pdf);
    expect(r.format).toBe('Pdf');
    expect(calls).toHaveLength(1);
    expect(calls[0]?.userName).toBe('alice');
    expect(calls[0]?.correlationId).toBeTruthy();
    expect(twoFactorRequests()).toHaveLength(1);
    expect(body(twoFactorRequests())).toEqual({
      userName: 'alice',
      code: '123456',
      otpType: 0,
      remember: true,
    });
    expect(loginRequests()).toHaveLength(1);
  });

  it('rejects a blank code without calling two-factor-auth', async () => {
    fake.scenario.loginRequires2FAOnFirstCall = true;
    const client = makeClient({ otpProvider: provider('   ') });
    const err = await catchErr(client.signPdf(pdf));
    expect(err).toBeInstanceOf(ESignGeneralException);
    expect((err as ESignGeneralException).category).toBe('Validation');
    expect((err as ESignGeneralException).rawCode).toBe('InvalidOtpSubmission');
    expect(twoFactorRequests()).toHaveLength(0);
  });

  it('propagates a second challenge after the exchange (no loop)', async () => {
    fake.scenario.loginRequires2FAOnFirstCall = true;
    const calls: Challenge[] = [];
    const client = makeClient({
      // A cache that never keeps tokens forces the retry to log in again.
      tokenCache: { get: () => undefined, set: () => undefined, delete: () => undefined },
      otpProvider: provider('123456', calls, () => {
        fake.scenario.loginRequires2FAOnFirstCall = true; // re-arm: next login wants 2FA again
      }),
    });
    const err = await catchErr(client.signPdf(pdf));
    expect(err).toBeInstanceOf(AuthenticationFailedException);
    expect((err as AuthenticationFailedException).requires2FA).toBe(true);
    expect(calls).toHaveLength(1);
    expect(twoFactorRequests()).toHaveLength(1);
  });
});

describe('explicit 2FA (no otpProvider)', () => {
  it('surfaces requires2FA, then signInWithOtp lets signPdf succeed', async () => {
    fake.scenario.loginRequires2FAOnFirstCall = true;
    const client = makeClient();
    const err = await catchErr(client.signPdf(pdf));
    expect(err).toBeInstanceOf(AuthenticationFailedException);
    expect((err as AuthenticationFailedException).requires2FA).toBe(true);
    await client.signInWithOtp('123456', 'SmsOrEmail', false);
    expect(body(twoFactorRequests())).toEqual({
      userName: 'alice',
      code: '123456',
      otpType: 0,
      remember: false,
    });
    const r = await client.signPdf(pdf);
    expect(r.format).toBe('Pdf');
    // Pending state is cleared on success.
    const again = await catchErr(client.signInWithOtp('1', 'SmsOrEmail', false));
    expect((again as ESignGeneralException).rawCode).toBe('NoPendingOtpChallenge');
  });

  it('signInWithOtp / resendOtp before any challenge throw NoPendingOtpChallenge', async () => {
    const client = makeClient();
    for (const call of [
      () => client.signInWithOtp('123456', 'SmsOrEmail', false),
      () => client.resendOtp(),
    ]) {
      const err = await catchErr(call());
      expect(err).toBeInstanceOf(ESignGeneralException);
      expect((err as ESignGeneralException).category).toBe('Validation');
      expect((err as ESignGeneralException).rawCode).toBe('NoPendingOtpChallenge');
    }
    expect(fake.requests).toHaveLength(0);
  });

  it('maps error 1002 to ExpiredOtpException', async () => {
    fake.scenario.loginRequires2FAOnFirstCall = true;
    const client = makeClient();
    await catchErr(client.signPdf(pdf));
    fake.scenario.nextTwoFactorErrorCode = '1002';
    const err = await catchErr(client.signInWithOtp('000000', 'SmsOrEmail', false));
    expect(err).toBeInstanceOf(ExpiredOtpException);
  });
});

describe('resendOtp', () => {
  async function pendingClient(over: Partial<MisaESignOptions> = {}) {
    fake.scenario.loginRequires2FAOnFirstCall = true;
    const client = makeClient(over);
    await catchErr(client.signPdf(pdf));
    return client;
  }

  it('success200 sends the pending user and the default language', async () => {
    const client = await pendingClient();
    const r = await client.resendOtp();
    expect(r.success).toBe(true);
    expect(body(resendRequests())).toEqual({
      userName: 'alice',
      language: 'en-US',
    });
  });

  it('uses otp.defaultResendLanguage, and an explicit language wins', async () => {
    const client = await pendingClient({ otp: { defaultResendLanguage: 'vi-VN' } });
    await client.resendOtp();
    await client.resendOtp('fr-FR');
    expect(
      resendRequests().map((r) => (JSON.parse(r.bodyText) as { language: string }).language),
    ).toEqual(['vi-VN', 'fr-FR']);
  });

  it('typedFailure200 returns a failed result with the MISA code', async () => {
    const client = await pendingClient();
    fake.scenario.resendMode = 'typedFailure200';
    const r = await client.resendOtp();
    expect(r).toMatchObject({
      success: false,
      rawCode: 'RateLimited',
      userMsg: 'please wait',
      devMsg: 'too soon',
    });
  });

  it('typedFailure4xx returns a failed result', async () => {
    const client = await pendingClient();
    fake.scenario.resendMode = 'typedFailure4xx';
    const r = await client.resendOtp();
    expect(r).toMatchObject({ success: false, rawCode: 'BadRequest', userMsg: 'u-bad' });
  });

  it('noEnvelope4xx returns a failed result with EmptyErrorCode', async () => {
    const client = await pendingClient();
    fake.scenario.resendMode = 'noEnvelope4xx';
    const r = await client.resendOtp();
    expect(r).toMatchObject({ success: false, rawCode: 'EmptyErrorCode' });
  });

  it('transport500 throws ESignTransportException', async () => {
    const client = await pendingClient();
    fake.scenario.resendMode = 'transport500';
    const err = await catchErr(client.resendOtp());
    expect(err).toBeInstanceOf(ESignTransportException);
  });
});

describe('2FA hardening', () => {
  const credsB: MisaCredentials = { ...creds, userName: 'bob', password: 'pw-b' };

  it('keeps pending challenges per principal on one client', async () => {
    const client = makeClient();
    fake.scenario.loginRequires2FAOnFirstCall = true;
    await catchErr(client.signPdf(pdf)); // A is challenged

    fake.requests.length = 0;
    const errB = await catchErr(
      client.signInWithOtp('123456', 'SmsOrEmail', false, { credentials: credsB }),
    );
    expect((errB as ESignGeneralException).rawCode).toBe('NoPendingOtpChallenge');
    const errBResend = await catchErr(client.resendOtp(undefined, { credentials: credsB }));
    expect((errBResend as ESignGeneralException).rawCode).toBe('NoPendingOtpChallenge');
    expect(twoFactorRequests()).toHaveLength(0);
    expect(resendRequests()).toHaveLength(0);

    // B signs successfully; A's entry survives.
    await client.signPdf(pdf, { credentials: credsB });
    await client.signInWithOtp('123456', 'SmsOrEmail', false);
    expect((body(twoFactorRequests()) as { userName: string }).userName).toBe('alice');
  });

  it('sends Authenticator as otpType 1', async () => {
    const client = makeClient();
    fake.scenario.loginRequires2FAOnFirstCall = true;
    await catchErr(client.signPdf(pdf));
    await client.signInWithOtp('123456', 'Authenticator', false);
    expect((body(twoFactorRequests()) as { otpType: number }).otpType).toBe(1);
  });

  it('rejects a blank code in signInWithOtp without any request', async () => {
    const client = makeClient();
    fake.scenario.loginRequires2FAOnFirstCall = true;
    await catchErr(client.signPdf(pdf));
    fake.requests.length = 0;
    const err = await catchErr(client.signInWithOtp('  ', 'SmsOrEmail', false));
    expect((err as ESignGeneralException).category).toBe('Validation');
    expect((err as ESignGeneralException).rawCode).toBe('InvalidOtpSubmission');
    expect(fake.requests).toHaveLength(0);
  });
});

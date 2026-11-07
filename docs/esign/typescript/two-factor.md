---
title: Two-factor sign-in
parent: TypeScript
grand_parent: ESign
nav_order: 4
---

# Two-factor sign-in

MISA can answer sign-in with a second-factor requirement (code `122`); see
[Two-factor sign-in (OTP)](../index#two-factor-sign-in-otp). The SDK recognises code `122` on any
HTTP status, in both the nested `{status:{...}}` and the flat body shape. It supports two ways to
answer it.

## Transparent: an `otpProvider`

Set `otpProvider`. The SDK asks it for the code, completes the exchange and retries the whole
operation once. A second challenge, or any other failure, propagates.

```ts
import { createMisaESignClient } from '@misaconnect/esign';
import type { MisaCredentials } from '@misaconnect/esign';

declare const baseUrl: string;
declare const creds: MisaCredentials;
declare function askUserForCode(userName: string): Promise<string>;

const client = createMisaESignClient({
  baseUrl,
  credentials: creds,
  otpProvider: {
    async provide({ userName }) {
      return { code: await askUserForCode(userName), otpType: 'SmsOrEmail', remember: true };
    },
  },
});
void client;
```

`provide` receives `{ userName, correlationId }` (always the caller's own user name) and returns
`{ code, otpType, remember }`. `otpType` is `'SmsOrEmail'` or `'Authenticator'`
(`OtpDeliveryChannel`). An empty code fails with `Validation` / `InvalidOtpSubmission` and is never
sent to MISA.

## Explicit: `signInWithOtp`

Without an `otpProvider`, a sign call throws `AuthenticationFailedException` with
`requires2FA: true`. Collect the code from the user, call `signInWithOtp`, then repeat the sign
call. `resendOtp()` asks MISA to send the code again.

```ts
import { AuthenticationFailedException } from '@misaconnect/esign';
import type { IMisaESignClient, MisaCredentials, SignPdfRequestDto } from '@misaconnect/esign';

declare const client: IMisaESignClient;
declare const request: SignPdfRequestDto;
declare const creds: MisaCredentials;
declare function askUserForCode(): Promise<string>;

try {
  await client.signPdf(request, { credentials: creds });
} catch (e) {
  if (e instanceof AuthenticationFailedException && e.requires2FA) {
    await client.signInWithOtp(await askUserForCode(), 'SmsOrEmail', true, { credentials: creds });
    await client.signPdf(request, { credentials: creds });
  } else throw e;
}
```

Pending challenges are held on the client instance, **per principal** (the caller's credentials):
call `signInWithOtp` / `resendOtp` with the same credentials that got the challenge. Without a
pending challenge for those credentials both fail with `Validation` / `NoPendingOtpChallenge`. The
client keeps at most 1000 pending principals; past that the oldest is dropped, and its owner simply
signs again to get a new challenge.

Wrong, expired or exhausted codes throw `InvalidOtpException`, `ExpiredOtpException`,
`ExhaustedOtpAttemptsException` or `OtpRejectedException`, all subclasses of
`AuthenticationFailedException`; see [Errors](./errors).

`beginSign*` follows the same policy as `sign*`. The webhook finalize never prompts for OTP: if MISA
requires 2FA while finalizing, the outcome is a `FailureWithError` with `webhook.finalize_failed`
(see [Webhook mode](./webhooks)).

A token obtained with a 2FA code is cached like any other; see
[Configuration → Token cache](./configuration#token-cache) for when a new process may be asked for
an OTP again.

---
title: Errors
parent: TypeScript
grand_parent: ESign
nav_order: 7
---

# Errors

Failures throw a subclass of `ESignException`, which carries `category`, `rawCode`, `detail`,
`correlationId` and `format`. What each category means for MISA is described in
[Error categories](../index#error-categories).

## Checking for an SDK error

Check with `isESignException(err)` rather than `instanceof` (dual-package hazard: if the ESM and CJS
builds both load, one copy's exception is not an `instanceof` the other's class).

```ts
import { isESignException } from '@misaconnect/esign';
import type { IMisaESignClient, SignPdfRequestDto } from '@misaconnect/esign';

declare const client: IMisaESignClient;
declare const request: SignPdfRequestDto;

try {
  await client.signPdf(request);
} catch (e) {
  if (!isESignException(e)) throw e;
  switch (e.category) {
    case 'Validation':
      console.error('fix the request or options:', e.rawCode, e.detail);
      break;
    case 'SignTerminalCancelled':
    case 'SignTerminalFailed':
      console.warn('signer did not approve', e.correlationId);
      break;
    case 'Transport':
      console.warn('network problem, try again later', e.correlationId);
      break;
    default:
      console.error(e.category, e.rawCode, e.detail, e.correlationId);
  }
}
```

## Exception classes

| Class                                                                                              | Category                                              | Extra fields                                    |
| -------------------------------------------------------------------------------------------------- | ----------------------------------------------------- | ----------------------------------------------- |
| `AuthenticationFailedException`                                                                    | `Authentication`                                      | `requires2FA`, `username`                       |
| `InvalidOtpException` / `ExpiredOtpException` / `ExhaustedOtpAttemptsException` / `OtpRejectedException` | `Authentication` (subclasses of the above)       | as above                                        |
| `NoActiveCertificateException`                                                                     | `NoActiveCertificate`                                 |                                                 |
| `SignRejectedException`                                                                            | `SignRejected`                                        | `requiresUserCertSetup`                         |
| `SignTerminalStateException`                                                                       | `SignTerminalFailed` / `SignTerminalCancelled` / `SignTerminalUnknown` | `terminalStatus`, `transactionId` |
| `SignTimeoutException`                                                                             | `SignTimeout`                                         | `transactionId`, `elapsedMs`                    |
| `ESignTransportException`                                                                          | `Transport`                                           | `lastStatusCode`, `attemptCount`                |
| `WebhookValidationException` and its specializations                                               | `WebhookValidation`                                   | `webhookCategory`, `matchedTransactionId`       |
| `ESignGeneralException`                                                                            | any other (`Validation`, `HashRejected`, ...)         |                                                 |

The `WebhookValidationException` specializations are `MalformedEnvelopeException`,
`ClientIdMismatchException`, `UnknownTransactionException`, `IncompleteSuccessEnvelopeException`
and `DocumentIdMismatchException`. `handleWebhook` turns them into ACKs rather than throwing them
(see [Webhook mode](./webhooks)); a custom `SigningSessionStore` may throw
`UnknownTransactionException` from `withFinalizeLock`.

`SignRejectedException.requiresUserCertSetup` is `true` when MISA says the signer's remote-signing
account is not connected or set up.

## Categories

| `category`                                                                          | Meaning                                                |
| ----------------------------------------------------------------------------------- | ------------------------------------------------------ |
| `Validation`                                                                        | Bad options or request, rejected before any HTTP call. |
| `Authentication`                                                                    | Sign-in failed or needs 2FA.                           |
| `NoActiveCertificate`, `CertificateLookupFailed`                                    | No usable certificate / lookup failed.                 |
| `HashRejected`, `SignRejected`, `AttachmentRejected`, `StatusLookupFailed`          | MISA refused that step of the flow.                    |
| `SignTerminalFailed`, `SignTerminalCancelled`, `SignTerminalUnknown`, `SignTimeout` | The transaction ended without a signature.             |
| `Transport`                                                                         | Network failure, timeout or unusable response.         |
| `WebhookValidation`                                                                 | Webhook envelope rejected.                             |
| `MisaUnknown`                                                                       | An error MISA returned that has no better mapping.     |

`ESignErrorCategory` also lists `WebhookFinalize`, which is reserved and never thrown (kept for
parity with .NET).

## SDK `rawCode` values

Where MISA returns an error code, `rawCode` is MISA's code. The SDK sets its own codes for:

| `rawCode`                                                                                    | When                                                                     |
| -------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| `InvalidOptions`                                                                             | Options rejected by `createMisaESignClient`.                             |
| `InvalidCredentials`                                                                         | A provider or per-call credential has a blank field.                     |
| `InvalidSignPdfRequest` / `InvalidSignXmlRequest` / `InvalidSignWordRequest` / `InvalidSignExcelRequest` | The request failed validation.                               |
| `WebhookModeOnly` / `PollingModeOnly`                                                        | `sign*` with `webhook.mode: 'webhook'` / `beginSign*` with `'polling'`.  |
| `NoPendingOtpChallenge`                                                                      | `signInWithOtp` / `resendOtp` without a pending challenge for the caller. |
| `InvalidOtpSubmission`                                                                       | An empty OTP code.                                                       |
| `InvalidOtp` / `ExpiredOtp` / `ExhaustedOtpAttempts` / `OtpRejected`                         | OTP failures when MISA gives no code of its own.                         |
| `NoActiveCertificate`                                                                        | The account has no ACTIVE certificate.                                   |
| `SignTimeout`                                                                                | Polling exceeded `polling.totalTimeoutMs`.                               |
| `UnexpectedContentType` / `UnparseableResponse`                                              | An eSign (ESRM) response was not JSON (reports type and length, never the body). |
| `Transport` or the HTTP status name                                                          | `ESignTransportException` without / with a last HTTP status.            |
| `EmptyErrorCode`                                                                             | MISA reported an error without a code.                                   |

## Details, aborts and retries

- Raw MISA error text appears in `detail` only with `errors.includeRawErrorMessage = true`.
- Aborting with an `AbortSignal` rejects with the signal's reason, unwrapped and never retried.
- Transport failures are retried with full-jitter backoff (`transportRetry`, `Retry-After`
  honoured). `Signing/hash` is retried only on 429 and on errors where the connection was never
  established (`ECONNREFUSED`, `ENOTFOUND`, `EAI_AGAIN`), because a retry could start a second
  signing transaction.
- An error thrown by your `tokenCache`, `signingSessionStore` or `webhookDeliveryHook` propagates
  unchanged; an error thrown by your `logger` is swallowed.
- Every exception carries the call's `correlationId` (sent to MISA as `X-Correlation-Id`); log it to
  match SDK lines with MISA support requests.

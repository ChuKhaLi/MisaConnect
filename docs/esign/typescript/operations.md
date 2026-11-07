---
title: Operations
parent: TypeScript
grand_parent: ESign
nav_order: 3
---

# Operations

The client returned by `createMisaESignClient(options)` implements `IMisaESignClient`. Every method
returns a `Promise` and takes an optional last argument `opts?: CallOptions`
(`{ signal?, correlationId?, credentials? }`; see [Configuration → Call options](./configuration#call-options)).

| Method                                                               | Returns                                       | Purpose                                                                             |
| -------------------------------------------------------------------- | --------------------------------------------- | ----------------------------------------------------------------------------------- |
| `signPdf(request)`                                                   | `SignPdfResultDto` (`signedPdf`)              | Sign and wait for the signer's approval (polling).                                  |
| `signXml(request)`                                                   | `SignXmlResultDto` (`signedXml`, UTF-8 bytes) | Same for XML; build the request with `SignXmlRequest.fromString` / `fromUtf8Bytes`. |
| `signWord(request)` / `signExcel(request)`                           | `SignWordResultDto` / `SignExcelResultDto`    | Same for Word / Excel.                                                              |
| `signInWithOtp(otpCode, otpType, remember)`                          | `void`                                        | Explicit 2FA: answer a pending OTP challenge.                                       |
| `resendOtp(language?)`                                               | `OtpResendResultDto`                          | Ask MISA to send the OTP again.                                                     |
| `beginSignPdf` / `beginSignXml` / `beginSignWord` / `beginSignExcel` | `BeginResultDto` (`transactionId`, `format`)  | Webhook mode: start signing and return.                                             |
| `handleWebhook(envelope)`                                            | `WebhookHandleResultDto` (`ack`, `outcome`)   | Webhook mode: process one MISA delivery.                                            |

Signing needs an ACTIVE certificate on the account; the first active certificate is used unless
you pass `certificateSelector`. If there is none, `NoActiveCertificateException` is thrown. See
[Certificates](../index#certificates) and [How signing works](../index#how-signing-works).

## MISA endpoints

Paths are relative to the origin of `baseUrl`. With `authUnderWebdev` (the sandbox default) the
login and two-factor paths get a `webdev/` prefix.

| Step                | Path                                                                |
| ------------------- | ------------------------------------------------------------------- |
| Login               | `api/auth/api/v1/auth/login-api`                                    |
| Two-factor (OTP)    | `api/auth/api/v1/auth/two-factor-auth`                              |
| Refresh token       | `webdev/api/auth/api/v1/auth/refreshtoken`                          |
| Resend OTP          | `webdev/api/auth/api/v1/auth/resend-otp-auth`                       |
| List certificates   | `external/esrm/service/general/api/v1/Certificates/by-userId`       |
| Hash the document   | `external/esrm/service/document/api/v1/documents/hash`              |
| Request a signature | `external/esrm/service/signing/api/v1/Signing/hash`                 |
| Transaction status  | `external/esrm/service/signing/api/v1/Signing/status`               |
| Attach signature    | `external/esrm/service/document/api/v1/documents/attachment`        |

Login and refresh happen only when the token cache has no usable token. A 401 on any eSign call
re-authenticates once and re-sends only that call.

## Request fields

PDF, Word and Excel requests share the visible-signature fields below; only the document field
differs (`pdf`, `word`, `excel`, each a non-empty `Uint8Array`).

| Field                           | Required | Notes                                                                    |
| ------------------------------- | -------- | ------------------------------------------------------------------------ |
| `documentName`                  | yes      | At most 100 characters.                                                  |
| `signerName`                    | yes      | Signature name and "signed by".                                          |
| `location`, `reason`, `contact` | yes      | Signature description.                                                   |
| `logoImageBase64`               | yes      | Base64 image shown in the signature; must not be empty.                  |
| `dataToBeDisplayed`             | yes      | Text shown to the signer in the MISA app.                                |
| `documentId`                    | no       | At most 36 characters; a random UUID is generated when absent or empty.  |
| `page`                          | no       | PDF: 1 or greater.                                                       |
| `positionX`, `positionY`, `width`, `height` | no | Signature rectangle.                                           |
| `renderingMode`                 | no       | 0, 1 or 2.                                                               |
| `displayText`                   | no       | Text inside the visible signature.                                       |
| `showSignedDate`                | no       | Defaults to `true`.                                                      |
| `additionalSignaturePositions`  | no       | Extra `{ positionX, positionY, width, height, page }` rectangles.        |

An invalid request is rejected before any HTTP call with an `ESignGeneralException` (category
`Validation`, `rawCode` `InvalidSignPdfRequest` / `InvalidSignXmlRequest` /
`InvalidSignWordRequest` / `InvalidSignExcelRequest`) listing every failure in `detail`.

## signPdf

<!-- snippet: partial -->
```ts
signPdf(request: SignPdfRequestDto, opts?: CallOptions): Promise<SignPdfResultDto>
```

Polling mode: signs a PDF and waits for the signer's approval.

- **MISA endpoints:** login / refresh as needed, `Certificates/by-userId`, `documents/hash`,
  `Signing/hash`, then `Signing/status` every `polling.intervalMs` until a terminal status or
  `polling.totalTimeoutMs`, then `documents/attachment`.
- **Input:** `SignPdfRequestDto`: `pdf` plus the [request fields](#request-fields).
- **Result:** `SignPdfResultDto`: `signedPdf` (`Uint8Array`), `transactionId`,
  `certificateKeyAlias`, `completedAtUtc` (ISO-8601 UTC), `format: 'Pdf'`.
- **Errors:** `Validation` (`InvalidSignPdfRequest`, or `WebhookModeOnly` when `webhook.mode` is
  `'webhook'`), `AuthenticationFailedException` (incl. `requires2FA`; see
  [Two-factor sign-in](./two-factor)), `NoActiveCertificateException`, `HashRejected`,
  `SignRejectedException`, `SignTerminalStateException` (signer rejected or cancelled),
  `SignTimeoutException`, `StatusLookupFailed`, `AttachmentRejected`, `ESignTransportException`.
  See [Errors](./errors).

```ts
import { createMisaESignClient } from '@misaconnect/esign';
import type { MisaCredentials } from '@misaconnect/esign';

declare const baseUrl: string;
declare const creds: MisaCredentials;
declare const pdfBytes: Uint8Array;
declare const logoBase64: string;

const client = createMisaESignClient({ baseUrl, credentials: creds });
const { signedPdf, transactionId } = await client.signPdf({
  pdf: pdfBytes,
  documentName: 'contract.pdf',
  signerName: 'Nguyen Van A',
  location: 'Hanoi',
  reason: 'Approved',
  contact: 'a@example.com',
  logoImageBase64: logoBase64,
  dataToBeDisplayed: 'Contract 2026-001',
  page: 1,
  positionX: 50,
  positionY: 50,
  width: 200,
  height: 80,
});
console.log(transactionId, signedPdf.byteLength);
```

## signXml

<!-- snippet: partial -->
```ts
signXml(request: SignXmlRequestDto, opts?: CallOptions): Promise<SignXmlResultDto>
```

Polling mode: signs an XML document (for example an e-invoice) and waits for approval.

- **MISA endpoints:** as for [`signPdf`](#signpdf).
- **Input:** `SignXmlRequestDto`: exactly one of `xml` (string) or `xmlUtf8Bytes` (`Uint8Array`,
  a leading UTF-8 BOM is kept), plus `documentName`, `dataToBeDisplayed`, optional `documentId`,
  and `signatureContext`: `signatureName`, `hashAlgorithm` (unparseable values fall back to
  `SHA256`) and `signatureDescription` (`signedBy`, `location`, `reason`, `contact`, optional
  `showSignedDate`, `displayText`). Build it with `SignXmlRequest.fromString(xml, rest)` or
  `SignXmlRequest.fromUtf8Bytes(bytes, rest)`.
- **Result:** `SignXmlResultDto`: `signedXml` (UTF-8 bytes), `transactionId`,
  `certificateKeyAlias`, `completedAtUtc`, `format: 'Xml'`.
- **Errors:** as for [`signPdf`](#signpdf), with `InvalidSignXmlRequest`.

```ts
import { SignXmlRequest } from '@misaconnect/esign';
import type { IMisaESignClient } from '@misaconnect/esign';

declare const client: IMisaESignClient;

const xml = await client.signXml(
  SignXmlRequest.fromString('<Invoice>...</Invoice>', {
    documentName: 'invoice.xml',
    dataToBeDisplayed: 'Invoice 0001',
    signatureContext: {
      signatureName: 'Seller',
      hashAlgorithm: 'SHA256',
      signatureDescription: {
        signedBy: 'ACME Co.',
        location: 'Hanoi',
        reason: 'Issued',
        contact: 'billing@example.com',
      },
    },
  }),
);
console.log(new TextDecoder().decode(xml.signedXml));
```

## signWord

<!-- snippet: partial -->
```ts
signWord(request: SignWordRequestDto, opts?: CallOptions): Promise<SignWordResultDto>
```

Polling mode: signs a Word document and waits for approval.

- **MISA endpoints:** as for [`signPdf`](#signpdf).
- **Input:** `SignWordRequestDto`: `word` plus the [request fields](#request-fields).
- **Result:** `SignWordResultDto`: `signedWord`, `transactionId`, `certificateKeyAlias`,
  `completedAtUtc`, `format: 'Word'`.
- **Errors:** as for [`signPdf`](#signpdf), with `InvalidSignWordRequest`.

```ts
import type { IMisaESignClient } from '@misaconnect/esign';

declare const client: IMisaESignClient;
declare const docxBytes: Uint8Array;
declare const logoBase64: string;

const { signedWord } = await client.signWord({
  word: docxBytes,
  documentName: 'agreement.docx',
  signerName: 'Nguyen Van A',
  location: 'Hanoi',
  reason: 'Approved',
  contact: 'a@example.com',
  logoImageBase64: logoBase64,
  dataToBeDisplayed: 'Agreement',
});
console.log(signedWord.byteLength);
```

## signExcel

<!-- snippet: partial -->
```ts
signExcel(request: SignExcelRequestDto, opts?: CallOptions): Promise<SignExcelResultDto>
```

Polling mode: signs an Excel workbook and waits for approval.

- **MISA endpoints:** as for [`signPdf`](#signpdf).
- **Input:** `SignExcelRequestDto`: `excel` plus the [request fields](#request-fields).
- **Result:** `SignExcelResultDto`: `signedExcel`, `transactionId`, `certificateKeyAlias`,
  `completedAtUtc`, `format: 'Excel'`.
- **Errors:** as for [`signPdf`](#signpdf), with `InvalidSignExcelRequest`.

```ts
import type { IMisaESignClient } from '@misaconnect/esign';

declare const client: IMisaESignClient;
declare const xlsxBytes: Uint8Array;
declare const logoBase64: string;

const { signedExcel } = await client.signExcel({
  excel: xlsxBytes,
  documentName: 'report.xlsx',
  signerName: 'Nguyen Van A',
  location: 'Hanoi',
  reason: 'Approved',
  contact: 'a@example.com',
  logoImageBase64: logoBase64,
  dataToBeDisplayed: 'Q3 report',
});
console.log(signedExcel.byteLength);
```

## signInWithOtp

<!-- snippet: partial -->
```ts
signInWithOtp(
  otpCode: string,
  otpType: OtpDeliveryChannel, // 'SmsOrEmail' | 'Authenticator'
  remember: boolean,
  opts?: CallOptions,
): Promise<void>
```

Explicit 2FA: answers the OTP challenge left by a sign call that failed with
`AuthenticationFailedException` and `requires2FA: true`. Call it with the same credentials that got
the challenge, then repeat the sign call. See [Two-factor sign-in](./two-factor).

- **MISA endpoint:** `two-factor-auth` (sent as `otpType` 0 for `SmsOrEmail`, 1 for
  `Authenticator`). The resulting token is cached.
- **Result:** none.
- **Errors:** `Validation` `NoPendingOtpChallenge` (no challenge pending for these credentials) or
  `InvalidOtpSubmission` (empty code); `InvalidOtpException`, `ExpiredOtpException`,
  `ExhaustedOtpAttemptsException`, `OtpRejectedException` (all `AuthenticationFailedException`
  subclasses); `ESignTransportException` on 429 / 5xx.

<!-- snippet: partial -->
```ts
await client.signInWithOtp(code, 'SmsOrEmail', true, { credentials: creds });
```

## resendOtp

<!-- snippet: partial -->
```ts
resendOtp(language?: string, opts?: CallOptions): Promise<OtpResendResultDto>
```

Asks MISA to send the pending OTP code again. `language` defaults to `otp.defaultResendLanguage`
(`'en-US'`).

- **MISA endpoint:** `resend-otp-auth`.
- **Result:** `OtpResendResultDto`: `success`, optional `rawCode`, `userMsg`, `devMsg`, and
  `correlationId`.
- **Errors:** `Validation` `NoPendingOtpChallenge`; `ESignTransportException`.

<!-- snippet: partial -->
```ts
const { success, userMsg } = await client.resendOtp('vi-VN', { credentials: creds });
```

## beginSignPdf

<!-- snippet: partial -->
```ts
beginSignPdf(request: SignPdfRequestDto, opts?: CallOptions): Promise<BeginResultDto>
```

Webhook mode: starts signing and returns without waiting for approval. MISA later posts the result
to your webhook endpoint, which calls [`handleWebhook`](#handlewebhook). See
[Webhook mode](./webhooks).

- **MISA endpoints:** login / refresh as needed, `Certificates/by-userId`, `documents/hash`,
  `Signing/hash`. No polling. The session (with the caller's `clientId` and `userName`, never the
  secrets) is then registered in `signingSessionStore` for `webhook.sessionTtlMs`.
- **Input:** as for [`signPdf`](#signpdf).
- **Result:** `BeginResultDto`: `transactionId`, `format: 'Pdf'`.
- **Errors:** `Validation` (`InvalidSignPdfRequest`, or `PollingModeOnly` when `webhook.mode` is
  `'polling'`), `AuthenticationFailedException`, `NoActiveCertificateException`, `HashRejected`,
  `SignRejectedException`, `ESignTransportException`, and anything your `signingSessionStore`
  throws. 2FA is handled as for `sign*`.

```ts
import type { IMisaESignClient, SignPdfRequestDto } from '@misaconnect/esign';

declare const client: IMisaESignClient;
declare const request: SignPdfRequestDto;

const { transactionId } = await client.beginSignPdf(request);
console.log('waiting for webhook', transactionId);
```

## beginSignXml

<!-- snippet: partial -->
```ts
beginSignXml(request: SignXmlRequestDto, opts?: CallOptions): Promise<BeginResultDto>
```

As [`beginSignPdf`](#beginsignpdf), for an XML request (see [`signXml`](#signxml)); result
`format: 'Xml'`, validation code `InvalidSignXmlRequest`.

<!-- snippet: partial -->
```ts
const { transactionId } = await client.beginSignXml(SignXmlRequest.fromString(xml, rest));
```

## beginSignWord

<!-- snippet: partial -->
```ts
beginSignWord(request: SignWordRequestDto, opts?: CallOptions): Promise<BeginResultDto>
```

As [`beginSignPdf`](#beginsignpdf), for a Word request; result `format: 'Word'`, validation code
`InvalidSignWordRequest`.

<!-- snippet: partial -->
```ts
const { transactionId } = await client.beginSignWord(wordRequest);
```

## beginSignExcel

<!-- snippet: partial -->
```ts
beginSignExcel(request: SignExcelRequestDto, opts?: CallOptions): Promise<BeginResultDto>
```

As [`beginSignPdf`](#beginsignpdf), for an Excel request; result `format: 'Excel'`, validation code
`InvalidSignExcelRequest`.

<!-- snippet: partial -->
```ts
const { transactionId } = await client.beginSignExcel(excelRequest);
```

## handleWebhook

<!-- snippet: partial -->
```ts
handleWebhook(envelope: unknown, opts?: CallOptions): Promise<WebhookHandleResultDto>
```

Webhook mode: processes one delivery MISA posted to your endpoint. Pass the parsed JSON body; send
the returned `ack` back to MISA as the response JSON. The SDK does **not** authenticate the HTTP
request; protect the route yourself (see [Webhook mode](./webhooks)).

- **MISA endpoints:** login / refresh as needed, `Signing/status` (the webhook is only a trigger;
  MISA's own status and signature are used), and `documents/attachment` on success. A delivery for
  a transaction whose result is already stored is answered without any MISA call.
- **Input:** the body, validated as a `WebhookEnvelopeDto` (`messageId`, `clientId`,
  `transactionId`, `status` SUCCESS / FAILED / CANCELLED in any case, `signatures`, optional
  `errorCode`, `extraData`). `opts.credentials` supplies finalize credentials when needed (see
  [Webhook mode](./webhooks)).
- **Result:** `WebhookHandleResultDto`: `ack` (`{ errorCode, devMsg, userMsg }`, `errorCode` `"0"`
  on success) and `outcome`, one of `SuccessWithSignedBytes` (`signedBytes`),
  `TerminalWithoutFinalize` (`status`, `misaErrorCode`) or `FailureWithError` (`categoryName`,
  `misaErrorCode`); every outcome has `format` and `correlationId`.
- **Errors:** malformed input and validation failures are **returned**, not thrown (ACK
  `webhook.malformed`, `webhook.unknown_transaction`, `webhook.client_id_mismatch`,
  `webhook.incomplete_success`, `webhook.document_id_mismatch`; finalize failures
  `webhook.finalize_failed`). Errors thrown by your `webhookDeliveryHook` or `signingSessionStore`
  propagate.

```ts
import type { IMisaESignClient } from '@misaconnect/esign';

declare const client: IMisaESignClient;
declare const body: unknown; // the parsed JSON MISA posted

const { ack, outcome } = await client.handleWebhook(body);
if (outcome.kind === 'SuccessWithSignedBytes') {
  console.log(outcome.transactionId, outcome.signedBytes.byteLength);
} else if (outcome.kind === 'FailureWithError') {
  console.warn(outcome.categoryName, outcome.correlationId);
}
console.log(JSON.stringify(ack)); // respond with this JSON
```

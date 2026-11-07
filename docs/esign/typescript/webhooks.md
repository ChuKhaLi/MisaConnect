---
title: Webhook mode
parent: TypeScript
grand_parent: ESign
nav_order: 5
---

# Webhook mode

Instead of blocking until the signer approves, `beginSign*` starts the transaction and returns;
MISA then posts to your endpoint and you call `handleWebhook`. The MISA side of this (what MISA
posts, the ACK it expects) is described in [Webhook mode](../index#webhook-mode).

`webhook.mode` decides which methods are available: `'both'` (default) allows `sign*` and
`beginSign*`; `'webhook'` makes `sign*` throw `Validation` / `WebhookModeOnly`; `'polling'` makes
`beginSign*` throw `Validation` / `PollingModeOnly`.

## Example (Express)

The example uses Express (`npm install express`); the full version, including route
authentication, is in `samples/typescript/express`.

<!-- snippet: partial -->
```ts
import express from 'express';
import { createMisaESignClient } from '@misaconnect/esign';
import type { MisaCredentials, SignPdfRequestDto } from '@misaconnect/esign';

declare const baseUrl: string;
declare const creds: MisaCredentials;
declare const request: SignPdfRequestDto;

const client = createMisaESignClient({
  baseUrl,
  credentials: creds,
  webhook: { mode: 'webhook' },
  webhookDeliveryHook: async (outcome) => {
    if (outcome.kind === 'SuccessWithSignedBytes') {
      // persist outcome.signedBytes for outcome.transactionId
    }
  },
});

const { transactionId } = await client.beginSignPdf(request); // returns immediately

const app = express();
app.use(express.json());
// AUTHENTICATE THIS ROUTE yourself (secret path segment + constant-time compare, IP allowlist).
app.post('/esign/webhook/:secret', async (req, res) => {
  const { ack } = await client.handleWebhook(req.body);
  res.json(ack); // MISA expects the ACK JSON
});
void transactionId;
```

## Things to know

- **The webhook is only a trigger.** The SDK confirms with an authenticated `Signing/status` call
  and uses MISA's signature and status, never the webhook body's (one extra MISA call per
  delivery). The SDK does **not** authenticate the HTTP request: hosts MUST protect the route
  themselves (a secret path segment compared with `crypto.timingSafeEqual`, and/or an IP
  allowlist). See `samples/typescript/express`.
- The first SUCCESS or FAILED/CANCELLED is stored; later deliveries for the transaction get the
  stored result without another MISA call.
- `webhookDeliveryHook` runs once per MISA-confirmed outcome: `SuccessWithSignedBytes`, or a
  `TerminalWithoutFinalize` that `Signing/status` confirmed. If it throws after the result was
  stored, it is not invoked again, so also persist the bytes from the `handleWebhook` result
  (`outcome.kind === 'SuccessWithSignedBytes'`).
- Validation failures (`webhook.malformed`, `webhook.unknown_transaction`,
  `webhook.client_id_mismatch`, `webhook.incomplete_success`, `webhook.document_id_mismatch`) are
  returned from `handleWebhook` with their ACK and a `FailureWithError` outcome, but are **never
  delivered to the hook**: anyone who gets past your route authentication can send one, so they say
  nothing about the transaction. A `webhook.finalize_failed` result does not reach the hook either;
  MISA's retry finishes the job. Hosts that want diagnostics should log the `handleWebhook` result.
- Finalize never prompts for OTP: if MISA requires 2FA while finalizing, the outcome is a
  `FailureWithError` with `webhook.finalize_failed`.
- Finalize has no caller, so it needs credentials by itself. With a provider, the SDK calls it with
  `{ clientId, userName }` from the stored session (the session never holds `clientKey` or
  `password`). If the transaction was started with a per-call `opts.credentials` while the client
  uses static credentials, pass the same credentials to `handleWebhook(envelope, { credentials })`,
  or use a provider. See [Credentials](./credentials).
- Malformed input is answered with ACK `webhook.malformed` and a `FailureWithError` outcome rather
  than an exception.
- **Sessions live in `signingSessionStore`.** The default is in-memory and single-process: a host
  running several instances behind a load balancer MUST supply a shared store (Redis, a database).
  A custom store implements `SigningSessionStore` (`register`, `get`, `recordMessageId`,
  `cacheSuccess`, `recordTerminal`, `withFinalizeLock`) and keeps each `SigningSession` as JSON;
  `withFinalizeLock` must serialize finalizers of one transaction across instances.

## ACK and outcome reference

| Situation                                                | `ack.errorCode`                | `outcome.kind`                                      | Hook called |
| -------------------------------------------------------- | ------------------------------ | --------------------------------------------------- | ----------- |
| MISA confirms SUCCESS, signature attached                | `0`                            | `SuccessWithSignedBytes`                            | yes, once   |
| MISA confirms FAILED / CANCELLED                         | `0`                            | `TerminalWithoutFinalize`                           | yes, once   |
| Later delivery for a stored result                       | `0`                            | rebuilt from the stored result (no MISA call)       | no          |
| Body not a valid envelope                                | `webhook.malformed`            | `FailureWithError` (`MalformedEnvelope`)            | no          |
| No session for `(clientId, transactionId)`               | `webhook.unknown_transaction`  | `FailureWithError` (`UnknownTransaction`)           | no          |
| `clientId` differs from the session's                    | `webhook.client_id_mismatch`   | `FailureWithError` (`ClientIdMismatch`)             | no          |
| SUCCESS without usable `signatures`                      | `webhook.incomplete_success`   | `FailureWithError` (`IncompleteSuccessEnvelope`)    | no          |
| `signatures[].documentId` differs from the session's     | `webhook.document_id_mismatch` | `FailureWithError` (`DocumentIdMismatch`)           | no          |
| Finalize failed (status lookup, attach, 2FA, still PENDING) | `webhook.finalize_failed` | `FailureWithError` (`None`, `misaErrorCode` = the error's `rawCode`) | no |

## Session store

A `SigningSession` is plain JSON: `clientId`, `userName`, `transactionId`, `format`, `hash` (the
`documents/hash` output needed to attach the signature), `recordedDocumentIds`, `createdAt`
(ISO-8601 UTC), `ttlMs`, `observedMessageIds`, and once known `cachedSuccess` (`ack` and base64
`signed`) or `terminal` (`SigningTerminal`: `status` `'Failed' | 'Cancelled'`, optional
`misaErrorCode`). `withFinalizeLock` throws `UnknownTransactionException` when no session exists.
The default store is `createInMemorySigningSessionStore()`.

<!-- snippet: partial -->
```ts
interface SigningSessionStore {
  register(s: SigningSession): Promise<void>;
  get(clientId: string, transactionId: string): Promise<SigningSession | undefined>;
  recordMessageId(clientId: string, transactionId: string, messageId: string): Promise<void>;
  cacheSuccess(clientId: string, transactionId: string, ack: WebhookAckDto, signedBase64: string): Promise<void>;
  recordTerminal(clientId: string, transactionId: string, terminal: SigningTerminal): Promise<void>;
  withFinalizeLock<T>(clientId: string, transactionId: string, fn: () => Promise<T>): Promise<T>;
}
```

# @misaconnect/esign

TypeScript SDK for **MISA eSign RemoteSigning** — a port of the .NET `MisaConnect.ESign` package. Signs PDF, XML, Word and Excel documents with a remote certificate, including two-factor (OTP) sign-in and a non-blocking webhook mode. Zero runtime dependencies; HTTP goes through Node's built-in `node:https` (or a `fetch` you supply). ESM and CommonJS builds, full type declarations.

Requires **Node.js >= 20**.

## Install

```
npm install @misaconnect/esign
```

## Quick start

```ts
import { readFile, writeFile } from 'node:fs/promises';
import { createMisaESignClient, isESignException } from '@misaconnect/esign';

const client = createMisaESignClient({
  environment: 'sandbox', // or 'production'
  baseUrl: process.env.MISACONNECT_ESIGN_SANDBOX_BASE_URL!,
  credentials: {
    clientId: process.env.MISACONNECT_ESIGN_SANDBOX_CLIENT_ID!,
    clientKey: process.env.MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY!,
    userName: process.env.MISACONNECT_ESIGN_SANDBOX_USERNAME!,
    password: process.env.MISACONNECT_ESIGN_SANDBOX_PASSWORD!,
  },
});

try {
  const result = await client.signPdf({
    pdf: new Uint8Array(await readFile('contract.pdf')),
    documentName: 'contract.pdf',
    signerName: 'Nguyen Van A',
    location: 'Ho Chi Minh City',
    reason: 'I approve this document',
    contact: 'a@example.com',
    logoImageBase64: '<base64 PNG>', // required, non-empty
    dataToBeDisplayed: 'Signed by Nguyen Van A',
    page: 1,
    positionX: 50,
    positionY: 50,
    width: 200,
    height: 80,
  });
  await writeFile('contract.signed.pdf', result.signedPdf);
  console.log(result.transactionId, result.certificateKeyAlias, result.completedAtUtc);
} catch (e) {
  if (isESignException(e)) console.error(e.category, e.rawCode, e.detail, e.correlationId);
  else throw e;
}
```

The quick start, the sample (`samples/typescript/express`) and the sandbox tests read the same `MISACONNECT_ESIGN_SANDBOX_*` variables the .NET SDK uses.

`baseUrl` must be an `https` URL (`http` only for `localhost`, `127.0.0.1`, `::1`). With `environment: 'production'` its host must be `esignapp.misa.vn`; with `'sandbox'` it must not be. Invalid options throw an `ESignGeneralException` with category `Validation` and `rawCode: 'InvalidOptions'`.

### Options (`MisaESignOptions`)

| Option                                                                                                            | Notes                                                                                                                                                         |
| ----------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `environment`                                                                                                     | `'sandbox'` (default) or `'production'`.                                                                                                                      |
| `baseUrl`                                                                                                         | Required. Only the origin is used.                                                                                                                            |
| `credentials`                                                                                                     | Required. A fixed `MisaCredentials` or a provider function; see [Credentials](#credentials).                                                                  |
| `authUnderWebdev`                                                                                                 | Route login under `/webdev`. Default `true` for sandbox, `false` for production.                                                                              |
| `polling.intervalMs` / `polling.totalTimeoutMs`                                                                   | Default 2000 / 60000. Total must exceed the interval. A timeout throws `SignTimeoutException`.                                                                |
| `transportRetry.maxAttempts` / `baseDelayMs` / `maxDelayMs`                                                       | Default 3 / 200 / 2000 (full-jitter backoff, `Retry-After` honoured). `Signing/hash` is retried less often; see [Differences](#differences-from-the-net-sdk). |
| `errors.includeRawErrorMessage`                                                                                   | Opt in to raw MISA error text in exception details (default `false`).                                                                                         |
| `otp.defaultResendLanguage`                                                                                       | Language for `resendOtp()` (default `'en-US'`).                                                                                                               |
| `webhook.mode`                                                                                                    | `'polling'`, `'webhook'` or `'both'` (default). `sign*` throws `WebhookModeOnly` in `'webhook'`; `beginSign*` throws `PollingModeOnly` in `'polling'`.        |
| `webhook.sessionTtlMs`                                                                                            | How long a `beginSign*` session lives (default 24 h).                                                                                                         |
| `tokenCache`, `tokenCacheKey`, `certificateSelector`, `otpProvider`, `signingSessionStore`, `webhookDeliveryHook` | Swappable collaborators; see below.                                                                                                                           |
| `logger`, `correlationId`, `clock`, `delay`                                                                       | `logger`: `{ debug, info, warn, error }(message, fields?)`, default no-op. `correlationId`: `() => string` (default `crypto.randomUUID`).                     |
| `fetch`                                                                                                           | Custom `fetch`. Default: a built-in `node:https` client; see [Network / TLS](#network--tls).                                                                  |

Every method takes an optional last argument `opts?: CallOptions` = `{ signal?: AbortSignal; correlationId?: string; credentials?: MisaCredentials }`.

## Operations

| Method                                                               | Returns                                       | Purpose                                                                             |
| -------------------------------------------------------------------- | --------------------------------------------- | ----------------------------------------------------------------------------------- |
| `signPdf(request)`                                                   | `SignPdfResultDto` (`signedPdf`)              | Sign and wait for the signer's approval (polling).                                  |
| `signXml(request)`                                                   | `SignXmlResultDto` (`signedXml`, UTF-8 bytes) | Same for XML; build the request with `SignXmlRequest.fromString` / `fromUtf8Bytes`. |
| `signWord(request)` / `signExcel(request)`                           | `SignWordResultDto` / `SignExcelResultDto`    | Same for Word / Excel.                                                              |
| `signInWithOtp(otpCode, otpType, remember)`                          | `void`                                        | Explicit 2FA: answer a pending OTP challenge.                                       |
| `resendOtp(language?)`                                               | `OtpResendResultDto`                          | Ask MISA to send the OTP again.                                                     |
| `beginSignPdf` / `beginSignXml` / `beginSignWord` / `beginSignExcel` | `BeginResultDto` (`transactionId`, `format`)  | Webhook mode: start signing and return.                                             |
| `handleWebhook(envelope)`                                            | `WebhookHandleResultDto` (`ack`, `outcome`)   | Webhook mode: process one MISA delivery.                                            |

Signing needs an ACTIVE certificate on the account; the first active certificate is used unless you pass `certificateSelector`. If there is none, `NoActiveCertificateException` is thrown.

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

## Credentials

`credentials` is one of:

```ts
import { createMisaESignClient } from '@misaconnect/esign';
import type { MisaCredentials } from '@misaconnect/esign';

declare const baseUrl: string;
declare const mine: MisaCredentials;
declare function lookupSecrets(userName?: string): Promise<MisaCredentials>;

// 1. Static: one account for the whole process.
const staticClient = createMisaESignClient({ baseUrl, credentials: mine });

// 2. Provider: resolved per call (e.g. a per-user vault). The context carries clientId / userName
//    when known (webhook finalize) and the call's AbortSignal.
const providerClient = createMisaESignClient({
  baseUrl,
  credentials: async (ctx) => lookupSecrets(ctx.userName),
});

// 3. Per call: overrides both of the above for this call only.
await staticClient.signPdf(
  {
    pdf: new Uint8Array(),
    documentName: 'a.pdf',
    signerName: 's',
    location: 'l',
    reason: 'r',
    contact: 'c',
    logoImageBase64: 'x',
    dataToBeDisplayed: 'd',
  },
  { credentials: mine },
);
void providerClient;
```

Resolution order: `opts.credentials`, then the provider or static value. Resolved once per call and used for sign-in, the `x-clientId` / `x-clientKey` headers and the token-cache key. `MisaCredentials` carry secrets: keep them out of your logs; the SDK never logs them.

## Two-factor sign-in (OTP)

MISA can answer sign-in with a second-factor requirement (code `122`).

**Transparent**: set `otpProvider`. The SDK asks it for the code, completes the exchange and retries the whole operation once.

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

**Explicit**: without an `otpProvider`, a sign call throws `AuthenticationFailedException` with `requires2FA: true`. Collect the code from the user, call `signInWithOtp`, then repeat the sign call. `resendOtp()` asks MISA to send the code again.

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

Pending challenges are held on the client instance, **per principal** (the caller's credentials): call `signInWithOtp` / `resendOtp` with the same credentials that got the challenge.

## Webhook mode

Instead of blocking until the signer approves, `beginSign*` starts the transaction and returns; MISA then posts to your endpoint and you call `handleWebhook`.

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

Things to know:

- **The webhook is only a trigger.** The SDK confirms with an authenticated `Signing/status` call and uses MISA's signature and status, never the webhook body's (one extra MISA call per delivery). The SDK does **not** authenticate the HTTP request: hosts MUST protect the route themselves (a secret path segment compared with `crypto.timingSafeEqual`, and/or an IP allowlist). See `samples/typescript/express`.
- The first SUCCESS or FAILED/CANCELLED is stored; later deliveries for the transaction get the stored result without another MISA call.
- `webhookDeliveryHook` runs once per MISA-confirmed outcome: `SuccessWithSignedBytes`, or a `TerminalWithoutFinalize` that `Signing/status` confirmed. If it throws after the result was stored, it is not invoked again, so also persist the bytes from the `handleWebhook` result (`outcome.kind === 'SuccessWithSignedBytes'`).
- Validation failures (`webhook.malformed`, `webhook.unknown_transaction`, `webhook.client_id_mismatch`, `webhook.incomplete_success`, `webhook.document_id_mismatch`) are returned from `handleWebhook` with their ACK and a `FailureWithError` outcome, but are **never delivered to the hook**: anyone who gets past your route authentication can send one, so they say nothing about the transaction. A `webhook.finalize_failed` result does not reach the hook either; MISA's retry finishes the job. Hosts that want diagnostics should log the `handleWebhook` result.
- Finalize never prompts for OTP: if MISA requires 2FA while finalizing, the outcome is a `FailureWithError` with `webhook.finalize_failed`.
- Finalize has no caller, so it needs credentials by itself. With a provider, the SDK calls it with `{ clientId, userName }` from the stored session (the session never holds `clientKey` or `password`). If the transaction was started with a per-call `opts.credentials` while the client uses static credentials, pass the same credentials to `handleWebhook(envelope, { credentials })`, or use a provider.
- Malformed input is answered with ACK `webhook.malformed` and a `FailureWithError` outcome rather than an exception.
- **Sessions live in `signingSessionStore`.** The default is in-memory and single-process: a host running several instances behind a load balancer MUST supply a shared store (Redis, a database). A custom store implements `SigningSessionStore` (`register`, `get`, `recordMessageId`, `cacheSuccess`, `recordTerminal`, `withFinalizeLock`) and keeps each `SigningSession` as JSON; `withFinalizeLock` must serialize finalizers of one transaction across instances.

## Swappable collaborators

Ports exported as types: `TokenCache`, `AccessToken`, `CertificateSelector`, `OtpProvider`, `MisaCredentialsProvider`, `SigningSessionStore`, `SigningSession`, `SigningTerminal`, `WebhookDeliveryHook`, `Clock`, `Delay`, `Logger`. Defaults: `createInMemoryTokenCache()` and `createInMemorySigningSessionStore()`.

Tokens are cached under `"{userName}|{clientId}|{host}"` (override with `tokenCacheKey`), refreshed 30 s before expiry, and a failed refresh falls back to one fresh login. A cached token is bound to a **salted fingerprint of password + clientKey** (`AccessToken.credentialFingerprint`): a custom `tokenCache` must store and return the token object exactly as given, and a custom `tokenCacheKey` cannot remove that binding (a token without a matching fingerprint is a miss). By default the salt is random per client instance, so a shared external cache (Redis) does not reuse tokens across processes or restarts: each process logs in once (and a 2FA account may be asked for an OTP again). To share tokens across hosts, set the same `tokenFingerprintSecret` (at least 32 characters, keep it as secret as the client key) on every host: the fingerprint then becomes HMAC-SHA256(secret, password + clientKey), identical on all hosts, and a caller with a wrong password still never matches a cached token.

An error thrown by your `tokenCache` propagates unchanged and is not wrapped.

## Error handling

Failures throw a subclass of `ESignException`, which carries `category`, `rawCode`, `detail`, `correlationId` and `format`. Check with `isESignException(err)` rather than `instanceof` (dual-package hazard: if the ESM and CJS builds both load, one copy's exception is not an `instanceof` the other's class). Subclasses: `AuthenticationFailedException` (`requires2FA`), `NoActiveCertificateException`, `SignRejectedException`, `SignTerminalStateException`, `SignTimeoutException`, `ESignTransportException`, `InvalidOtpException` / `ExpiredOtpException` / `ExhaustedOtpAttemptsException` / `OtpRejectedException`, `WebhookValidationException` and its specializations, `ESignGeneralException`.

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

Aborting with an `AbortSignal` rejects with the signal's reason, unwrapped and never retried. Raw MISA error text appears in `detail` only with `errors.includeRawErrorMessage = true`.

## Network / TLS

Without a `fetch` option the SDK uses a small `fetch`-compatible client over `node:https` (a copy of the one in `@misaconnect/einvoice`). Its keep-alive agents pin the TLS key-exchange groups to the classical `X25519:P-256:P-384`: Node >= 22 with OpenSSL 3.5 otherwise also offers the post-quantum hybrid `X25519MLKEM768`, and the larger ClientHello is reset by the network in front of MISA for traffic from outside Vietnam. No global TLS setting is changed. The built-in client connects directly (no proxy support), follows no redirects and requests no compression. Pass your own `fetch` for proxies or custom TLS; the SDK then cannot set TLS options for it.

**Calling MISA from outside Vietnam:** measured from GitHub-hosted runners (2026-11-03) for the **eInvoice** host `testapi.meinvoice.vn`, the network path is a path-MTU blackhole: TCP segments near 1500 bytes are dropped and the ICMP message that would make the sender shrink them never arrives, so any request larger than about 1.4 KB hangs and ends in `ECONNRESET`. Authenticated calls carry a ~2 KB bearer token, so they are affected; curl suffers as much as Node, so the SDK cannot work around it. Small requests still succeed, which makes the failure look intermittent. Hosts in Vietnam are not affected. Elsewhere, lower the MTU of the interface that reaches MISA (e.g. `ip link set dev eth0 mtu 1280` on Linux) or enable TCP MTU probing (`sysctl -w net.ipv4.tcp_mtu_probing=1`; untested against MISA).

**Connect timeout (happy-eyeballs):** MISA publishes an AAAA record, so on IPv4-only hosts every connection goes through Node's `autoSelectFamily`, whose default per-address attempt is 250 ms. From outside Vietnam the TCP handshake alone takes ~210–250 ms (more on slow links), which made connections fail with `ETIMEDOUT` at random. The built-in client allows 2 s per attempt (`autoSelectFamilyAttemptTimeout: 2000`). If you pass your own `fetch`, set the same option, e.g. undici `new Agent({ connect: { autoSelectFamilyAttemptTimeout: 2000, ecdhCurve: 'X25519:P-256:P-384' } })`, or call `net.setDefaultAutoSelectFamilyAttemptTimeout(2000)` once at startup.

For eSign, the **login** works from GitHub runners with MTU 1280. **Signing itself is not yet verified live**: the sandbox account used so far has no ACTIVE certificate, so the hash / sign / attachment calls (which carry the token and base64 documents) have only been exercised against the in-process fake server. The same MTU advice likely applies to them, unverified.

## Logging

```ts
import pino from 'pino';
import { createMisaESignClient, fromPino } from '@misaconnect/esign';
import type { MisaCredentials } from '@misaconnect/esign';

declare const baseUrl: string;
declare const creds: MisaCredentials;

const client = createMisaESignClient({ baseUrl, credentials: creds, logger: fromPino(pino()) });
void client;
```

One started and one completed line per wire call (`endpoint`, `method`, `correlationId`, `durationMs`, `outcome`, plus `errorCategory` / `rawCode` on failure). Credentials, tokens, document content and signatures are never logged. An exception thrown by your logger is swallowed.

## Names: .NET to TypeScript

| .NET                                           | TypeScript                                                       |
| ---------------------------------------------- | ---------------------------------------------------------------- |
| `IMisaESignClient`                             | `IMisaESignClient` / `createMisaESignClient(options)`            |
| `SignPdfAsync` / `SignXmlAsync` / ...          | `signPdf` / `signXml` / `signWord` / `signExcel`                 |
| `BeginSignPdfAsync` / ...                      | `beginSignPdf` / ...                                             |
| `HandleWebhookAsync`                           | `handleWebhook`                                                  |
| `SignInWithOtpAsync`, `ResendOtpAsync`         | `signInWithOtp`, `resendOtp`                                     |
| `MisaESignOptions`                             | `MisaESignOptions` (camelCase)                                   |
| `CredentialsMode` + `IMisaCredentialsAccessor` | `credentials` (static or provider) + `opts.credentials`          |
| `ITokenCache`, `ISigningSessionStore`          | `TokenCache`, `SigningSessionStore`                              |
| `CancellationToken`                            | `opts.signal` (`AbortSignal`)                                    |
| `byte[]`                                       | `Uint8Array`                                                     |
| `ESignException` and subclasses                | same names; `Category` / `RawCode` become `category` / `rawCode` |

DTO property names are the camelCase of the .NET names; wire field names match MISA's API verbatim.

## Differences from the .NET SDK

These are .NET defects or deliberate choices that the TypeScript SDK does not reproduce (the .NET follow-ups are tracked in the design document).

1. **Token after a 401 refresh.** The transport owns the current token, so after a refresh the rest of the flow uses the new token. (.NET keeps using the stale one and refreshes again on every later call.)
2. **Login fallback.** If refreshing fails on a 401, the SDK evicts the token and logs in once more before failing with `Authentication`. (.NET fails.)
3. **`Signing/hash` retries.** It is retried only on 429 and on errors where the connection was never established (`ECONNREFUSED`, `ENOTFOUND`, `EAI_AGAIN`), never on 5xx, timeouts or resets, because a retry could start a second signing transaction (a second approval on the signer's phone). The other calls keep .NET's retry rules.
4. **Content-type guard on every eSign (ESRM) JSON response**, not just the certificate list (`UnexpectedContentType` / `UnparseableResponse`; the error reports the content type and body length, never the body). The auth calls (login, two-factor, refresh, resend OTP) parse JSON whatever the content type, as .NET does.
5. **`error` field typed loosely.** MISA's `error` accepts `boolean | string | null` (`"true"` / `"false"` are parsed), so a string never drops the envelope.
6. **Malformed webhooks** are answered with ACK `webhook.malformed` and a `FailureWithError` outcome instead of escaping as an exception (`messageId`, `clientId`, `transactionId`, `status` (case-insensitive) and `signatures` are validated).
7. **2FA code `122`** is recognised on any HTTP status, in both the nested `{status:{...}}` and the flat body shape.
8. **Proactive token refresh** happens when the token has under 30 s left (.NET declares a skew but never uses it).
9. **Dynamic credentials work in webhook mode** (.NET slice 008 leaves it unusable): the session stores `clientId` and `userName`, and finalize calls your provider with them.

Further behaviour decided while implementing:

- **Token binding.** Cached tokens carry a salted fingerprint of password + clientKey; see [Swappable collaborators](#swappable-collaborators).
- **XML bytes keep a BOM.** XML passed as bytes keeps a leading UTF-8 BOM, the same as .NET.
- **OTP challenges are per principal**, not one pending username per client.
- **Webhooks are a trigger, not a source of truth** (confirmed with `Signing/status`); the terminal state is stored once; finalize never prompts for OTP. See [Webhook mode](#webhook-mode).
- **No DI scope.** One client object plays the role of one .NET DI scope; use per-call credentials to serve several users from one client.

## Sandbox tests

The sandbox suite skips cleanly unless `MISACONNECT_ESIGN_SANDBOX_BASE_URL`, `_CLIENT_ID`, `_CLIENT_KEY`, `_USERNAME` and `_PASSWORD` are all set (`MISACONNECT_ESIGN_SANDBOX_REQUIRED=1` turns a skip into a failure):

```
npm run test:sandbox -w @misaconnect/esign
```

## License

MIT. Design: [docs/superpowers/specs/2026-11-04-ts-esign-design.md](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/superpowers/specs/2026-11-04-ts-esign-design.md).

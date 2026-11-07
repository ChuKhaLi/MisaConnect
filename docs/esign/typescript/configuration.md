---
title: Configuration
parent: TypeScript
grand_parent: ESign
nav_order: 2
---

# Configuration

`createMisaESignClient(options: MisaESignOptions)` validates the options and builds the client.
One client holds one token cache and one HTTP transport: create it once and reuse it rather than
creating one per call.

## Options (`MisaESignOptions`)

| Option                                                                                                            | Notes                                                                                                                                                         |
| ----------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `environment`                                                                                                     | `'sandbox'` (default) or `'production'`.                                                                                                                      |
| `baseUrl`                                                                                                         | Required. Only the origin is used.                                                                                                                            |
| `credentials`                                                                                                     | Required. A fixed `MisaCredentials` or a provider function; see [Credentials](./credentials).                                                                 |
| `authUnderWebdev`                                                                                                 | Route login and two-factor calls under `/webdev`. Default `true` for sandbox, `false` for production.                                                         |
| `polling.intervalMs` / `polling.totalTimeoutMs`                                                                   | Default 2000 / 60000. Total must exceed the interval. A timeout throws `SignTimeoutException`.                                                                |
| `transportRetry.maxAttempts` / `baseDelayMs` / `maxDelayMs`                                                       | Default 3 / 200 / 2000 (full-jitter backoff, `Retry-After` honoured). `Signing/hash` is retried less often; see [Fixed in .NET 2.5.0](./index#fixed-in-net-250).           |
| `errors.includeRawErrorMessage`                                                                                   | Opt in to raw MISA error text in exception details (default `false`).                                                                                         |
| `otp.defaultResendLanguage`                                                                                       | Language for `resendOtp()` (default `'en-US'`).                                                                                                               |
| `webhook.mode`                                                                                                    | `'polling'`, `'webhook'` or `'both'` (default). `sign*` throws `WebhookModeOnly` in `'webhook'`; `beginSign*` throws `PollingModeOnly` in `'polling'`.        |
| `webhook.sessionTtlMs`                                                                                            | How long a `beginSign*` session lives (default 24 h).                                                                                                         |
| `tokenFingerprintSecret`                                                                                          | Optional shared secret (at least 32 characters) for the token fingerprint; see [Token cache](#token-cache).                                                   |
| `tokenCache`, `tokenCacheKey`, `certificateSelector`, `otpProvider`, `signingSessionStore`, `webhookDeliveryHook` | Swappable collaborators; see [below](#swappable-collaborators).                                                                                               |
| `logger`, `correlationId`, `clock`, `delay`                                                                       | `logger`: `{ debug, info, warn, error }(message, fields?)`, default no-op. `correlationId`: `() => string` (default `crypto.randomUUID`).                     |
| `fetch`                                                                                                           | Custom `fetch`. Default: a built-in `node:https` client; see [Network / TLS](#network--tls).                                                                  |

### Validation

Options are checked when the client is created. Every failure is collected into one
`ESignGeneralException` (category `Validation`, `rawCode: 'InvalidOptions'`, the failures joined in
`detail`):

- `environment` is `'sandbox'` or `'production'`.
- `baseUrl` is an absolute `https` URL (`http` only for `localhost`, `127.0.0.1`, `::1`); in
  production its host is `esignapp.misa.vn`, in sandbox it is not.
- Static `credentials` have non-blank `clientId`, `clientKey`, `userName` and `password` (a
  provider's result is checked per call instead and fails with `rawCode: 'InvalidCredentials'`).
- `polling.intervalMs > 0` and `polling.totalTimeoutMs > polling.intervalMs`.
- `transportRetry.maxAttempts >= 1` and `transportRetry.maxDelayMs >= transportRetry.baseDelayMs`.
- `otp.defaultResendLanguage` is not empty; `webhook.sessionTtlMs > 0`.
- `tokenFingerprintSecret`, when set, has at least 32 non-blank characters.

### Example

```ts
import { createMisaESignClient } from '@misaconnect/esign';

const client = createMisaESignClient({
  environment: 'production',
  baseUrl: 'https://esignapp.misa.vn',
  credentials: {
    clientId: process.env.MISA_ESIGN_CLIENT_ID!,
    clientKey: process.env.MISA_ESIGN_CLIENT_KEY!,
    userName: process.env.MISA_ESIGN_USERNAME!,
    password: process.env.MISA_ESIGN_PASSWORD!,
  },
  polling: { intervalMs: 3000, totalTimeoutMs: 120_000 },
  transportRetry: { maxAttempts: 4 },
  otp: { defaultResendLanguage: 'vi-VN' },
  webhook: { mode: 'polling' },
});
void client;
```

## Call options

Every method takes an optional last argument `opts?: CallOptions`:

<!-- snippet: partial -->
```ts
interface CallOptions {
  signal?: AbortSignal; // aborts the call; rejects with the signal's reason, never retried or wrapped
  correlationId?: string; // overrides the `correlationId` option for this call (sent as X-Correlation-Id)
  credentials?: MisaCredentials; // credentials for this call only; win over the `credentials` option
}
```

## Swappable collaborators

Ports exported as types: `TokenCache`, `AccessToken`, `CertificateSelector`, `OtpProvider`,
`MisaCredentialsProvider`, `SigningSessionStore`, `SigningSession`, `SigningTerminal`,
`WebhookDeliveryHook`, `Clock`, `Delay`, `Logger`. Defaults: `createInMemoryTokenCache()` and
`createInMemorySigningSessionStore()`.

| Option                | Type                                     | Default                                                     |
| --------------------- | ---------------------------------------- | ----------------------------------------------------------- |
| `tokenCache`          | `TokenCache`                             | in-memory, per client (`createInMemoryTokenCache()`)        |
| `tokenCacheKey`       | `(c: { userName, clientId, host }) => string` | `userName`, `clientId` and host; see [Token cache](#token-cache) |
| `certificateSelector` | `CertificateSelector`                    | the first ACTIVE certificate                                |
| `otpProvider`         | `OtpProvider`                            | none: explicit 2FA; see [Two-factor sign-in](./two-factor)  |
| `signingSessionStore` | `SigningSessionStore`                    | in-memory, process-local; see [Webhook mode](./webhooks)    |
| `webhookDeliveryHook` | `WebhookDeliveryHook`                    | no-op; see [Webhook mode](./webhooks)                       |
| `clock`               | `Clock` (`{ now(): Date }`)              | system clock                                                |
| `delay`               | `Delay` (`(ms, signal?) => Promise<void>`) | `setTimeout` that rejects on abort                          |
| `logger`              | `Logger`                                 | no-op; see [Logging](#logging)                              |

### Certificate selector

`certificateSelector` receives the account's ACTIVE certificates (never an empty list: with none,
`NoActiveCertificateException` is thrown before it is called) and returns the one to sign with.

```ts
import { createMisaESignClient } from '@misaconnect/esign';
import type { Certificate, MisaCredentials } from '@misaconnect/esign';

declare const baseUrl: string;
declare const creds: MisaCredentials;

const client = createMisaESignClient({
  baseUrl,
  credentials: creds,
  certificateSelector: (certs: readonly Certificate[]): Certificate => {
    const preferred = certs.find((c) => c.keyAlias === 'company-seal');
    const chosen = preferred ?? certs[0];
    if (chosen === undefined) throw new Error('no certificate');
    return chosen;
  },
});
void client;
```

### Token cache

Tokens are cached under `"{userName}|{clientId}|{host}"` (override with `tokenCacheKey`), refreshed
30 s before expiry, and a failed refresh falls back to one fresh login. A cached token is bound to a
**salted fingerprint of password + clientKey** (`AccessToken.credentialFingerprint`): a custom
`tokenCache` must store and return the token object exactly as given, and a custom `tokenCacheKey`
cannot remove that binding (a token without a matching fingerprint is a miss). By default the salt
is random per client instance, so a shared external cache (Redis) does not reuse tokens across
processes or restarts: each process logs in once (and a 2FA account may be asked for an OTP again).
To share tokens across hosts, set the same `tokenFingerprintSecret` (at least 32 characters, keep it
as secret as the client key) on every host: the fingerprint then becomes
HMAC-SHA256(secret, password + clientKey), identical on all hosts, and a caller with a wrong
password still never matches a cached token.

After a failed refresh the SDK evicts a cached token only while the entry still holds that stale
token, so a token another call has just stored is reused, not thrown away. It does this through the
optional `TokenCache.deleteIfMatches(key, value)` when your cache provides it: implement it
atomically (for example a Redis Lua script) and forward it from any cache that wraps another.
Without it the SDK reads, compares and deletes, which leaves a small race in which a just-stored
token can be evicted (costing one extra login). `createInMemoryTokenCache()` implements it.

An error thrown by your `tokenCache` propagates unchanged and is not wrapped.

A cache that wraps the default one and forwards `deleteIfMatches`:

```ts
import { createInMemoryTokenCache, createMisaESignClient } from '@misaconnect/esign';
import type { AccessToken, MisaCredentials, TokenCache } from '@misaconnect/esign';

declare const baseUrl: string;
declare const creds: MisaCredentials;

const inner = createInMemoryTokenCache();
const counting: TokenCache = {
  get: (key: string) => inner.get(key),
  set: (key: string, token: AccessToken) => inner.set(key, token), // store the object as given
  delete: (key: string) => inner.delete(key),
  deleteIfMatches: (key: string, value: string) => inner.deleteIfMatches?.(key, value),
};

const client = createMisaESignClient({ baseUrl, credentials: creds, tokenCache: counting });
void client;
```

## Network / TLS

Without a `fetch` option the SDK uses a small `fetch`-compatible client over `node:https` (a copy
of the one in `@misaconnect/einvoice`). Its keep-alive agents pin the TLS key-exchange groups to the
classical `X25519:P-256:P-384`: Node >= 22 with OpenSSL 3.5 otherwise also offers the post-quantum
hybrid `X25519MLKEM768`, and the larger ClientHello is reset by the network in front of MISA for
traffic from outside Vietnam. No global TLS setting is changed. The built-in client connects
directly (no proxy support), follows no redirects and requests no compression. Pass your own
`fetch` for proxies or custom TLS; the SDK then cannot set TLS options for it.

**Calling MISA from outside Vietnam:** requests larger than about 1.4 KB can hang and end in
`ECONNRESET` because of a path-MTU blackhole; the measurements and the MTU / TCP-probing
workarounds are on [Calling MISA from outside Vietnam](../index#calling-misa-from-outside-vietnam).
For eSign, the **login** works from GitHub runners with MTU 1280. **Signing itself is not yet
verified live**: the sandbox account used so far has no ACTIVE certificate, so the hash / sign /
attachment calls (which carry the token and base64 documents) have only been exercised against the
in-process fake server. The same MTU advice likely applies to them, unverified.

**Connect timeout (happy-eyeballs):** MISA publishes an AAAA record, so on IPv4-only hosts every
connection goes through Node's `autoSelectFamily`, whose default per-address attempt is 250 ms.
From outside Vietnam the TCP handshake alone takes ~210–250 ms (more on slow links), which made
connections fail with `ETIMEDOUT` at random. The built-in client allows 2 s per attempt
(`autoSelectFamilyAttemptTimeout: 2000`). If you pass your own `fetch`, set the same option, e.g.
undici `new Agent({ connect: { autoSelectFamilyAttemptTimeout: 2000, ecdhCurve: 'X25519:P-256:P-384' } })`,
or call `net.setDefaultAutoSelectFamilyAttemptTimeout(2000)` once at startup.

## Logging

Pass any object with `debug`, `info`, `warn` and `error` methods taking `(message, fields?)`, or
adapt a pino logger with `fromPino`:

```ts
import { createMisaESignClient, fromPino } from '@misaconnect/esign';
import type { MisaCredentials, PinoLike } from '@misaconnect/esign';

declare const baseUrl: string;
declare const creds: MisaCredentials;
declare const pinoLogger: PinoLike; // e.g. `pino()` from the pino package

const client = createMisaESignClient({ baseUrl, credentials: creds, logger: fromPino(pinoLogger) });
void client;
```

One started and one completed line per wire call (`endpoint`, `method`, `correlationId`,
`durationMs`, `outcome`, plus `errorCategory` / `rawCode` on failure). Credentials, tokens, document
content and signatures are never logged. An exception thrown by your logger is swallowed.

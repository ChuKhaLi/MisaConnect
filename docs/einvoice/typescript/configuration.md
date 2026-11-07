---
title: Configuration
parent: TypeScript
grand_parent: EInvoice
nav_order: 2
---

# Configuration
{: .no_toc }

1. TOC
{:toc}

`createMisaEInvoiceClient(options: MisaEInvoiceOptions)` validates the options and builds the
client. Invalid options throw a `MeInvoiceException` with category `Configuration` and
`rawErrorCode: 'InvalidOptions'`; every problem found is listed in `failures`.

## Options (`MisaEInvoiceOptions`)

| Option | Notes |
| --- | --- |
| `environment` | `'sandbox'` (default) or `'production'`. |
| `baseUrl`, `taxCode`, `userName`, `password`, `appId` | Required. `baseUrl` must be `https` with the host of the chosen environment (`testapi.meinvoice.vn` / `api.meinvoice.vn`). `taxCode` is the bare tax code ([Tax code and AppID](../index#tax-code-and-appid)). |
| `delete.includeRawErrorMessage` | Opt in to raw MISA error text (default `false`); see below. |
| `logger` | `{ debug, info, warn, error }(message, fields?)`. Default is a no-op. See [Logging](#logging). |
| `correlationId` | `() => string` producing the id logged per call (default `crypto.randomUUID`). Overridable per call, see [Cancellation and correlation](./operations#cancellation-and-correlation). |
| `fetch` | Custom `fetch`. Default: a built-in `node:https` client; see [Network / TLS](#network--tls). |
| `tokenCache`, `refIdGenerator`, `clock`, `validator`, `templateResolver` | Swappable collaborators (same ports as the .NET SDK); see [Swappable collaborators](#swappable-collaborators). |

<!-- snippet: partial -->
```ts
const client = createMisaEInvoiceClient({
  environment: 'production',
  baseUrl: 'https://api.meinvoice.vn/api/integration',
  taxCode: process.env.MISA_TAX_CODE!,
  userName: process.env.MISA_USERNAME!,
  password: process.env.MISA_PASSWORD!,
  appId: process.env.MISA_APP_ID!,
  delete: { includeRawErrorMessage: false },
  correlationId: () => requestContext.id,
});
```

### `delete.includeRawErrorMessage`

`delete.includeRawErrorMessage` gates exactly these: the `message` on `deleteDraft` outcomes and on
`lookupByRefId` per-RefId outcomes, the MISA-text excerpt in the `ARCH-DEL-001` warning, and
logging of an amendment's `changeReason` (with credential-looking JSON fragments such as
`"password":"..."` masked, as .NET `MeInvoiceLogScrubber` does). `saveDraft` and
`issueReplacement` / `issueAdjustment` results always carry MISA's `errorMessage` (as in .NET), and
`MeInvoiceException.message` is not gated.

### Default HTTP client and test interceptors

The default HTTP client calls `http.request` / `https.request` on every request, so an interceptor
installed after the client is created (MSW `setupServer`, nock) is honoured. It does not go
through `globalThis.fetch`, so `vi.stubGlobal('fetch', ...)` does not reach it: pass `fetch` in the
options instead.

## Swappable collaborators

The port types are exported: `TokenCache`, `AccessToken`, `Clock`, `RefIdGenerator`,
`InvoiceValidator`, `TemplateResolver` and `TemplateResolution`. So are the defaults
`defaultInvoiceValidator` and `resolveTemplate`, so you can wrap them instead of reimplementing
them.

| Option | Port | Default |
| --- | --- | --- |
| `tokenCache` | `TokenCache`: `get(key)`, `set(key, token)`, `delete(key)`, all async; `AccessToken` is `{ value: string; expiresAt: Date }` | In-memory, per client |
| `refIdGenerator` | `RefIdGenerator`: `() => string`, used when an invoice has no `refId` | UUID |
| `clock` | `Clock`: `{ now(): Date }` | System clock |
| `validator` | `InvoiceValidator`: `validate(invoice)`, `validateBatch(invoices)`, each returning `ValidationFailure[]` | `defaultInvoiceValidator` |
| `templateResolver` | `TemplateResolver`: `(activeTemplates, ref?) => TemplateResolution` | `resolveTemplate` |

`InvoiceValidator` has two methods, `validate` and `validateBatch`. `validateBatch` is part of the
port for parity with .NET; `saveDraft` itself validates each invoice with `validate` so that one bad
invoice does not fail the others.

Wrapping the default validator with a rule of your own:

```ts
import { defaultInvoiceValidator, type InvoiceValidator } from '@misaconnect/einvoice';

export const validator: InvoiceValidator = {
  validate(invoice) {
    const failures = defaultInvoiceValidator.validate(invoice);
    if (invoice.currency !== 'VND') {
      failures.push({ fieldPath: 'currency', message: 'Only VND invoices are allowed here.' });
    }
    return failures;
  },
  validateBatch: (invoices) => defaultInvoiceValidator.validateBatch(invoices),
};
```

### Token cache

<!-- snippet: partial -->
```ts
import type { TokenCache } from '@misaconnect/einvoice';

// Share tokens across processes (e.g. Redis). `expiresAt` is a `Date`: revive it when reading.
const tokenCache: TokenCache = {
  async get(key) {
    const raw = await redis.get(`misa:token:${key}`);
    if (raw === null) return undefined;
    const { value, expiresAt } = JSON.parse(raw) as { value: string; expiresAt: string };
    return { value, expiresAt: new Date(expiresAt) };
  },
  async set(key, token) {
    await redis.set(`misa:token:${key}`, JSON.stringify(token));
  },
  async delete(key) {
    await redis.del(`misa:token:${key}`);
  },
};
```

If a cache returns an ISO string or an epoch number instead of a `Date`, it is converted; an
unparseable value counts as expired, so a new token is acquired.

An error thrown (or a promise rejected) by your `TokenCache` propagates unchanged from every
operation, including `deleteDraft`, `lookupByRefId` and `saveDraft`, whose MISA and transport
failures otherwise become outcomes: it is not wrapped in a `MeInvoiceException`. Concurrent calls
waiting on the same token all get that same error, and the next call retries the cache.

## Logging

<!-- snippet: partial -->
```ts
import pino from 'pino';
import { createMisaEInvoiceClient, fromPino } from '@misaconnect/einvoice';

const client = createMisaEInvoiceClient({ /* ... */ logger: fromPino(pino()) });
```

Any object with `debug/info/warn/error(message, fields?)` works. An exception thrown by your logger
is swallowed and never changes a call's result. Tokens, credentials, tax-code headers, buyer data,
line content, amounts and the customer tax code are never logged.

Each call logs one completion line carrying its correlation id (from the `correlationId` option or
the per-call override). Failures are logged at `info` with `outcome: 'Error'` and `errorCategory`;
a caller abort with `outcome: 'Cancelled'` and no `errorCategory`.

## Network / TLS

Without a `fetch` option, the SDK uses a small `fetch`-compatible client over `node:https`. Its
keep-alive agents are shared by every client (idle sockets close after 4 s) and pin the TLS
key-exchange groups to the classical `X25519:P-256:P-384` (TLS 1.2 and 1.3 both allowed). Node >= 22
built with OpenSSL 3.5 (22.20+ / 24) otherwise also offers the post-quantum hybrid
`X25519MLKEM768`; the larger TLS ClientHello is reset by the network in front of MISA for traffic
from outside Vietnam, so every call failed with `TransportFailed` / `ConnectionFailed`
(`fetch failed`, `ECONNRESET`). No global setting (`tls.DEFAULT_ECDH_CURVE`, the global undici
dispatcher) is changed.

### Calling MISA from outside Vietnam

The large ClientHello is one case of a wider problem: from outside Vietnam the network path to MISA
is a path-MTU blackhole, so any request larger than about 1.4 KB (every authenticated call, since
the bearer token alone is about 2 KB) hangs and ends in `ECONNRESET`, for curl as much as for Node.
The SDK cannot work around it; lower the MTU of the interface that reaches MISA (e.g.
`ip link set dev eth0 mtu 1280` on Linux, which is what this repo's sandbox CI does) or enable TCP
MTU probing (`sysctl -w net.ipv4.tcp_mtu_probing=1`, not tested against MISA). The measurements and
symptoms are described under
[Calling MISA from outside Vietnam](../index#calling-misa-from-outside-vietnam).

### Connect timeout (happy-eyeballs)

MISA publishes an AAAA record, so on IPv4-only hosts every connection goes through Node's
`autoSelectFamily`, whose default per-address attempt is 250 ms. From outside Vietnam the TCP
handshake alone takes ~210–250 ms (more on slow links), which made connections fail with
`ETIMEDOUT` at random. The built-in client allows 2 s per attempt
(`autoSelectFamilyAttemptTimeout: 2000`). If you pass your own `fetch`, set the same option, e.g.
undici `new Agent({ connect: { autoSelectFamilyAttemptTimeout: 2000, ecdhCurve: 'X25519:P-256:P-384' } })`,
or call `net.setDefaultAutoSelectFamilyAttemptTimeout(2000)` once at startup.

### Proxies

The built-in client always connects directly. It ignores undici's
`setGlobalDispatcher(new ProxyAgent(...))` and `NODE_USE_ENV_PROXY` / `HTTP(S)_PROXY`, which the
global `fetch` used before 0.1.1 honoured. Behind a proxy, pass your own `fetch` as below.

### Your own `fetch`

If you pass your own `fetch` (for a proxy, custom CA, tracing...), it is used unchanged and the SDK
cannot set TLS options for it. On Node >= 22 outside Vietnam configure the groups yourself, for
example with undici:

<!-- snippet: partial -->
```ts
import { Agent, ProxyAgent, fetch as undiciFetch } from 'undici';

const tls = { ecdhCurve: 'X25519:P-256:P-384' };
const dispatcher = process.env.HTTPS_PROXY
  ? new ProxyAgent({ uri: process.env.HTTPS_PROXY, requestTls: tls }) // TLS to MISA through the tunnel
  : new Agent({ connect: { ...tls, autoSelectFamilyAttemptTimeout: 2000 } });
const client = createMisaEInvoiceClient({
  /* ... */
  fetch: ((input, init) => undiciFetch(input, { ...init, dispatcher })) as typeof fetch,
});
```

Alternatively set `tls.DEFAULT_ECDH_CURVE = 'X25519:P-256:P-384'` once at application startup; that
changes every TLS connection in the process, which is why the SDK does not do it for you.

The built-in client does not follow redirects or request compressed responses; MISA's API needs
neither.

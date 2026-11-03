# Changelog

All notable changes to `@misaconnect/einvoice` are documented here.

## [0.1.1] - 2026-11-03

### Fixed

- Every call failed with `TransportFailed` / `ConnectionFailed` (`fetch failed`, cause `ECONNRESET`) on Node >= 22 built with OpenSSL 3.5 (22.20+ / 24) when running outside Vietnam: Node's default TLS ClientHello offers the post-quantum hybrid `X25519MLKEM768` key share, and the resulting larger handshake is reset by the network in front of MISA. Without a `fetch` option the SDK now uses its own `node:https` client (keep-alive agents shared by all clients, idle sockets closed after 4 s) that offers only the classical groups `X25519:P-256:P-384`; TLS 1.2 and 1.3 remain allowed and no global TLS setting or dispatcher is changed.

### Changed

- The default HTTP client no longer goes through `globalThis.fetch`, so `vi.stubGlobal('fetch', ...)` no longer reaches it; it calls `http.request` / `https.request` per request, so MSW `setupServer` and nock interceptors still apply. A caller-supplied `fetch` is used unchanged: on Node >= 22 outside Vietnam configure its TLS groups yourself (e.g. an undici `Agent` with `connect: { ecdhCurve: 'X25519:P-256:P-384' }`); see the README section "Network / TLS".
- Proxies: the old default (`globalThis.fetch`) honoured undici's `setGlobalDispatcher(new ProxyAgent(...))` and `NODE_USE_ENV_PROXY` / `HTTP(S)_PROXY`; the new default client bypasses both and always connects directly. Behind a proxy, pass your own `fetch` (e.g. undici `fetch` with `new ProxyAgent({ uri, requestTls: { ecdhCurve: 'X25519:P-256:P-384' } })`).

## [0.1.0] - 2026-11-03

### Added

- Initial release: TypeScript port of `MisaConnect.EInvoice` with all ten operations — `listTemplates`, `preview`, `saveDraft`, `getDraftPdf`, `deleteDraft`, `lookupByRefId`, `lookupStandard`, `lookupCalculating`, `issueReplacement`, `issueAdjustment`.
- Token caching with single-flight acquisition, retry with backoff, `AbortSignal` support, structured per-call logging (`fromPino` adapter), and `MeInvoiceException` / `MeInvoiceErrorCategory` error model.
- `saveDraft` reports per-invoice results in input order, as .NET `SaveDraftInvoices`: an invalid invoice (validator failures, or values that cannot be mapped to the wire such as a malformed money string or an impossible date) becomes its own `Validation` / `ValidationFailed` result with `invoices[i].`-prefixed failures and is not sent; only an empty batch (`Validation`) or one of more than 30 invoices (`BatchOrSize` / `InvoiceQuantityTooLarge`) throws.
- Exported collaborator ports (`TokenCache`, `AccessToken`, `Clock`, `RefIdGenerator`, `InvoiceValidator`, `TemplateResolver`, `TemplateResolution`), the defaults `defaultInvoiceValidator` and `resolveTemplate`, and the `isMeInvoiceException` type guard (safe across the ESM/CJS dual-package hazard).
- The default `fetch` is looked up on every call, so mocks installed after client creation apply. A token cache returning a string or number `expiresAt` is accepted.
- A throwing logger never changes a call's result; a caller abort is logged as `outcome: 'Cancelled'`.
- `createMisaEInvoiceClient` always pins `baseUrl` to https and the MISA host of the chosen environment; no option key can disable it.
- Untyped-JS `null` / missing inputs throw or report `Validation` / `ValidationFailed` instead of a `TypeError`; a `null` optional field counts as absent.
- Caller-input `Validation` errors use `rawErrorCode: 'ValidationFailed'` throughout (preview, paged lookups, empty batch), with rule ids in `failures[].ruleId`. `InvalidMoney` / `InvalidDate` / `InvalidNumber` / `InvalidVatRate` are raised directly by every operation except `saveDraft`, which wraps them into the invoice's result. `validateBatch` maps an invoice-root failure to `invoices[i]`.
- A non-finite number in a non-money field fails only that invoice in `saveDraft`.
- Numbers are written as plain decimal text (no exponent). Money must fit the .NET `decimal` range (|x| ≤ `decimal.MaxValue`, ≤ 28 fractional digits): a string beyond it, or a number above `decimal.MaxValue`, is rejected with `InvalidMoney`; a `number` with more than 28 fractional digits (a float residue such as `0.1 + 0.2 - 0.3`) is rounded half-to-even to 28, as .NET's decimal parser rounds that JSON literal, and the validator's discount check uses the same rounded value. `bigint` values are rejected with `InvalidNumber`.
- `changeReason` is logged only with `delete.includeRawErrorMessage`, with credential-looking fragments masked. Extra log fields must be primitives and never override the fixed log fields.
- The `ARCH-DEL-001` warning carries the correlation id.
- Token acquisition is single-flight including the cache read; a failing `tokenCache.get` rejects all joined waiters with the cache's own error, is not cached, and the next call retries; a waiter's abort rejects only that waiter.
- `listTemplates` skips `null` / non-object rows and rows without a string `IPTemplateID`; a row with a null `InvSeries` is kept with a blank series (as .NET).
- Clearer messages for the `KHAC` VAT rate and for malformed date-times.
- ESM + CJS builds with type declarations; Node >= 20.

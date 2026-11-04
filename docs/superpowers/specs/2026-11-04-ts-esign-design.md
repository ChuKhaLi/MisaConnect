# `@misaconnect/esign` — TypeScript port of MisaConnect.ESign — Design

Date: 2026-11-04
Status: Owner asked for a full port of `MisaConnect.ESign` ("port toàn bộ MisaConnect.ESign sang TypeScript") and skipped the design Q&A. Every decision below that the owner did not state is marked *(main agent's call)*.
Process: superpowers design → plan → implementation (constitution v1.2.0, Principle V).
Reference: [2026-11-04-esign-dotnet-inventory.md](2026-11-04-esign-dotnet-inventory.md) (the .NET behaviour, with file pointers). Sibling design: [2026-11-02-typescript-sdk-design.md](2026-11-02-typescript-sdk-design.md) (`@misaconnect/einvoice`); everything it decided about tooling, naming, layout, logging shape and release applies here unless this document says otherwise.

## 1. Intent and why

Same audience as `@misaconnect/einvoice`: Node.js backends (plain Node, Express, NestJS, Fastify…) that need MISA eSign RemoteSigning. The .NET `MisaConnect.ESign` is the behavioural source of truth. The package must feel like the einvoice one, so a team using both sees one style.

## 2. Goals

- npm package `@misaconnect/esign` in `src/typescript/esign/`, zero runtime dependencies, Node ≥ 20, ESM + CJS + `.d.ts`.
- **All of the .NET facade**: `signPdf`, `signXml`, `signWord`, `signExcel`, `signInWithOtp`, `resendOtp`, `beginSignPdf`, `beginSignXml`, `beginSignWord`, `beginSignExcel`, `handleWebhook`.
- Behavioural parity with .NET on: routing (origin + `authUnderWebdev`), headers, token acquisition / refresh / single-flight / 401 retry, certificate listing and selection, per-format hash and attachment wire shapes, polling, 2FA (transparent and explicit), OTP resend, webhook session store and finalize, error categories, error-code synthesis, retry, logging fields.
- **Except** the .NET defects in §9, which the port does not reproduce.
- Same naming rules as einvoice: methods camelCase without `Async`; type names kept (`SignPdfRequestDto`, `ESignErrorCategory`, `AuthenticationFailedException`…); properties camelCase with acronyms preserved; wire names verbatim (PascalCase `DocumentId`, MISA's `certiticateChain` typo, camelCase `signaturePosInfos` inner keys, …).

## 3. Non-goals

- No browser support.
- No framework adapters; the webhook HTTP endpoint (path secret, IP allowlist) is the host's job, shown in a sample, as in .NET.
- No new MISA features beyond the .NET facade (e.g. no public "list certificates" call; `CertificateDto` stays unexported as in .NET where nothing returns it).
- No .NET changes in this work; .NET defects found here are listed in §9 for a later Spec Kit slice.

## 4. Anti-goals

- Logging tokens, client keys, passwords, OTP codes, certificates, document bytes or digests.
- Creating two MISA signing transactions for one call (see §9.3).
- Wire shapes that differ from what the .NET SDK sends today (it is the only shape proven against MISA).
- Unit tests that touch the network.

## 5. Public surface

### 5.1 Entry point

```ts
const client = createMisaESignClient({
  environment: 'sandbox' | 'production',      // default 'sandbox'
  baseUrl: 'https://…',                        // required; origin is used
  authUnderWebdev?: boolean,                   // default: environment === 'sandbox'
  credentials: MisaCredentials | MisaCredentialsProvider,   // §6
  polling?: { intervalMs?: 2000, totalTimeoutMs?: 60000 },
  transportRetry?: { maxAttempts?: 3, baseDelayMs?: 200, maxDelayMs?: 2000 },
  errors?: { includeRawErrorMessage?: false },
  otp?: { defaultResendLanguage?: 'en-US' },
  webhook?: { mode?: 'polling' | 'webhook' | 'both' /* default 'both' */, sessionTtlMs?: 86_400_000 },
  // swappable collaborators (the .NET ports):
  tokenCache?, tokenCacheKey?, certificateSelector?, otpProvider?,
  signingSessionStore?, webhookDeliveryHook?, clock?, delay?,
  correlationId?, logger?, fetch?,
});
```

- Options are validated at creation with the .NET rules (inventory §1.2); all problems reported in one `ESignGeneralException` of category `Validation` *(main agent's call: .NET throws `OptionsValidationException`, which TS lacks; einvoice uses a `Configuration` category that eSign does not have)*.
- Durations are milliseconds (`…Ms`), matching einvoice's `number` idiom.
- `webhook.path`, `secret`, `allowedIps` are not SDK options (they are sample-host settings in .NET); the sample shows them.

### 5.2 Methods

| .NET | TS |
|---|---|
| `SignPdfAsync(request)` | `signPdf(request, opts?)` → `SignPdfResultDto` |
| `SignXmlAsync(request)` | `signXml(request, opts?)`; request has `xml?: string` or `xmlUtf8Bytes?: Uint8Array` (exactly one); helpers `SignXmlRequest.fromString(…)` / `fromUtf8Bytes(…)` |
| `SignWordAsync`, `SignExcelAsync` | `signWord`, `signExcel` |
| `SignInWithOtpAsync(code, otpType, remember)` | `signInWithOtp(code, otpType, remember, opts?)` |
| `ResendOtpAsync(language?)` | `resendOtp(language?, opts?)` → `OtpResendResultDto` |
| `BeginSign{Pdf,Xml,Word,Excel}Async` | `beginSign{Pdf,Xml,Word,Excel}(request, opts?)` → `BeginResultDto` |
| `HandleWebhookAsync(envelope)` | `handleWebhook(envelope: unknown, opts?)` → `WebhookHandleResultDto` |

`opts = { signal?: AbortSignal; correlationId?: string; credentials?: MisaCredentials }` (§6).

`handleWebhook` takes `unknown` (the parsed JSON body) and validates it itself, so a malformed body becomes a `webhook.malformed` ACK, never a thrown error (§9.6).

### 5.3 Types

- Request/result DTOs as in .NET (inventory §1.3) with `Uint8Array` for bytes and ISO-8601 strings for `DateTimeOffset` (`completedAtUtc`).
- Enums as string-literal unions plus a frozen const object (einvoice idiom): `DocumentFormat`, `OtpDeliveryChannel` (wire values 0/1 stay numeric on the wire), `ESignErrorCategory`, `WebhookStatusDto`, `WebhookMode`.
- `WebhookOutcomeDto` = discriminated union on `kind: 'SuccessWithSignedBytes' | 'FailureWithError' | 'TerminalWithoutFinalize'`.
- Errors: one class per .NET exception, same names and extra fields, all extending `ESignException extends Error` with `category`, `rawCode`, `detail`, `correlationId`, `format`; `isESignException(e)` guard (dual-package safe, as in einvoice). `ESignTransportException.rawCode` keeps .NET's `HttpStatusCode` enum names ("ServiceUnavailable", "TooManyRequests", …) via a status→name table, or "Transport".

## 6. Credentials (the TS replacement for `CredentialsMode` + `IMisaCredentialsAccessor`) *(main agent's call)*

.NET reads per-user credentials from an `AsyncLocal` inside a singleton accessor. TS idiom is explicit:

- `credentials` option: either a fixed `MisaCredentials { clientId, clientKey, userName, password }` (Static), or a provider `(ctx: { clientId?: string; userName?: string; signal?: AbortSignal }) => MisaCredentials | Promise<MisaCredentials>` (Dynamic).
- Per call, `opts.credentials` overrides both. Resolution order: `opts.credentials` → provider/static.
- Resolved once per call and used for login, `x-clientId`/`x-clientKey`, and the token-cache key `"{userName}|{clientId}|{host}"`.
- **Webhook finalize** has no caller context. `beginSign*` stores the call's `clientId` and `userName` in the session (never the key or password); `handleWebhook` matches the envelope `clientId` against the session's stored clientId and, to finalize, calls the provider with `{ clientId, userName }`. A static `credentials` value is used as is. This closes the .NET slice-008 gap (FR-015) where webhook mode only worked with static options.
- `MisaCredentials` objects are never logged; the README says to keep them out of logs.

## 7. Behaviour to port as is

Everything in the inventory §2–§7, in particular:

- Routing table and `authUnderWebdev` rule (inventory §2.2); `encodeURIComponent` for the transaction id.
- Headers: `x-clientId`, `x-clientKey`, `X-Correlation-Id` on every request; `AuthorizationRM: Bearer {remoteSigningAccessToken}` on ESRM calls only; no `Authorization` header.
- Token: cache by key; refresh-then-login on expiry; login `user.id` is the `UserId` sent to `Signing/hash`; single-flight per key for refresh and OTP exchange.
- Sign flow, polling rules, terminal-status mapping, `CANCELED` alias.
- 2FA: code "122" detection; transparent mode with one retry of the whole operation when an `otpProvider` is set; explicit mode via `signInWithOtp` / `resendOtp` with the pending username **held on the client instance** (TS has no DI scope; one client object plays the role of one .NET scope). `opts.credentials`/provider still decide which user signs in.
- Webhook: session register/lookup/TTL (lazy expiry), observed message ids, cached success, finalize lock per (clientId, tx), ACK codes and messages, `TerminalWithoutFinalize` for FAILED/CANCELLED, hook not invoked on finalize failure (as .NET; the .NET doc comment that says otherwise is wrong).
- Error mapping tables and code synthesis verbatim (inventory §5.1–§5.3), including Vietnamese keywords; detail text format; raw-message opt-in.
- Retry: full-jitter backoff, `Retry-After` clamp, attempt counting — with the §9.3 exception.
- Validation rules per format (inventory §7), run before any HTTP call.
- Logging: one started + one completed line per wire call with `endpoint` (short label), `method`, `correlationId`, `durationMs`, `outcome`, plus `errorCategory`/`rawCode` on failure. Logger option and `fromPino` exactly as einvoice.
- HTTP: reuse einvoice's default `node:https` client with classical ECDH curves and shared keep-alive agents *(main agent's call: copy the module into the esign package — no shared internal package exists, and a third package for ~200 lines is not worth a new publish pipeline; note the duplication in both files)*.

## 8. Ports (options) and defaults

| Option | Default | Notes |
|---|---|---|
| `tokenCache` | in-memory map | `{ get(key), set(key, token), delete(key) }`, sync or async |
| `tokenCacheKey` | `"{userName}|{clientId}|{host}"` | `(creds, baseUrl) => string` |
| `certificateSelector` | first active | `(certs) => cert`, may be async |
| `otpProvider` | none | `{ provide(challenge): Promise<OtpSubmission> }` |
| `signingSessionStore` | in-memory with lazy TTL and per-session lock | interface mirrors `ISigningSessionStore` + `IFinalizeLockOwner`; README warns multi-instance hosts must supply a shared store |
| `webhookDeliveryHook` | no-op | `(outcome: WebhookOutcomeDto) => void | Promise<void>` |
| `clock`, `delay`, `correlationId`, `logger`, `fetch` | as einvoice | `delay(ms, signal)` replaces `IDelayer` |

## 9. .NET defects — do not reproduce (fix later in .NET)

1. **Stale token after a 401 refresh.** .NET retries the one request with the new token but the use case keeps the old one, so every later call in the flow 401s and refreshes again. TS: the transport owns the current token; later calls use the refreshed one.
2. **No login fallback when refresh fails on a 401.** MISA's doc says re-login. TS: on refresh failure, evict and log in again once; only then fail with `Authentication`.
3. **Retrying `POST Signing/hash`.** .NET retries it on 5xx and timeouts, which can start a second signing transaction (a second OTP/approval on the signer's phone). TS: `Signing/hash` is retried only when the request provably did not reach MISA (connection refused/reset before any response, DNS failure) or on 429; 5xx and timeouts surface immediately as `ESignTransportException`. Every other call keeps .NET's retry rules. *(main agent's call)*
4. **Content-type guard on certificates only.** TS applies the same guard (`UnexpectedContentType` / `UnparseableResponse`) to every ESRM JSON response.
5. **`ResponseError.error` typed bool.** TS accepts `boolean | string | null` (`"true"`/`"false"` strings parsed), so a string never drops the envelope.
6. **Webhook malformed input escapes as an exception.** TS validates `messageId`, `clientId`, `transactionId`, `status` (case-insensitive) and `signatures` shape and answers `webhook.malformed` with a `FailureWithError` outcome.
7. **2FA "122" missed on non-2xx login bodies.** TS checks both the nested `{status:{…}}` and the flat shape on any status.
8. **Proactive refresh skew declared but unused.** TS refreshes when `expiresAt - 30 s <= now` (MISA tokens live 60 min). *(main agent's call)*
9. **Dynamic credentials unusable in webhook mode** (slice 008 FR-015). Fixed by §6.

Docs-only: `docs/esign/getting-started.md` imports an internal namespace, passes `LogoImageBase64: null` (fails validation) and claims `SignXmlAsync` overloads; `configuration.md` says https-only and "register before or after"; `sandbox-setup.md` claims a reachability probe. List these in the .NET follow-up.

## 10. Network

The path-MTU blackhole measured for `testapi.meinvoice.vn` (requests over ~1.4 KB from outside Vietnam hang) almost certainly affects eSign too: every ESRM call carries a ~2 KB `AuthorizationRM` token and hash/attachment bodies carry base64 documents. Not yet measured for the eSign hosts. README gets the same "Calling MISA from outside Vietnam" section, marked as unverified for eSign until measured; any eSign sandbox CI job lowers MTU to 1280 like `ts-sandbox.yml`.

## 11. Tests

- Unit (vitest, no network, < 30 s): mappers, synthesis tables, validators, wire builders per format (port the .NET wire-shape tests in `tests/dotnet/MisaConnect.ESign.UnitTests/Signing/Wire/`), token cache/refresh/single-flight, polling with fake clock/delay, webhook state machine.
- Integration: an in-process fake MISA eSign server (port of `FakeMisaESignServer.cs`, node:http) covering every scenario it has, run against the real client.
- Drift guards like einvoice: parse the .NET route constants and error-mapper tables and assert the TS copies match.
- Sandbox: `MISACONNECT_ESIGN_SANDBOX_*` env vars (same names as .NET), skip unless all set; `MISACONNECT_ESIGN_SANDBOX_REQUIRED=1` turns a skip into a failure (as einvoice). **The `misa-sandbox` GitHub environment has no eSign secrets today**, so live verification needs the owner (§13).

## 12. Release

- Version `0.1.0`; tag `esign-ts-v*`; generalize `release-ts.yml` to both packages (derive the package from the tag prefix) rather than copying it.
- `ts-ci.yml` already has an `esign` job waiting for `src/typescript/esign/package.json`; extend it to build + lint + Node 20 smoke like einvoice.
- npm trusted publishing is configured per package: the first `0.1.0` publish (or a manual trusted-publisher setup for `@misaconnect/esign` on npmjs.com) needs the owner, as it did for einvoice.
- Root `README.md` supported-operations table, package `README.md` (incl. name table, differences from .NET = §9, network section), `src/typescript/esign/CHANGELOG.md`.
- Samples: extend `samples/typescript/express` with a sign endpoint and a webhook endpoint (secret path + IP allowlist, as the .NET sample) *(main agent's call; NestJS sample unchanged)*.

## 13. Needs the owner (does not block building against the fake server)

1. eSign sandbox credentials (`MISACONNECT_ESIGN_SANDBOX_BASE_URL`, `_CLIENT_ID`, `_CLIENT_KEY`, `_USERNAME`, `_PASSWORD`) — locally for a live run, and as GitHub secrets for CI. A 2FA-enabled account and a reachable webhook URL are optional extras.
2. npm: allow `@misaconnect/esign` publishing (trusted publisher entry, or a first manual publish).

## 14. Left to the builder

Module split (expected: `src/domain`, `src/application` (use cases, mappers, validators), `src/infrastructure` (transport, wire builders, fake-able HTTP, stores), `src/client.ts`, `src/index.ts`); how much per-format code is shared (a format table is encouraged — the .NET ×4 duplication is not a goal); fake-server structure; exact README wording.

## 15. Order of work

1. Package scaffold + CI wiring (build/lint/test green with an empty client).
2. Domain types, errors, options validation.
3. Transport: routing, headers, retry (incl. §9.3), content-type guard, logging; token use cases (login, refresh, single-flight, 401 path incl. §9.1/§9.2, skew).
4. Certificates + PDF sign flow end to end against the fake server.
5. XML, Word, Excel (format table).
6. 2FA transparent + explicit, OTP resend.
7. Webhook: begin, session store, handleWebhook, finalize, hook.
8. Drift guards, sandbox suite, README/CHANGELOG/samples, release workflow generalization.
9. Publish `0.1.0` once the owner provides npm access (live sandbox verification when eSign credentials exist).

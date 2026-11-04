# `@misaconnect/esign` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `@misaconnect/esign`, a TypeScript port of the full `MisaConnect.ESign` facade (polling sign ×4 formats, 2FA, OTP resend, webhook begin/handle/finalize), tested against an in-process fake MISA server.

**Architecture:** Same layering and tooling as `src/typescript/einvoice`: `src/domain` (types, errors), `src/application` (use cases, mappers, validators, ports), `src/infrastructure` (options, transport, token manager, wire builders, stores, logging), `src/client.ts` facade, `src/index.ts` public exports. One generic sign pipeline driven by a per-format table instead of .NET's ×4 duplication.

**Tech Stack:** TypeScript strict, Node ≥ 20, tsup (ESM+CJS+d.ts), vitest, ESLint + Prettier, zero runtime dependencies.

**Spec:** `docs/superpowers/specs/2026-11-04-ts-esign-design.md`. Behaviour reference (tables, wire shapes, messages to copy verbatim): `docs/superpowers/specs/2026-11-04-esign-dotnet-inventory.md` — cited below as "INV §n". The .NET source it points to is authoritative where the inventory is terse.

## Global Constraints

- Package name `@misaconnect/esign`, version `0.1.0`, dir `src/typescript/esign/`, `"engines": {"node": ">=20"}`, `"type": "module"`, zero `dependencies`.
- Copy einvoice's `package.json` shape (exports map, files, scripts `build`/`test`/`test:sandbox`/`typecheck`/`smoke`, publishConfig provenance), `tsconfig.json`, `tsup.config.ts`, `vitest.config.ts`, `vitest.sandbox.config.ts`, `scripts/smoke.{mjs,cjs}`.
- Naming: methods camelCase without `Async`; .NET type names kept; properties camelCase with acronyms preserved; wire names verbatim (INV §7, incl. `certiticateChain`).
- Enums: string-literal union + frozen const object of the same name (einvoice `MeInvoiceErrorCategory` idiom).
- Bytes are `Uint8Array`; base64 via `Buffer.from(bytes).toString('base64')` / `new Uint8Array(Buffer.from(s, 'base64'))`. Durations are `…Ms` numbers. Timestamps out are ISO-8601 strings.
- `opts` on every method: `{ signal?: AbortSignal; correlationId?: string; credentials?: MisaCredentials }`.
- Never log: tokens, client key, password, OTP code, certificates, document bytes, digests, `MisaCredentials` objects.
- Unit tests no network, whole unit suite < 30 s. Lint and format must pass (`npm run lint`, `npm run format:check` at repo root).
- Prettier on changed files only; do not reformat einvoice golden files.

## Review Focus

1. Caller aborts during polling → the pending delay rejects with the signal's reason within one tick, no further MISA request is sent (Task 9 test `abort during polling stops without another status call`).
2. Ten concurrent `signPdf` calls for one user on a cold cache → exactly one login request (Task 6 test `single flight login`).
3. The same SUCCESS webhook delivered twice concurrently → exactly one attachment request, both answers are ACK "0", the hook runs once (Task 11 test `concurrent duplicate webhook finalizes once`).
4. XML containing Vietnamese text and a UTF-8 BOM-less byte input → `FileToSign` is the exact decoded text; `signedXml` bytes decode back to MISA's text unchanged (Task 7 test `xml utf8 round trip keeps Vietnamese text`).
5. A 5 MB PDF → base64 encode/decode works without stack overflow and the request body is sent intact (Task 7 test `large pdf base64 round trip`).

---

### Task 1: Package scaffold and CI wiring

**Files:**
- Create: `src/typescript/esign/{package.json,tsconfig.json,tsup.config.ts,vitest.config.ts,vitest.sandbox.config.ts,README.md,CHANGELOG.md}`, `src/typescript/esign/scripts/{smoke.mjs,smoke.cjs}`, `src/typescript/esign/src/index.ts`, `src/typescript/esign/test/unit/package.test.ts`
- Modify: `.github/workflows/ts-ci.yml` (esign job: drop the "exists" guard, add build, lint is root-wide already, add a `esign-node20` smoke job mirroring `einvoice-node20`), `package-lock.json` (via `npm install` at root)

**Interfaces:**
- Produces: workspace `@misaconnect/esign` that builds and tests; `src/index.ts` exporting `const VERSION = '0.1.0'` (removed again in Task 12 if unused — keep it, the smoke scripts import it).

- [ ] **Step 1: Write `test/unit/package.test.ts`**: `expect(VERSION).toBe(JSON.parse(readFileSync('package.json')).version)`.
- [ ] **Step 2:** `npm install` at repo root; run `npm test -w @misaconnect/esign` → PASS (1 test). `npm run lint`, `npm run typecheck`, `npm run format:check` → clean. `npm run smoke -w @misaconnect/esign` prints the version from ESM and CJS.
- [ ] **Step 3:** Update `ts-ci.yml` esign job to the einvoice job's steps (`npm ci`, build, typecheck, test) and add `esign-node20` smoke job. Validate YAML with `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ts-ci.yml'))"`.
- [ ] **Step 4: Commit** `chore(ts-esign): scaffold @misaconnect/esign package and CI job`.

### Task 2: Domain — enums, DTOs, errors

**Files:**
- Create: `src/domain/enums.ts`, `src/domain/dtos.ts`, `src/domain/errors.ts`, `src/domain/http-status-names.ts`
- Test: `test/unit/domain.test.ts`

**Interfaces:**
- Produces (exported from `index.ts`):
  - `DocumentFormat = 'Unknown'|'Pdf'|'Xml'|'Word'|'Excel'`; `OtpDeliveryChannel = 'SmsOrEmail'|'Authenticator'` with `otpChannelWireValue(c): 0|1`; `ESignErrorCategory` (16 members, INV §1.4, same order); `WebhookMode = 'polling'|'webhook'|'both'`; `WebhookStatusDto = 'Success'|'Failed'|'Cancelled'`; `SignStatus = 'PENDING'|'SUCCESS'|'FAILED'|'CANCELLED'|'UNKNOWN'`; `WebhookValidationCategory` (INV §1.4).
  - DTO interfaces per INV §1.3, camelCase: `SignPdfRequestDto { pdf: Uint8Array; documentName; signerName; location; reason; contact; logoImageBase64; dataToBeDisplayed; page?; positionX?; positionY?; width?; height?; renderingMode?; documentId?; displayText?; showSignedDate?: boolean /* default true */; additionalSignaturePositions?: SignaturePosInfoDto[] }`, same for `SignWordRequestDto` (`word`) and `SignExcelRequestDto` (`excel`); `SignXmlRequestDto { xml?: string; xmlUtf8Bytes?: Uint8Array; signatureContext: XmlSignatureContextDto; documentName; dataToBeDisplayed; documentId? }`; `SignXmlRequest.fromString(xml, rest)` / `fromUtf8Bytes(bytes, rest)`; results `Sign{Pdf,Xml,Word,Excel}ResultDto { signed{Pdf|Xml|Word|Excel}: Uint8Array; transactionId; certificateKeyAlias; completedAtUtc: string; format }`; `OtpResendResultDto`; `BeginResultDto`; `WebhookAckDto { errorCode; devMsg; userMsg }`; `WebhookOutcomeDto` union on `kind`; `WebhookHandleResultDto { ack; outcome }`; `MisaCredentials { clientId; clientKey; userName; password }`; internal `Certificate` (INV §2.4 fields + `chain: [string,string,string]`).
  - Errors: `class ESignException extends Error { category; rawCode?; detail; correlationId; format: DocumentFormat }` (constructor throws `TypeError` on empty correlationId, mirroring .NET's `ArgumentException`), and subclasses with the .NET names and extra fields from INV §1.4: `ESignGeneralException`, `AuthenticationFailedException { requires2FA; username? }`, `InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`, `OtpRejectedException`, `NoActiveCertificateException`, `SignRejectedException { requiresUserCertSetup }`, `SignTerminalStateException { terminalStatus; transactionId }`, `SignTimeoutException { transactionId; elapsedMs }`, `ESignTransportException { lastStatusCode?; attemptCount }`, `WebhookValidationException { webhookCategory; matchedTransactionId? }` + 5 subclasses. `isESignException(e)` checks a `Symbol.for('misaconnect.esign.ESignException')` brand (dual-package safe, as einvoice).
  - `httpStatusName(status: number): string` — .NET `HttpStatusCode` enum names for every status 100–511 that .NET defines (e.g. 429 → "TooManyRequests", 503 → "ServiceUnavailable"); unknown → `String(status)`.

- [ ] **Step 1: Tests**: each subclass sets `category` as .NET (`SignTerminalStateException` FAILED→SignTerminalFailed, CANCELLED→SignTerminalCancelled, else SignTerminalUnknown); default rawCodes ("InvalidOtp", "ExpiredOtp", "ExhaustedOtpAttempts", "OtpRejected", "NoActiveCertificate", "SignTimeout", transport "Transport" when no status / `httpStatusName(503)` = "ServiceUnavailable"); empty correlationId throws; `isESignException` true for every subclass and for a structurally branded object from another copy; `name` property equals the class name; `ESignErrorCategory` keys equal the 16 .NET members in order.
- [ ] **Step 2:** Run `npx vitest run test/unit/domain.test.ts` → FAIL; implement; → PASS.
- [ ] **Step 3: Commit** `feat(ts-esign): domain enums, DTOs and exception hierarchy`.

### Task 3: Options and credentials resolution

**Files:**
- Create: `src/infrastructure/options.ts`, `src/application/ports.ts`
- Test: `test/unit/options.test.ts`

**Interfaces:**
- Produces:
  - `ports.ts`: `Logger` (same shape as einvoice), `AccessToken { value: string /*remoteSigningAccessToken*/; refreshToken?: string; expiresAt: Date; userId: string; username: string }`, `TokenCache { get(key): AccessToken|undefined|Promise<…>; set(key, t): void|Promise<void>; delete(key): void|Promise<void> }`, `Clock { now(): Date }`, `Delay = (ms: number, signal?: AbortSignal) => Promise<void>`, `CertificateSelector = (certs: readonly Certificate[]) => Certificate | Promise<Certificate>`, `OtpProvider { provide(challenge: { userName: string; correlationId: string }): Promise<{ code: string; otpType: OtpDeliveryChannel; remember: boolean }> }`, `MisaCredentialsProvider = (ctx: { clientId?: string; userName?: string; signal?: AbortSignal }) => MisaCredentials | Promise<MisaCredentials>`, `SigningSessionStore` (Task 11 defines members; declare the interface here with the members listed in Task 11), `WebhookDeliveryHook = (o: WebhookOutcomeDto) => void | Promise<void>`.
  - `options.ts`: `interface MisaESignOptions` exactly as design §5.1; `interface ResolvedESignOptions` with every default filled, `origin: string` (scheme://host[:port]/), `host: string`, `authUnderWebdev: boolean`; `validateOptions(o: MisaESignOptions): ResolvedESignOptions`; `resolveCredentials(resolved, ctx, override?): Promise<MisaCredentials>`.
- Rules (INV §1.2): `baseUrl` required, absolute, `https:` or `http:` only for `localhost`/`127.0.0.1`/`::1`; production ⇔ host `esignapp.misa.vn`; `intervalMs > 0`, `totalTimeoutMs > intervalMs`, `maxAttempts ≥ 1`, `maxDelayMs ≥ baseDelayMs`, `defaultResendLanguage` non-empty, `sessionTtlMs > 0`; static credentials need all four non-blank; provider function accepted as is. All failures collected, thrown as one `ESignGeneralException(category 'Validation', rawCode 'InvalidOptions', detail = failures joined by '; ', correlationId 'options')`.

- [ ] **Step 1: Tests**: defaults (2000/60000/3/200/2000/'en-US'/'both'/86_400_000, `authUnderWebdev` true for sandbox and false for production unless set); origin drops path `https://h.vn/webdev/x` → `https://h.vn/`; each rule's failure; two failures reported together; `resolveCredentials` prefers override, then provider (called with ctx), then static; provider returning blank fields → `Validation` error "InvalidCredentials".
- [ ] **Step 2:** FAIL → implement → PASS.
- [ ] **Step 3: Commit** `feat(ts-esign): options validation and credentials resolution`.

### Task 4: Routes, response envelope, error mappers

**Files:**
- Create: `src/infrastructure/routes.ts`, `src/infrastructure/wire/response-error.ts`, `src/application/errors/esign-error-mapper.ts`, `src/application/errors/otp-error-mapper.ts`
- Test: `test/unit/routes.test.ts`, `test/unit/error-mappers.test.ts`

**Interfaces:**
- Produces:
  - `Routes` const with the 9 canonical paths of INV §2.2 (`login`, `twoFactor`, `refresh`, `resendOtp`, `certificates`, `hash`, `signHash`, `signStatus`, `attachment`); `resolveUrl(resolved, route, suffix?): string` applying the origin and the `webdev/` rule (login + twoFactor only); `endpointLabel(route): string` short labels for logs ("login-api", "two-factor-auth", "refreshtoken", "resend-otp-auth", "Certificates/by-userId", "documents/hash", "Signing/hash", "Signing/status", "documents/attachment").
  - `parseResponseError(json: unknown): ResponseError | undefined` with `ResponseError { error?: boolean; errorCode?: string; devMsg?: string; userMsg?: string; validationFailures?: {propertyName?: string; errorMessage?: string}[] }`; accepts flat `{error, errorCode, devMsg, userMsg}` and nested `{status:{…}}`; `error` may be boolean, `"true"`/`"false"` string or null (design §9.5); `errorCode` string/number/bool → string.
  - `mapESignError({ route, status, envelope, format, correlationId, includeRaw }): ESignException` (INV §5.1, synthesis tables verbatim, `RequiresUserCertSetup` keywords, detail text `MISA returned errorCode=${raw ?? '<none>'} on ${route}.` + raw suffix when `includeRaw`).
  - `mapTwoFactorError({ status, envelope, correlationId, userName }): ESignException` (INV §5.2).
  - `isTwoFactorRequired(envelope): boolean` — errorCode "122" (case-insensitive) in either shape (design §9.7).

- [ ] **Step 1: Tests (routes)**: sandbox login → `https://h.vn/webdev/api/auth/api/v1/auth/login-api`; production login → root; `authUnderWebdev:false` on sandbox → root; refresh/resend always one `webdev/`; ESRM at root even when `baseUrl` has `/webdev/`; status suffix `encodeURIComponent('a/b c')`.
- [ ] **Step 2: Tests (mappers)**: one `it.each` row per synthesis rule in INV §5.1 (both tables, incl. "không hợp lệ" and format-gated rules), Signing/hash `RequiresUserCertSetup` true for devMsg "Account not connected" and userMsg "chưa thiết lập", 401 → Authentication, 429/503 → Transport, login "122" → `requires2FA`, OTP codes 1001/1002/1003, each keyword list entry, unknown → `OtpRejectedException`; detail text with and without `includeRaw` (validationFailures rendered `[Prop] reason; `); `parseResponseError({error:"true", errorCode: 122})` → `{error:true, errorCode:"122"}`.
- [ ] **Step 3:** FAIL → implement → PASS.
- [ ] **Step 4: Commit** `feat(ts-esign): routes, response envelope and error mappers`.

### Task 5: Transport (HTTP, headers, retry, content type, logging)

**Files:**
- Create: `src/infrastructure/node-https-fetch.ts` (copy of einvoice's file; header comment "Duplicated from @misaconnect/einvoice — keep in sync"; add the reverse note to the einvoice copy), `src/infrastructure/logging.ts` (einvoice's `noopLogger`, `safeLogger`, `fromPino`; plus `scrubText(text)` = INV §6 scrubber rules), `src/infrastructure/transport.ts`
- Modify: `src/typescript/einvoice/src/infrastructure/node-https-fetch.ts` (comment only)
- Test: `test/unit/transport.test.ts`, `test/unit/logging.test.ts`

**Interfaces:**
- Consumes: Task 3 `ResolvedESignOptions`, `MisaCredentials`; Task 4 `Routes`, `resolveUrl`, `endpointLabel`.
- Produces: `createTransport(resolved, deps?: { random?: () => number }): Transport` with
  `send(req: { route: RouteKey; method: 'GET'|'POST'; suffix?: string; body?: unknown; credentials: MisaCredentials; bearer?: string; correlationId: string; signal?: AbortSignal; expectJson?: boolean }): Promise<{ status: number; json: unknown; text: string; contentType: string }>`.
  - Headers: `x-clientId`, `x-clientKey`, `X-Correlation-Id`, `Content-Type: application/json` when body, `AuthorizationRM: Bearer ${bearer}` when bearer. Body JSON via `JSON.stringify` (callers omit undefined fields).
  - Retry (INV §5.5): attempts ≤ `maxAttempts`; transient = 429, ≥ 500, network error, per-attempt timeout (100 s) not caused by caller; delay full jitter `random() * min(maxDelayMs, baseDelayMs * 2^(n-1))`, 429 `Retry-After` seconds or HTTP date clamped to `maxDelayMs`; exhausted → `ESignTransportException(lastStatus, attempts)`. **`signHash` route: retry only on 429 or a network error raised before a response (error code in `ECONNREFUSED`, `ECONNRESET` when no bytes received, `ENOTFOUND`, `EAI_AGAIN`); 5xx and timeouts throw immediately** (design §9.3).
  - Caller abort: rejects with `signal.reason`, never retried or wrapped.
  - `expectJson` (all ESRM routes): 2xx with non-JSON content type → `ESignGeneralException('MisaUnknown','UnexpectedContentType', detail includes label, content type, first 256 chars)`; JSON content type but unparseable non-empty body → `'UnparseableResponse'` with body length only.
  - Logging per attempt group: `info('MISA eSign call started', {endpoint, method, correlationId})`, `info('MISA eSign call completed', {…, durationMs, outcome:'Success'|'Error'|'Cancelled', status})`; failures add `errorCategory`, `rawCode` at `warn`.

- [ ] **Step 1: Tests** with a scripted fake `fetch`: headers present and `Authorization` absent; bearer only when passed; 503,503,200 → 3 calls then success; 503×3 → `ESignTransportException` rawCode "ServiceUnavailable", attemptCount 3; 429 with `Retry-After: 1` and `maxDelayMs 500` waits 500 (fake delay records); `signHash` + 503 → 1 call, throws; `signHash` + ECONNREFUSED → retried; caller abort → `signal.reason`, 1 call; HTML 200 on `certificates` → UnexpectedContentType containing `<html`; `application/json` + `{bad` → UnparseableResponse whose message has no body text; log lines contain no `x-clientKey`/bearer value (assert by scanning all logged JSON for the secrets); `scrubText` masks each INV §6 field name and a 40-char base64 run.
- [ ] **Step 2:** FAIL → implement → PASS.
- [ ] **Step 3: Commit** `feat(ts-esign): transport with retry, content-type guard and safe logging`.

### Task 6: Token manager

**Files:**
- Create: `src/application/token-manager.ts`, `src/infrastructure/wire/auth.ts` (login/refresh/two-factor/resend request builders and response parsers), `src/infrastructure/defaults.ts` (`createInMemoryTokenCache`, `systemClock`, `abortableDelay`, `defaultTokenCacheKey`, `firstCertificateSelector`)
- Test: `test/unit/token-manager.test.ts`

**Interfaces:**
- Consumes: Task 5 `Transport`; Task 4 mappers.
- Produces: `createTokenManager(resolved, transport, deps): TokenManager` with
  - `getToken(creds, ctx): Promise<AccessToken>` — cache key `tokenCacheKey(creds, baseUrl)` (default `${userName}|${clientId}|${host}`, empty user/client → `ESignGeneralException('Validation','InvalidCredentials')`); hit when `expiresAt - 30_000 > now` (design §9.8); else refresh if refresh token (merge omitted fields from previous), on refresh failure evict and login; login body `{userName, password}`; single-flight per key for the whole acquire.
  - `withAuth<T>(creds, ctx, call: (bearer: string, userId: string) => Promise<T>): Promise<T>` — runs `call`; on `AuthenticationFailedException` with rawCode "401" from an ESRM route: single-flight refresh, on failure login once (design §9.2), store, re-run `call` once with the **new** token; second 401 → `AuthenticationFailedException('401')`. Later `getToken` calls return the refreshed token (design §9.1).
  - `exchangeOtp(creds, submission, ctx): Promise<AccessToken>` — two-factor body `{userName, code, otpType: 0|1, remember}`, single-flight per key, failures via `mapTwoFactorError`, success cached.
  - `resendOtp(creds, language, ctx): Promise<OtpResendResultDto>` — INV §2.5 result rules.
  - Login response: 2xx with `status.error` and code "122", or any status with `isTwoFactorRequired` → `AuthenticationFailedException({requires2FA:true, username: creds.userName})`; `data.remoteSigningAccessToken` → `value`, `data.user.id` → `userId`, `expiresIn` seconds → `expiresAt`. Refresh accepts both shapes (INV §2.3).
- Transport 401 signal: `Transport.send` returns status 401 as a normal response; ESRM callers convert it with `mapESignError` (401 → `AuthenticationFailedException` rawCode "401"), which `withAuth` catches.

- [ ] **Step 1: Tests**: `single flight login` (10 parallel `getToken` → 1 login call); skew (token expiring in 20 s → refresh); refresh omits refreshToken → previous kept; refresh 500 → login; `withAuth` 401 → refresh → call re-run with new bearer and a subsequent `getToken` returns the new token without another refresh; refresh fails on 401 → one login → re-run; second 401 → rawCode "401"; login 2FA in nested 4xx body detected; `exchangeOtp` 1001 → `InvalidOtpException`; resend 429 → transport; resend 400 without body → `{success:false, rawCode:'EmptyErrorCode'}`.
- [ ] **Step 2:** FAIL → implement → PASS.
- [ ] **Step 3: Commit** `feat(ts-esign): token manager with single-flight refresh and login fallback`.

### Task 7: Per-format wire builders and request validation

**Files:**
- Create: `src/infrastructure/wire/formats.ts`, `src/infrastructure/wire/base64.ts`, `src/application/validation/sign-request-validator.ts`, `src/application/mapping/sign-request-mapper.ts`
- Test: `test/unit/wire-formats.test.ts`, `test/unit/validator.test.ts`

**Interfaces:**
- Produces:
  - `NormalizedSignRequest { format: Exclude<DocumentFormat,'Unknown'>; fileToSign: string /* base64, or XML text */; documentName; documentId; dataToBeDisplayed; signatureInfo: WireSignatureInfo }` from `toNormalized(format, dto): NormalizedSignRequest` (mapper rules INV §1.3: SignatureName = SignedBy = signerName, HashAlgorithm "SHA256", renderingMode ?? 0, documentId ?? `crypto.randomUUID()`, XML slim info with `LogoImage:""`, `RenderingMode:0`, XML bytes UTF-8 decoded, XML `hashAlgorithm` other than SHA256 → "SHA256").
  - `validateSignRequest(format, dto, correlationId)`: throws `ESignGeneralException('Validation', 'InvalidSign{Pdf|Xml|Word|Excel}Request', detail listing failures)`; rules INV §7 Validators; XML: exactly one of `xml`/`xmlUtf8Bytes`.
  - `FORMATS: Record<Format, FormatSpec>` with `FormatSpec { docsKey: 'pdfDocs'|'xmlDocs'|'wordDocs'|'excelDocs'; buildHashBody(cert, n): object; parseHash(json, ctx): HashOutput; buildAttachBody(cert, hash, signature, n): object; parseAttach(json, ctx): Uint8Array }` with shapes and guards from INV §7 table exactly (PDF `Page ?? 1`, omitted arrays, PascalCase hash entry keys, camelCase `signaturePosInfos` inner keys, XML attachment result = UTF-8 bytes of the text, others base64-decoded; guard failures → `HashRejected` "EmptyDigest" (PDF) / "IncompleteHashResponse", attach missing → `MisaUnknown` "EmptyResponse" (PDF) / `AttachmentRejected` "MissingSignedDocument").
  - `HashOutput` (per-format union, stored in webhook sessions — must be JSON-serializable).
- [ ] **Step 1: Tests**: port the assertions of `tests/dotnet/MisaConnect.ESign.UnitTests/Signing/Wire/HashRequestSerializationTests.cs`, `AttachmentRequestSerializationTests.cs`, `SignatureInfoPageDefaultTests.cs` (read them; assert the same JSON key sets and values); `xml utf8 round trip keeps Vietnamese text` (`<a>Hóa đơn điện tử</a>` bytes → FileToSign equals the string; attach response text → bytes decode back equal); `large pdf base64 round trip` (5 MB random bytes → base64 → bytes equal, in < 2 s); each validator rule pass/fail; XML both/neither inputs fail.
- [ ] **Step 2:** FAIL → implement → PASS.
- [ ] **Step 3: Commit** `feat(ts-esign): per-format wire builders and request validation`.

### Task 8: Fake MISA eSign server

**Files:**
- Create: `test/fake/fake-esign-server.ts`, `test/fake/fake-esign-server.test.ts`

**Interfaces:**
- Produces: `startFakeESignServer(): Promise<FakeESignServer>` on `http://127.0.0.1:<port>`, with `baseUrl`, `requests: FakeRequest[]` (method, path, headers, bodyText), `close()`, and a mutable `scenario` object covering every scenario in INV §8 (`loginAlways401`, `loginRequires2FAOnFirstCall`, `nextTwoFactorErrorCode`, `resendMode`, `certsForce401Once`, `certsReturnHtml200`, `certsNoContentType200`, `certsJsonNonArray200`, `certsEmptyArray200`, `hashRejectionForFormat`, `pendingsBeforeSuccess` (default 1), `statusAlwaysFailed`, `statusCancelled`, `statusPending`, `attachmentMissingFormatArrayFor`, `attachmentRejectionForFormat`) plus `signHashStatus` (to inject 503 for Task 5/9 tests) and `postWebhook(targetUrl, body)` helper. Mounts auth routes at root and under `/webdev`; ESRM at root; rejects present-but-empty doc arrays with the MISA-shaped 400 + `validationFailures`; records `AuthorizationRM` value and asserts nothing.
- Port canned JSON from `tests/dotnet/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs` (read it; reuse its bodies verbatim).
- [ ] **Step 1: Tests**: smoke each route returns its default; `/webdev/` and root login both work; empty `pdfDocs: []` → 400 with `validationFailures`.
- [ ] **Step 2:** Implement → PASS.
- [ ] **Step 3: Commit** `test(ts-esign): in-process fake MISA eSign server`.

### Task 9: Polling sign flow and facade (all four formats)

**Files:**
- Create: `src/application/use-cases/list-active-certificates.ts`, `src/application/use-cases/sign-document.ts`, `src/application/use-cases/poll-sign-status.ts`, `src/client-internal.ts`, `src/client.ts`
- Modify: `src/index.ts`
- Test: `test/unit/poll-sign-status.test.ts`, `test/integration/sign.test.ts`

**Interfaces:**
- Consumes: Tasks 3–8.
- Produces:
  - `listActiveCertificates(ctx) → Certificate[]` (INV §2.4 step 3; chain length ≠ 3 → `MisaUnknown` "InvalidCertChainLength"; empty/`[]` → `NoActiveCertificateException`).
  - `signDocument(format, dto, ctx) → { signed: Uint8Array; transactionId; certificateKeyAlias; completedAtUtc }`: validate → token → certificates → select → hash → `Signing/hash` body INV §2.4 step 5 → poll → attach.
  - `pollSignStatus(tx, ctx)`: INV §2.4 step 6 rules; `delay(min(intervalMs, remaining), signal)`.
  - `createMisaESignClient(options): IMisaESignClient` and `IMisaESignClient` with all 11 methods (Tasks 10–11 fill OTP and webhook; until then they throw `ESignGeneralException('MisaUnknown','NotImplemented')` — remove that before Task 12); `createMisaESignClientInternal(options, deps)` for tests (accepts `http://127.0.0.1` base URLs, fake clock/delay, deterministic ids).
  - Mode guards: `sign*` throws `ESignGeneralException('Validation','WebhookModeOnly')` when mode `'webhook'`; `beginSign*` throws `'PollingModeOnly'` when `'polling'` *(TS replaces .NET's `InvalidOperationException`)*.
- [ ] **Step 1: Unit tests (poll)**: PENDING×2 then SUCCESS → 3 status calls; `canceled` → SignTerminalCancelled; `weird` → SignTerminalUnknown rawCode from errorCode or "UnknownStatus"; timeout with fake clock → `SignTimeoutException` with `elapsedMs`; `abort during polling stops without another status call`.
- [ ] **Step 2: Integration tests (fake server)**, one `describe` per format: happy path returns the fake's signed bytes and `certificateKeyAlias`; request order login → certificates → hash → Signing/hash → status×2 → attachment; `AuthorizationRM` present on ESRM requests only; `x-clientKey` on all; HTML certificates → UnexpectedContentType; empty certs → NoActiveCertificate; hash rejection per format → HashRejected with synthesized code; `certsForce401Once` → refresh then success; status FAILED → SignTerminalFailed; Signing/hash 503 → one Signing/hash request and `ESignTransportException` (design §9.3); `opts.credentials` override uses the override's client id header.
- [ ] **Step 3:** FAIL → implement → PASS (`npm test -w @misaconnect/esign`).
- [ ] **Step 4: Commit** `feat(ts-esign): polling sign flow for PDF, XML, Word and Excel`.

### Task 10: 2FA and OTP resend

**Files:**
- Modify: `src/client-internal.ts`, `src/application/use-cases/sign-document.ts`
- Test: `test/integration/two-factor.test.ts`

**Interfaces:**
- Consumes: Task 6 `exchangeOtp`, `resendOtp`.
- Produces: transparent mode (otpProvider set): catch `requires2FA`, `provide({userName, correlationId})`, empty code → `ESignGeneralException('Validation','InvalidOtpSubmission')`, exchange, retry whole operation once. Explicit mode: client instance field `pendingUserName` set on a `requires2FA` failure when no provider, cleared on success; `signInWithOtp(code, otpType, remember, opts)` / `resendOtp(language?, opts)` throw `ESignGeneralException('Validation','NoPendingOtpChallenge')` when none; both resolve credentials (opts → provider/static) and use `pendingUserName`.
- [ ] **Step 1: Tests** (fake `loginRequires2FAOnFirstCall`): with provider → one sign succeeds, two-factor body `{userName, code, otpType, remember}`; without provider → `AuthenticationFailedException.requires2FA`, then `signInWithOtp('123456','SmsOrEmail',false)` then `signPdf` succeeds; `signInWithOtp` before any failure → NoPendingOtpChallenge; `nextTwoFactorErrorCode '1002'` → ExpiredOtpException; resend modes (5) map per INV §2.5; resend uses `otp.defaultResendLanguage` when no language.
- [ ] **Step 2:** FAIL → implement → PASS.
- [ ] **Step 3: Commit** `feat(ts-esign): transparent and explicit 2FA, OTP resend`.

### Task 11: Webhook mode

**Files:**
- Create: `src/infrastructure/in-memory-session-store.ts`, `src/application/use-cases/begin-sign.ts`, `src/application/use-cases/handle-webhook.ts`, `src/application/use-cases/finalize-webhook.ts`, `src/application/webhook-envelope.ts`
- Modify: `src/application/ports.ts` (`SigningSessionStore` members), `src/client-internal.ts`
- Test: `test/unit/session-store.test.ts`, `test/unit/webhook-envelope.test.ts`, `test/integration/webhook.test.ts`

**Interfaces:**
- Produces:
  - `SigningSession { clientId; userName; transactionId; format; hash: HashOutput; recordedDocumentIds: string[]; createdAt: string; ttlMs; observedMessageIds: string[]; cachedSuccess?: { ack: WebhookAckDto; signed: string /* base64 */ } }`.
  - `SigningSessionStore { register(s): Promise<void>; get(clientId, tx): Promise<SigningSession|undefined>; recordMessageId(clientId, tx, id): Promise<void>; cacheSuccess(clientId, tx, ack, signedBase64): Promise<void>; withFinalizeLock<T>(clientId, tx, fn: () => Promise<T>): Promise<T> }`; in-memory default: lazy TTL on `get`, per-key promise-chain lock, `withFinalizeLock` on a missing session throws `UnknownTransactionException`.
  - `beginSign(format, dto, opts) → BeginResultDto` (INV §3 begin; session stores the call's credentials' `clientId` and `userName`).
  - `parseWebhookEnvelope(body: unknown): WebhookEnvelope` — requires non-empty string `messageId`, `clientId`, `transactionId`; `status` case-insensitive SUCCESS|FAILED|CANCELLED|CANCELED; `signatures` array of `{documentId, signature}` strings when present; else `MalformedEnvelopeException` (design §9.6).
  - `handleWebhook(body, opts) → WebhookHandleResultDto`: INV §3 handle steps with session lookup by envelope `clientId` + tx and match against the session's stored clientId; finalize resolves credentials via provider `{clientId, userName}` from the session (static used as is); ACK codes and texts verbatim INV §3; hook calls and swallow rules as INV §3; a `MalformedEnvelopeException` → ACK `webhook.malformed` + `FailureWithError` (no session → `transactionId` from body if a string).
- [ ] **Step 1: Unit tests**: store TTL expiry with fake clock; lock serializes two finalizers; envelope parse table (missing each field, bad status, lower-case `success` accepted, signatures not array).
- [ ] **Step 2: Integration tests**: begin → fake posts SUCCESS → `handleWebhook` returns ACK "0" + `SuccessWithSignedBytes` and hook received it; FAILED → `TerminalWithoutFinalize`, no attachment call, ACK "0"; unknown tx → `webhook.unknown_transaction`; wrong clientId → `webhook.client_id_mismatch`; foreign documentId → `webhook.document_id_mismatch`; empty signatures → `webhook.incomplete_success`; attachment rejection → `webhook.finalize_failed`, hook not called, a retry with the scenario cleared succeeds; replay after success → cached ACK, hook not called again, no attachment call; `concurrent duplicate webhook finalizes once`; Dynamic provider: begin with `opts.credentials` for user B, finalize calls the provider with `{clientId, userName:'B'}`; mode `'polling'` → `beginSign*` throws PollingModeOnly; garbage body `"x"` → `webhook.malformed`.
- [ ] **Step 3:** FAIL → implement → PASS.
- [ ] **Step 4: Commit** `feat(ts-esign): webhook mode — begin, handle, finalize, session store`.

### Task 12: Drift guards, public surface, sandbox suite

**Files:**
- Create: `test/unit/drift-guard.test.ts`, `test/unit/public-surface.test.ts`, `test/sandbox/sandbox.test.ts`, `.github/workflows/ts-sandbox.yml` (modify: add an `esign-sandbox` job)
- Modify: `src/index.ts`

**Interfaces:**
- Drift guards (skip if the .NET file is missing, as einvoice): parse `src/dotnet/MisaConnect.ESign.Infrastructure/ESign/ESignHttpRoutes.cs` string constants → equal `Routes` values; parse `ESignErrorCategory.cs` members → equal TS order; parse `MisaWebhookAckCodes.cs` → equal TS ACK codes; parse OTP keyword arrays in `OtpErrorMapper.cs` → equal TS lists.
- Public surface test: snapshot of `Object.keys(await import('../../src/index.js')).sort()`; no `NotImplemented` throw remains (grep test over `src/`).
- Sandbox: env `MISACONNECT_ESIGN_SANDBOX_{BASE_URL,CLIENT_ID,CLIENT_KEY,USERNAME,PASSWORD}`; skip unless all set; `MISACONNECT_ESIGN_SANDBOX_REQUIRED=1` → failing test instead of skip (einvoice pattern); tests: sign a tiny PDF (a fixture PDF generated in-test as a minimal valid PDF string), sign XML; abort-safe timeouts 120 s. `ts-sandbox.yml` esign job: same steps as einvoice incl. **MTU 1280** step, env from secrets `MISA_ESIGN_{BASE_URL,CLIENT_ID,CLIENT_KEY,USERNAME,PASSWORD}`, **without** `REQUIRED=1` until the secrets exist (comment says so).
- [ ] **Step 1:** Write tests → FAIL where applicable → fix → PASS. `npm run test:sandbox -w @misaconnect/esign` with no env → all skipped.
- [ ] **Step 2: Commit** `test(ts-esign): drift guards, public surface snapshot, opt-in sandbox suite`.

### Task 13: Docs, samples, release workflow

**Files:**
- Modify: `src/typescript/esign/README.md` (quick start, options table, credentials static/provider/per-call, 2FA both modes, webhook mode with multi-instance store warning, error categories, name table .NET→TS, "Differences from the .NET SDK" = design §9 items 1–9, "Calling MISA from outside Vietnam" marked unverified for eSign), `src/typescript/esign/CHANGELOG.md` (`[0.1.0]`), root `README.md` (TS supported-operations table gets esign rows), `CONTRIBUTING.md` Releasing (esign tag), `.github/workflows/release-ts.yml` (accept `einvoice-ts-v*` and `esign-ts-v*`; derive workspace dir and package from the tag prefix; tag must equal that package's version), `samples/typescript/express` (POST `/esign/pdf` signing an uploaded PDF; POST `/esign/webhook/:secret` with constant-time secret compare and optional CIDR allowlist, calling `handleWebhook` and returning the ACK JSON). The .NET follow-ups (design §9) stay in the design doc; no .NET or root-CHANGELOG change in this plan.
- [ ] **Step 1:** `npm run build && npm test && npm run lint && npm run format:check && npm run typecheck` at root → all green; `python -c "import yaml; yaml.safe_load(open('.github/workflows/release-ts.yml'))"`; dry-run the tag→package derivation logic with a small bash test in the workflow's own script (`TAG=esign-ts-v0.1.0` → dir `src/typescript/esign`).
- [ ] **Step 2: Commit** `docs(ts-esign): README, CHANGELOG, express sample, release workflow for esign tags`.

### Task 14: Branch verification and PR

- [ ] **Step 1:** Full verification: `npm ci && npm run build && npm test && npm run lint && npm run typecheck && npm run format:check`; `dotnet build MisaConnect.slnx -c Release` (nothing .NET changed, must stay green); record counts.
- [ ] **Step 2:** Push `ts/esign`, open PR "feat(ts): @misaconnect/esign 0.1.0", wait for CI, report. Publishing (`esign-ts-v0.1.0` tag, npm trusted publisher) waits for the owner (design §13).

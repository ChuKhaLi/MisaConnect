# MisaConnect.ESign (.NET) — inventory for the TypeScript port

Date: 2026-11-04. Reference material for [2026-11-04-ts-esign-design.md](2026-11-04-ts-esign-design.md). Produced by reading `src/dotnet/MisaConnect.ESign.*` at `main` 6e48525; line numbers drift — treat them as starting points.

Path prefixes: `[D]` `src/dotnet/MisaConnect.ESign.Domain/`, `[A]` `…Application/`, `[I]` `…Infrastructure/`, `[C]` `…Client/`, `[T]` `tests/dotnet/`.

## 1. Public consumer surface

### 1.1 `IMisaESignClient` (`[C]IMisaESignClient.cs`)

| .NET | Notes |
|---|---|
| `SignPdfAsync(SignPdfRequestDto)` → `SignPdfResultDto` | polling mode |
| `SignXmlAsync(SignXmlRequestDto)` → `SignXmlResultDto` | one method; DTO has `Xml` (string) or `XmlUtf8Bytes`, exactly one; factories `SignXmlRequest.FromString/FromUtf8Bytes` |
| `SignWordAsync`, `SignExcelAsync` | same shape as PDF |
| `SignInWithOtpAsync(otpCode, OtpDeliveryChannel otpType, bool remember)` | explicit 2FA; uses the pending username captured by the last failed Sign/BeginSign on the same (scoped) facade |
| `ResendOtpAsync(string? language = null)` → `OtpResendResultDto` | same pending-username rule |
| `BeginSign{Pdf,Xml,Word,Excel}Async(request)` → `BeginResultDto(TransactionId, Format)` | webhook mode |
| `HandleWebhookAsync(WebhookEnvelopeDto)` → `WebhookHandleResultDto(Ack, Outcome)` | |

Mode guards: `Sign*` throws `InvalidOperationException` when `Webhook.Mode == Webhook`; `BeginSign*` when `Mode == Polling`. 2FA capture: when `AuthenticationFailedException.Requires2FA && no IOtpProvider`, the facade stores `ex.Username` and rethrows; success clears it; `SignInWithOtp`/`ResendOtp` without a pending username → `InvalidOperationException`.

### 1.2 Options (`[I]Configuration/MisaESignOptions.cs`, validator `MisaESignOptionsValidator.cs`; collects all errors)

| Option | Type / default | Validation |
|---|---|---|
| `Environment` | Sandbox(0) / Production(1), default Sandbox | Production requires host `esignapp.misa.vn`; Sandbox forbids it |
| `BaseUrl` | "" | required; absolute; https, or http only on loopback/localhost |
| `AuthUnderWebdev` | bool?, null | effective = value ?? (Environment == Sandbox) |
| `ClientId`, `ClientKey`, `UserName`, `Password` | "" | required non-blank only when `CredentialsMode == Static` |
| `CredentialsMode` | Static / Dynamic | validator only — no runtime effect |
| `Polling.Interval` / `TotalTimeout` | 2s / 60s | interval > 0; timeout > interval |
| `TransportRetry.MaxAttempts` / `BaseDelay` / `MaxDelay` | 3 / 200ms / 2s | attempts ≥ 1; max ≥ base |
| `Errors.IncludeRawErrorMessage` | false | |
| `Otp.DefaultResendLanguage` | "en-US" | non-empty |
| `Webhook.Mode` | Polling=1, Webhook=2, Both=3; default Both | |
| `Webhook.Session.Ttl` | 24h | > 0 |
| `Webhook.Path` / `Secret` / `AllowedIps` | sample host only | secret ≥ 32 chars; CIDRs |

### 1.3 DTOs (`[C]Dtos/`)

- `Sign{Pdf,Word,Excel}RequestDto`: bytes (`Pdf`/`Word`/`Excel`), `DocumentName`, `SignerName`, `Location`, `Reason`, `Contact`, `LogoImageBase64`, `DataToBeDisplayed`, optional `Page`, `PositionX`, `PositionY`, `Width`, `Height`, `RenderingMode`, `DocumentId`, `DisplayText`, `ShowSignedDate = true`, `AdditionalSignaturePositions: SignaturePosInfoDto[]`.
- Mapper: `SignatureName = SignedBy = SignerName`, `HashAlgorithm = SHA256`, `RenderingMode ?? 0`, `DocumentId ?? new GUID`.
- `SignXmlRequestDto(Xml?, XmlUtf8Bytes?, XmlSignatureContextDto SignatureContext, DocumentName, DataToBeDisplayed, DocumentId?)`; `XmlSignatureContextDto(SignatureName, HashAlgorithm string — unparseable → SHA256, SignatureDescriptionDto)`; `SignatureDescriptionDto(SignedBy, Location, Reason, Contact, ShowSignedDate?, DisplayText?)`; `SignaturePosInfoDto(PositionX, PositionY, Width, Height, Page)`.
- Results `Sign{X}ResultDto(Signed{X} bytes, TransactionId, CertificateKeyAlias, CompletedAtUtc, Format)`; `SignedXml` is UTF-8 bytes.
- `OtpResendResultDto(Success, RawCode?, UserMsg?, DevMsg?, CorrelationId)`.
- `CertificateDto` declared but returned by nothing.
- Webhook: `BeginResultDto`; `WebhookEnvelopeDto` JSON camelCase `messageId, clientId, extraData, status, errorCode, transactionId, signatures[{documentId, signature}]`; `WebhookAckDto {errorCode, devMsg, userMsg}`; `WebhookOutcomeDto` = `SuccessWithSignedBytes(TransactionId, Format, SignedBytes, CorrelationId)` | `FailureWithError(TransactionId?, Format, CategoryName, MisaErrorCode?, CorrelationId)` | `TerminalWithoutFinalize(TransactionId, Format, Status, MisaErrorCode?, CorrelationId)`; `WebhookStatusDto {Success=1, Failed=2, Cancelled=3}`; consumer hook `IWebhookDeliveryHook.DeliverAsync(WebhookOutcomeDto)`.

### 1.4 Domain (`[D]`)

Enums: `DocumentFormat {Unknown, Pdf, Xml, Word, Excel}`, `OtpDeliveryChannel {SmsOrEmail=0, Authenticator=1}`, `KeyStatus`, `SignStatus {PENDING, SUCCESS, FAILED, CANCELLED, UNKNOWN}`, `WebhookValidationCategory {None, MalformedEnvelope, ClientIdMismatch, UnknownTransaction, IncompleteSuccessEnvelope, DocumentIdMismatch}`.

`ESignErrorCategory`: MisaUnknown, Authentication, NoActiveCertificate, CertificateLookupFailed, HashRejected, SignRejected, SignTerminalFailed, SignTerminalCancelled, SignTerminalUnknown, SignTimeout, AttachmentRejected, StatusLookupFailed, Transport, Validation, WebhookValidation, WebhookFinalize (never thrown).

Exceptions: abstract `ESignException(Category, RawCode?, Detail, CorrelationId — required non-empty, Format)`; `ESignGeneralException`; `AuthenticationFailedException(+Requires2FA, Username)` with subclasses `InvalidOtpException` "InvalidOtp", `ExpiredOtpException` "ExpiredOtp", `ExhaustedOtpAttemptsException` "ExhaustedOtpAttempts", `OtpRejectedException` "OtpRejected"; `NoActiveCertificateException` "NoActiveCertificate"; `SignRejectedException(+RequiresUserCertSetup)`; `SignTerminalStateException(+TerminalStatus, TransactionId)` category by status; `SignTimeoutException(+TransactionId, ElapsedTime)` "SignTimeout"; `ESignTransportException(+LastStatusCode, AttemptCount)` rawCode = `HttpStatusCode` enum name (e.g. "ServiceUnavailable") or "Transport"; abstract `WebhookValidationException(+WebhookCategory, MatchedTransactionId)` with 5 subclasses.

`MisaCredentials(ClientId, ClientKey, UserName, Password)`, `ToString` redacts key and password. `AccessToken.Value` = `remoteSigningAccessToken`; MISA's `accessToken` is parked unused.

## 2. Operations

### 2.1 HTTP

- Every request: `x-clientId`, `x-clientKey` (from the credentials accessor, per call), `X-Correlation-Id`.
- ESRM calls: `AuthorizationRM: Bearer {remoteSigningAccessToken}` ("Bearer " hard-coded). **No `Authorization` header ever.** Auth calls carry no AuthorizationRM.
- JSON: explicit property names, case-sensitive, nulls omitted.

### 2.2 Routes (`[I]ESign/ESignHttpRoutes.cs`, resolver `ESignRouteResolver.cs`)

Base = **origin** of `BaseUrl` (path discarded). `webdev/` is prefixed onto login and two-factor only when effective `AuthUnderWebdev`.

| Route | Path | Base |
|---|---|---|
| login | `api/auth/api/v1/auth/login-api` | Sandbox `/webdev/…`, Production root |
| two-factor | `api/auth/api/v1/auth/two-factor-auth` | same as login |
| refresh | `webdev/api/auth/api/v1/auth/refreshtoken` | always `/webdev/` |
| resend OTP | `webdev/api/auth/api/v1/auth/resend-otp-auth` | always `/webdev/` |
| certificates | `external/esrm/service/general/api/v1/Certificates/by-userId` (GET) | root |
| hash | `external/esrm/service/document/api/v1/documents/hash` | root |
| sign | `external/esrm/service/signing/api/v1/Signing/hash` | root |
| status | `external/esrm/service/signing/api/v1/Signing/status/{encodeURIComponent(tx)}` (GET) | root |
| attachment | `external/esrm/service/document/api/v1/documents/attachment` | root |

### 2.3 Token (`[A]UseCases/EnsureAccessToken.cs`, `RefreshAccessToken.cs`, `[I]ESign/Mapping/AuthSessionMapper.cs`)

Cache key `"{UserName}|{ClientId}|{host of BaseUrl}"` (empty user/client → error). Hit when `expiresAt > now` (a 30 s skew field exists but is never read). Expired + refresh token → refresh (keep previous refresh token / userId / username if omitted; any failure → evict and fall through to login). Login body `{"userName","password"}`. `expiresAt = now + expiresIn s`. Refresh response accepts `{status, data:{…}}` or flat `{accessToken, remoteSigningAccessToken, refreshToken, expiresIn}`.

401 on an ESRM call (`[I]Http/RemoteSigningAuthHandler.cs`): no cached entry or no refresh token → `AuthenticationFailedException("401")`; else single-flight refresh per key; refresh failure → evict + rethrow (no login fallback); success → retry once with new bearer; second 401 → "401". Use cases keep passing the old token, so later calls in the same flow 401 again.

### 2.4 Sign sequence (`[A]UseCases/SignPdf.cs`; Xml/Word/Excel identical except hash/attach)

1. Validate. 2. Token. 3. `GET Certificates/by-userId` → bare array of `{userId, keyAlias, appName, keyStatus, certificate, certiticateChain (MISA typo), certStatus, effectiveDate, expirationDate, emailName, isAutoSign}`; chain length ≠ 3 → MisaUnknown "InvalidCertChainLength"; keep `keyStatus` == "ACTIVE" (case-insensitive); none → `NoActiveCertificateException`; `ICertificateSelector` (default first). 4. `POST documents/hash`. 5. `POST Signing/hash` body `{"DataToBeDisplayed","UserId": login user.id,"CertAlias": keyAlias,"Documents":[{"DocumentId","FileToSign": digest,"DocumentName"}]}` → `{transactionId}` (missing → MisaUnknown "EmptyResponse"). 6. Poll `GET Signing/status/{tx}` → `{status, errorCode (loose), errorDescription, transactionId, signatures[{documentId, signature}]}`; status case-insensitive PENDING/SUCCESS/FAILED/CANCELLED|CANCELED else UNKNOWN; SUCCESS → continue; FAILED/CANCELLED/UNKNOWN → `SignTerminalStateException` (UNKNOWN rawCode `errorCode ?? "UnknownStatus"`); after each poll `now >= start + timeout` → `SignTimeoutException`; sleep `min(interval, remaining)`. 7. `POST documents/attachment` with `signatures[0].signature` (or ""). 8. Result `CompletedAtUtc = clock.now`, `CertificateKeyAlias = cert.keyAlias`.

### 2.5 2FA / OTP

- Login 2xx with `status.error == true` and `errorCode "122"` → `AuthenticationFailedException(Requires2FA, Username)`. Non-2xx login parses only the flat ResponseError (a nested `{status:{errorCode:"122"}}` on 4xx is missed).
- Transparent: if an `IOtpProvider` exists, Sign*/BeginSign* catch the challenge, call `ProvideAsync(OtpChallenge(UserName, CorrelationId))` (code non-empty), exchange, retry the whole operation once. `RequestResendAsync` is never called.
- Exchange: `POST two-factor-auth` `{"userName","code","otpType":0|1,"remember"}`, single-flight per key, response like login, cached.
- Resend: `POST resend-otp-auth` `{"userName","language"}` (default option). 429/≥500 → transport. Else result: success when 2xx and envelope null or `error:false`; no envelope → RawCode "EmptyErrorCode"; else copy errorCode/userMsg/devMsg.

## 3. Webhook mode

- `BeginSign*`: validate → token → certs → hash → Signing/hash, no polling. Registers `SigningSession(ClientId = static options.ClientId, TransactionId, Format, HashPayload (per-format hash output), RecordedDocumentIds = [DocumentId], CreatedAtUtc, Ttl, ObservedMessageIds, CachedSuccess = null)`. Empty ClientId → error (Dynamic mode gap).
- Facade `HandleWebhookAsync`: case-sensitive status parse; unknown status throws `MalformedEnvelopeException` **outside** the handler (escapes instead of ACKing); null messageId/clientId/transactionId unchecked.
- `HandleWebhook`: clientId must equal `options.ClientId` → else ClientIdMismatch; session by (clientId, tx) → else UnknownTransaction; record messageId; FAILED/CANCELLED → hook `TerminalWithoutFinalize`, success ACK "0", no attach; success needs non-empty signatures (IncompleteSuccessEnvelope) and every documentId recorded (DocumentIdMismatch); cached success → cached ACK + outcome, hook not re-invoked; else finalize. Validation exception → ACK per category + `FailureWithError`, hook invoked (hook errors swallowed).
- `FinalizeFromWebhook`: finalize lock per (clientId, tx); re-read session (evicted → UnknownTransaction); cached → short-circuit; token (credentials accessor — Dynamic gap); list certs + select; attach with `signatures[0].signature`; success → ACK `{"0","Webhook accepted.","Webhook accepted."}`, cache, hook `SuccessWithSignedBytes` (hook error propagates); `ESignException` → ACK `{"webhook.finalize_failed","Finalize attempt failed; MISA may retry.","Signing finalization failed; please wait for retry."}` + `FailureWithError(Category None, MisaErrorCode = rawCode)`, hook **not** invoked, not cached.
- ACK codes: "0", "webhook.malformed", "webhook.client_id_mismatch", "webhook.unknown_transaction", "webhook.incomplete_success", "webhook.document_id_mismatch", "webhook.finalize_failed".
- SDK verification = clientId match only. Secret path, constant-time compare, CIDR allowlist live in `samples/dotnet/MisaConnect.Samples.Api/Endpoints/ESignWebhookEndpoint.cs`.
- In-memory store: map keyed (clientId, tx); TTL enforced lazily on read; finalize lock = per-entry mutex; throws if session missing.

## 4. Ports

`ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector` (first), `IMisaCredentialsAccessor` (options), `ISystemClock`, `IDelayer`, `IOtpProvider` (none by default), `ICorrelationIdAccessor` (GUID per scope), `ISigningSessionStore` + `IFinalizeLockOwner` (in-memory), `IWebhookDeliveryHook` (null/logging). All `TryAdd…`.

## 5. Errors

### 5.1 `ESignErrorMapper.Map(endpoint, status, envelope)` (`[A]Errors/ESignErrorMapper.cs`)

401 on non-auth → Authentication; 429/≥500 → Transport; then by endpoint: login → Authentication (Requires2FA iff code "122"); refresh → Authentication; certificates → CertificateLookupFailed (raw or "EmptyErrorCode"); hash → HashRejected (synthesized); Signing/hash → `SignRejectedException` (`RequiresUserCertSetup` if devMsg contains "not connected"/"not set up"/"remote signing account" or userMsg contains "chưa kết nối"/"chưa thiết lập"); status → StatusLookupFailed; attachment → AttachmentRejected (synthesized); other → MisaUnknown.

Hash synthesis (probe = raw+devMsg+userMsg, case-insensitive, first match): empty → "EmptyErrorCode"; XML and "xml" + ("malformed"|"invalid"|"không hợp lệ") → "InvalidXmlInput"; "unsupported"|"variant"|"format not supported" → "UnsupportedDocumentVariant"; else on first non-null of raw/devMsg/userMsg: "Cert" → "InvalidCertificate", "Hash"|"Digest" → "InvalidHash", "File"|"Document" → "InvalidDocument", "Signature" → "InvalidSignatureInfo", else raw.

Attachment synthesis: Word/Excel and "mainDom"|"main dom"|"missing main" → "MissingMainDom"; XML/Word/Excel and "signatureId"|"signature id"|"missing signature" → "MissingSignatureId"; "unsupported"|"variant" → "UnsupportedDocumentVariant"; else "Signature" → "InvalidSignature", "Cert" → "InvalidCertificate", "Doc"|"Hash" → "InvalidHashInputs", else raw.

Detail: `"MISA returned errorCode={raw|<none>} on {endpoint}."`; with `IncludeRawErrorMessage` append `" | userMsg=… | devMsg=… | validationFailures: [Prop] reason; …"`.

### 5.2 `OtpErrorMapper.MapTwoFactor`

429/≥500 → Transport; "122" → Requires2FA; "1001" Invalid; "1002" Expired; "1003" Exhausted; keyword fallback (lower-cased probe): invalid {"invalid otp","wrong otp","otp incorrect","otp wrong","sai mã","không đúng","otp không hợp lệ"}, expired {"expired","hết hạn","quá hạn","timed out"}, exhausted {"max attempts","exceeded","vượt quá số lần","too many attempts","locked"}; else `OtpRejectedException`. 2FA failure body: parse `{status}` first, fall back to flat ResponseError.

### 5.3 Other raised errors

MisaUnknown "EmptyResponse" (missing data/docs/transactionId/status), "UnexpectedContentType", "UnparseableResponse", "InvalidCertChainLength"; HashRejected "EmptyDigest" (PDF), "IncompleteHashResponse" (XML/Word/Excel); AttachmentRejected "MissingSignedDocument" (XML/Word/Excel); Validation "InvalidSign{Pdf,Xml,Word,Excel}Request", "InvalidOtpSubmission"; Authentication "401".

### 5.4 Content type

Only `Certificates/by-userId` checks it: 2xx must be `application/json` or `*+json`, else "UnexpectedContentType" (message includes endpoint, content type, first 256 body chars); JSON but unparseable → "UnparseableResponse" (body length only); empty/`[]` → NoActiveCertificate. `ResponseError.error` is `bool?` (MISA documents a string; a string drops the envelope). `errorCode` accepts string/number/bool.

### 5.5 Retry (`[I]Http/TransientFailureRetryHandler.cs`)

MaxAttempts total; transient = 429, ≥500, connection error, timeout not caused by caller; **every method retried, including `POST Signing/hash`**; full jitter `random[0, min(MaxDelay, BaseDelay·2^(n−1)))`; 429 `Retry-After` (seconds or date) clamped to MaxDelay; exhaustion → `ESignTransportException(lastStatus, MaxAttempts)`.

## 6. Logging

`ESignCallLogger`: info started/completed with `endpoint` (short label), `method`, `correlationId`, `durationMs`, `outcome`; warning on `ESignException` with `errorCategory`, `rawCode`; error otherwise (exception object not logged). `ESignLogScrubber` (unused in src): masks `extraData`; `AuthorizationRM|Authorization` values; `Bearer …`; JSON string fields accessToken, remoteSigningAccessToken, refreshToken, authorizationRM, authorization, password, code, certificate, certiticateChain, certificateChain, fileToSign, documentBytes, documentHash, sh, digest, signature, signatureId, mainDom, document, fontData, signatureImage, logoImage, email, emailName, phoneNumber, firstName, lastName, extraData, secret; `"device*"` fields; base64 runs ≥ 32 chars.

## 7. Formats (wire)

Hash and attachment body: `{"certificate", "certificateChain": [signing, intermediate, root], "<fmt>Docs": [one entry]}`; unused arrays **omitted** (MISA 400s on `[]`).

Hash entry keys PascalCase: `DocumentId`, `FileToSign`, `SignatureInfo`. `SignatureInfo`: `TextColor, PositionX, PositionY, Width, Height, FontSize, FontData, SignatureImage, Page, SignatureName, HashAlgorithm ("SHA256"), LogoImage, SignatureDescription{SignedBy, ShowSignedDate, Location, Reason, Contact, DisplayText}, RenderingMode, SignaturePosInfos[{positionX, positionY, width, height, page}]` (inner keys camelCase).

| | PDF | XML | Word / Excel |
|---|---|---|---|
| array | `pdfDocs` | `xmlDocs` | `wordDocs` / `excelDocs` |
| `FileToSign` | base64 | raw XML text (bytes UTF-8-decoded) | base64 |
| `SignatureInfo` | full; `Page ?? 1` | slim: SignatureName, HashAlgorithm, `LogoImage:""`, `RenderingMode:0`, Description | full; `Page ?? 1` |
| hash response | `{documentId, documentBytes, documentHash, sh, signatureName, digest}` | `{documentId, document, signatureId, digest, sh}` | `{documentId, documentBytes, signatureId, digest, mainDom}` |
| hash guard | digest | document, signatureId, digest, sh | documentBytes, signatureId, digest, mainDom |
| attach entry | `{signature, documentId, documentBytes, digest, signatureName, sh, documentHash}` | `{signature, documentId, documentBytes: hash.document, digest, signatureName:"", sh, signatureId, documentHash:""}` | `{signature, documentId, documentBytes, digest, mainDom, signatureName:"", sh:"", signatureId, documentHash:""}` |
| attach response | `pdfDocs[0].document` base64 | `xmlDocs[0].document` UTF-8 text (not base64) | base64 |

Validators (`[A]Validation/Sign*RequestValidator.cs`): bytes non-empty; DocumentName ≤ 100; DocumentId ≤ 36; DataToBeDisplayed, SignatureName, LogoImage, SignedBy, Location, Reason, Contact required; RenderingMode 0..2; PDF only: Page ≥ 1 when set. XML: no LogoImage/RenderingMode checks.

## 8. Test assets

Fake server `[T]MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs` (637 LOC): login (root + `/webdev`, scenarios 401 / 2FA-first-call), two-factor (error code injection), resend (5 modes), refresh (flat), certificates (force-401-once, HTML 200, no content type, non-array, empty), hash (rejects present-but-empty arrays with MISA-shaped 400 + validationFailures; per-format rejection), Signing/hash (`tx-fake-1`), status (pendings-before-success, failed, cancelled, pending; signature documentId "doc-1"), attachment (missing array, multi-array, rejection); synthetic webhook helpers. Wire-shape unit tests: `[T]MisaConnect.ESign.UnitTests/Signing/Wire/`. No golden JSON for ESign.

Sandbox env: `MISACONNECT_ESIGN_SANDBOX_BASE_URL`, `_CLIENT_ID`, `_CLIENT_KEY`, `_USERNAME`, `_PASSWORD`; 2FA `_OTP_PROVIDER`, `_USER_2FA_ENABLED`; webhook `_WEBHOOK_URL`, `_WEBHOOK_TIMEOUT`. None set in the `misa-sandbox` GitHub environment (2026-11-04).

## 9. Size

~174 source files / ~8,500 LOC (Domain 562, Application 3,406, Infrastructure 3,514, Client 1,017); much is ×4 format duplication. Tests: unit 5,257, integration 3,225.

## 10. Code vs MISA doc (`docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md`)

Auth paths (doc: login/two-factor at root; code: `/webdev` on sandbox, empirical); header names (doc `clientId` on two-factor, code `x-clientId`); bearer hard-coded "Bearer " vs doc `{tokenType} {token}`; Signing/hash `UserId` from login vs doc's cert userId; doc says re-login when refresh fails (401 path doesn't); doc says let the user choose among ≥ 2 certificates (code takes first); `ResponseError.error` string in doc vs bool in code; status values (doc PENDING/SUCCESS/FAILED); XML attachment output treated as text vs doc base64; webhook ACK sample `errorCode:"status_sign"` vs code "0"; token lifetime 60 min in doc, no refresh skew in code.

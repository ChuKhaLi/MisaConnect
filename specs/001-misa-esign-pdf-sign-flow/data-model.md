# Phase 1 — Data Model: MISA eSign PDF Sign Flow

This document captures the Domain entities, Application-layer DTOs/results, options/configuration shape, and state transitions for slice 1. Every type listed here lives at the layer indicated (Domain / Application / Infrastructure-Wire / Client-Dto) and respects Constitution Principle I (zero-dep Domain, Application-only-Domain, Infrastructure-internal-except-DI/options/ports).

Field names in the **Wire DTO** column reproduce MISA's published shape verbatim per Principle IV — including MISA's `certiticateChain` typo. Field names in Domain / Application / Client columns are SDK-idiomatic (PascalCase).

---

## Layered overview

```
Client (public DTOs)         Client.Dtos.SignPdfRequestDto / SignPdfResultDto / CertificateDto
                                        │  Mapping
                                        ▼
Application (use cases,      Application.UseCases.SignPdf orchestrator
ports, error mapper,         Application.Abstractions.* ports
domain workflows)            Application.Errors.ESignErrorMapper
                                        │
                                        ▼
Domain (entities, value      Domain.Authentication.AuthSession
objects, errors, enums)      Domain.Certificates.{Certificate, CertificateChain, KeyStatus}
                             Domain.Documents.PdfDocument
                             Domain.Signing.{SignatureInfo, SignatureDescription, SignTransaction,
                                              SignStatus, SignedDocument, HashAlgorithm}
                             Domain.Errors.{ESignException, AuthenticationFailedException,
                                             NoActiveCertificateException, SignRejectedException,
                                             SignTerminalStateException, SignTimeoutException,
                                             ESignTransportException}
                                        ▲
                                        │  Mapping (Infrastructure)
Infrastructure (wire DTOs)   Infrastructure.ESign.Wire.* (verbatim MISA shapes)
```

---

## 1. Domain layer — entities & value objects

### 1.1. `AuthSession` (Domain.Authentication)

| Field | Type | Source | Notes |
|---|---|---|---|
| `AccessToken` | `string` | login-api / refreshtoken `accessToken` | MISA ID access token. Not used for downstream eSign calls. Held for completeness. |
| `RemoteSigningAccessToken` | `string` | login-api / refreshtoken `remoteSigningAccessToken` | The token actually used as `AuthorizationRM` header value. 60-minute lifetime per MISA's note in §4.11. |
| `RefreshToken` | `string` | login-api / refreshtoken `refreshToken` | Used to acquire a fresh `RemoteSigningAccessToken` via `/auth/refreshtoken`. |
| `ExpiresAtUtc` | `DateTimeOffset` | `ISystemClock.UtcNow + expiresIn` (seconds) | Computed at acquisition time; FR-005 proactive refresh keys off this. |
| `UserId` | `string` | login-api `data.user.id` | Required by `Signing/hash` as the `UserId` field. |
| `Username` | `string` | login-api `data.user.username` | Used as part of `ITokenCacheKeySelector` default key. |

`AuthSession` is an immutable `record`. PII fields beyond `UserId` / `Username` (email, phone, first/last name) are **not** captured into Domain — they're discarded at the mapping boundary so they cannot leak.

### 1.2. `Certificate` (Domain.Certificates)

| Field | Type | Wire DTO | Notes |
|---|---|---|---|
| `UserId` | `string` (GUID) | `userId` | |
| `KeyAlias` | `string` (GUID) | `keyAlias` | Used as `CertAlias` in `Signing/hash` and as `SignatureId` in `documents/attachment` (per MISA doc §4.14). |
| `AppName` | `string` | `appName` | |
| `KeyStatus` | `KeyStatus` enum | `keyStatus` | `ACTIVE`, `INACTIVE`. Only `ACTIVE` certs pass the selector. |
| `CertStatus` | `string?` | `certStatus` | Reported separately from `keyStatus`; surfaced for transparency. |
| `Certificate` | `string` (base64) | `certificate` | The signing certificate (chain[0]). Treated as opaque base64 string. |
| `CertificateChain` | `CertificateChain` | `certiticateChain` [sic] | 3-element list. See 1.3. |
| `EffectiveDate` | `DateTimeOffset?` | `effectiveDate` | |
| `ExpirationDate` | `DateTimeOffset?` | `expirationDate` | |
| `EmailName` | `string?` | `emailName` | Email tied to the cert. Held internally for the selector; **not** logged. |
| `IsAutoSign` | `bool` | `isAutoSign` | Surfaced for transparency; SDK doesn't branch on it in slice 1. |

### 1.3. `CertificateChain` (Domain.Certificates)

| Field | Type | Wire DTO | Notes |
|---|---|---|---|
| `Signing` | `string` | `certiticateChain[0]` | "chứng thư ký" |
| `Intermediate` | `string` | `certiticateChain[1]` | MISA CA intermediate cert |
| `Root` | `string` | `certiticateChain[2]` | NEAC root cert |

Value object — `record`. Validators assert exactly three elements.

### 1.4. `KeyStatus` (Domain.Certificates) — enum

`ACTIVE`, `INACTIVE`. Unrecognized values from MISA → `INACTIVE` defensively (so the selector skips them) plus an Information-level structured log entry.

### 1.5. `PdfDocument` (Domain.Documents)

Wraps `byte[] Bytes`. Parallels `MisaConnect.EInvoice.Domain.Pdf.PdfDocument`. Used both as input (consumer's source PDF) and output (signed PDF returned to consumer).

### 1.6. `SignatureInfo` (Domain.Signing) + `SignatureDescription`

Input to `documents/hash`. Field names match MISA's §4.2 / §4.3:

`SignatureInfo` — `TextColor?`, `PositionX?`, `PositionY?`, `Width?`, `Height?`, `FontSize?`, `FontData?` (base64), `SignatureImage?` (base64), `Page?`, **`SignatureName`** (required), **`HashAlgorithm`** (required — slice 1 only supports `SHA256`), **`LogoImage`** (required, base64), **`SignatureDescription`** (required), **`RenderingMode`** (required, enum `0|1|2`), `SignaturePosInfos?` (list).

`SignatureDescription` — **`SignedBy`** (required), `ShowSignedDate?`, **`Location`** (required), **`Reason`** (required), **`Contact`** (required), `DisplayText?`.

`SignaturePosInfo` — `PositionX`, `PositionY`, `Width`, `Height`, `Page`, all required.

`HashAlgorithm` is a Domain enum with one allowed value (`SHA256`) — exposed as enum (not string) so the public surface forces correctness.

### 1.7. `SignTransaction` (Domain.Signing)

| Field | Type | Source | Notes |
|---|---|---|---|
| `TransactionId` | `string` | `Signing/hash` response `transactionId` | The poll key. |
| `SubmittedAtUtc` | `DateTimeOffset` | `ISystemClock.UtcNow` at submission | Used by the poll loop's deadline math. |

### 1.8. `SignStatus` (Domain.Signing) — enum

`PENDING`, `SUCCESS`, `FAILED`, `CANCELLED`. Unknown wire value → mapped via `SignStatusMapper` to a `SignTerminalStateException` (defensive — see R-7).

### 1.9. `SignedDocument` (Domain.Documents — alias of `PdfDocument`)

Type-aliased so the orchestrator's return type reads as `SignedDocument` (better self-documentation) without inventing a redundant wrapper.

### 1.10. Error hierarchy (Domain.Errors)

```
ESignException (abstract base, carries Category, RawCode, CorrelationId, Detail)
├── AuthenticationFailedException        (FR-008-adjacent — login/refresh rejected)
│      Properties: Requires2FA (bool) — true iff MISA returned errorCode 122 on login
├── NoActiveCertificateException         (FR-008 — no ACTIVE certs)
├── SignRejectedException                (Signing/hash rejected the request)
│      Properties: RequiresUserCertSetup (bool) — true iff MISA's "user not connected" message
├── SignTerminalStateException           (Signing/status returned FAILED or CANCELLED)
│      Properties: TerminalStatus (SignStatus — FAILED|CANCELLED|unknown)
├── SignTimeoutException                 (poll deadline elapsed)
│      Properties: TransactionId, ElapsedTime
└── ESignTransportException              (FR-025 — transport-retry budget exhausted)
       Properties: LastStatusCode (HttpStatusCode?), AttemptCount (int)
```

Every exception carries `CorrelationId` (string, mandatory) and `RawCode` (string?, the MISA `errorCode`). `Detail` defaults to a scrubbed summary; raw `userMsg`/`devMsg` only present when `MisaESignOptions.Errors.IncludeRawErrorMessage = true`.

### 1.11. `ResponseError` (Domain.Errors)

Plain value object mirroring MISA's §4.13 shape: `Error`, `ErrorCode`, `DevMsg`, `UserMsg`. Used internally by `ESignErrorMapper`; not surfaced to consumers (they get the typed exception).

---

## 2. Application layer — ports & use-case I/O

### 2.1. `AccessToken` (Application.Abstractions)

```csharp
public sealed record AccessToken(
    string Value,                // = AuthSession.RemoteSigningAccessToken
    string RawAccessToken,       // = AuthSession.AccessToken (held but unused downstream)
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string UserId,
    string Username);
```

Sole type held by `ITokenCache`. Note: parallels `MisaConnect.EInvoice.Application.Abstractions.AccessToken` but with the extra fields eSign needs.

### 2.2. Port interfaces

`ITokenCache` — `TryGetAsync(key, ct)`, `SetAsync(key, value, ct)`, `RemoveAsync(key, ct)`. Identical signature to the eInvoice port.

`ITokenCacheKeySelector` — `string Compose(MisaESignOptions options)`. Default: `$"{options.UserName}|{options.ClientId}|{new Uri(options.BaseUrl).Host}"`.

`ICertificateSelector` — `Task<Certificate> SelectAsync(IReadOnlyList<Certificate> activeOnly, CancellationToken ct)`. Called only after the SDK has already filtered to `keyStatus == ACTIVE`; throwing `NoActiveCertificateException` from inside the selector is allowed but the default selector throws *before* invoking the selector (when the active list is empty).

`ISystemClock` — `DateTimeOffset UtcNow { get; }`. Wraps `TimeProvider.System` by default; tests inject a fake.

`ICorrelationIdAccessor` — `string Current { get; }`. Mirrors the eInvoice port.

`IMisaESignWireClient` (Application-layer port, sole concrete impl in Infrastructure) — one method per MISA endpoint:

```csharp
Task<AuthSession> LoginAsync(string userName, string password, CancellationToken ct);
Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct);
Task<IReadOnlyList<Certificate>> ListCertificatesByUserIdAsync(CancellationToken ct);
Task<PdfHashOutput> HashPdfAsync(Certificate cert, byte[] pdfBytes, SignatureInfo sigInfo, CancellationToken ct);
Task<SignTransaction> SubmitSignHashAsync(Certificate cert, string userId, string dataToBeDisplayed, PdfHashOutput hash, string documentName, CancellationToken ct);
Task<SignStatusSnapshot> GetSignStatusAsync(string transactionId, CancellationToken ct);
Task<byte[]> AttachSignatureAsync(Certificate cert, PdfHashOutput hash, string signatureData, CancellationToken ct);
```

`PdfHashOutput` and `SignStatusSnapshot` are Application-layer records (defined below).

### 2.3. Application records

```csharp
// Output of /documents/hash — kept opaque to consumers; needed for /attachment.
public sealed record PdfHashOutput(
    string DocumentId,
    string DocumentBytes,    // base64 — sourced from MISA's response
    string DocumentHash,
    string Sh,
    string SignatureName,
    string Digest);          // fed into /Signing/hash

// One snapshot of /Signing/status.
public sealed record SignStatusSnapshot(
    SignStatus Status,
    string? ErrorCode,
    string? ErrorDescription,
    string TransactionId,
    string? FirstSignatureData);  // signatures[0].signature when SUCCESS
```

### 2.4. Orchestrator request/result

`SignPdf` use case input — `SignPdfWorkRequest`:

| Field | Type | Required | Notes |
|---|---|---|---|
| `Pdf` | `PdfDocument` | yes | Input PDF bytes |
| `SignatureInfo` | `SignatureInfo` | yes | Display + position metadata. Requires `SignatureName`, `LogoImage`, `SignatureDescription`, `RenderingMode`, `HashAlgorithm = SHA256` |
| `DocumentName` | `string` | yes | ≤100 chars per MISA §4.5 |
| `DocumentId` | `string?` | no | Generated as a GUID if not supplied; ≤36 chars per MISA §4.5 |
| `DataToBeDisplayed` | `string` | yes | Shown on the user's MISA eSign app at confirmation time. Can be HTML. |

`SignPdf` use case output — `SignPdfWorkResult`:

| Field | Type | Notes |
|---|---|---|
| `SignedPdf` | `SignedDocument` | The output bytes |
| `TransactionId` | `string` | For consumer auditing / cross-correlation with MISA logs |
| `CertificateKeyAlias` | `string` | Which cert was used |
| `CompletedAtUtc` | `DateTimeOffset` | Per `ISystemClock` |

---

## 3. Infrastructure layer — wire DTOs (verbatim MISA shapes)

The wire DTOs live in `MisaConnect.ESign.Infrastructure.ESign.Wire/`. Field names, casing, and envelope shapes are reproduced from [docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md](../../docs/misa-api-reference/T%C3%A0i%20li%E1%BB%87u%20t%C3%ADch%20h%E1%BB%A3p%20API%20eSign%20RemoteSigning%20-%20V2.md) unchanged. Detailed envelopes are documented in [contracts/wire-envelopes.md](./contracts/wire-envelopes.md); a summary here:

### 3.1. `LoginDtos`

- Request: `{ userName, password }`
- Response envelope: `{ status: { type, code, message, error, errorCode, devMsg, userMsg }, data: { accessToken, remoteSigningAccessToken, tokenType, expiresIn, refreshToken, user: { id, email, phoneNumber, firstName, lastName, username }, default: { email, phoneNumber, appAuthenticator }, verifyUser?: { emailsVerify, phoneNumberIsVerify, isChangePassword } } }`

### 3.2. `RefreshTokenDtos`

- Request: `{ refreshToken }`
- Response: `{ remoteSigningAccessToken, accessToken, refreshToken, expiresIn }` (no envelope around `data` — note this is different from login)

### 3.3. `CertificateDtos`

- Response array: `[{ userId, keyAlias, appName, keyStatus, certificate, certiticateChain[3], certStatus, effectiveDate, expirationDate, emailName, isAutoSign }, ...]`

### 3.4. `HashDtos`

- Request: `{ certificate, certificateChain[], pdfDocs[], xmlDocs[], wordDocs[], excelDocs[] }`. In slice 1 only `pdfDocs` is populated.
- `pdfDocs[i]` request element: `{ DocumentId, FileToSign (base64), SignatureInfo: { ... see §4.2 ... } }`
- Response: `{ pdfDocs[], xmlDocs[], wordDocs[], excelDocs[] }`. Slice 1 reads only `pdfDocs`.
- `pdfDocs[i]` response element: `{ documentId, documentBytes, documentHash, sh, signatureName, digest }`

### 3.5. `SignHashDtos`

- Request: `{ DataToBeDisplayed, UserId, CertAlias, Documents: [{ DocumentId, FileToSign, DocumentName }] }`. Note PascalCase on outer fields per MISA's §3.5 example.
- Response: `{ transactionId }`

### 3.6. `SignStatusDtos`

- Response: `{ status, errorCode, errorDescription, transactionId, signatures: [{ documentId, signature }] }`

### 3.7. `AttachmentDtos`

- Request: `{ certificate, certificateChain[], pdfDocs[], xmlDocs[], wordDocs[], excelDocs[] }` — same wrapper as hash. Slice 1 populates `pdfDocs[i] = { signature, documentId, documentBytes, digest, signatureName, sh, documentHash }`.
- Response: `{ pdfDocs[], xmlDocs[], wordDocs[], excelDocs[] }` where `pdfDocs[i] = { documentId, document }` (base64 signed file).

### 3.8. `ResponseErrorDto`

`{ error, errorCode, devMsg, userMsg }` — used in any 4xx body and inside `status` on the login envelope.

---

## 4. Configuration shape (Infrastructure.Configuration)

```csharp
public sealed class MisaESignOptions
{
    public const string SectionName = "Misa:ESign";
    public const string ProductionHost = "esignapp.misa.vn";

    public ESignEnvironment Environment { get; set; }   // Sandbox | Production
    public string BaseUrl { get; set; } = "";           // e.g. https://esignapp.misa.vn
    public string ClientId { get; set; } = "";          // x-clientId header value
    public string ClientKey { get; set; } = "";         // x-clientKey header value
    public string UserName { get; set; } = "";          // MISAID userName
    public string Password { get; set; } = "";          // MISAID password
    public MisaESignPollingOptions Polling { get; set; } = new();
    public MisaESignTransportRetryOptions TransportRetry { get; set; } = new();
    public MisaESignErrorOptions Errors { get; set; } = new();
}

public sealed class MisaESignPollingOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

public sealed class MisaESignTransportRetryOptions
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(2);
}

public sealed class MisaESignErrorOptions
{
    public bool IncludeRawErrorMessage { get; set; } = false;
}
```

Validator rules (see research R-4 for the full list):
- All `string` fields non-empty.
- `BaseUrl` is a valid absolute URI with scheme `https`.
- `Environment == Production` ⇒ host **must** equal `esignapp.misa.vn`.
- `Environment == Sandbox` ⇒ host **must not** equal `esignapp.misa.vn`.
- `Polling.Interval > 0`, `Polling.TotalTimeout > Polling.Interval`.
- `TransportRetry.MaxAttempts ≥ 1`, `TransportRetry.MaxDelay ≥ TransportRetry.BaseDelay`.

---

## 5. Client-facing DTOs (Client.Dtos)

The Client surface is intentionally small.

```csharp
public sealed record SignPdfRequestDto(
    byte[] Pdf,
    string DocumentName,
    string SignerName,
    string Location,
    string Reason,
    string Contact,
    string LogoImageBase64,
    string DataToBeDisplayed,            // shown on the signer's MISA eSign app at confirmation
    // Optional rendering / positioning fields:
    int? Page = null,
    int? PositionX = null,
    int? PositionY = null,
    int? Width = null,
    int? Height = null,
    int? RenderingMode = null,           // 0|1|2; default 0
    string? DocumentId = null,           // auto-generated GUID if null
    string? DisplayText = null,
    bool ShowSignedDate = true,
    IReadOnlyList<SignaturePosInfoDto>? AdditionalSignaturePositions = null);

public sealed record SignaturePosInfoDto(int PositionX, int PositionY, int Width, int Height, int Page);

public sealed record SignPdfResultDto(
    byte[] SignedPdf,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc);

public sealed record CertificateDto(            // surfaced via IMisaESignClient.ListCertificatesAsync if exposed; not strictly needed in slice 1
    string KeyAlias,
    string UserId,
    string AppName,
    string KeyStatus,
    DateTimeOffset? EffectiveDate,
    DateTimeOffset? ExpirationDate,
    bool IsAutoSign);
```

In slice 1 the **only** facade method is `SignPdfAsync(SignPdfRequestDto, CancellationToken)`. Cert listing is intentionally kept internal to the orchestrator for slice 1; a future slice may expose `ListCertificatesAsync` for consumers wanting to render a picker UI.

---

## 6. State transitions

### 6.1. Token lifecycle (per cache key)

```
                (cache miss, first call)
[None] ────────────────────────────────────────► [Cached & valid]
   ▲                                                  │
   │                                                  │ on cache hit + ExpiresAtUtc > now
   │                                                  ▼
   │                                              [Reused]
   │
   │ refresh-success
   │
[Refreshing] ◄────── on 401 OR clock-based proactive (FR-005)
   │
   │ refresh-fail (FR-004 terminal)
   ▼
[Cleared + AuthenticationFailedException]
```

Concurrency: at most one in-flight refresh per cache key (`SingleFlightRefresh`). All concurrent 401-observers attach to the same `Task<AccessToken>`. Failure propagates as the same exception instance to all waiters (FR-028).

### 6.2. Sign transaction lifecycle

```
[Submitting] ──Signing/hash 200──► [Polling: PENDING]
     │                                  │
     │ Signing/hash 4xx                 │  GET Signing/status/{tx} = SUCCESS
     ▼                                  ▼
[SignRejectedException]            [Attaching] ──/attachment 200──► [Returned: SignedDocument]
                                        │                                │
                                        │ /attachment 4xx                │ /attachment 5xx (exhausted)
                                        ▼                                ▼
                                    ESignException(AttachmentRejected) ESignTransportException

[Polling: PENDING] ──/status = FAILED──► SignTerminalStateException(TerminalStatus = FAILED)
[Polling: PENDING] ──/status = CANCELLED──► SignTerminalStateException(TerminalStatus = CANCELLED)
[Polling: PENDING] ──deadline elapsed──► SignTimeoutException(TransactionId)
```

The orchestrator does not attempt to resume after a terminal/timeout outcome — MISA's transaction is left as-is (no cancel endpoint documented). The `TransactionId` is preserved on every relevant exception for consumer-side reconciliation.

### 6.3. Transport-retry budget (per HTTP call)

```
[Attempt 1] ── 2xx ─► [Returned to caller layer]
     │ ── 5xx/429/timeout/HttpRequestException
     ▼
[Backoff = base + jitter, optionally Retry-After]
     │
     ▼
[Attempt 2] ── 2xx ─► [Returned]
     │ ── transient
     ▼
[Backoff = 2× base + jitter, capped at MaxDelay]
     │
     ▼
[Attempt 3] ── 2xx ─► [Returned]
     │ ── transient
     ▼
[ESignTransportException(LastStatusCode, AttemptCount=3)]
```

This budget is per-call and does NOT include the auth-401-refresh-then-retry-once (FR-004), which lives in the inner `RemoteSigningAuthHandler` and consumes its own one-attempt budget.

---

## 7. Validation rules (cross-layer summary)

| Layer | Validation |
|---|---|
| Client `SignPdfRequestDto` | `Pdf != null && Pdf.Length > 0`; `DocumentName` non-empty and ≤100 chars; `SignerName`, `Location`, `Reason`, `Contact`, `LogoImageBase64`, `DataToBeDisplayed` non-empty; `DocumentId` (if supplied) ≤36 chars; `Page` (if supplied) ≥1; `RenderingMode` (if supplied) ∈ {0,1,2}. Performed in `SignPdfRequestValidator` (Application) before any MISA call. |
| Application `SignPdf` use case | Asserts `EnsureAccessToken` returned a non-empty `Username` + `UserId` (required by `Signing/hash`); asserts cert has a 3-element `CertificateChain`; asserts `PdfHashOutput.Digest` is non-empty before submitting `Signing/hash`. |
| Infrastructure `MisaESignOptionsValidator` | See §4 above. Runs at startup via `.ValidateOnStart()`. |
| Infrastructure wire deserialization | Defensive: missing `data` on a 200 login → `ESignException(MisaUnknown, "EmptyResponse")`. Missing `transactionId` on `Signing/hash` 200 → same. Unknown `status` value on `Signing/status` → `SignTerminalStateException(TerminalStatus = unknown)` per R-7. |

---

## 8. Audit pointers

- Every Domain type has zero `using` statements outside `System.*`. Verified by the namespace-audit test (extended to cover the ESign Domain csproj).
- Every Application use case method takes `CancellationToken`.
- Every public exception inherits `ESignException` and accepts `(string rawCode, string detail, string correlationId, Exception? inner)` so the mapper has a single construction path.
- Every wire DTO is `internal` and lives under `Infrastructure.ESign.Wire`. Mapping between wire and Domain is the only allowed bridge.

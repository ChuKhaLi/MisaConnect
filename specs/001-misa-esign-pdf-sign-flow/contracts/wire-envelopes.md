# Wire-envelope contract — MISA eSign RemoteSigning (slice 1 endpoints)

This file pins the seven HTTP request/response shapes that `MisaConnect.ESign.Infrastructure.ESign.Wire/` must mirror verbatim per Constitution Principle IV.

Source of truth: [docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md](../../../docs/misa-api-reference/T%C3%A0i%20li%E1%BB%87u%20t%C3%ADch%20h%E1%BB%A3p%20API%20eSign%20RemoteSigning%20-%20V2.md). When that doc and the Postman collection disagree, the Postman collection wins per [docs/misa-esign-spec-plan.md](../../../docs/misa-esign-spec-plan.md) — divergences are flagged inline below as **FLAG**.

Common header set for all seven endpoints (the auth/login endpoints use a subset — noted per endpoint):

| Header | Value | Notes |
|--------|-------|-------|
| `Content-Type` | `application/json` | POSTs only |
| `x-clientId` | `MisaESignOptions.ClientId` | Required everywhere |
| `x-clientKey` | `MisaESignOptions.ClientKey` | Required everywhere |
| `AuthorizationRM` | `"Bearer " + remoteSigningAccessToken` | Required on `Certificates/by-userId`, `documents/hash`, `Signing/hash`, `Signing/status/{tx}`, `documents/attachment`. Absent on `login-api` and `refreshtoken`. |

Note on header naming: MISA's doc §3.2 uses `clientId`/`clientKey` (no `x-` prefix) on the 2FA endpoint and `x-clientId`/`x-clientKey` everywhere else. Slice 1 does not call 2FA. The implementation injects `x-clientId`/`x-clientKey` on every slice-1 request.

---

## E1. `POST /api/auth/api/v1/auth/login-api`

**Used by**: `EnsureAccessToken` (cache miss) and after a refresh failure.

**Request body**

```json
{
  "userName": "string",
  "password": "string"
}
```

**Response body** (200)

```json
{
  "status": {
    "type": "string",
    "code": 200,
    "message": "string",
    "error": false,
    "errorCode": 0,
    "devMsg": "string",
    "userMsg": "string"
  },
  "data": {
    "accessToken": "string",
    "remoteSigningAccessToken": "string",
    "tokenType": "Bearer",
    "expiresIn": 3600,
    "refreshToken": "string",
    "user": {
      "id": "string",
      "email": "string",
      "phoneNumber": "string",
      "firstName": "string",
      "lastName": "string",
      "username": "string"
    },
    "default": { "email": false, "phoneNumber": false, "appAuthenticator": true },
    "verifyUser": { "emailsVerify": false, "phoneNumberIsVerify": true, "isChangePassword": true }
  }
}
```

**Error envelope** (4xx) — the `status` block is populated with `error: true`, `errorCode`, `devMsg`, `userMsg`. `data` may be omitted or `null`. `errorCode = 122` ⇒ MISA wants 2FA (out of slice 1 — `AuthenticationFailedException.Requires2FA = true`).

---

## E2. `POST /webdev/api/auth/api/v1/auth/refreshtoken`

**Used by**: `RemoteSigningAuthHandler` on 401 and `EnsureAccessToken` on proactive refresh.

**Request body**

```json
{
  "refreshToken": "string"
}
```

**Response body** (200) — **NOT** wrapped in a `status`/`data` envelope.

```json
{
  "remoteSigningAccessToken": "string",
  "accessToken": "string",
  "refreshToken": "string",
  "expiresIn": 3600
}
```

**FLAG** — the doc shows `refreshtoken` returning a flat object (no envelope) while `login-api` returns the envelope. The implementation must support both shapes when reading (try envelope first, fall back to flat) and match the doc when writing. The integration tests against the sandbox confirm the live shape.

**Error envelope** (4xx) — `ResponseError { error, errorCode, devMsg, userMsg }` directly in the body (no `status` wrapper).

---

## E3. `GET /external/esrm/service/general/api/v1/Certificates/by-userId`

**Used by**: `ListActiveCertificates`.

**Request**: GET, no body. The user is identified via the `AuthorizationRM` header — there is no userId query parameter.

**Response body** (200)

```json
[
  {
    "userId": "guid",
    "keyAlias": "guid",
    "appName": "string",
    "keyStatus": "ACTIVE | INACTIVE",
    "certificate": "base64-string",
    "certiticateChain": ["base64-signing", "base64-intermediate", "base64-root"],
    "certStatus": "ACTIVE | INACTIVE",
    "effectiveDate": "2026-05-18T00:00:00Z",
    "expirationDate": "2027-05-18T00:00:00Z",
    "emailName": "string",
    "isAutoSign": false
  }
]
```

Notes:
- `certiticateChain` (sic) — MISA's documented spelling. Preserved verbatim.
- Exactly 3 elements expected. Defensive deserialization tolerates other lengths and surfaces a `ESignException(MisaUnknown, "InvalidCertChainLength")`.

**Error envelope** (4xx) — `ResponseError`.

---

## E4. `POST /external/esrm/service/document/api/v1/documents/hash`

**Used by**: `HashPdfDocument`.

**Request body** (slice 1: only `pdfDocs` populated)

```json
{
  "certificate": "base64-from-Certificate.Certificate",
  "certificateChain": ["base64-signing", "base64-intermediate", "base64-root"],
  "pdfDocs": [
    {
      "DocumentId": "guid (<=36 chars)",
      "FileToSign": "base64-of-the-input-PDF-bytes",
      "SignatureInfo": {
        "TextColor": 0,
        "PositionX": 100,
        "PositionY": 100,
        "Width": 200,
        "Height": 80,
        "FontSize": 12,
        "FontData": "base64-font",
        "SignatureImage": "base64-signature-image",
        "Page": 1,
        "SignatureName": "signer name",
        "HashAlgorithm": "SHA256",
        "LogoImage": "base64-logo",
        "SignatureDescription": {
          "SignedBy": "Signer Name",
          "ShowSignedDate": true,
          "Location": "Hà Nội",
          "Reason": "Approved",
          "Contact": "signer@example.com",
          "DisplayText": "optional custom text\\nwith newlines"
        },
        "RenderingMode": 0,
        "SignaturePosInfos": [
          { "positionX": 50, "positionY": 50, "width": 100, "height": 40, "page": 2 }
        ]
      }
    }
  ],
  "xmlDocs": [],
  "wordDocs": [],
  "excelDocs": []
}
```

Note: in the request, top-level field names inside `SignatureInfo` are PascalCase per the doc; inside `SignaturePosInfos` the doc uses camelCase. Both are reproduced verbatim — this is the documented inconsistency and slice 1 does **not** "fix" it.

**Response body** (200)

```json
{
  "pdfDocs": [
    {
      "documentId": "guid",
      "documentBytes": "base64",
      "documentHash": "base64",
      "sh": "base64",
      "signatureName": "string",
      "digest": "base64"
    }
  ],
  "xmlDocs": [],
  "wordDocs": [],
  "excelDocs": []
}
```

**Error envelope** (4xx) — `ResponseError`.

---

## E5. `POST /external/esrm/service/signing/api/v1/Signing/hash`

**Used by**: `SubmitSignHash`.

**Request body**

```json
{
  "DataToBeDisplayed": "html-or-plain-text shown to the signer on MISA eSign mobile",
  "UserId": "guid (from login-api data.user.id)",
  "CertAlias": "guid (= Certificate.KeyAlias)",
  "Documents": [
    {
      "DocumentId": "guid (same as hash request DocumentId)",
      "FileToSign": "base64 (= digest from /documents/hash response)",
      "DocumentName": "string (<=100 chars)"
    }
  ]
}
```

**Response body** (200)

```json
{
  "transactionId": "guid"
}
```

**Error envelope** (4xx) — `ResponseError`. MISA's doc highlights one frequent case: "user not connected to eSign account" surfaces as a documented errorCode — this maps to `SignRejectedException.RequiresUserCertSetup = true` (see [error-mapping.md](./error-mapping.md)).

---

## E6. `GET /external/esrm/service/signing/api/v1/Signing/status/{transactionId}`

**Used by**: `PollSignStatus` (polled until terminal state per FR-011).

**Request**: GET, no body.

**Response body** (200)

```json
{
  "status": "PENDING | SUCCESS | FAILED | CANCELLED",
  "errorCode": "string (empty when status == PENDING|SUCCESS)",
  "errorDescription": "string",
  "transactionId": "guid",
  "signatures": [
    { "documentId": "guid", "signature": "base64" }
  ]
}
```

Notes:
- `signatures` is populated only when `status == SUCCESS`. Slice 1 reads `signatures[0]` since slice 1 submits exactly one document per transaction.
- The doc lists `PENDING / SUCCESS / FAILED` but the spec extends to include `CANCELLED` (mentioned in MISA's edge-case copy). The implementation accepts all four; anything else is treated as a terminal "unknown" state per [research.md R-7](../research.md#r-7-sign-pipeline-orchestration--polling-state-machine).

**Error envelope** (4xx) — `ResponseError`. Note that `status: "FAILED"` is **not** a 4xx — it's a 200 carrying an `errorCode`; the orchestrator surfaces `SignTerminalStateException`.

---

## E7. `POST /external/esrm/service/document/api/v1/documents/attachment`

**Used by**: `AttachSignature` (only when `Signing/status` returned `SUCCESS`).

**Request body** (slice 1: only `pdfDocs` populated)

```json
{
  "certificate": "base64",
  "certificateChain": ["base64", "base64", "base64"],
  "pdfDocs": [
    {
      "signature": "base64 (from Signing/status signatures[0].signature)",
      "documentId": "guid (from hash response)",
      "documentBytes": "base64 (from hash response)",
      "digest": "base64 (from hash response)",
      "mainDom": "string (from hash response — PDF: not required)",
      "signatureName": "string (from hash response)",
      "sh": "base64 (from hash response)",
      "signatureId": "string (from hash response — PDF: not required)",
      "documentHash": "base64 (from hash response)"
    }
  ],
  "xmlDocs": [],
  "wordDocs": [],
  "excelDocs": []
}
```

Per MISA §4.7 (Doc_Attackment [sic]) — `mainDom` and `signatureId` are required for Excel/Word/XML but not for PDF. Slice 1 only signs PDFs, so the implementation populates them when present in the hash response and omits them otherwise.

**Response body** (200)

```json
{
  "pdfDocs": [
    {
      "documentId": "guid",
      "document": "base64 (the final signed PDF)"
    }
  ],
  "xmlDocs": [],
  "wordDocs": [],
  "excelDocs": []
}
```

`document` is base64-decoded into `byte[]` and returned to the consumer as `SignPdfResultDto.SignedPdf`.

**Error envelope** (4xx) — `ResponseError`. Likely codes: invalid signature data, signature/cert mismatch, malformed hash inputs. Mapped to `ESignException(category = AttachmentRejected)`.

---

## Wire-format compliance checklist (for `Infrastructure.ESign.Wire/`)

The following invariants are enforced by unit tests in `tests/MisaConnect.ESign.UnitTests/`:

1. ✅ Field names match this document verbatim (case-sensitive). Including `certiticateChain` typo.
2. ✅ Required fields are emitted (no `JsonIgnore` on a required field).
3. ✅ Optional/absent fields are either omitted entirely or sent as `null` — matching how the live sandbox responds.
4. ✅ Date fields use ISO-8601 with `Z` suffix when serializing; deserialization tolerates `Z`, offset, and unspecified-kind inputs.
5. ✅ Base64 fields are strings (not byte arrays) on the wire; conversion happens in mappers.
6. ✅ `RememberRoot` / chain ordering: `certiticateChain[0]` is the signing cert, `[1]` MISA CA intermediate, `[2]` NEAC root — preserved on round-trip.

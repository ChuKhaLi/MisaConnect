# Wire-envelope contract — slice 3 additions (`/documents/hash` and `/documents/attachment` for XML/Word/Excel)

This file pins how slice 3 strengthens the existing `/documents/hash` (E5 in the slice-1 numbering) and `/documents/attachment` (E7 in the slice-1 numbering) request/response shapes that `MisaConnect.ESign.Infrastructure.ESign.Wire/` must mirror verbatim per Constitution Principle IV.

Source of truth: [docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md](../../../docs/misa-api-reference/T%C3%A0i%20li%E1%BB%87u%20t%C3%ADch%20h%E1%BB%A3p%20API%20eSign%20RemoteSigning%20-%20V2.md) — specifically §4.1.1 (PDF/Word/Excel hash-request entry), §4.1.2 (XML hash-request entry), §4.2 (`SignatureInfo` block), §4.6 (`Doc_Attackment` shape — preserving the misspelling), and §4.15 (per-format hash-response field set). When the doc and the Postman collection disagree, the Postman collection wins per [docs/misa-esign-spec-plan.md](../../../docs/misa-esign-spec-plan.md) — divergences are flagged inline as **FLAG**.

The seven slice-1 endpoint shapes (E1–E7) plus the slice-2 additions (E8 — `/two-factor-auth`, E9 — `/resend-otp-auth`) remain authoritative under their respective slices' `wire-envelopes.md`. This file adds:
- **E5.x** — strengthened `/documents/hash` request/response with typed `xmlDocs` / `wordDocs` / `excelDocs` arrays (replaces the slice-1 `List<object>` placeholders).
- **E7.x** — strengthened `/documents/attachment` request/response with typed per-format arrays.

The slice-1 PDF request/response shape on E5 (`pdfDocs[*]` with `PdfDocRequestDto` / `PdfHashOutputDto`) and E7 (`pdfDocs[*]` with `AttachmentPdfDocRequestDto` / `AttachmentPdfDocResponseDto`) is preserved byte-identically per FR-065.

---

## Header convention reminder

| Header | Value | Applies to E5 / E7 per-format? |
|--------|-------|---|
| `Content-Type` | `application/json` | Both |
| `x-clientId` | `MisaESignOptions.ClientId` | Both |
| `x-clientKey` | `MisaESignOptions.ClientKey` | Both |
| `AuthorizationRM` | `"Bearer " + remoteSigningAccessToken` | Both — these are post-auth endpoints; the slice-1 `RemoteSigningAuthHandler` injects the header. |
| `X-Correlation-Id` | `ICorrelationIdAccessor.Current` | Both (FR-064) |

No header changes for slice 3. The per-format DTOs ride on the existing header set.

---

## E5.x. `POST /external/esrm/service/document/api/v1/documents/hash` — strengthened per-format arrays

**Used by**: `MisaESignWireClient.HashPdfAsync` (slice 1, unchanged), `HashXmlAsync` (slice 3), `HashWordAsync` (slice 3), `HashExcelAsync` (slice 3).

**Request body** — full envelope (slice 3 fills the previously-`List<object>` arrays with typed per-format entries; the slice-1 `pdfDocs[*]` shape is unchanged):

```json
{
  "certificate": "<base64 cert>",
  "certificateChain": ["<chain[0]>", "<chain[1]>", "<chain[2]>"],
  "pdfDocs":   [ /* PdfDocRequestDto — slice-1 shape, unchanged */ ],
  "xmlDocs":   [ /* XmlHashDocRequestDto — see E5.1 */ ],
  "wordDocs":  [ /* WordHashDocRequestDto — see E5.2 */ ],
  "excelDocs": [ /* ExcelHashDocRequestDto — see E5.3 */ ]
}
```

Per FR-052, exactly one of the four arrays is populated on any given request (matching the called facade); the other three are sent as empty arrays. (Slice 1 currently sends empty `xmlDocs`/`wordDocs`/`excelDocs` arrays on every PDF call — slice 3 preserves that wire shape, just with typed empty arrays instead of `List<object>` empty arrays. Functionally identical on the wire.)

### E5.1. `xmlDocs[*]` — XML hash-request entry (§4.1.2)

```json
{
  "DocumentId": "string",
  "FileToSign": "<raw XML text — NOT base64>",
  "SignatureInfo": { /* per §4.2; slice-3 XmlSignatureContextMapper fills no-visualization defaults */ }
}
```

| Field | Type | Notes |
|---|---|---|
| `DocumentId` | `string` | Consumer-supplied or SDK-generated (mapper default). Same convention as PDF. |
| `FileToSign` | `string` | **Raw XML text**, NOT base64. MISA §4.1.2 unambiguously calls out "text content" for XML. The slice-3 `string` overload forwards the consumer's input verbatim; the `byte[]` overload UTF-8-decodes via `Encoding.UTF8.GetString(...)` at the Client-layer mapper before forwarding. |
| `SignatureInfo` | object per §4.2 | Populated by `XmlSignatureContextMapper.ToWireSignatureInfo(xmlSignatureContext)`. The mapper supplies the consumer-provided XAdES-meaningful fields (`SignatureName`, `HashAlgorithm`, `SignatureDescription`) and fills MISA-documented "no signature visualization" defaults (`LogoImage = ""`, `RenderingMode = 0`, all visual-position fields null/omitted) for the fields the slim `XmlSignatureContext` Domain record does not expose. The serialized JSON omits null fields via `[JsonIgnore(Condition = WhenWritingNull)]`. |

### E5.2. `wordDocs[*]` — Word hash-request entry (§4.1.1)

```json
{
  "DocumentId": "string",
  "FileToSign": "<base64 of the .docx bytes>",
  "SignatureInfo": { /* per §4.2; full slice-1 visual surface forwarded verbatim per FR-059 */ }
}
```

Same shape as slice-1 PDF (§4.1.1 is shared across PDF/Word/Excel). The slice-3 Word facade `SignWordAsync(SignWordRequestDto)` accepts the consumer's `SignWordRequestDto.Word` `byte[]`, base64-encodes it for the `FileToSign` field, and forwards the slice-1-shaped `SignatureInfo` verbatim (including visual position fields).

### E5.3. `excelDocs[*]` — Excel hash-request entry (§4.1.1)

```json
{
  "DocumentId": "string",
  "FileToSign": "<base64 of the .xlsx bytes>",
  "SignatureInfo": { /* per §4.2; full slice-1 visual surface forwarded verbatim per FR-059 */ }
}
```

Symmetric with `wordDocs[*]`.

### E5.4. Response — per-format hash-output arrays (§4.15)

```json
{
  "pdfDocs":   [ /* PdfHashOutputDto — slice-1 shape, unchanged */ ],
  "xmlDocs":   [ /* XmlHashOutputDto — see below */ ],
  "wordDocs":  [ /* WordHashOutputDto — see below */ ],
  "excelDocs": [ /* ExcelHashOutputDto — see below */ ]
}
```

| Array | Per-entry shape | Notes |
|---|---|---|
| `xmlDocs[*]` | `{ documentId, document, signatureId, digest, sh }` | XML uses `document` (raw text), NOT `documentBytes`. Carries `signatureId` (required for XML attachment per §4.6). |
| `wordDocs[*]` | `{ documentId, documentBytes, signatureId, digest, mainDom }` | Word uses `documentBytes` (base64). Carries `mainDom` (required for Word attachment per §4.6) and `signatureId`. |
| `excelDocs[*]` | `{ documentId, documentBytes, signatureId, digest, mainDom }` | Identical shape to `wordDocs[*]`. |

Field-casing note: response-side fields are camelCase per MISA §4.15 (contrast with the request-side §4.1 PascalCase). The wire DTOs match exactly.

**Per FR-053**, the Application-layer `HashXmlDocument` / `HashWordDocument` / `HashExcelDocument` use cases assert non-empty values for every required field of the requested format before proceeding to `/Signing/hash`. If any required field is missing or empty, the use case raises `ESignGeneralException(category = HashRejected, rawCode = "IncompleteHashResponse", format = Xml | Word | Excel)`.

**FLAG** — the doc shows the PDF response array uses `documentBytes` while the XML response array uses `document`. This is a real per-format difference (PDF is binary→base64; XML is text). Slice 3 honors both verbatim.

---

## E7.x. `POST /external/esrm/service/document/api/v1/documents/attachment` — strengthened per-format arrays (§4.6 `Doc_Attackment`)

**Used by**: `MisaESignWireClient.AttachSignatureAsync` (slice 1, unchanged), `AttachSignatureToXmlAsync` (slice 3), `AttachSignatureToWordExcelAsync` (slice 3 — shared for Word and Excel).

**MISA's spelling**: the doc row literally writes `Doc_Attackment` (sic). Per Constitution Principle IV, slice 3 preserves the misspelling verbatim on the wire DTOs and contracts.

**Request body** — full envelope:

```json
{
  "certificate": "<base64 cert>",
  "certificateChain": ["<chain[0]>", "<chain[1]>", "<chain[2]>"],
  "pdfDocs":   [ /* AttachmentPdfDocRequestDto — slice-1 shape, unchanged */ ],
  "xmlDocs":   [ /* XmlAttachmentDocRequestDto — see E7.1 */ ],
  "wordDocs":  [ /* WordExcelAttachmentDocRequestDto — see E7.2 (shared with Excel) */ ],
  "excelDocs": [ /* WordExcelAttachmentDocRequestDto — see E7.2 (same shape) */ ]
}
```

Per FR-056, exactly one of the four arrays is populated on any given request (matching the called facade); the other three are sent as empty arrays.

### E7.1. `xmlDocs[*]` — XML attachment-request entry (§4.6 minus `mainDom`)

```json
{
  "signature":     "<MISA-returned signature data>",
  "documentId":    "<from XmlHashOutput.DocumentId>",
  "documentBytes": "<from XmlHashOutput.Document — see flag>",
  "digest":        "<from XmlHashOutput.Digest>",
  "signatureName": "<from XmlSignatureContext.SignatureName>",
  "sh":            "<from XmlHashOutput.Sh>",
  "signatureId":   "<from XmlHashOutput.SignatureId — REQUIRED for XML per §4.6>",
  "documentHash":  "<from XmlHashOutput — empty string if MISA's XML hash response omits it>"
}
```

**FLAG** — the XML hash response carries `document` (text); the XML attachment request carries `documentBytes`. The XML attachment-request DTO's `documentBytes` field is populated by passing through the value MISA gave us in the hash response, regardless of whether the source semantic is "text" or "bytes". This is the published `Doc_Attackment` shape per §4.6 (the row enumerates `documentBytes` for all formats; XML hash-response divergence is independent of the attachment shape).

`mainDom` is NOT present in the XML attachment-request entry. XAdES has no internal `mainDom` notion, and MISA's §4.6 doc row explicitly marks `mainDom` as "bắt buộc với Excel và Word" (required for Excel and Word only). The wire DTO type `XmlAttachmentDocRequestDto` omits the field entirely (System.Text.Json sees no property, so nothing is serialized).

### E7.2. `wordDocs[*]` / `excelDocs[*]` — Word/Excel attachment-request entry (§4.6 full shape)

```json
{
  "signature":     "<MISA-returned signature data>",
  "documentId":    "<from WordExcelHashOutput.DocumentId>",
  "documentBytes": "<from WordExcelHashOutput.DocumentBytes>",
  "digest":        "<from WordExcelHashOutput.Digest>",
  "mainDom":       "<from WordExcelHashOutput.MainDom — REQUIRED for Word and Excel per §4.6>",
  "signatureName": "<from SignatureInfo.SignatureName>",
  "sh":            "<empty string — Word/Excel hash response does not carry sh per §4.15>",
  "signatureId":   "<from WordExcelHashOutput.SignatureId — REQUIRED for Word/Excel per §4.6>",
  "documentHash":  "<empty string — Word/Excel hash response does not carry documentHash per §4.15>"
}
```

Word and Excel share the same `WordExcelAttachmentDocRequestDto` shape because MISA §4.6 publishes one row that covers both. The Application-layer `AttachSignatureToWordExcel` use case (and the wire-client method) routes to the correct `wordDocs[0]` vs. `excelDocs[0]` array based on the `DocumentFormat format` parameter.

**Empty-string note**: `sh` and `documentHash` are part of the `Doc_Attackment` row in §4.6 but MISA does not surface them on the §4.15 Word/Excel hash responses. The slice-3 wire client populates them as empty strings on the request — matching what the slice-1 PDF path would do if the field were missing from the hash response. If MISA's behavior in production traffic indicates a stricter requirement (e.g. "non-empty `sh` required for Word"), the SDK adjusts via a single-line change at the wire-client request composition site, and the change is flagged in the wire-envelopes contract on the next slice/iteration. The integration test `SignWordHappyPathFakeServerTests.cs` covers the empty-string path against the in-repo fake; the `[SandboxFact]` `SignWordSandboxTests.cs` covers the real MISA behavior.

### E7.3. Response — per-format attachment-output arrays

```json
{
  "pdfDocs":   [ { "documentId": "string", "document": "<base64 signed PDF>" } ],   /* slice-1 — unchanged */
  "xmlDocs":   [ { "documentId": "string", "document": "<signed XML — UTF-8 text>" } ],
  "wordDocs":  [ { "documentId": "string", "document": "<base64 signed Word>" } ],
  "excelDocs": [ { "documentId": "string", "document": "<base64 signed Excel>" } ]
}
```

**Per-format payload extraction**: the Application-layer `AttachSignatureToXml` / `AttachSignatureToWordExcel` use cases extract the signed payload from the matching per-format array's `[0].document` field. The XML response carries raw text (UTF-8); the Word/Excel responses carry base64.

**Per FR-057 / FR-058**: the SDK returns only the matching array's payload. Other format arrays in the same response — even if populated — are ignored without error (US2 acceptance scenario 2). If the matching array is empty or missing on a successful HTTP response, the SDK raises `ESignGeneralException(category = AttachmentRejected, rawCode = "MissingSignedDocument", format = Xml | Word | Excel)`. The SDK does NOT silently return a payload from a different format's array.

**FLAG** — the response shape uses `document` (singular) for all four format arrays, even when the wire-side semantic is base64 (PDF/Word/Excel). The integration tests confirm this against the in-repo fake; sandbox tests confirm it against real MISA. If a future MISA change uses `documentBytes` for Word/Excel responses, the slice-3 DTOs would need a per-format response-DTO split — slice 3 ships against the current doc.

---

## E5 / E7 — what does NOT change

For the avoidance of doubt, the following slice-1 wire properties are byte-identical after slice 3 ships:

- `/documents/hash` request envelope shape (four-array structure) — unchanged.
- `pdfDocs[*]` request entry shape (`DocumentId + FileToSign + SignatureInfo`) — unchanged.
- `pdfDocs[*]` response entry shape (`documentId + documentBytes + documentHash + sh + signatureName + digest`) — unchanged.
- `/documents/attachment` request envelope shape — unchanged.
- `pdfDocs[*]` attachment-request entry shape — unchanged (no `mainDom`, no `signatureId`).
- `pdfDocs[*]` attachment-response entry shape — unchanged.
- Header set / casing — unchanged.

FR-065 / SC-021 enforcement: re-running slice 1's `SignPdfHappyPathSandboxTests.cs` and `SignPdfHappyPathFakeServerTests.cs` unchanged after slice 3 ships must pass byte-identically.

---

## Maintenance discipline

When MISA's published doc or Postman collection updates a per-format field:

1. Update the matching wire DTO in `Infrastructure.ESign.Wire/HashDtos.cs` or `AttachmentDtos.cs` to match the new field name / casing.
2. Update the row in this contract file to reflect the change.
3. Update the matching unit test fixture (`tests/MisaConnect.ESign.UnitTests/Signing/{Xml,Word,Excel}/Hash{Xml,Word,Excel}DocumentTests.cs`).
4. Update the `EsignFake/FakeMisaESignServer.cs` request inspection / response composition to match.
5. Bump the package version per Principle VII (typically a patch on the preview line for additive field changes, a minor for a new per-format response shape).

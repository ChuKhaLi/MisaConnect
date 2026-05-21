# Error-mapping contract — slice 3 additions (per-format `/documents/hash` and `/documents/attachment`)

This file pins how the slice-3 `ESignErrorMapper` translates MISA's `ResponseError { error, errorCode, devMsg, userMsg }` envelope (or its absence) into the typed slice-3 outputs for the three new format paths. Per FR-061, the mapping is **hybrid**: canonical `errorCode` table first, substring keyword fallback second — same pattern slice 2's `OtpErrorMapper` established.

Per FR-062, every typed exception surfaced by the per-format paths carries a `DocumentFormat Format` property matching the requested facade.

The slice-1 mapping tables under [specs/001-misa-esign-pdf-sign-flow/contracts/error-mapping.md](../../001-misa-esign-pdf-sign-flow/contracts/error-mapping.md) (A.1–A.7) remain authoritative for endpoints E1–E7's PDF behavior. The slice-2 mappings under [specs/002-misa-esign-2fa-otp/contracts/error-mapping.md](../../002-misa-esign-2fa-otp/contracts/error-mapping.md) (A.8, A.9) remain authoritative for the OTP endpoints. This file adds A.10 covering the per-format `/documents/hash` and `/documents/attachment` rejections introduced by slice 3.

---

## A.10. Per-format `/documents/hash` and `/documents/attachment` (E5.x / E7.x)

The per-format hash and attachment endpoints share MISA's response envelope with the slice-1 PDF paths. The mapping difference is in (a) how the `Format` property is populated on the surfaced exception and (b) the format-aware synthesizers that produce per-format synthesized `RawCode` values for the keyword-fallback layer.

### A.10.1. Layer 1 — canonical `errorCode` table (extends slice-1 A.4 / A.7)

When MISA returns a 4xx response with a `ResponseError` envelope whose `errorCode` matches one of the values below (case-insensitive exact match), the mapper produces the indicated typed exception with `Format = requestedFormat`. Initial seed values are sourced from the MISA Postman collection on 2026-05-22; new codes are added as the integration suite hits them.

| `errorCode` | Endpoint | Maps to | Notes |
|---|---|---|---|
| (slice-1 canonical codes for `/documents/hash`) | E5 | `ESignGeneralException(HashRejected, rawCode, Format = requestedFormat)` | Slice-1 canonical codes (e.g. `"InvalidCertificate"`) apply across all formats; format property is populated from the requested facade. |
| (slice-1 canonical codes for `/documents/attachment`) | E7 | `ESignGeneralException(AttachmentRejected, rawCode, Format = requestedFormat)` | Same — format-agnostic codes carry the requested format on the exception. |
| Any other value | (any) | (fall through to Layer 2) | |

Slice 3 does NOT introduce per-format canonical codes at this layer. MISA's Postman collection enumerates format-agnostic codes (the per-format semantics show up in the keyword layer below).

### A.10.2. Layer 2 — substring keyword fallback with format-aware synthesizers

When `errorCode` is empty or not in the canonical table, the mapper concatenates `errorCode ?? "" + " " + devMsg ?? "" + " " + userMsg ?? ""`, lower-cases it, and matches against the keyword table below. The first matching row wins. Both English and Vietnamese keywords are listed because MISA's `userMsg` is Vietnamese in production traffic.

#### A.10.2.1. `/documents/hash` (E5.x) — extends slice-1 A.4

| Keyword group (case-insensitive, substring) | Format scope | Maps to | Synthesized `RawCode` |
|---|---|---|---|
| `"xml"` AND (`"malformed"` OR `"invalid"` OR `"không hợp lệ"`) | Xml | `ESignGeneralException(HashRejected, …, Format = Xml)` | `"InvalidXmlInput"` |
| `"unsupported"` OR `"variant"` OR `"format not supported"` | (any) | `ESignGeneralException(HashRejected, …, Format = requestedFormat)` | `"UnsupportedDocumentVariant"` |
| `"cert"` OR `"certificate"` | (any) | `ESignGeneralException(HashRejected, …, Format = requestedFormat)` | `"InvalidCertificate"` (slice-1 synthesizer; format-agnostic) |
| `"hash"` OR `"digest"` | (any) | `ESignGeneralException(HashRejected, …, Format = requestedFormat)` | `"InvalidHash"` (slice-1) |
| `"file"` OR `"document"` | (any) | `ESignGeneralException(HashRejected, …, Format = requestedFormat)` | `"InvalidDocument"` (slice-1) |
| `"signature"` | (any) | `ESignGeneralException(HashRejected, …, Format = requestedFormat)` | `"InvalidSignatureInfo"` (slice-1) |
| (no match) | (any) | `ESignGeneralException(HashRejected, rawCode = MISA's errorCode verbatim or "EmptyErrorCode", Format = requestedFormat)` | preserves MISA's value (or `"EmptyErrorCode"` if absent) |

#### A.10.2.2. `/documents/attachment` (E7.x) — extends slice-1 A.7

| Keyword group (case-insensitive, substring) | Format scope | Maps to | Synthesized `RawCode` |
|---|---|---|---|
| `"mainDom"` OR `"main dom"` OR `"missing main"` | Word OR Excel | `ESignGeneralException(AttachmentRejected, …, Format = Word|Excel)` | `"MissingMainDom"` |
| `"signatureId"` OR `"signature id"` OR `"missing signature"` | Xml OR Word OR Excel | `ESignGeneralException(AttachmentRejected, …, Format = requestedFormat)` | `"MissingSignatureId"` |
| `"signature"` (without `"id"` qualifier) | (any) | `ESignGeneralException(AttachmentRejected, …, Format = requestedFormat)` | `"InvalidSignature"` (slice-1) |
| `"cert"` | (any) | `ESignGeneralException(AttachmentRejected, …, Format = requestedFormat)` | `"InvalidCertificate"` (slice-1) |
| `"doc"` OR `"hash"` | (any) | `ESignGeneralException(AttachmentRejected, …, Format = requestedFormat)` | `"InvalidHashInputs"` (slice-1) |
| (no match) | (any) | `ESignGeneralException(AttachmentRejected, rawCode = MISA's errorCode verbatim or "EmptyErrorCode", Format = requestedFormat)` | preserves MISA's value |

### A.10.3. Application-layer synthesized errors (pre-wire-rejection)

Two synthesized error paths are emitted by the Application-layer use cases BEFORE any wire-level rejection — they fire on successful HTTP responses where MISA returned a valid envelope but the per-format payload is incomplete (FR-053) or missing (FR-058).

| Use case | Trigger | Surfaced exception |
|---|---|---|
| `HashXmlDocument` | `XmlHashOutput.Document` OR `.SignatureId` OR `.Digest` OR `.Sh` is empty after wire deserialization | `ESignGeneralException(HashRejected, "IncompleteHashResponse", Format = Xml)` |
| `HashWordDocument` | `WordExcelHashOutput.DocumentBytes` OR `.SignatureId` OR `.Digest` OR `.MainDom` is empty | `ESignGeneralException(HashRejected, "IncompleteHashResponse", Format = Word)` |
| `HashExcelDocument` | Same field set as Word | `ESignGeneralException(HashRejected, "IncompleteHashResponse", Format = Excel)` |
| `AttachSignatureToXml` | Signed-bytes payload from `$.xmlDocs[0].document` is empty or the array is missing | `ESignGeneralException(AttachmentRejected, "MissingSignedDocument", Format = Xml)` |
| `AttachSignatureToWordExcel` (format = Word) | Signed-bytes payload from `$.wordDocs[0].document` is empty or the array is missing | `ESignGeneralException(AttachmentRejected, "MissingSignedDocument", Format = Word)` |
| `AttachSignatureToWordExcel` (format = Excel) | Signed-bytes payload from `$.excelDocs[0].document` is empty or the array is missing | `ESignGeneralException(AttachmentRejected, "MissingSignedDocument", Format = Excel)` |

These synthesized codes follow the same publication discipline as the wire-rejection synthesizers: stable identifiers (`IncompleteHashResponse`, `MissingSignedDocument`) consumers can match against in logs.

### A.10.4. Property-population rules (per exception)

Every typed exception surfaced on the slice-3 per-format paths:

1. `CorrelationId` — non-empty, sourced from `ICorrelationIdAccessor.Current` at the throw site.
2. `RawCode` — MISA's `errorCode` verbatim when populated; otherwise the synthesized identifier from the table.
3. `Detail` — built by the existing `ESignErrorMapper.BuildDetail(endpoint, rawCode, envelope, includeRawErrorMessage)`. Unchanged from slice 1.
4. `Format` — **always** matches the called facade per FR-062 (`Xml` / `Word` / `Excel` for slice-3 paths; `Pdf` for slice-1 PDF paths; `Unknown` for failures outside any facade call per the rules in [public-surface.md §5](./public-surface.md#5-additive-format-property-on-every-typed-exception)).
5. `Category` — unchanged from slice 1's mapping (`HashRejected` for `/documents/hash`; `AttachmentRejected` for `/documents/attachment`).

### A.10.5. Cross-cutting cases on E5.x / E7.x

| Trigger | Maps to | Notes |
|---|---|---|
| 401 from `/documents/hash` or `/documents/attachment` while in a facade call | `AuthenticationFailedException(requires2FA: false, Format = requestedFormat)` | Slice-1's refresh-on-401-then-retry-once via `RemoteSigningAuthHandler` runs first; if the retry also fails, the typed auth exception surfaces with the facade's format. |
| 429 / 5xx with retry budget exhausted | `ESignTransportException(Format = requestedFormat)` | Slice-1 `TransientFailureRetryHandler` applies; the orchestrator wraps the call site so the format is in scope. |
| `TaskCanceledException` (caller cancel) | (propagated unchanged) | — |
| `TaskCanceledException` (operation timeout — retry exhausted) | `ESignTransportException(LastStatusCode = null, Format = requestedFormat)` | Same as slice-1 cross-cutting. |
| Malformed JSON on a 200 | `ESignGeneralException(category = MisaUnknown, rawCode = "EmptyResponse" or "MalformedResponse", Format = requestedFormat)` | Same shape as slice-1; format property carries the requested facade. |

---

## B. Test-case manifest (one row per unit test)

The unit test `tests/MisaConnect.ESign.UnitTests/Errors/PerFormatErrorMapperTests.cs` covers exactly one row per table entry above. The test uses `[Theory]` with inline data so each row is a self-contained case:

- Layer 1 (canonical errorCode) cases on A.10.1: format-property-only rows since slice 3 does not introduce per-format canonical codes — verify slice-1 canonical codes propagate `Format = requestedFormat`. ~5 rows × 3 new formats = 15 rows.
- Layer 2 (keyword fallback) cases on A.10.2.1 (hash): 6 keyword groups + residual = 7 base rows × format-scope variations = ~12 rows.
- Layer 2 (keyword fallback) cases on A.10.2.2 (attachment): 6 keyword groups + residual = 7 base rows × format-scope variations = ~10 rows.
- Application-layer synthesized error cases on A.10.3: 6 rows (one per use-case trigger).
- Cross-cutting cases on A.10.5: 5 rows × 3 new formats = ~12 rows.

Total: ~55 inline rows across the per-format mapper test, executable in `< 200ms` collectively. Within the constitution's `< 30s` unit-suite budget for the cumulative slice-1+2+3 test suite (SC-022).

The Format-property contract test `tests/MisaConnect.ESign.UnitTests/Errors/FormatPropertyContractTests.cs` covers SC-019 separately: it constructs every typed exception across every documented construction context (slice-1 PDF paths + slice-2 OTP paths + slice-3 per-format paths + failures outside any facade) and asserts the Format value matches the expected rule. ~30 rows.

---

## C. Maintenance discipline

When a new MISA `errorCode` is observed in production traffic that doesn't match any current row:

1. Add a row to A.10.1 (canonical) or A.10.2 (keyword fallback) and a `[Theory]` inline-data row to `PerFormatErrorMapperTests.cs`.
2. Update `CHANGELOG.md` under `[Unreleased]` ("Mapper now recognizes errorCode XYZ for the Word format as MissingMainDom").
3. The change is a patch-level update on the preview line (no breaking surface change).

The discipline is the same as slice 1's `ESignErrorMapper` and slice 2's `OtpErrorMapper` — the test rows ARE the spec of the mapper's behavior.

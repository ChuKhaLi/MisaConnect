# Feature Specification: Fix ESRM `documents/hash` & `documents/attachment` empty-doc-array rejection

**Feature Branch**: `fix/esign-hash-empty-doc-arrays`
**Created**: 2026-06-14
**Status**: Done (implemented, tested, sandbox-verified; released as `MisaConnect.ESign 2.1.1`)
**Input**: Verified bug report [`bug-report.md`](./bug-report.md) (in this slice), cross-checked against source and `docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md`.

## Summary

`MisaConnect.ESign` cannot complete a remote sign against the live MISA ESRM service. A PDF
(or Word/Excel/XML) sign builds a request in which only the relevant document-type array is
populated, but the other three are left as **empty, non-null lists**. The wire serializer drops
only `null` (`JsonIgnoreCondition.WhenWritingNull`), so the request body ships
`"xmlDocs":[],"wordDocs":[],"excelDocs":[]`. MISA's `documents/hash` endpoint validates each
array as "must contain at least one document" (`Phải có ít nhất 1 tài liệu`) and returns **HTTP
400** before signing begins. The identical empty-list-default pattern is present on every
`documents/attachment` build path, so the attachment step is rejected the same way.

This was latent before `MisaConnect.ESign 2.1.0`: the prior ESRM routing defect meant these
requests never reached the validating service (they returned the SPA `index.html`). The 2.1.0
routing fix made the call reach the genuine endpoint, exposing the empty-array rejection.

This slice makes the unused document-type arrays **omit-when-absent**, fills a related
client-side gap (an unset `SignatureInfo.Page` is dropped on the wire, which MISA also rejects),
and surfaces MISA's `validationFailures` so the next such 400 is self-diagnosing.

### Verified defect status (from the bug report)

| # | Defect | Status | In scope |
|---|--------|--------|----------|
| 1 | `documents/hash` ships empty `xmlDocs`/`wordDocs`/`excelDocs` on a PDF-only sign → 400 | **Confirmed** (source-verified) | ✅ |
| 2 | `documents/attachment` has the identical empty-list-default pattern on all build paths | **Pattern confirmed** (source-verified); attachment rejection **inferred by symmetry**, not independently reproduced | ✅ |
| 3 | Unset `SignatureInfo.Page` (`int? = null`) is dropped on the wire; MISA requires `Page >= 1` | **Confirmed** — `SignPdfRequestValidator` rejects `Page < 1` but **not** `Page == null` | ✅ |
| 4 | The 400's `validationFailures` (the only field explaining the cause) are silently dropped | **Confirmed** — `ResponseErrorDto` has no `validationFailures` field | ✅ |
| 5 | The whole 400 envelope is swallowed: MISA sends `"error": null`, but `ResponseErrorDto.Error` was a non-nullable `bool`, so deserialization threw and the wire client's `Deserialize<T>` returned null → `errorCode=<none>` | **Confirmed during implementation** (the actual cause of the report's `errorCode=<none>`) | ✅ |
| — | Report claim "SDK surfaces `errorCode=<none>`" | **Correct in effect** — caused by defect 5 (`"error": null` broke parsing), not by the mapper. Once `Error` is nullable the body parses and the mapper surfaces `e400`. (An earlier static read mis-attributed this to the mapper; running the real body corrected it.) | ✅ (defect 5) |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - PDF/Word/Excel/XML remote signing succeeds against ESRM (Priority: P1)

A consumer calls `IMisaESignClient.SignPdfAsync` (or `SignWordAsync`/`SignExcelAsync`/
`SignXmlAsync`) with a valid document and an ACTIVE certificate. Today the outbound
`documents/hash` request is rejected with HTTP 400 because it carries empty arrays for the
document types not in use, so signing fails 100% of the time. After the fix, the request omits
the unused arrays, `documents/hash` returns `200` with the document hash, and the
`documents/attachment` step likewise succeeds.

**Why this priority**: This is the defect that blocks every remote-signing flow against the real
ESRM service. Without it the package is unusable for its primary purpose.

**Independent Test**: Point the SDK at a fake server that returns `400` when any unused
document-type array is present-but-empty and `200` otherwise; assert each of the four sign
flows completes.

**Acceptance Scenarios**:

1. **Given** a valid PDF and ACTIVE certificate, **When** the consumer signs, **Then** the
   `documents/hash` request body contains `pdfDocs` and **no** `xmlDocs`/`wordDocs`/`excelDocs`
   keys, and signing proceeds.
2. **Given** a Word (resp. Excel, XML) sign, **When** the consumer signs, **Then** the request
   body contains only the matching array and omits the other three.
3. **Given** the attachment step for any format, **When** it runs, **Then** the
   `documents/attachment` body contains only the matching array and omits the other three.

---

### User Story 2 - A sign with an unset Page still succeeds (Priority: P2)

A consumer signs a visible-signature document (PDF/Word/Excel) without setting
`SignatureInfo.Page`. Today the field is `null`, dropped on the wire, and MISA rejects the
request ("`Page must be a value greater than or equal to 1`"). After the fix, the SDK sends
`Page = 1` and records that the default was applied, so signing succeeds without the consumer
having to know MISA's rule.

**Why this priority**: A realistic, easily-hit caller mistake that produces the same opaque 400;
fixing it removes a sharp edge that the report shows consumers currently work around themselves.

**Independent Test**: Build a wire request from a `SignatureInfo` with `Page = null`; assert the
serialized body contains `Page: 1` and a structured log entry recorded the defaulting. With an
explicit `Page`, assert it is sent unchanged and no defaulting log is emitted.

**Acceptance Scenarios**:

1. **Given** `SignatureInfo.Page == null` on a PDF/Word/Excel sign, **When** the wire request is
   built, **Then** `Page = 1` is sent and a structured log records the default.
2. **Given** an explicit `Page >= 1`, **When** the wire request is built, **Then** that value is
   sent unchanged and no defaulting log is emitted.
3. **Given** `Page < 1` (explicitly set), **When** the request is validated, **Then** the
   existing client-side validation still rejects it before any HTTP call.

---

### User Story 3 - A rejected request is diagnosable (Priority: P3)

When MISA rejects a request with field-level `validationFailures`, a consumer who has opted into
raw error detail (`IncludeRawErrorMessage = true`) sees which properties failed and why, instead
of a generic `HashRejected` / `errorCode=e400`.

**Why this priority**: This is the observability gap that turned a one-line serialization bug
into a hard-to-diagnose failure. It is hardening that protects against this and future MISA
validation rejections.

**Independent Test**: Feed a 400 body containing `validationFailures` to the error path; with
`IncludeRawErrorMessage = true` assert the resulting exception detail names each failing property
and reason; with `false` assert none of that text appears.

**Acceptance Scenarios**:

1. **Given** a 400 body with `validationFailures` and `IncludeRawErrorMessage = true`, **When**
   the error is mapped, **Then** the exception detail includes each `{property, failureReason}`.
2. **Given** the same body with `IncludeRawErrorMessage = false`, **When** the error is mapped,
   **Then** the detail contains no `validationFailures` text (only the existing summary).
3. **Given** a 400 body with no `validationFailures` field, **When** the error is mapped, **Then**
   the detail is unchanged from today (graceful absence).

---

### Edge Cases

- A request that legitimately populates more than one document-type array → all populated arrays
  are sent; only genuinely-unused (null) arrays are omitted.
- `CertificateChain` (always assigned, non-null) and other always-populated fields are
  unaffected by the nullable-array change.
- `validationFailures` present but `IncludeRawErrorMessage = false` → omitted (no leak).
- A 400 body whose `validationFailures` entries have missing/empty `property` or `failureReason`
  → surfaced defensively (skipped or rendered as empty) without throwing.
- XML signing: `Page` is carried through unchanged (XML signatures are not page-positioned); the
  `Page` default applies to the visible-signature formats (PDF/Word/Excel).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A `documents/hash` request MUST serialize only the document-type array(s) actually
  populated for the sign; unused arrays among `pdfDocs`/`xmlDocs`/`wordDocs`/`excelDocs` MUST be
  omitted from the request body entirely, never sent as an empty `[]`.
- **FR-002**: A `documents/attachment` request MUST apply the same rule to its
  `pdfDocs`/`xmlDocs`/`wordDocs`/`excelDocs` arrays across all four document-format paths (PDF,
  XML, Word, Excel — Word and Excel share a single build method).
- **FR-003**: For the single-format flows the SDK produces today, each flow MUST produce a body
  containing exactly the matching array: a PDF sign emits `pdfDocs` only; XML emits `xmlDocs`
  only; Word emits `wordDocs` only; Excel emits `excelDocs` only. (The general rule — omit any
  unpopulated array — also holds for a hypothetical multi-array request; see Edge Cases.)
- **FR-004**: When `SignatureInfo.Page` is unset (`null`) on a visible-signature format
  (PDF/Word/Excel), the SDK MUST send `Page = 1` and MUST emit a structured log entry recording
  that the default was applied. An explicitly provided `Page` MUST be sent unchanged with no
  defaulting log.
- **FR-005**: The existing client-side validation rejecting `Page < 1` MUST remain in force; this
  change only fills the `Page == null` gap and MUST NOT relax the lower bound.
- **FR-006**: The SDK MUST parse a `validationFailures` array (entries of
  `{ property, failureReason }`) from a MISA error response body and MUST include those entries
  in the surfaced error detail **only when** `IncludeRawErrorMessage = true`, gated identically
  to the existing `devMsg`/`userMsg` raw-detail handling.
- **FR-007**: The synthesized error codes and exception categories produced by
  `ESignErrorMapper` (e.g. `HashRejected`, `AttachmentRejected`, and their format-specific
  synthesized codes) MUST remain unchanged; only the human-readable detail string gains the
  `validationFailures` content under the opt-in flag.
- **FR-008**: No secret material or buyer PII MUST appear in any log or error produced by this
  feature. The surfaced `validationFailures` carry only MISA field names and generic reason
  strings; tokens (`AuthorizationRM`), credentials, and document content MUST NOT be logged or
  surfaced.
- **FR-009**: The change MUST NOT alter the public API surface of the `MisaConnect.ESign` family
  (the affected request/response DTOs and `ResponseErrorDto` are `internal`); it MUST ship as a
  patch release (target `2.1.1`) with a `CHANGELOG` `[Unreleased]` entry. README updates are
  made only if a documented behaviour changes.
- **FR-010**: All pre-existing `MisaConnect.ESign` unit and integration tests MUST continue to
  pass; new tests MUST cover the four hash paths, the four attachment paths, the `Page` default,
  and the `validationFailures` surfacing (both flag states).

### Key Concepts

- **Document-type arrays**: `pdfDocs` / `xmlDocs` / `wordDocs` / `excelDocs` on both
  `HashRequestDto` and `AttachmentRequestDto`. A single sign populates exactly one of them.
- **`WhenWritingNull` serialization**: the wire profile drops `null` properties but keeps empty
  collections. The fix relies on making the unused arrays `null` (not empty) so they are dropped —
  the same shape the response DTOs already use.
- **`SignatureInfo.Page`**: `int?`, default `null`; required `>= 1` by MISA for visible
  signatures. Defaulted to `1` (logged) when unset.
- **`validationFailures`**: MISA's per-property rejection detail
  (`[{ property, failureReason }]`); surfaced only under `IncludeRawErrorMessage`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A PDF-only sign produces a `documents/hash` body with **zero** empty document-type
  arrays in 100% of cases, and signing reaches MISA without the empty-array 400.
- **SC-002**: All four hash build paths and all four attachment build paths omit every unused
  document-type array — verified by serialization unit tests asserting the absent keys.
- **SC-003**: A sign with `SignatureInfo.Page` unset produces a wire body carrying `Page = 1` and
  a structured log recording the default; an explicit `Page` is preserved verbatim.
- **SC-004**: With `IncludeRawErrorMessage = true`, a 400 carrying `validationFailures` yields an
  error whose detail names each failing property and reason; with the flag `false`, none of that
  text appears (zero leakage).
- **SC-005**: The full pre-existing `MisaConnect.ESign` test suite passes alongside the new tests;
  `dotnet build` is warning-clean (`TreatWarningsAsErrors`).

## Assumptions

- MISA accepts a `documents/hash` / `documents/attachment` request that **omits** the unused
  document-type arrays. Substantiated by the bug report's consumer-side workaround, which strips
  the empty arrays from the outbound body and unblocks signing — i.e. absent arrays are accepted
  where present-but-empty arrays are rejected. **Verified against the MISA eSign sandbox
  (2026-06-14):** with the fix, an XML sign's `documents/hash` request was accepted (HTTP 200,
  surfacing only a downstream incomplete-response condition) where the empty-array body previously
  returned HTTP 400. Login, certificate listing, and ESRM routing against a `/webdev/` base all
  succeeded end to end. (Word/Excel sandbox runs return HTTP 500 on the repo's dummy non-OOXML
  fixtures — unrelated to this fix.)
- The `validationFailures` shape is `[{ "property": <string>, "failureReason": <string> }]` per
  the captured 400 body in the bug report.
- `Page = 1` is the correct, safe default for a visible signature when the caller did not specify
  a page (first page). Consumers needing another page set `Page` explicitly.

## Out of Scope

- Reworking the one-array-per-request model into true multi-document or multi-format batching.
- Changing MISA-side synthesized error codes or exception categories (only the detail string
  changes, under the existing opt-in flag).
- XML page-positioning semantics — `Page` is carried through for XML unchanged.
- Re-litigating the ESRM routing / token / content-type findings closed in slice 006.

## Dependencies

- Verified bug report: [`bug-report.md`](./bug-report.md) (this slice).
- Official API reference: `docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md`.
- Builds on the routing fix released in `MisaConnect.ESign 2.1.0` (slice 006), which exposed this
  latent defect.

## Implementation Guardrails *(carried into plan/tasks)*

- Scope the nullable-array change to the **four document-type arrays only** on `HashRequestDto`
  and `AttachmentRequestDto`. `Certificate` and `CertificateChain` are assigned at every build
  site and MUST stay non-null `string` / `List<string>`.
- The six DTO build sites are all in `MisaESignWireClient.cs` (hash: `HashPdfAsync`,
  `HashXmlAsync`, `HashWordOrExcelAsync`; attachment: `AttachSignatureAsync`,
  `AttachSignatureToXmlAsync`, `AttachSignatureToWordExcelAsync`). No other code constructs these
  internal DTOs; the request arrays are write-only (never read back), so a `null` default cannot
  NPE, and the response-side `Count > 0` guards read the separate `HashResponseDto` /
  `AttachmentResponseDto` types and are unaffected.
- The `Page`-omission mechanism is the global `DefaultIgnoreCondition = WhenWritingNull`
  (`ESignJsonOptions.cs`), **not** a per-property `[JsonIgnore]`. The comment at
  `XmlSignatureContextMapper.cs:30-32` attributing the omission to `[JsonIgnore]` is misleading
  and should be corrected while in this area.

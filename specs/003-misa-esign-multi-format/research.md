# Phase 0 — Research: MISA eSign Multi-Format Signing

This document resolves the open questions left by the Technical Context above. Spec-level clarifications (per-format facade methods, the slim `XmlSignatureContext`, both `string` and `byte[]` XML overloads, the closed `DocumentFormat` enum) landed in [spec.md §Clarifications](./spec.md#clarifications) on 2026-05-21; the entries below pick up where the spec stopped.

Each entry follows: **Decision** → **Rationale** → **Alternatives considered**.

---

## R-1. Why three orchestrators (`SignXml` / `SignWord` / `SignExcel`) instead of one generic `SignDocument<T>` use case?

**Decision**: Each non-PDF facade has its own thin orchestrator use case in `Application.UseCases`, named symmetrically with slice 1's `SignPdf`. Each orchestrator shares the same fields and the same body shape as `SignPdf` (login → cert select → format-specific hash → submit signing hash → poll status → format-specific attach → result), differing only in (a) which `Hash...Document` use case it threads through, (b) which `AttachSignatureTo...` use case finishes the chain, (c) the wrapper result type (`SignXmlWorkResult` / `SignWordWorkResult` / `SignExcelWorkResult`) carrying the right `DocumentFormat` discriminator.

**Rationale**:
- Slice 1's `SignPdf` already absorbs the 2FA-required transparent flow (slice 2's `IOtpProvider` catch-block). Cloning the orchestrator three times into `SignXml` / `SignWord` / `SignExcel` keeps each format's pipeline trivially readable and makes the orchestrator-level diff vs. `SignPdf` literally one helper-method swap per format — easy to audit during code review.
- A `SignDocument<TFormat, TContext, THashOutput>` generic would require introducing two new abstraction layers (one for the per-format hash output, one for the per-format wire-shape selection) into Application. Those layers exist to amortize duplicated code, and there is barely any duplicated code worth amortizing — the orchestrator body is ~30 lines and each format's per-format helpers already encapsulate the variance.
- The Constitution's "no premature abstraction" stance (Principle V, slice-driven development) prefers copy-paste-at-this-size over generic complexity when the duplication budget is small and the formats are a closed set.

**Alternatives considered**:
- *Single `SignDocument` orchestrator with a `DocumentFormat` parameter and a wire-shape selector port*: rejected — requires an `IPerFormatHashWriter` / `IPerFormatAttachmentWriter` port pair purely to satisfy the orchestrator's polymorphism. No consumer would swap them; they are internal implementation detail.
- *Generic `SignDocument<TContext, THashOutput>` with closed `where TContext : IFormatSignatureContext` constraint*: rejected — the closed-set property is already enforced by having per-format methods on `IMisaESignWireClient`. The generic adds compile-time machinery without runtime benefit.
- *Inline all three formats into `SignPdf`'s existing orchestrator with a format switch*: rejected — would force renaming `SignPdf` to `SignDocument` (an unnecessary churn on slice-1's public-facing use case name) and would violate FR-065's "PDF path byte-identical" guarantee by making the existing orchestrator visibly format-aware.

---

## R-2. XML facade — both `string` and `byte[]` overloads, but how do they share the orchestrator?

**Decision**: The two overloads of `IMisaESignClient.SignXmlAsync(...)` are realized by two constructors on a single `SignXmlRequestDto` record (Client.Dtos):

```csharp
public sealed record SignXmlRequestDto(
    string? Xml,                       // primary input — exactly one of (Xml, XmlUtf8Bytes) must be non-null
    byte[]? XmlUtf8Bytes,              // secondary input — UTF-8-decoded by the SDK before forwarding
    XmlSignatureContextDto SignatureContext,
    string DocumentName,
    string DataToBeDisplayed,
    string? DocumentId = null);
```

The Client-layer `SignXmlRequestMapper` is the single conversion point: when `XmlUtf8Bytes` is non-null, the mapper calls `Encoding.UTF8.GetString(XmlUtf8Bytes)` (no BOM stripping, no charset sniffing — Assumption 6 from the spec) and threads the resulting string into `SignXmlWorkRequest.Xml`. The Application-layer `SignXml` orchestrator is byte-overload-agnostic — it sees only the canonicalized string. This keeps the wire-layer mapping simple: there is exactly one `FileToSign` encoding decision (raw text per MISA §4.1.2) and exactly one orchestrator path.

Validation: `SignXmlRequestValidator` rejects requests with both `Xml == null` AND `XmlUtf8Bytes == null` (no input), as well as requests with both fields non-null (ambiguous input). The validator does not parse the XML or validate UTF-8 conformance — MISA owns malformed-input rejection per FR-060 / Assumption 4.

**Rationale**:
- The spec's 2026-05-21 clarification on Q3 explicitly enumerated both overloads with the `string` form as primary; the implementation must surface both, but the Application layer should not double up on orchestration code.
- Decoding at the Client-layer mapping boundary (rather than at the wire layer) means the entire Application layer can assume "if we reached the orchestrator, the input is a valid `string`". This simplifies tests — the orchestrator's unit test fixtures don't need to thread `byte[]?` everywhere.
- Forwarding the `string` verbatim to the wire DTO honors MISA §4.1.2 (`FileToSign` carries XML text content for the XML format) without an additional encoding step. The wire DTO's `FileToSign` field is `string`, not `byte[]`.

**Alternatives considered**:
- *Two distinct `SignXmlRequestDto` types (`SignXmlStringRequestDto` / `SignXmlBytesRequestDto`)*: rejected — forces the `IMisaESignClient` facade to have two distinctly-named methods (`SignXmlFromStringAsync` / `SignXmlFromBytesAsync`), which contradicts the spec's overload phrasing.
- *Decode at the wire layer*: rejected — pushes encoding concerns past the Application boundary and forces every wire-DTO test to know about the encoding nuance.
- *Validate UTF-8 well-formedness on the bytes overload*: rejected per Assumption 6 (the SDK does not BOM-strip or sniff alternative encodings; consumers with non-UTF-8 input must decode themselves).

---

## R-3. Wire-shape decisions — per-format request and response DTOs

**Decision**: Strengthen the existing `HashRequestDto.XmlDocs / WordDocs / ExcelDocs` and `AttachmentRequestDto.XmlDocs / WordDocs / ExcelDocs` from `List<object>` to typed `List<XmlHashDocRequestDto>` / `List<WordHashDocRequestDto>` / `List<ExcelHashDocRequestDto>` (and the symmetric attachment DTOs) per MISA §4.1.1 / §4.1.2 / §4.6 / §4.15 verbatim.

**Per-format hash-request entry** (§4.1):
- PDF: `DocumentId + FileToSign(base64) + SignatureInfo` (existing).
- Word: `DocumentId + FileToSign(base64) + SignatureInfo` (same shape as PDF — §4.1.1).
- Excel: `DocumentId + FileToSign(base64) + SignatureInfo` (same shape as PDF — §4.1.1).
- XML: `DocumentId + FileToSign(text) + SignatureInfo` (§4.1.2 — `FileToSign` is raw text, not base64).

**Per-format hash-response entry** (§4.15):
- PDF: `documentId + documentBytes + documentHash + sh + signatureName + digest` (existing).
- Word: `documentId + documentBytes + signatureId + digest + mainDom`.
- Excel: `documentId + documentBytes + signatureId + digest + mainDom`.
- XML: `documentId + document + signatureId + digest + sh` (note `document`, NOT `documentBytes` — XML carries raw text).

The XML response uses `document` (text) rather than `documentBytes` (base64) — confirmed against MISA §4.15. The Word/Excel responses use `documentBytes` (base64). The `mainDom` field appears on Word/Excel responses; the `signatureId` field appears on XML/Word/Excel responses. Per FR-053, the Application-layer `Hash...Document` use cases assert non-empty values for every required field and raise a typed "incomplete hash response" error before proceeding to `/Signing/hash` if any are missing.

**Per-format attachment-request entry** (§4.6 — `Doc_Attackment`):
- PDF: `signature + documentId + documentBytes + digest + signatureName + sh + documentHash`. (No `mainDom`, no `signatureId`.)
- Word/Excel: All fields above PLUS `mainDom` (required for Word/Excel per the doc) and `signatureId` (required for non-PDF per the doc).
- XML: All fields above PLUS `signatureId`. (No `mainDom` — XAdES has no internal `mainDom` notion.)

**Per-format attachment-response entry**:
- PDF: `documentId + document` (signed PDF base64) — existing.
- Word: `documentId + document` (signed Word base64).
- Excel: `documentId + document` (signed Excel base64).
- XML: `documentId + document` (signed XML text).

The Application-layer `AttachSignatureTo...` use cases extract the signed payload from the matching per-format array on the response per FR-057; if the matching array is empty / missing on a successful HTTP response, they raise a typed "missing signed document" error per FR-058 carrying `Format = (Xml|Word|Excel)` and the correlation ID.

**Rationale**:
- The existing four-array request envelope already exists at the DTO level (slice 1 left `xmlDocs` / `wordDocs` / `excelDocs` as `List<object>` placeholders); slice 3 just fills them with concrete types. No envelope restructure.
- Strengthening from `List<object>` to typed lists eliminates a class of "we forgot to JSON-serialize the per-format entries correctly" bugs at compile time and gives the unit tests a strong type to assert against.
- MISA's doc-vs-Postman-collection tie-break rule (per [docs/misa-esign-spec-plan.md](../../docs/misa-esign-spec-plan.md)) applies: if the slice-3 integration suite hits a field-name discrepancy where the doc says one thing and the Postman collection sends another, the Postman collection wins and the divergence is flagged in [contracts/wire-envelopes.md](./contracts/wire-envelopes.md).

**Alternatives considered**:
- *Keep `List<object>` and JSON-serialize anonymous types per format*: rejected — fragile to MISA's field casing rules, breaks layer-audit tests because anonymous types can't be detected by reflection-based JSON path assertions, and makes the unit-test fixtures unreadable.
- *Generic `PerFormatHashDocRequestDto<T>`*: rejected — the per-format shapes diverge enough (XML uses text `FileToSign`; Word/Excel use base64 `FileToSign`; XML response uses `document` while Word/Excel use `documentBytes`) that a generic forces ugly `if (typeof(T) == typeof(Xml))` branches in the wire layer.

---

## R-4. `XmlSignatureContext` — Domain record with explicit field set, MISA-default-filling at the wire boundary

**Decision**: `Domain.Signing.XmlSignatureContext` is a slim record exposing ONLY the XAdES-meaningful fields:

```csharp
public sealed record XmlSignatureContext(
    string SignatureName,
    HashAlgorithm HashAlgorithm,
    SignatureDescription SignatureDescription);
```

It does NOT expose `PositionX`, `PositionY`, `Width`, `Height`, `Page`, `SignaturePosInfos`, `FontSize`, `FontData`, `SignatureImage`, `LogoImage`, `RenderingMode`, or `TextColor` — these are PDF / Word / Excel visual-positioning fields with no meaning for XAdES signatures. The type system forbids consumers from supplying them via the XML facade.

On the wire, the SDK fills MISA-documented "no signature visualization" defaults for the omitted fields so the `/documents/hash` XML request remains wire-compatible with MISA's published `SignatureInfo` shape (§4.2):
- `TextColor`: `null` (omitted from JSON via `[JsonIgnore(Condition = WhenWritingNull)]`).
- `PositionX` / `PositionY` / `Width` / `Height`: `null` (omitted).
- `FontSize`: `null` (omitted).
- `FontData` / `SignatureImage`: `null` (omitted).
- `Page`: `null` (omitted).
- `LogoImage`: `""` (empty string — the wire DTO defaults this to non-null per slice-1 implementation, so it sends as `"LogoImage":""`).
- `RenderingMode`: `0` (per MISA's documented "diễn giải only" semantics — the safe XAdES default).
- `SignaturePosInfos`: `null` (omitted).

The mapping lives in `Infrastructure.ESign.Mapping.XmlSignatureContextMapper.ToWireSignatureInfo(XmlSignatureContext)`. The mapper is Infrastructure (not Domain) because the wire-default-filling decisions are Infrastructure concerns — Domain doesn't know about JSON serialization.

`Client.Dtos.XmlSignatureContextDto` is a 1:1 mirror of the Domain record that lives in `Client.Dtos` so consumers don't need a `using MisaConnect.ESign.Domain.Signing;` to construct an XML signing request. The Client-layer `SignXmlRequestMapper` converts `XmlSignatureContextDto → XmlSignatureContext` 1:1.

**Rationale**:
- The 2026-05-21 spec clarification on Q2 was explicit: hide the no-op visual fields at the type level, not via docstring warnings. The type system enforces correctness.
- Filling defaults at the wire layer keeps Domain and Application layer-pure — the slim context type carries only XAdES semantics, and the JSON-serialization-shape concern lives in Infrastructure where it belongs.
- The dual-layer DTO pattern (`Domain.Signing.XmlSignatureContext` + `Client.Dtos.XmlSignatureContextDto`) follows slice 1's precedent (`SignatureInfo` in Domain + `SignPdfRequestDto` flat fields in Client.Dtos that get composed into a `SignatureInfo` via `SignPdfRequestMapper`). For XML the mapping is simpler because the slim context has fewer fields, but the layering shape is identical.

**Alternatives considered**:
- *Reuse `SignatureInfo` with the visual fields nullable + a runtime "you shouldn't pass these for XML" warning*: rejected per the clarification — runtime warnings are friction; a type-system-enforced split is the right answer.
- *Send positionally-zero defaults from a struct, not a record*: rejected — `record` with explicit nullable parameters is the idiomatic .NET 8 way to model "these are optional and have explicit non-zero defaults the SDK fills" without introducing struct-vs-class confusion.
- *Hide `XmlSignatureContext` entirely and let the SDK synthesize the whole context from `SignatureDescription` only*: rejected — the consumer must still supply a `SignatureName` (per MISA §4.2 it is required), and `HashAlgorithm` defaults to `SHA256` but is conceptually consumer-controllable for forward compat.

---

## R-5. `DocumentFormat` discriminator — closed enum on every typed exception

**Decision**: `Domain.Documents.DocumentFormat` is a `byte`-backed closed enum:

```csharp
public enum DocumentFormat : byte
{
    Unknown = 0,
    Pdf = 1,
    Xml = 2,
    Word = 3,
    Excel = 4,
}
```

Every existing `ESignException` subclass (`AuthenticationFailedException`, `ESignTransportException`, `ESignGeneralException`, `NoActiveCertificateException`, `SignRejectedException`, `SignTerminalStateException`, `SignTimeoutException`, plus the slice-2 OTP exceptions) gains a `public DocumentFormat Format { get; }` property. The base `ESignException` constructor gains a new optional parameter at the end with default `DocumentFormat.Unknown`:

```csharp
protected ESignException(
    ESignErrorCategory category,
    string? rawCode,
    string detail,
    string correlationId,
    Exception? inner = null,
    DocumentFormat format = DocumentFormat.Unknown)   // NEW — additive
```

Slice-1 and slice-2 call sites that don't pass `format` keep working (the default is `Unknown`). Slice-3 call sites — and the slice-1 PDF call sites that need to start setting `Format = Pdf` per FR-062 — pass the explicit format value. The change is binary-compatible because the new constructor parameter is optional and at the end.

**Per FR-062**, every typed exception surfaced by any of the four facades MUST carry the format the consumer requested:
- `SignPdfAsync` paths: `Format == Pdf` (slice-1 exception construction sites are updated to forward `DocumentFormat.Pdf`; this is the only slice-1-touching change FR-065 permits).
- `SignXmlAsync` paths: `Format == Xml`.
- `SignWordAsync` paths: `Format == Word`.
- `SignExcelAsync` paths: `Format == Excel`.
- Failures surfaced outside any specific facade call (e.g. background refresh failures from `RefreshAccessToken` when invoked by `RemoteSigningAuthHandler`, DI-time validation errors from `MisaESignOptionsValidator`, OTP exchange failures that escape `ExchangeOtp` without being caught by a facade): `Format == Unknown`.

The `Application.Errors.ESignErrorMapper.Map(...)` signature gains a `DocumentFormat requestedFormat` parameter (default `DocumentFormat.Pdf` to preserve existing call sites that all came from PDF paths). The Infrastructure-layer `MisaESignWireClient` per-format methods pass `requestedFormat = Xml | Word | Excel` when calling the mapper.

**Rationale**:
- The 2026-05-21 spec clarification on Q4 was explicit: closed enum with `Unknown = 0` as the default sentinel. A sealed class hierarchy was rejected because the format set is genuinely closed (MISA publishes exactly four; adding a fifth would be a major-version event).
- Adding the property to the base class (rather than to each subclass independently) ensures the contract "every typed exception carries Format" cannot be silently violated by a future exception subclass that forgets to add the property.
- The default-`Unknown` sentinel covers the "failure happened before we knew which format" cases without forcing every exception construction site to either pick one of the four formats or invent a special "n/a" value. The unit tests assert `Format == Unknown` is observed only on the documented pre-format-shaping failure paths (refresh-on-401 inside a non-facade context, DI-time validation, etc.) — every in-facade failure must carry the called facade's format.

**Alternatives considered**:
- *Sealed class hierarchy (`DocumentFormat.Pdf` / `.Xml` / `.Word` / `.Excel` as static members of an abstract class)*: rejected per the clarification — closed-enum syntactic ergonomics (`switch` exhaustiveness, no allocation) are preferable for a closed set.
- *Open struct-enum (smart enum)*: rejected — would imply extensibility we don't want; consumers must NOT be able to add their own `DocumentFormat` values.
- *Per-facade exception types (e.g. `XmlSignFailedException`, `WordSignFailedException`)*: rejected — would explode the public surface from ~12 typed exceptions to ~48 and provide no real ergonomic benefit; consumers branch on `(ExceptionType, Format)` more naturally than on a deep type hierarchy.

---

## R-6. Per-format error mapping — extends slice 2's hybrid pattern

**Decision**: `Application.Errors.ESignErrorMapper.Map(...)` extends its existing per-endpoint switch with format-aware synthesizers:

- For `/documents/hash` rejections, the existing `SynthesizeHashCode(...)` helper grows a `requestedFormat`-aware overload `SynthesizeHashCodeForFormat(rawCode, envelope, requestedFormat)` that maps additional XML/Word/Excel-specific substrings to per-format synthesized codes:
  - `InvalidXmlInput` (XML format + `errorCode/devMsg` substring match on `"xml"` + `"malformed"|"invalid"|"không hợp lệ"`).
  - `UnsupportedDocumentVariant` (any format + `errorCode/devMsg` substring match on `"unsupported"|"variant"|"format not supported"`).
  - All other synthesized codes from slice 1 (`InvalidCertificate`, `InvalidHash`, `InvalidDocument`, `InvalidSignatureInfo`) continue to apply across all formats.
- For `/documents/attachment` rejections, the existing `SynthesizeAttachmentCode(...)` helper grows a `requestedFormat`-aware overload that adds:
  - `MissingMainDom` (Word | Excel format + `errorCode/devMsg` substring match on `"mainDom"|"main dom"|"missing main"`).
  - `MissingSignatureId` (Xml | Word | Excel format + `errorCode/devMsg` substring match on `"signatureId"|"signature id"|"missing signature"`).
- Both layers continue to honor the slice-2 hybrid pattern: canonical `errorCode` table first (when MISA's Postman collection enumerates one), substring synthesis fallback on `errorCode + devMsg + userMsg` second.

Per FR-053 and FR-058, the Application-layer `Hash...Document` and `AttachSignatureTo...` use cases also synthesize "incomplete hash response" and "missing signed document" errors before any wire-level rejection — those errors carry the requested format too and are surfaced as `ESignGeneralException(category = HashRejected | AttachmentRejected, rawCode = "IncompleteHashResponse" | "MissingSignedDocument", format = Xml|Word|Excel)`.

Per FR-062, every typed exception produced by the per-format mapping carries `Format = requestedFormat` regardless of whether MISA's `errorCode` was format-specific. The mapping table is published in [contracts/error-mapping.md §A.10](./contracts/error-mapping.md#a10-per-format--documentshash-and--documentsattachment).

**Rationale**:
- Following slice 2's hybrid pattern keeps the error-mapping mental model consistent — consumers who learned how slice 2's `OtpErrorMapper` works do not need to learn a new pattern for slice 3.
- The synthesized codes (`InvalidXmlInput`, `MissingMainDom`, `MissingSignatureId`, `UnsupportedDocumentVariant`) give consumers stable internal identifiers to log even when MISA's wire-side code is unstable. Operators can grep their logs for `MissingMainDom` and reliably find every "Word/Excel signing failed because the request body was missing the required `mainDom`" incident.
- The mapping is encoded in `[Theory]` rows in `tests/MisaConnect.ESign.UnitTests/Errors/PerFormatErrorMapperTests.cs` — one row per table entry, so adding a new code is one PR line in the table + one PR line in the test.

**Alternatives considered**:
- *Per-format error mappers (`XmlErrorMapper`, `WordErrorMapper`, `ExcelErrorMapper`) as siblings of `ESignErrorMapper`*: rejected — the mapping logic is 95% identical across formats; siblings would duplicate the slice-1 transport/auth/canonical-errorCode handling three times. Adding a `requestedFormat` parameter to the existing mapper is strictly less code.
- *A new `IPerFormatErrorMapper` port that consumers can override per format*: rejected — no consumer would override the mapping (it's wire-shape-driven, not policy-driven), and the existing static helper class shape is the right granularity.
- *Don't synthesize codes — just propagate MISA's `errorCode` verbatim and let consumers string-match*: rejected per FR-061 ("MUST produce stable internal codes") and the operability concerns motivating the spec.

---

## R-7. Token cache, certificate selection, transport behavior — unchanged

**Decision**: Slice 3 makes **no edits** to: `EnsureAccessToken`, `RefreshAccessToken`, `ListActiveCertificates`, `SubmitSignHash`, `PollSignStatus`, `AttachSignature` (slice 1's PDF `AttachSignature` is preserved verbatim; the two new attachment use cases `AttachSignatureToXml` and `AttachSignatureToWordExcel` are siblings, not replacements), `ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`, `ICorrelationIdAccessor`, `ISystemClock`, the slice-2 `IOtpProvider` / `ExchangeOtp` / `ResendOtp`, the slice-1/2 `ClientHeadersHandler` / `RemoteSigningAuthHandler` / `TransientFailureRetryHandler` / `SingleFlightRefresh`, the `MisaESignOptions.{Polling, TransportRetry, Errors, Otp}` blocks, the `MisaESignOptionsValidator`'s base validation rules, the `ESignErrorCategory` enum, or the `MisaESignWireClient.LoginAsync` / `RefreshAsync` / `TwoFactorAuthAsync` / `ResendOtpAsync` / `ListCertificatesByUserIdAsync` / `SubmitSignHashAsync` / `GetSignStatusAsync` methods.

The only existing components that gain new edits are:
- `Application.Errors.ESignErrorMapper.Map(...)` — adds a `DocumentFormat requestedFormat` parameter (with backward-compatible default `Pdf`) and per-format synthesizer helpers.
- `Application.Abstractions.IMisaESignWireClient` — adds five new methods (additive on the interface).
- `Application.UseCases.SignPdf` — internally constructs typed exceptions with `Format = Pdf` (where slice 1 left them defaulting to `Unknown`); ZERO observable behavior change to consumers (FR-065).
- `Infrastructure.ESign.MisaESignWireClient` — implements the five new methods.
- `Infrastructure.ESign.Wire.HashDtos.cs` / `AttachmentDtos.cs` — strengthens existing per-format arrays from `List<object>` to typed lists; adds the per-format DTO types.
- `Infrastructure.Logging.ESignLogScrubber` — adds per-format JSON-path matchers per FR-063 / SC-020.
- `Infrastructure.DependencyInjection.ServiceCollectionExtensions` — registers the new use cases.
- `Client.IMisaESignClient` — adds three new facade methods (additive on the interface).
- `Client.MisaESignClient` — wires the three new methods through the new orchestrators.
- `Domain.Errors.*` — every existing exception subclass gains a `Format` property via constructor extension; behavior change is zero (every existing call site keeps the default `Unknown`, which is then overridden by slice-3 / slice-1-PDF call sites that pass the explicit value).

**Rationale**:
- FR-066 and FR-067 explicitly require slice 3 to be additive on top of slice 1 + 2. Discipline is enforced by:
  1. The slice-1 PDF integration test (`SignPdfHappyPathSandboxTests.cs` + `SignPdfHappyPathFakeServerTests.cs`) must continue to pass byte-identically (FR-065 / SC-021) — any change to slice-1 wire shaping breaks them.
  2. The slice-2 OTP integration tests (`TwoFactorAuthHappyPathFakeServerTests.cs` etc.) must continue to pass — any change to slice-1/2 token caching breaks them.
  3. The layer-audit unit test (`MisaConnectESignLayerAuditTests.cs`) flags any new layer-crossing.
- Refactoring `SignPdf` into a state machine or extracting a shared base class would churn slice-1 code for no gain. The thin per-format orchestrator clone is the lowest-friction shape.

**Alternatives considered**:
- *Extract a shared `SignDocumentOrchestratorBase` abstract class in Application*: rejected — would force slice 1's `SignPdf` to inherit it (changing its constructor shape, breaking the existing tests' constructor calls) for an aesthetic improvement worth zero LoC.
- *Introduce a `SignDocumentContext<TFormat>` discriminated-union-shape thing in Application*: rejected as premature.

---

## R-8. Logging — extend the existing scrubber, do not duplicate it

**Decision**: `Infrastructure.Logging.ESignLogScrubber` gains additional JSON-path matchers in its existing per-endpoint rule-table (per FR-063 / SC-020):

Hash-request rules:
- `$.xmlDocs[*].FileToSign` → `"<redacted-doc>"` (matches `/documents/hash` requests).
- `$.wordDocs[*].FileToSign` → `"<redacted-doc>"`.
- `$.excelDocs[*].FileToSign` → `"<redacted-doc>"`.
- `$.pdfDocs[*].FileToSign` → `"<redacted-doc>"` (already present in slice 1; mentioned here for completeness).

Hash-response rules:
- `$..documentBytes` → `"<redacted>"` (catches PDF/Word/Excel).
- `$..document` → `"<redacted>"` (catches XML — but only when applied to response bodies on `/documents/hash` and `/documents/attachment`; the JSON-path matcher is scoped via the existing per-endpoint rule list to avoid false positives on unrelated `document` fields in user PII blocks).
- `$..documentHash` → `"<redacted>"`.
- `$..digest` → `"<redacted>"`.
- `$..mainDom` → `"<redacted>"`.
- `$..sh` → `"<redacted>"`.
- `$..signatureId` → `"<redacted>"`.

Attachment-request rules:
- `$..signature` → `"<redacted>"` (catches all four format arrays' `signature` field).
- All hash-response field redactions repeat here because the attachment request carries the same fields back to MISA.

Attachment-response rules:
- `$.xmlDocs[*].document` → `"<redacted>"`.
- `$.wordDocs[*].document` → `"<redacted>"`.
- `$.excelDocs[*].document` → `"<redacted>"`.
- `$.pdfDocs[*].document` → `"<redacted>"` (already present in slice 1).

The scrubber's existing per-endpoint rule-table architecture (slice 1 introduced it; slice 2 extended it for OTP redaction) accommodates the new rules by appending tuples — no new infrastructure code.

The unit test `tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs` gains assertions per FR-063: for each format, a captured-log scan over a full sign-and-attach exchange contains zero occurrences of (a) the source-document byte sequence, (b) the signed-document byte sequence, (c) `digest`, (d) `documentHash`, (e) `mainDom`, (f) `sh`, (g) `signatureId`, (h) `signature` values from the request/response bodies. The integration test `PerFormatErrorMappingFakeServerTests.cs` adds a parallel assertion at the integration level.

**Rationale**:
- Slice 2 established the pattern: extend the existing scrubber's rule table, do not create a parallel scrubber. Slice 3 follows it.
- Scoping rules to specific endpoints (e.g. `$..document` only applies to `/documents/*` response bodies, not to any `document` field MISA might invent in a future user-info envelope) prevents over-redaction. The scrubber already supports per-endpoint scoping via predicates on the URL path.

**Alternatives considered**:
- *Per-format scrubber classes*: rejected (same reasoning as slice 2).
- *Drop the whole `/documents/hash` and `/documents/attachment` request bodies from logs*: rejected — loses the per-format-array-shape context that operators want for incident response.

---

## R-9. The in-repo fake server — extend, do not fork

**Decision**: `tests/MisaConnect.ESign.IntegrationTests/EsignFake/FakeMisaESignServer.cs` extends its existing `/documents/hash` and `/documents/attachment` route handlers to:
1. Inspect which per-format array the request body populates (`xmlDocs[0]` / `wordDocs[0]` / `excelDocs[0]` / `pdfDocs[0]`).
2. Compose a response that populates the matching array with realistic per-format fields (per §4.15 for hash, per §4.6 for attachment). For tests that drive the "multi-array response" path per US2 AS2, the fake supports a scripted mode that populates multiple arrays at once.
3. Support scripted per-format rejection fixtures for the error-mapping integration tests (`PerFormatErrorMappingFakeServerTests.cs`): malformed-XML rejection, missing-`mainDom`-on-Excel rejection, missing-`signatureId`-on-XML-attachment rejection.
4. Continue to serve the slice-1 PDF path byte-identically — the slice-1 `SignPdfHappyPathFakeServerTests.cs` must pass unchanged after slice 3 ships.

The fake server's per-format response composition is keyed by inspecting the populated request array; it does not need any new configuration ports. The scripted rejection mode follows the existing slice-2 OTP-rejection scripting pattern.

**Rationale**:
- Constitution Principle VI requires in-repo deterministic harnesses for integration tests; the existing fake is the established harness and extending it is strictly cheaper than forking.
- Per-format inspection in the fake mirrors the per-format response selection logic on the real SDK side — the fake is effectively a small reference implementation of MISA's documented behavior, which makes the integration tests an indirect specification of MISA's wire shape too.

**Alternatives considered**:
- *Build a separate `EsignFakeMultiFormat` server*: rejected — duplicates the auth/cert/polling state machine for no benefit.
- *Use record/replay against the real MISA sandbox*: rejected per Constitution Principle VI ("no mocks at the MISA HTTP boundary in integration tests; in-repo fake for deterministic offline runs"). Record/replay is mocking by another name.

---

## R-10. The 2FA / OTP layer is format-agnostic — no per-format OTP logic

**Decision**: The slice-2 OTP plumbing (`IOtpProvider`, `ExchangeOtp`, `ResendOtp`, the explicit `SignInWithOtpAsync` / `ResendOtpAsync` facade methods, the `AuthenticationFailedException.Requires2FA` signal) applies to all four format paths unchanged. A 2FA-enrolled account can sign XML / Word / Excel documents via the same `SignInWithOtpAsync` flow slice 2 established (FR-067).

Implementation: each of the three new orchestrators (`SignXml`, `SignWord`, `SignExcel`) clones slice 1's `SignPdf` 2FA catch-block verbatim. The `OtpChallenge` carries `UserName + CorrelationId` only — nothing format-specific — so the same provider implementation can satisfy 2FA challenges raised by any of the four facades. When the orchestrator catches a `Requires2FA = true` exception, it invokes the registered `IOtpProvider.ProvideAsync(challenge, ct)`, calls `ExchangeOtp.ExecuteAsync(...)`, and recursively re-executes its own pipeline (bounded by R-10 from slice 2 — one OTP attempt per facade invocation, then propagate).

**Rationale**:
- FR-067 explicitly requires 2FA contracts to apply unchanged. The simplest implementation is "each orchestrator embeds the same catch-block" — no shared base class, no per-format OTP customization.
- The `OtpChallenge` not carrying a `DocumentFormat` is intentional. 2FA is an account-level concern, not a per-call-format concern. If a future slice needs format-aware OTP routing (e.g. "use authenticator-app OTPs for XML signing and SMS OTPs for Word signing"), it can introduce a new optional field on `OtpChallenge` then; slice 3 does not need it.

**Alternatives considered**:
- *Add `OtpChallenge.RequestedFormat` field*: rejected — no current use case; would force every existing provider implementation to ignore a new field.
- *Format-specific `IOtpProviderForXml` / `IOtpProviderForWord` ports*: rejected — useless ceremony.

---

## R-11. Scope discipline — what slice 3 explicitly does NOT touch

**Decision**: Slice 3 makes **no edits** to: certificate listing/selection, the wire-DTO `CertificateDto`, the `MisaESignOptions` schema (no new options blocks — explicit FR-066), `MisaESignOptionsValidator` (no new validation rules beyond what the per-format request validators cover), `ITokenCache`'s interface or default `InMemoryTokenCache` implementation, `ITokenCacheKeySelector`'s default composition (format is NOT part of the cache key — slice-1 FR-026, reaffirmed by FR-066), `SystemClock`, `SingleFlightRefresh`'s implementation, slice 2's OTP error mapper, the `ESignErrorCategory` enum (no new categories — `HashRejected` / `AttachmentRejected` / `Transport` already cover the new failure modes), the public-surface shape of any slice-1 or slice-2 Client.Dtos type (the new Client.Dtos are pure additions), the existing PDF `SignPdfAsync` / `SignInWithOtpAsync` / `ResendOtpAsync` method signatures.

The one observable change slice 3 makes to slice-1 / slice-2 public surface is the additive `Format` property on every typed exception (with the slice-1 PDF construction sites updated to pass `DocumentFormat.Pdf` so the property is meaningful, not `Unknown`, on slice-1 paths). This is the only slice-1-touching change FR-065 permits.

**Rationale**:
- Constitution Principle V (slice-driven) and the spec's "Reuse contracts — no regression" section (FR-065, FR-066, FR-067) both require slice 3 to be additive. Discipline is enforced by the test suite — slice 1 and slice 2's full test suites continue to pass without modification (with the exception of the `Format == Pdf` / `Format == Unknown` assertions added per FR-062 to existing test rows; those additions are test code, not production code).

**Alternatives considered**:
- *Refactor the orchestrators into a shared state machine*: rejected as premature.
- *Move OTP logic out of `SignPdf` into a shared `SignDocumentWith2FA` decorator*: rejected — would churn slice-2 code for no slice-3 gain.

---

## R-12. `SignedDocument` Domain type — reuse or per-format clone?

**Decision**: Reuse slice 1's `Domain.Documents.SignedDocument(byte[] Bytes)` for all four format outputs. The format discriminator lives on the Client-layer result DTOs (`SignXmlResultDto.Format` etc.), NOT on the Domain entity. Rationale: the Domain entity describes "we have signed bytes" — what format those bytes are in is a return-context property that belongs at the boundary where the consumer can branch on it.

**Rationale**:
- A `SignedXmlDocument` / `SignedWordDocument` / `SignedExcelDocument` per-format Domain type would force every internal collaborator (the `AttachSignatureTo...` use cases, the orchestrator results, the test fixtures) to know about format-specific Domain types, when the only consumer-visible difference is "what file extension should I save it with" — and that's a Client-DTO concern.
- The `DocumentFormat` discriminator on the Client.Dtos result types is sufficient: `SignXmlResultDto.Format == Xml` answers "what format is this", and `SignXmlResultDto.SignedXml` returns the bytes.

**Alternatives considered**:
- *Per-format Domain record types*: rejected as above.
- *Generic `SignedDocument<TFormat>`*: rejected — adds generic ceremony for zero behavior change.

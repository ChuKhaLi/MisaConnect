# Phase 1 — Data Model: MISA eSign Multi-Format Signing

This document captures the Domain entities, Application-layer DTOs/ports/results, wire-DTO strengthenings, mapping additions, and state transitions added (or, where applicable, extended) by slice 3. Every type listed below lives at the layer indicated (Domain / Application / Infrastructure-Wire / Client-Dto) and respects Constitution Principle I.

Field names in the **Wire DTO** column reproduce MISA's published shape verbatim per Principle IV (including the misspelling `Doc_Attackment` on the attachment row). Field names in Domain / Application / Client columns are SDK-idiomatic (PascalCase).

Cross-references to slice 1 use the form [slice-1 §X.Y](../001-misa-esign-pdf-sign-flow/data-model.md#xy). Cross-references to slice 2 use [slice-2 §X.Y](../002-misa-esign-2fa-otp/data-model.md#xy).

---

## Layered overview (slice-3 additions only)

```
Client (public DTOs)         Client.Dtos.SignXmlRequestDto             (NEW)
                             Client.Dtos.SignWordRequestDto            (NEW)
                             Client.Dtos.SignExcelRequestDto           (NEW)
                             Client.Dtos.SignXmlResultDto              (NEW)
                             Client.Dtos.SignWordResultDto             (NEW)
                             Client.Dtos.SignExcelResultDto            (NEW)
                             Client.Dtos.XmlSignatureContextDto        (NEW)
                             Client.IMisaESignClient                   (EDITED — three new methods)
                             Client.MisaESignClient                    (EDITED — wires new orchestrators)
                             Client.Mapping.SignXmlRequestMapper       (NEW)
                             Client.Mapping.SignWordRequestMapper      (NEW)
                             Client.Mapping.SignExcelRequestMapper     (NEW)
                                        │  Mapping
                                        ▼
Application (use cases,      Application.UseCases.SignXml              (NEW orchestrator)
ports, error mapper)         Application.UseCases.SignWord             (NEW orchestrator)
                             Application.UseCases.SignExcel            (NEW orchestrator)
                             Application.UseCases.HashXmlDocument      (NEW)
                             Application.UseCases.HashWordDocument     (NEW)
                             Application.UseCases.HashExcelDocument    (NEW)
                             Application.UseCases.AttachSignatureToXml          (NEW)
                             Application.UseCases.AttachSignatureToWordExcel    (NEW — shared for Word + Excel)
                             Application.UseCases.SignXmlWorkRequest   (NEW record)
                             Application.UseCases.SignWordWorkRequest  (NEW record)
                             Application.UseCases.SignExcelWorkRequest (NEW record)
                             Application.UseCases.SignXmlWorkResult    (NEW record)
                             Application.UseCases.SignWordWorkResult   (NEW record)
                             Application.UseCases.SignExcelWorkResult  (NEW record)
                             Application.Abstractions.IMisaESignWireClient (EDITED — 5 new methods)
                             Application.Abstractions.XmlHashOutput    (NEW record)
                             Application.Abstractions.WordExcelHashOutput  (NEW record — shared for Word + Excel)
                             Application.Validation.SignXmlRequestValidator   (NEW)
                             Application.Validation.SignWordRequestValidator  (NEW)
                             Application.Validation.SignExcelRequestValidator (NEW)
                             Application.Errors.ESignErrorMapper       (EDITED — gains DocumentFormat parameter + per-format synthesizers)
                                        │
                                        ▼
Domain (entities, errors,    Domain.Documents.DocumentFormat           (NEW enum)
enums)                       Domain.Signing.XmlSignatureContext        (NEW record)
                             Domain.Documents.SignedDocument           (REUSED — generic "signed bytes" wrapper for all 4 formats)
                             Domain.Errors.ESignException              (EDITED — base class gains Format property via additive ctor param)
                             Domain.Errors.{Authentication, NoActiveCertificate,
                                            SignRejected, SignTerminalState,
                                            SignTimeout, ESignTransport,
                                            ESignGeneral, InvalidOtp,
                                            ExpiredOtp, ExhaustedOtpAttempts,
                                            OtpRejected}*Exception      (EDITED — every existing exception forwards Format to base)
                                        ▲
                                        │  Mapping (Infrastructure)
Infrastructure (wire DTOs,   Infrastructure.ESign.MisaESignWireClient  (EDITED — 5 new methods)
mapping, scrubber,           Infrastructure.ESign.Wire.HashDtos.cs     (EDITED — typed per-format arrays + 3 new request DTOs + 3 new response DTOs)
DI extension)                Infrastructure.ESign.Wire.AttachmentDtos.cs   (EDITED — typed per-format arrays + 2 new request DTOs + 3 new response DTOs)
                             Infrastructure.ESign.Mapping.XmlSignatureContextMapper (NEW)
                             Infrastructure.Logging.ESignLogScrubber   (EDITED — per-format JSON-path matchers per FR-063)
                             Infrastructure.DependencyInjection.ServiceCollectionExtensions (EDITED — registers 9 new types)
```

---

## 1. Domain layer — entities, value objects, enum, error-property additions

### 1.1. `DocumentFormat` (Domain.Documents) — NEW

Closed `byte`-backed enum identifying which MISA-supported format is at play for a given request, result, or exception. The four format values are the closed set MISA publishes; adding a new value would be a binary-breaking semver event by definition (Principle VII).

| Member | Value | Meaning |
|---|---|---|
| `Unknown` | `0` | Default sentinel. Reserved for failures surfaced outside any specific facade call — background refresh failures, DI-time validation errors, exception construction sites where no consumer-facing format was requested. |
| `Pdf` | `1` | The consumer called `SignPdfAsync(...)`. Slice-1 PDF path. |
| `Xml` | `2` | The consumer called `SignXmlAsync(...)` (either overload). |
| `Word` | `3` | The consumer called `SignWordAsync(...)`. |
| `Excel` | `4` | The consumer called `SignExcelAsync(...)`. |

Wire serialization: there is no wire-level serialization of `DocumentFormat` — it never appears in any MISA request or response body. It lives entirely inside the SDK as a result-typing and exception-typing discriminator. The `byte` backing keeps the runtime cost minimal (the property add to every `ESignException` is one byte of state).

### 1.2. `XmlSignatureContext` (Domain.Signing) — NEW

Slim record exposing ONLY the XAdES-meaningful signing-context fields. The type system enforces that consumers cannot supply visual-positioning fields (which have no meaning for XML signatures) via the XML facade.

```csharp
public sealed record XmlSignatureContext(
    string SignatureName,
    HashAlgorithm HashAlgorithm,
    SignatureDescription SignatureDescription);
```

| Field | Type | Required? | Notes |
|---|---|---|---|
| `SignatureName` | `string` | yes (non-empty after validation) | The XML signature's identifier. MISA §4.2 requires it. |
| `HashAlgorithm` | `HashAlgorithm` (slice-1 enum) | yes | Defaults to `HashAlgorithm.SHA256` when constructed via the Client-layer `XmlSignatureContextDto` mapper. |
| `SignatureDescription` | `SignatureDescription` (slice-1 record) | yes | Reused verbatim from slice 1 — `SignedBy`, `ShowSignedDate`, `Location`, `Reason`, `Contact`, `DisplayText`. All are XAdES-meaningful (XAdES signatures carry signer attestation metadata). |

The Domain record does NOT expose: `TextColor`, `PositionX`, `PositionY`, `Width`, `Height`, `FontSize`, `FontData`, `SignatureImage`, `Page`, `LogoImage`, `RenderingMode`, `SignaturePosInfos`. The `Infrastructure.ESign.Mapping.XmlSignatureContextMapper` (see §3.5) fills MISA-documented "no signature visualization" defaults for these on the wire.

### 1.3. `SignedDocument` (Domain.Documents) — REUSED

Unchanged from [slice-1 §1.X](../001-misa-esign-pdf-sign-flow/data-model.md). The record `SignedDocument(byte[] Bytes)` carries signed bytes for any of the four formats. The format discriminator is NOT on this record (R-12 in research) — it lives on the Client-layer result DTOs (`SignXmlResultDto.Format` etc.).

### 1.4. `ESignException` and subclasses (Domain.Errors) — EDITED

Per FR-062, every typed exception surfaced by any of the four facades MUST carry the format the consumer requested. The change is implemented by adding a `Format` property to the base `ESignException` class, populated via a new optional constructor parameter at the end:

```csharp
public abstract class ESignException : Exception
{
    protected ESignException(
        ESignErrorCategory category,
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null,
        DocumentFormat format = DocumentFormat.Unknown)   // NEW — additive, optional
        : base(detail, inner)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("CorrelationId must be non-empty.", nameof(correlationId));

        Category = category;
        RawCode = rawCode;
        Detail = detail;
        CorrelationId = correlationId;
        Format = format;
    }

    public ESignErrorCategory Category { get; }
    public string? RawCode { get; }
    public string Detail { get; }
    public string CorrelationId { get; }
    public DocumentFormat Format { get; }   // NEW
}
```

Every existing subclass (`ESignGeneralException`, `AuthenticationFailedException`, `NoActiveCertificateException`, `SignRejectedException`, `SignTerminalStateException`, `SignTimeoutException`, `ESignTransportException`, slice-2 OTP exceptions) gains a corresponding optional `format` parameter at the end of its own constructor that forwards to the base. The change is binary-compatible because every new constructor parameter is optional with a default of `DocumentFormat.Unknown`. Existing call sites in slice 1 and slice 2 that don't pass `format` keep working; slice 1's PDF construction sites are updated to pass `DocumentFormat.Pdf` per FR-062.

The Format-property contract per construction context:

| Construction context | `Format` value |
|---|---|
| `MisaESignWireClient.HashPdfAsync(...)` failure paths | `Pdf` |
| `MisaESignWireClient.HashXmlAsync(...)` failure paths | `Xml` |
| `MisaESignWireClient.HashWordAsync(...)` failure paths | `Word` |
| `MisaESignWireClient.HashExcelAsync(...)` failure paths | `Excel` |
| `MisaESignWireClient.AttachSignatureAsync(...)` (PDF) failure paths | `Pdf` |
| `MisaESignWireClient.AttachSignatureToXmlAsync(...)` failure paths | `Xml` |
| `MisaESignWireClient.AttachSignatureToWordExcelAsync(..., format: Word)` failure paths | `Word` |
| `MisaESignWireClient.AttachSignatureToWordExcelAsync(..., format: Excel)` failure paths | `Excel` |
| `MisaESignWireClient.LoginAsync(...)` / `RefreshAsync(...)` / `TwoFactorAuthAsync(...)` / `ResendOtpAsync(...)` failure paths reached during a facade call | the called facade's format (`Pdf` | `Xml` | `Word` | `Excel`) — the orchestrator threads it down via the `ESignErrorMapper.Map(..., requestedFormat: ...)` parameter |
| `MisaESignWireClient.ListCertificatesByUserIdAsync(...)` failure paths reached during a facade call | the called facade's format |
| `MisaESignWireClient.SubmitSignHashAsync(...)` / `GetSignStatusAsync(...)` failure paths reached during a facade call | the called facade's format |
| Failures originating outside a facade call (background refresh from `RemoteSigningAuthHandler`, DI-time validation, `MisaESignOptionsValidator` errors, `OtpErrorMapper.MapResendResult(...)` typed-failure when invoked outside a facade) | `Unknown` |
| `SignTimeoutException` raised by `PollSignStatus.ExecuteAsync(...)` inside a facade call | the called facade's format |
| `SignTerminalStateException` raised by `PollSignStatus.ExecuteAsync(...)` inside a facade call | the called facade's format |
| `NoActiveCertificateException` raised by `ICertificateSelector.SelectAsync(...)` inside a facade call | the called facade's format |
| `ESignTransportException` raised by `TransientFailureRetryHandler` inside a facade call | the called facade's format (the orchestrator wraps the call site so the format is in scope) |

### 1.5. `ESignErrorCategory` (Domain.Errors) — UNCHANGED

Slice 1's categories (`MisaUnknown`, `Authentication`, `NoActiveCertificate`, `CertificateLookupFailed`, `HashRejected`, `SignRejected`, `SignTerminalFailed`, `SignTerminalCancelled`, `SignTerminalUnknown`, `SignTimeout`, `AttachmentRejected`, `StatusLookupFailed`, `Transport`, `Validation`) are reused. No new categories — slice-3 failures all fit into the existing buckets (`HashRejected` for per-format hash failures; `AttachmentRejected` for per-format attachment failures; `Transport` for transport failures; etc.).

### 1.6. `HashAlgorithm` (Domain.Signing) — UNCHANGED

Slice-1 enum (currently only `SHA256` is supported, matching MISA's documented value). XML signing uses the same enum verbatim.

### 1.7. `SignatureDescription` (Domain.Signing) — UNCHANGED

Slice-1 record (`SignedBy`, `ShowSignedDate`, `Location`, `Reason`, `Contact`, `DisplayText`). Reused by both `SignatureInfo` (PDF/Word/Excel) and `XmlSignatureContext` (XML) — all six fields are XAdES-meaningful.

### 1.8. `SignatureInfo` (Domain.Signing) — UNCHANGED

Slice-1 record. PDF/Word/Excel facades share it verbatim per FR-059. XML facade uses `XmlSignatureContext` instead (FR-060).

---

## 2. Application layer — ports, records, use cases, mapper

### 2.1. `IMisaESignWireClient` (Application.Abstractions) — EDITED

Five new methods are added to the existing interface. All five are additive on a not-yet-1.0 interface and are documented in CHANGELOG. None of slice-1 / slice-2's existing methods change shape.

```csharp
public interface IMisaESignWireClient
{
    // (existing — unchanged)
    // Task<AuthSession> LoginAsync(string userName, string password, CancellationToken ct);
    // Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct);
    // Task<AuthSession> TwoFactorAuthAsync(string userName, string code, OtpDeliveryChannel otpType, bool remember, CancellationToken ct);
    // Task<OtpResendResult> ResendOtpAsync(string userName, string language, CancellationToken ct);
    // Task<IReadOnlyList<Certificate>> ListCertificatesByUserIdAsync(string accessToken, CancellationToken ct);
    // Task<PdfHashOutput> HashPdfAsync(string accessToken, Certificate cert, byte[] pdfBytes, string documentId, SignatureInfo signatureInfo, CancellationToken ct);
    // Task<SignTransaction> SubmitSignHashAsync(string accessToken, Certificate cert, string userId, string dataToBeDisplayed, PdfHashOutput hash, string documentName, CancellationToken ct);
    // Task<SignStatusSnapshot> GetSignStatusAsync(string accessToken, string transactionId, CancellationToken ct);
    // Task<byte[]> AttachSignatureAsync(string accessToken, Certificate cert, PdfHashOutput hash, string signatureData, CancellationToken ct);

    // NEW — XML hashing (the wire layer always sees the canonicalized string per R-2)
    Task<XmlHashOutput> HashXmlAsync(
        string accessToken,
        Certificate cert,
        string xmlContent,
        string documentId,
        XmlSignatureContext signatureContext,
        CancellationToken ct);

    // NEW — Word hashing
    Task<WordExcelHashOutput> HashWordAsync(
        string accessToken,
        Certificate cert,
        byte[] wordBytes,
        string documentId,
        SignatureInfo signatureInfo,
        CancellationToken ct);

    // NEW — Excel hashing
    Task<WordExcelHashOutput> HashExcelAsync(
        string accessToken,
        Certificate cert,
        byte[] excelBytes,
        string documentId,
        SignatureInfo signatureInfo,
        CancellationToken ct);

    // NEW — XML attachment (returns the signed XML bytes; the wire's `document` field carries text)
    Task<byte[]> AttachSignatureToXmlAsync(
        string accessToken,
        Certificate cert,
        XmlHashOutput hash,
        string signatureData,
        CancellationToken ct);

    // NEW — Word+Excel attachment (shared shape per MISA §4.6; format-aware in the response array selection)
    Task<byte[]> AttachSignatureToWordExcelAsync(
        string accessToken,
        Certificate cert,
        WordExcelHashOutput hash,
        string signatureData,
        DocumentFormat format,   // must be Word or Excel
        CancellationToken ct);
}
```

The `format` parameter on `AttachSignatureToWordExcelAsync(...)` selects which array of the `AttachmentRequestDto` carries the request entry (`wordDocs` vs. `excelDocs`) and which array of the `AttachmentResponseDto` the signed bytes are extracted from. Validation: the wire client throws `ArgumentException` if `format` is anything other than `Word` or `Excel` (defense-in-depth — the Application-layer callers always pass the correct value).

### 2.2. `XmlHashOutput` (Application.Abstractions) — NEW

Per-format hash-response record for XML. Matches the field set MISA §4.15 publishes for the XML response array (`document`, NOT `documentBytes`; carries `signatureId`).

```csharp
public sealed record XmlHashOutput(
    string DocumentId,
    string Document,        // raw XML text (not base64)
    string SignatureId,     // required for XML attachment per §4.6
    string Digest,
    string Sh);
```

Per FR-053, the Application-layer `HashXmlDocument` use case asserts non-empty values for `Document`, `SignatureId`, `Digest`, `Sh` before returning; if any is missing, it raises `ESignGeneralException(category = HashRejected, rawCode = "IncompleteHashResponse", format = Xml)`.

### 2.3. `WordExcelHashOutput` (Application.Abstractions) — NEW

Shared per-format hash-response record for Word and Excel (MISA §4.15 publishes identical fields for both). Carries `documentBytes` (base64), `mainDom` (required for Word/Excel attachment per §4.6), and `signatureId`.

```csharp
public sealed record WordExcelHashOutput(
    string DocumentId,
    string DocumentBytes,   // base64 of the (intermediate) document
    string SignatureId,     // required for Word/Excel attachment per §4.6
    string Digest,
    string MainDom);        // required for Word/Excel attachment per §4.6
```

Per FR-053, the Application-layer `HashWordDocument` / `HashExcelDocument` use cases assert non-empty values for `DocumentBytes`, `SignatureId`, `Digest`, `MainDom` before returning; if any is missing, they raise `ESignGeneralException(category = HashRejected, rawCode = "IncompleteHashResponse", format = Word | Excel)`.

### 2.4. `HashXmlDocument` / `HashWordDocument` / `HashExcelDocument` (Application.UseCases) — NEW

Siblings of slice 1's `HashPdfDocument`. Each one:
- Takes the access token, the selected certificate, the format-specific payload, the `documentId`, the format-specific signing context, and `CancellationToken`.
- Calls the matching `IMisaESignWireClient.Hash...Async(...)` method.
- Asserts non-empty required fields on the returned `XmlHashOutput` / `WordExcelHashOutput` per FR-053.
- Returns the per-format hash output record to the orchestrator.

| Use case | Required fields asserted | Synthesized error on missing fields |
|---|---|---|
| `HashXmlDocument` | `Document`, `SignatureId`, `Digest`, `Sh` | `ESignGeneralException(HashRejected, "IncompleteHashResponse", format = Xml)` |
| `HashWordDocument` | `DocumentBytes`, `SignatureId`, `Digest`, `MainDom` | `ESignGeneralException(HashRejected, "IncompleteHashResponse", format = Word)` |
| `HashExcelDocument` | `DocumentBytes`, `SignatureId`, `Digest`, `MainDom` | `ESignGeneralException(HashRejected, "IncompleteHashResponse", format = Excel)` |

### 2.5. `AttachSignatureToXml` (Application.UseCases) — NEW

Calls `IMisaESignWireClient.AttachSignatureToXmlAsync(...)`. Returns the signed XML bytes (decoded from the wire's `document` text — UTF-8 byte conversion happens at the wire layer; the Application layer sees `byte[]`).

| Step | Detail |
|---|---|
| Inputs | `accessToken`, `Certificate`, `XmlHashOutput hash`, `string signatureData`, `CancellationToken` |
| Behavior | Calls wire client; on success, asserts the returned bytes are non-empty; surfaces typed `ESignGeneralException(AttachmentRejected, "MissingSignedDocument", format = Xml)` if empty per FR-058. |
| Output | `byte[]` (signed XML bytes — UTF-8-encoded text) |

### 2.6. `AttachSignatureToWordExcel` (Application.UseCases) — NEW

Shared use case for Word + Excel (MISA §4.6 publishes one `Doc_Attackment` shape for both — the only difference is which response array carries the signed bytes). Takes a `DocumentFormat format` parameter that must be `Word` or `Excel`.

| Step | Detail |
|---|---|
| Inputs | `accessToken`, `Certificate`, `WordExcelHashOutput hash`, `string signatureData`, `DocumentFormat format`, `CancellationToken` |
| Behavior | Calls wire client; on success, asserts the returned bytes are non-empty; surfaces typed `ESignGeneralException(AttachmentRejected, "MissingSignedDocument", format = Word | Excel)` if empty per FR-058. |
| Output | `byte[]` (signed Word or Excel bytes — base64-decoded by the wire layer) |

### 2.7. `SignXml` / `SignWord` / `SignExcel` (Application.UseCases) — NEW orchestrators

Three thin orchestrators, each mirroring slice 1's `SignPdf` body shape exactly. Each:
1. Validates the per-format work request via `SignXmlRequestValidator` / `SignWordRequestValidator` / `SignExcelRequestValidator`.
2. Ensures the access token via the existing `EnsureAccessToken` use case.
3. Lists active certificates via the existing `ListActiveCertificates` use case.
4. Selects the certificate via the existing `ICertificateSelector` port.
5. Hashes the document via the format-specific `Hash...Document` use case.
6. Submits the signing hash via the existing `SubmitSignHash` use case (format is invisible at this hop — slice 1's `SubmitSignHashAsync(..., PdfHashOutput hash, ...)` is reused by adapting the per-format hash output's `Digest` into a slice-1 `PdfHashOutput`-shaped temporary; the field is the same `digest` value MISA expects regardless of format).
   - **Implementation note**: the cleanest shape is to widen `SubmitSignHash.ExecuteAsync(...)` to accept the digest string directly rather than threading a per-format hash output type through. To preserve FR-065 (slice-1 byte-identical), slice 3 keeps `SubmitSignHash.ExecuteAsync(..., PdfHashOutput hash, ...)` and adds a constructor-style helper on `PdfHashOutput`: e.g. each per-format hash output exposes a slice-1-compatible projection `ToSignHashInput()` that returns the slice-1 `PdfHashOutput` populated with the format's `DocumentId` and `Digest` (other fields default-empty, since they aren't needed downstream). Or: introduce a new lightweight `SignHashInput(string DocumentId, string Digest)` Application record and have `SubmitSignHash` take that; rename the slice-1 path to convert `PdfHashOutput → SignHashInput` at the call site. The implementation work picks the variant; both honor FR-065 because the wire call to `/Signing/hash` is byte-identical.
7. Polls status via the existing `PollSignStatus` use case (no change).
8. Attaches the signature via the format-specific `AttachSignatureTo...` use case.
9. Returns the per-format work result (`SignXmlWorkResult` / `SignWordWorkResult` / `SignExcelWorkResult`).

Each orchestrator inherits the slice-2 OTP catch-block pattern (the `try { … } catch (AuthenticationFailedException ex) when (allowOtpRecursion && ex.Requires2FA && _otpProvider is not null && _exchangeOtp is not null) { … }`) verbatim — see R-10. The per-format orchestrator does NOT re-pre-execute slice-1's `SignPdf` orchestrator; each is independent.

Per FR-062, every exception raised inside the orchestrator's `try` block carries the orchestrator's format. The wire-client exceptions already carry it (because the wire client passes `requestedFormat` into the mapper); the orchestrator does not need to wrap or rethrow.

### 2.8. `SignXmlWorkRequest` / `SignWordWorkRequest` / `SignExcelWorkRequest` (Application.UseCases) — NEW records

Application-layer canonical input records (the Client.Dtos layer maps into these). Mirror slice 1's `SignPdfWorkRequest` structure with the format-appropriate signing-context type:

```csharp
public sealed record SignXmlWorkRequest(
    string Xml,                          // always a string at the Application layer (R-2)
    XmlSignatureContext SignatureContext,
    string DocumentId,
    string DocumentName,
    string DataToBeDisplayed);

public sealed record SignWordWorkRequest(
    byte[] Word,
    SignatureInfo SignatureInfo,
    string DocumentId,
    string DocumentName,
    string DataToBeDisplayed);

public sealed record SignExcelWorkRequest(
    byte[] Excel,
    SignatureInfo SignatureInfo,
    string DocumentId,
    string DocumentName,
    string DataToBeDisplayed);
```

### 2.9. `SignXmlWorkResult` / `SignWordWorkResult` / `SignExcelWorkResult` (Application.UseCases) — NEW records

Application-layer canonical result records. The `Format` discriminator is set per orchestrator (`Xml` / `Word` / `Excel`):

```csharp
public sealed record SignXmlWorkResult(
    SignedDocument SignedXml,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Xml);

// (Word and Excel mirror this shape with `SignedWord` / `SignedExcel` and `Format = Word` / `Format = Excel`.)
```

The Client.Dtos result types (`SignXmlResultDto` etc.) wrap these 1:1.

### 2.10. `SignXmlRequestValidator` / `SignWordRequestValidator` / `SignExcelRequestValidator` (Application.Validation) — NEW

Siblings of slice 1's `SignPdfRequestValidator`. Each follows the same shape (collect errors, throw `ESignGeneralException(Validation, "Invalid…Request", format = Xml | Word | Excel)` if any errors).

| Validator | Rules |
|---|---|
| `SignXmlRequestValidator` | Non-empty `Xml`; non-empty `DocumentId` (≤ 36 chars); non-empty `DocumentName` (≤ 100 chars); non-empty `DataToBeDisplayed`; non-null `SignatureContext` with non-empty `SignatureName` and non-null `SignatureDescription` whose `SignedBy`/`Location`/`Reason`/`Contact` are non-empty. NO visual-position validation (the slim context has no visual-position fields). |
| `SignWordRequestValidator` | Non-empty `Word` bytes; non-empty `DocumentId` (≤ 36 chars); non-empty `DocumentName` (≤ 100 chars); non-empty `DataToBeDisplayed`; non-null `SignatureInfo` with non-empty `SignatureName`, non-empty `LogoImage`, non-null `SignatureDescription` whose `SignedBy`/`Location`/`Reason`/`Contact` are non-empty, `RenderingMode` in `{0, 1, 2}`. NO `Page >= 1` check (Word documents don't paginate the same way as PDFs; MISA accepts whatever value or null). |
| `SignExcelRequestValidator` | Symmetric with `SignWordRequestValidator`. |

The XML validator rejects the bad-input cases listed in R-2 (both `Xml` and `XmlUtf8Bytes` null on the Client DTO would already be caught by the Client-layer mapper before reaching here; both non-null is also caught at the Client-layer mapper).

### 2.11. `ESignErrorMapper` (Application.Errors) — EDITED

Two changes to the slice-1 mapper, both additive:

**Change 1**: The `Map(...)` method gains a `DocumentFormat requestedFormat` parameter with default `DocumentFormat.Pdf`:

```csharp
public static ESignException Map(
    string endpoint,
    int statusCode,
    ResponseError? envelope,
    string correlationId,
    bool includeRawErrorMessage = false,
    string? transactionId = null,
    int? attemptCount = null,
    HttpStatusCode? lastStatusCode = null,
    string? userName = null,
    DocumentFormat requestedFormat = DocumentFormat.Pdf)   // NEW
```

The default `Pdf` keeps existing slice-1 / slice-2 call sites (which all originated from PDF paths or auth paths that should now carry `Pdf`) working with the same behavior. Slice-3 wire-client per-format methods pass `Xml | Word | Excel` explicitly. Every typed exception construction inside `Map(...)` forwards `requestedFormat` into the new `format` constructor parameter on the exception.

**Change 2**: Two new format-aware synthesizer helpers, both private static:

```csharp
private static string SynthesizeHashCodeForFormat(string? rawCode, ResponseError? envelope, DocumentFormat requestedFormat);
private static string SynthesizeAttachmentCodeForFormat(string? rawCode, ResponseError? envelope, DocumentFormat requestedFormat);
```

`SynthesizeHashCodeForFormat` extends the slice-1 `SynthesizeHashCode` table with:
- Format == Xml + substring match on `"xml"` AND (`"malformed"` | `"invalid"` | `"không hợp lệ"`) → `"InvalidXmlInput"`.
- Any format + substring match on `"unsupported"` | `"variant"` | `"format not supported"` → `"UnsupportedDocumentVariant"`.
- Otherwise: delegates to the slice-1 `SynthesizeHashCode` table (which produces `InvalidCertificate` / `InvalidHash` / `InvalidDocument` / `InvalidSignatureInfo`).

`SynthesizeAttachmentCodeForFormat` extends the slice-1 `SynthesizeAttachmentCode` table with:
- Format ∈ {Word, Excel} + substring match on `"mainDom"` | `"main dom"` | `"missing main"` → `"MissingMainDom"`.
- Format ∈ {Xml, Word, Excel} + substring match on `"signatureId"` | `"signature id"` | `"missing signature"` → `"MissingSignatureId"`.
- Otherwise: delegates to the slice-1 `SynthesizeAttachmentCode` table (which produces `InvalidSignature` / `InvalidCertificate` / `InvalidHashInputs`).

The endpoint switch inside `Map(...)` selects which synthesizer helper to call: `EndpointHash` uses `SynthesizeHashCodeForFormat`; `EndpointAttachment` uses `SynthesizeAttachmentCodeForFormat`; all other endpoints continue to use the existing slice-1 logic (no per-format synthesis applies to login/refresh/certificates/sign-hash/sign-status). The mapping is enumerated in [contracts/error-mapping.md §A.10](./contracts/error-mapping.md#a10-per-format--documentshash-and--documentsattachment).

### 2.12. `EnsureAccessToken` / `RefreshAccessToken` / `ListActiveCertificates` / `SubmitSignHash` / `PollSignStatus` / `AttachSignature` (slice-1 PDF) / `ExchangeOtp` / `ResendOtp` — UNCHANGED

No edits beyond the format-property propagation (which is invisible at the orchestrator level — the exception construction sites inside the wire client do the propagation).

The one note for `SubmitSignHash`: per §2.7 above, either (a) each per-format hash output exposes a `ToSignHashInput()` helper that produces a slice-1 `PdfHashOutput`-shaped record carrying `DocumentId + Digest` for the call into `SubmitSignHash`, or (b) a lightweight Application record `SignHashInput(string DocumentId, string Digest)` is introduced and `SubmitSignHash` takes that. The implementation chooses; both honor FR-065.

---

## 3. Infrastructure layer — wire DTOs, mapping, scrubber, options, DI

### 3.1. `HashRequestDto` (Infrastructure.ESign.Wire) — EDITED

The four per-format request arrays are strengthened from `List<object>` to typed lists. Three new request DTOs are added next to the existing `PdfDocRequestDto`.

```csharp
internal sealed class HashRequestDto
{
    [JsonPropertyName("certificate")]
    public string Certificate { get; set; } = string.Empty;

    [JsonPropertyName("certificateChain")]
    public List<string> CertificateChain { get; set; } = new();

    [JsonPropertyName("pdfDocs")]
    public List<PdfDocRequestDto> PdfDocs { get; set; } = new();

    [JsonPropertyName("xmlDocs")]
    public List<XmlHashDocRequestDto> XmlDocs { get; set; } = new();   // EDITED — strengthened from List<object>

    [JsonPropertyName("wordDocs")]
    public List<WordHashDocRequestDto> WordDocs { get; set; } = new(); // EDITED

    [JsonPropertyName("excelDocs")]
    public List<ExcelHashDocRequestDto> ExcelDocs { get; set; } = new();   // EDITED
}
```

New per-format request entry DTOs (all with `internal sealed class` visibility):

```csharp
internal sealed class XmlHashDocRequestDto                                     // §4.1.2
{
    [JsonPropertyName("DocumentId")]     public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("FileToSign")]     public string FileToSign { get; set; } = string.Empty;   // raw XML text
    [JsonPropertyName("SignatureInfo")]  public SignatureInfoDto SignatureInfo { get; set; } = new();
}

internal sealed class WordHashDocRequestDto                                    // §4.1.1
{
    [JsonPropertyName("DocumentId")]     public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("FileToSign")]     public string FileToSign { get; set; } = string.Empty;   // base64
    [JsonPropertyName("SignatureInfo")]  public SignatureInfoDto SignatureInfo { get; set; } = new();
}

internal sealed class ExcelHashDocRequestDto                                   // §4.1.1
{
    [JsonPropertyName("DocumentId")]     public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("FileToSign")]     public string FileToSign { get; set; } = string.Empty;   // base64
    [JsonPropertyName("SignatureInfo")]  public SignatureInfoDto SignatureInfo { get; set; } = new();
}
```

All three reuse the existing slice-1 `SignatureInfoDto` for the `SignatureInfo` field. The XML facade's wire-side `SignatureInfo` is populated by `XmlSignatureContextMapper` (§3.5), which fills MISA-documented "no signature visualization" defaults for the visual fields the slim Domain context omits.

### 3.2. `HashResponseDto` (Infrastructure.ESign.Wire) — EDITED

The three per-format response arrays are strengthened from `List<object>?` to typed lists. Three new response DTOs are added.

```csharp
internal sealed class HashResponseDto
{
    [JsonPropertyName("pdfDocs")]
    public List<PdfHashOutputDto> PdfDocs { get; set; } = new();

    [JsonPropertyName("xmlDocs")]
    public List<XmlHashOutputDto>? XmlDocs { get; set; }     // EDITED

    [JsonPropertyName("wordDocs")]
    public List<WordHashOutputDto>? WordDocs { get; set; }   // EDITED

    [JsonPropertyName("excelDocs")]
    public List<ExcelHashOutputDto>? ExcelDocs { get; set; } // EDITED
}
```

New per-format response entry DTOs (per MISA §4.15):

```csharp
internal sealed class XmlHashOutputDto              // XML uses `document`, not `documentBytes`
{
    [JsonPropertyName("documentId")]   public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("document")]     public string Document { get; set; } = string.Empty;   // raw XML text
    [JsonPropertyName("signatureId")]  public string SignatureId { get; set; } = string.Empty;
    [JsonPropertyName("digest")]       public string Digest { get; set; } = string.Empty;
    [JsonPropertyName("sh")]           public string Sh { get; set; } = string.Empty;
}

internal sealed class WordHashOutputDto             // Word uses `documentBytes` and `mainDom`
{
    [JsonPropertyName("documentId")]    public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("documentBytes")] public string DocumentBytes { get; set; } = string.Empty;
    [JsonPropertyName("signatureId")]   public string SignatureId { get; set; } = string.Empty;
    [JsonPropertyName("digest")]        public string Digest { get; set; } = string.Empty;
    [JsonPropertyName("mainDom")]       public string MainDom { get; set; } = string.Empty;
}

internal sealed class ExcelHashOutputDto            // Excel is identical to Word per §4.15
{
    [JsonPropertyName("documentId")]    public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("documentBytes")] public string DocumentBytes { get; set; } = string.Empty;
    [JsonPropertyName("signatureId")]   public string SignatureId { get; set; } = string.Empty;
    [JsonPropertyName("digest")]        public string Digest { get; set; } = string.Empty;
    [JsonPropertyName("mainDom")]       public string MainDom { get; set; } = string.Empty;
}
```

> **Note (audit)** — Word and Excel share the same field set per §4.15, but slice 3 keeps two separate DTO types (rather than a shared `WordExcelHashOutputDto`) because the JSON path matters for the scrubber: the per-format `$.wordDocs[*]` and `$.excelDocs[*]` JSON-path matchers are simpler to reason about when each array has its own typed DTO. The Application-layer `WordExcelHashOutput` record is a single shared type because, in Application, the format dispatching is already a concern of the calling orchestrator.

### 3.3. `AttachmentRequestDto` (Infrastructure.ESign.Wire) — EDITED

The four per-format request arrays are strengthened from `List<object>` to typed lists. Two new request DTOs are added (XML carries `signatureId` but no `mainDom`; Word/Excel share a shape that carries both).

```csharp
internal sealed class AttachmentRequestDto
{
    [JsonPropertyName("certificate")]
    public string Certificate { get; set; } = string.Empty;

    [JsonPropertyName("certificateChain")]
    public List<string> CertificateChain { get; set; } = new();

    [JsonPropertyName("pdfDocs")]
    public List<AttachmentPdfDocRequestDto> PdfDocs { get; set; } = new();

    [JsonPropertyName("xmlDocs")]
    public List<XmlAttachmentDocRequestDto> XmlDocs { get; set; } = new();             // EDITED

    [JsonPropertyName("wordDocs")]
    public List<WordExcelAttachmentDocRequestDto> WordDocs { get; set; } = new();      // EDITED

    [JsonPropertyName("excelDocs")]
    public List<WordExcelAttachmentDocRequestDto> ExcelDocs { get; set; } = new();     // EDITED — shares the Word DTO
}
```

New per-format attachment-request DTOs (per MISA §4.6 — `Doc_Attackment`, preserving the misspelling verbatim):

```csharp
internal sealed class XmlAttachmentDocRequestDto
{
    [JsonPropertyName("signature")]      public string Signature { get; set; } = string.Empty;
    [JsonPropertyName("documentId")]     public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("documentBytes")]  public string DocumentBytes { get; set; } = string.Empty;
    [JsonPropertyName("digest")]         public string Digest { get; set; } = string.Empty;
    [JsonPropertyName("signatureName")]  public string SignatureName { get; set; } = string.Empty;
    [JsonPropertyName("sh")]             public string Sh { get; set; } = string.Empty;
    [JsonPropertyName("signatureId")]    public string SignatureId { get; set; } = string.Empty;   // required for XML per §4.6
    [JsonPropertyName("documentHash")]   public string DocumentHash { get; set; } = string.Empty;
    // mainDom NOT present — XAdES has no mainDom notion.
}

internal sealed class WordExcelAttachmentDocRequestDto
{
    [JsonPropertyName("signature")]      public string Signature { get; set; } = string.Empty;
    [JsonPropertyName("documentId")]     public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("documentBytes")]  public string DocumentBytes { get; set; } = string.Empty;
    [JsonPropertyName("digest")]         public string Digest { get; set; } = string.Empty;
    [JsonPropertyName("mainDom")]        public string MainDom { get; set; } = string.Empty;        // required for Word/Excel per §4.6
    [JsonPropertyName("signatureName")]  public string SignatureName { get; set; } = string.Empty;
    [JsonPropertyName("sh")]             public string Sh { get; set; } = string.Empty;
    [JsonPropertyName("signatureId")]    public string SignatureId { get; set; } = string.Empty;   // required for Word/Excel per §4.6
    [JsonPropertyName("documentHash")]   public string DocumentHash { get; set; } = string.Empty;
}
```

### 3.4. `AttachmentResponseDto` (Infrastructure.ESign.Wire) — EDITED

Three new per-format response arrays are added. The existing PDF array (`PdfDocs`) is unchanged.

```csharp
internal sealed class AttachmentResponseDto
{
    [JsonPropertyName("pdfDocs")]
    public List<AttachmentPdfDocResponseDto>? PdfDocs { get; set; }

    [JsonPropertyName("xmlDocs")]
    public List<XmlAttachmentDocResponseDto>? XmlDocs { get; set; }                    // NEW

    [JsonPropertyName("wordDocs")]
    public List<WordExcelAttachmentDocResponseDto>? WordDocs { get; set; }             // NEW

    [JsonPropertyName("excelDocs")]
    public List<WordExcelAttachmentDocResponseDto>? ExcelDocs { get; set; }            // NEW — shares the Word DTO
}

internal sealed class XmlAttachmentDocResponseDto
{
    [JsonPropertyName("documentId")]   public string? DocumentId { get; set; }
    [JsonPropertyName("document")]     public string? Document { get; set; }   // signed XML text (UTF-8)
}

internal sealed class WordExcelAttachmentDocResponseDto
{
    [JsonPropertyName("documentId")]   public string? DocumentId { get; set; }
    [JsonPropertyName("document")]     public string? Document { get; set; }   // signed Word/Excel base64
}
```

### 3.5. `XmlSignatureContextMapper` (Infrastructure.ESign.Mapping) — NEW

Maps `Domain.Signing.XmlSignatureContext` to the wire `SignatureInfoDto`, filling MISA-documented "no signature visualization" defaults for the visual fields the slim context omits (R-4 / FR-060).

```csharp
internal static class XmlSignatureContextMapper
{
    public static SignatureInfoDto ToWireSignatureInfo(XmlSignatureContext source) => new()
    {
        SignatureName    = source.SignatureName,
        HashAlgorithm    = source.HashAlgorithm.ToString(),
        LogoImage        = string.Empty,       // no visual logo for XML signatures
        RenderingMode    = 0,                  // MISA's documented "diễn giải only" default for non-visual signatures
        SignatureDescription = new SignatureDescriptionDto
        {
            SignedBy        = source.SignatureDescription.SignedBy,
            ShowSignedDate  = source.SignatureDescription.ShowSignedDate,
            Location        = source.SignatureDescription.Location,
            Reason          = source.SignatureDescription.Reason,
            Contact         = source.SignatureDescription.Contact,
            DisplayText     = source.SignatureDescription.DisplayText,
        },
        // TextColor, PositionX/Y, Width, Height, FontSize, FontData, SignatureImage, Page, SignaturePosInfos — all left null/default.
        // System.Text.Json's [JsonIgnore(Condition = WhenWritingNull)] on the wire DTO causes these to be omitted from the JSON payload.
    };
}
```

The mapper lives in Infrastructure (not Domain) because the wire-default-filling decisions are JSON-serialization concerns. The slim Domain record stays free of wire knowledge.

### 3.6. `MisaESignWireClient` (Infrastructure.ESign) — EDITED

Implements the five new methods on `IMisaESignWireClient`. Each method mirrors the existing `HashPdfAsync(...)` / `AttachSignatureAsync(...)` structure:
- POST with `ApplyAuth(req, accessToken)` for the `AuthorizationRM` header.
- Composes the request body via the new per-format DTO into the correct array on the existing `HashRequestDto` / `AttachmentRequestDto` envelope; leaves the other three arrays empty per FR-052.
- Reads the response, deserializes via the existing `Deserialize<T>` helper.
- On non-2xx: calls `ESignErrorMapper.Map(..., requestedFormat: Xml | Word | Excel)` via `ThrowMappedAsync`.
- On 2xx: extracts the matching response array (`HashResponseDto.XmlDocs[0]` etc. for hash; `AttachmentResponseDto.XmlDocs[0].Document` etc. for attachment); raises `ESignGeneralException(MisaUnknown, "EmptyResponse", format = …)` if the matching array is empty (which would be a doc-level "we expected this format's array to be populated but it wasn't" condition).
- Returns the per-format hash output / signed bytes.

The `AttachSignatureToWordExcelAsync(..., DocumentFormat format, ...)` method branches at two points internally:
1. Request composition: if `format == Word`, populates `req.WordDocs[0]`; if `format == Excel`, populates `req.ExcelDocs[0]`. Validates `format ∈ {Word, Excel}` defensively at the entry.
2. Response extraction: reads `resp.WordDocs[0].Document` or `resp.ExcelDocs[0].Document` to match.

The wire client passes `requestedFormat = format` to the error mapper on every failure path so the surfaced exception's `Format` property matches.

The slice-1 `HashPdfAsync(...)` / `AttachSignatureAsync(...)` methods are touched only to pass `requestedFormat: DocumentFormat.Pdf` to the error mapper. No request-shape change, no response-shape change. FR-065 / SC-021 are preserved.

### 3.7. `ESignLogScrubber` (Infrastructure.Logging) — EDITED

Adds JSON-path matchers per FR-063 / SC-020. The scrubber's existing per-endpoint rule table accommodates the new rules without infrastructure changes.

| Rule | Endpoint scope | Stage | Replacement |
|---|---|---|---|
| `$.xmlDocs[*].FileToSign` | `/documents/hash` | Request | `"<redacted-doc>"` |
| `$.wordDocs[*].FileToSign` | `/documents/hash` | Request | `"<redacted-doc>"` |
| `$.excelDocs[*].FileToSign` | `/documents/hash` | Request | `"<redacted-doc>"` |
| `$..documentBytes` | `/documents/hash`, `/documents/attachment` | Both stages | `"<redacted>"` |
| `$..document` | `/documents/hash`, `/documents/attachment` | Response stage | `"<redacted>"` |
| `$..documentHash` | `/documents/hash`, `/documents/attachment` | Both stages | `"<redacted>"` |
| `$..digest` | `/documents/hash`, `/documents/attachment` | Both stages | `"<redacted>"` |
| `$..mainDom` | `/documents/hash`, `/documents/attachment` | Both stages | `"<redacted>"` |
| `$..sh` | `/documents/hash`, `/documents/attachment` | Both stages | `"<redacted>"` |
| `$..signatureId` | `/documents/hash`, `/documents/attachment` | Both stages | `"<redacted>"` |
| `$..signature` | `/documents/attachment` | Request stage | `"<redacted>"` |

The existing slice-1 `$.pdfDocs[*].FileToSign` / `$.pdfDocs[*].document` rules continue to apply to PDF paths.

> **Note (audit)** — `$..document` is scoped to `/documents/*` endpoints so it does not over-redact unrelated `document` fields (e.g. nothing in slice 1/2 has such a field, but defending against future MISA envelope additions matters). The scrubber's existing per-endpoint scoping mechanism is sufficient.

### 3.8. `ServiceCollectionExtensions` (Infrastructure.DependencyInjection) — EDITED

Registers the slice-3 use cases and validators with `TryAddScoped` (mirroring the slice-1 / slice-2 registration pattern):

```csharp
services.TryAddScoped<HashXmlDocument>();
services.TryAddScoped<HashWordDocument>();
services.TryAddScoped<HashExcelDocument>();
services.TryAddScoped<AttachSignatureToXml>();
services.TryAddScoped<AttachSignatureToWordExcel>();
services.TryAddScoped<SignXmlRequestValidator>();
services.TryAddScoped<SignWordRequestValidator>();
services.TryAddScoped<SignExcelRequestValidator>();
services.TryAddScoped<SignXml>(sp => /* lambda mirrors the existing SignPdf lambda, including the optional IOtpProvider resolution */);
services.TryAddScoped<SignWord>(sp => /* same shape */);
services.TryAddScoped<SignExcel>(sp => /* same shape */);
```

No new option-binding code, no new port registrations beyond the use cases. The existing `IMisaESignClient` registration (the `MisaESignClient` facade) is updated to resolve the three new orchestrators from the service provider.

### 3.9. `MisaESignOptions` / `MisaESignOptionsValidator` (Infrastructure.Configuration) — UNCHANGED

Slice 3 introduces NO new options per FR-066. The existing `Polling`, `TransportRetry`, `Errors`, `Otp` blocks all apply to the new format paths unchanged. The validator gains NO new rules.

---

## 4. Client layer — facade additions, DTOs, mappers

### 4.1. `IMisaESignClient` (Client) — EDITED

Adds three new methods (the XML facade is one method whose `SignXmlRequestDto` parameter carries either `Xml` or `XmlUtf8Bytes` — see §4.4):

```csharp
public interface IMisaESignClient
{
    // (existing — unchanged)
    // Task<SignPdfResultDto> SignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default);
    // Task SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken ct = default);
    // Task<OtpResendResultDto> ResendOtpAsync(string? language = null, CancellationToken ct = default);

    /// <summary>
    /// Sign an XML document end-to-end via MISA eSign RemoteSigning. Same
    /// orchestration as SignPdfAsync (login or cached-token reuse, certificate
    /// selection, server-side hashing, signing, status polling, signature
    /// attachment). The request DTO accepts either `Xml` (string — primary;
    /// matches MISA's "text content" semantics for FileToSign per §4.1.2) or
    /// `XmlUtf8Bytes` (byte[] — secondary; UTF-8-decoded by the SDK). Returns
    /// the signed XML bytes (UTF-8 encoded) in <see cref="SignXmlResultDto.SignedXml"/>.
    /// </summary>
    /// <exception cref="ESignGeneralException">Validation failure, hash rejection, attachment rejection (Format == Xml).</exception>
    /// <exception cref="ESignTransportException">Transport-retry budget exhausted (Format == Xml).</exception>
    /// <exception cref="AuthenticationFailedException">Cached token invalid; if Requires2FA, see SignInWithOtpAsync (Format == Xml on the surfaced exception even when caused by the auth layer because the consumer requested an XML signing operation).</exception>
    Task<SignXmlResultDto> SignXmlAsync(SignXmlRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Sign a Word (OOXML .docx) document end-to-end. Mirrors SignPdfAsync;
    /// uses MISA's `wordDocs` per-format array on /documents/hash and
    /// /documents/attachment per §4.1.1 / §4.6 / §4.15. Returns the signed
    /// Word bytes (OOXML) in <see cref="SignWordResultDto.SignedWord"/>.
    /// </summary>
    Task<SignWordResultDto> SignWordAsync(SignWordRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Sign an Excel (OOXML .xlsx) document end-to-end. Mirrors SignPdfAsync;
    /// uses MISA's `excelDocs` per-format array per §4.1.1 / §4.6 / §4.15.
    /// Returns the signed Excel bytes (OOXML) in
    /// <see cref="SignExcelResultDto.SignedExcel"/>.
    /// </summary>
    Task<SignExcelResultDto> SignExcelAsync(SignExcelRequestDto request, CancellationToken ct = default);
}
```

The 2FA-captured-userName `AsyncLocal<string?>` flow added in slice 2 ([slice-2 §4.1](../002-misa-esign-2fa-otp/data-model.md#41-imisaesignclient-client--edited)) carries over verbatim: the three new facade methods can each raise `AuthenticationFailedException(Requires2FA = true)`, and after such an exception escapes any of the four facade methods, the consumer's subsequent `SignInWithOtpAsync(...)` call uses the captured userName as established by slice 2.

### 4.2. `MisaESignClient` (Client) — EDITED

Wires:

| Method | Implementation |
|---|---|
| `SignXmlAsync(request, ct)` | Maps `SignXmlRequestDto → SignXmlWorkRequest` via `SignXmlRequestMapper`. Calls `SignXml.ExecuteAsync(workRequest, ct)`. Maps `SignXmlWorkResult → SignXmlResultDto`. On `AuthenticationFailedException(Requires2FA = true)`, captures `ex.Username` into the `AsyncLocal<string?>` (slice-2 behavior). |
| `SignWordAsync(request, ct)` | Maps `SignWordRequestDto → SignWordWorkRequest` via `SignWordRequestMapper`. Calls `SignWord.ExecuteAsync(workRequest, ct)`. Maps `SignWordWorkResult → SignWordResultDto`. Same 2FA-capture behavior. |
| `SignExcelAsync(request, ct)` | Symmetric with `SignWordAsync`. |
| `SignPdfAsync(...)` (existing) | UNCHANGED behavior. The internal exception construction sites pass `DocumentFormat.Pdf` to satisfy FR-062, but the observable wire and exception behavior is byte-identical to slice 1 (FR-065). |

### 4.3. `XmlSignatureContextDto` (Client.Dtos) — NEW

Public, consumer-facing mirror of `Domain.Signing.XmlSignatureContext`. Lives in `Client.Dtos` so consumers do not need `using MisaConnect.ESign.Domain.Signing;` to construct an XML signing request.

```csharp
public sealed record XmlSignatureContextDto(
    string SignatureName,
    string HashAlgorithm,                  // "SHA256" (default) — string for forward compat
    SignatureDescriptionDto SignatureDescription);
```

`SignatureDescriptionDto` is a Client-level mirror of `Domain.Signing.SignatureDescription` — slice 1 already exposes it (or an equivalent flat-field set in `SignPdfRequestDto`). For slice 3 it is convenient to introduce a small `SignatureDescriptionDto` record so all four format facades reuse the same shape; if slice 1 currently inlines the fields onto `SignPdfRequestDto` only, slice 3 introduces the record additively without changing `SignPdfRequestDto`'s surface.

### 4.4. `SignXmlRequestDto` (Client.Dtos) — NEW

Public consumer-facing request shape. Exactly one of (`Xml`, `XmlUtf8Bytes`) MUST be non-null.

```csharp
public sealed record SignXmlRequestDto(
    string? Xml,
    byte[]? XmlUtf8Bytes,
    XmlSignatureContextDto SignatureContext,
    string DocumentName,
    string DataToBeDisplayed,
    string? DocumentId = null);
```

The Client-layer `SignXmlRequestMapper` enforces the "exactly one of (Xml, XmlUtf8Bytes) non-null" invariant and decodes `XmlUtf8Bytes` via `Encoding.UTF8.GetString(...)` when used. The decoded result and the supplied `XmlSignatureContextDto` (mapped to `Domain.Signing.XmlSignatureContext`) feed into the Application-layer `SignXmlWorkRequest`.

Consumers may construct it using either named arguments or two convenience factory methods (recommended in the quickstart):

```csharp
public static class SignXmlRequest
{
    public static SignXmlRequestDto FromString(string xml, XmlSignatureContextDto ctx, string documentName, string dataToBeDisplayed, string? documentId = null)
        => new SignXmlRequestDto(xml, null, ctx, documentName, dataToBeDisplayed, documentId);

    public static SignXmlRequestDto FromUtf8Bytes(byte[] xmlUtf8Bytes, XmlSignatureContextDto ctx, string documentName, string dataToBeDisplayed, string? documentId = null)
        => new SignXmlRequestDto(null, xmlUtf8Bytes, ctx, documentName, dataToBeDisplayed, documentId);
}
```

The factory class lives in `Client.Dtos` alongside the DTO.

### 4.5. `SignWordRequestDto` / `SignExcelRequestDto` (Client.Dtos) — NEW

Mirror slice 1's `SignPdfRequestDto` shape for the Word and Excel format facades. They reuse the slice-1 flat-field `SignatureInfo` shape because PDF/Word/Excel all share the visual-positioning surface per FR-059.

```csharp
public sealed record SignWordRequestDto(
    byte[] Word,
    string DocumentName,
    string SignerName,
    string Location,
    string Reason,
    string Contact,
    string LogoImageBase64,
    string DataToBeDisplayed,
    int? Page = null,
    int? PositionX = null,
    int? PositionY = null,
    int? Width = null,
    int? Height = null,
    int? RenderingMode = null,
    string? DocumentId = null,
    string? DisplayText = null,
    bool ShowSignedDate = true,
    IReadOnlyList<SignaturePosInfoDto>? AdditionalSignaturePositions = null);

// SignExcelRequestDto is symmetric — same fields with `Excel` in place of `Word`.
```

The `Page` / position fields are exposed and forwarded to MISA verbatim per FR-059 (Word/Excel may or may not honor them; that's MISA's decision, not the SDK's).

### 4.6. `SignXmlResultDto` / `SignWordResultDto` / `SignExcelResultDto` (Client.Dtos) — NEW

Public consumer-facing result types. Mirror slice 1's `SignPdfResultDto` shape with the format-appropriate signed-bytes field name and a `Format` discriminator.

```csharp
public sealed record SignXmlResultDto(
    byte[] SignedXml,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Xml);

public sealed record SignWordResultDto(
    byte[] SignedWord,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Word);

public sealed record SignExcelResultDto(
    byte[] SignedExcel,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Excel);
```

The `Format` default is set on each result type — consumers can branch on it uniformly. The XML result's `SignedXml` is UTF-8-encoded bytes; consumers who want a string call `Encoding.UTF8.GetString(...)`.

> **Note (audit)** — `SignPdfResultDto` (slice 1) is updated to add a `DocumentFormat Format = DocumentFormat.Pdf` property additively. Existing call sites that don't read it keep working; consumers who want to branch on `Format` now have a uniform shape across all four results.

### 4.7. `SignXmlRequestMapper` / `SignWordRequestMapper` / `SignExcelRequestMapper` (Client.Mapping) — NEW

Each mapper is a single static helper class with a `ToWorkRequest(...)` method:

| Mapper | Input → Output |
|---|---|
| `SignXmlRequestMapper` | `SignXmlRequestDto → SignXmlWorkRequest`. Enforces "exactly one of (Xml, XmlUtf8Bytes) non-null". UTF-8-decodes `XmlUtf8Bytes` if used. Builds `XmlSignatureContext` from `XmlSignatureContextDto` (string→enum parse on `HashAlgorithm` with default `HashAlgorithm.SHA256` for null/empty). Generates a `DocumentId` if the consumer didn't supply one (same default behavior as slice 1's `SignPdfRequestMapper`). |
| `SignWordRequestMapper` | `SignWordRequestDto → SignWordWorkRequest`. Composes a slice-1-shaped `SignatureInfo` from the flat fields (mirrors `SignPdfRequestMapper`). |
| `SignExcelRequestMapper` | Symmetric with `SignWordRequestMapper`. |

---

## 5. State machine — slice-3 paths

```
┌──────────────────────────┐
│ Consumer calls           │
│ SignXml/Word/ExcelAsync  │
└──────────┬───────────────┘
           │
           ▼
┌─────────────────────────────────────────┐
│ Client.MisaESignClient.Sign{Format}Async│
│  Map Client.DTO → Application.WorkRequest│
└──────────┬──────────────────────────────┘
           │
           ▼
┌─────────────────────────────────────────┐
│ Application.UseCases.Sign{Format}       │
│  (1) validator.Validate(request)        │
│  (2) EnsureAccessToken → cache/login    │
│  (3) ListActiveCertificates             │
│  (4) ICertificateSelector.SelectAsync   │
│  (5) Hash{Format}Document               │
│        → MisaESignWireClient.Hash{Format}│
│        → assert required-field profile  │
│           per §2.4                      │
│  (6) SubmitSignHash (slice 1 — format-  │
│        invisible at this hop)           │
│  (7) PollSignStatus (slice 1)           │
│  (8) AttachSignatureTo{Xml|WordExcel}   │
│        → MisaESignWireClient.Attach...  │
│        → assert non-empty signed bytes  │
│           per FR-058                    │
│  (9) Build Sign{Format}WorkResult with  │
│        Format = {Xml | Word | Excel}    │
└──────────┬──────────────────────────────┘
           │
   ┌───────┴──────────────────┐
   │                          │
   ▼ (success)                ▼ (typed failure)
┌────────────────────┐  ┌────────────────────────────────────┐
│ Map WorkResult →   │  │ ESignErrorMapper or use-case-level │
│ Sign{Format}ResultDto │ synthesized error — every typed   │
│ Return to consumer │  │ exception carries Format =        │
└────────────────────┘  │ {Xml | Word | Excel}              │
                        │                                    │
                        │ Special case — AuthFailedException │
                        │ with Requires2FA = true:           │
                        │   capture ex.Username in AsyncLocal│
                        │   (slice 2 behavior); orchestrator │
                        │   either invokes IOtpProvider      │
                        │   (transparent) or rethrows        │
                        │   (explicit pair).                 │
                        └────────────────────────────────────┘
```

The PDF path (slice 1) follows the same state machine using `SignPdf` → `HashPdfDocument` → slice-1 `AttachSignature`. The wire-level details are byte-identical to slice 1 per FR-065 / SC-021.

The OTP path (slice 2) follows the same state machine — the `OtpChallenge` carries only `UserName + CorrelationId` so the same `IOtpProvider` implementation can satisfy 2FA for any of the four facades (FR-067).

---

## 6. Validation summary (slice-3 only)

- `SignXmlRequestDto`: enforced at the Client-layer `SignXmlRequestMapper`: exactly one of (`Xml`, `XmlUtf8Bytes`) non-null. Subsequent Application-layer `SignXmlRequestValidator` checks per §2.10.
- `SignWordRequestDto` / `SignExcelRequestDto`: Application-layer `SignWordRequestValidator` / `SignExcelRequestValidator` per §2.10.
- `XmlSignatureContext` / `XmlSignatureContextDto`: validated indirectly via the request validators (no separate validator for the context type).
- `DocumentFormat`: closed enum, no runtime validation needed (the compiler enforces the closed-set property).
- Per-format hash-response field profiles (FR-053): enforced inside the Application-layer `Hash...Document` use cases — see §2.4.
- Per-format attachment-response signed-bytes profile (FR-058): enforced inside the Application-layer `AttachSignatureTo...` use cases — see §2.5 / §2.6.
- No new options validation — slice 3 adds no new options (FR-066).

---

## 7. Out-of-scope confirmation

The following entities and behaviors are **not** introduced or modified by slice 3 (per [research.md R-11](./research.md#r-11-scope-discipline--what-slice-3-explicitly-does-not-touch)):

- `Certificate`, `CertificateChain`, `KeyStatus` (slice 1) — unchanged.
- `SignatureInfo`, `SignatureDescription`, `SignTransaction`, `SignStatus`, `HashAlgorithm`, `SignaturePosInfo` (slice 1) — unchanged.
- `OtpDeliveryChannel`, `IOtpProvider`, `OtpChallenge`, `OtpSubmission`, `OtpResendResult`, `OtpResendResultDto`, `MisaESignOtpOptions` (slice 2) — unchanged.
- `ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`, `ISystemClock`, `ICorrelationIdAccessor` (slice 1) — unchanged. `IMisaESignWireClient` gains 5 new methods (additive, non-breaking).
- `MisaESignOptions.Polling`, `MisaESignOptions.TransportRetry`, `MisaESignOptions.Errors`, `MisaESignOptions.Otp` — unchanged.
- Wire DTOs: `LoginRequestDto`, `LoginResponseDto`, `RefreshTokenRequestDto`/`ResponseDto`, `CertificateDto`, `SignHashRequestDto`/`ResponseDto`, `SignStatusResponseDto`, `TwoFactorAuthDtos`, `ResendOtpDtos`, `ResponseErrorDto`, `PdfDocRequestDto`, `PdfHashOutputDto`, `AttachmentPdfDocRequestDto`, `AttachmentPdfDocResponseDto` — unchanged.
- `ESignErrorCategory` enum — no new members.
- `MisaESignOptionsValidator` — no new validation rules.
- The slice-1 `SignPdfAsync` / slice-2 `SignInWithOtpAsync` / `ResendOtpAsync` method signatures — unchanged.
- `ESignLogScrubber`'s existing slice-1 and slice-2 rules — unchanged (slice 3 only adds new rules).
- The in-repo `EsignFake/FakeMisaESignServer.cs` handlers for `/api/auth/api/v1/auth/login-api`, `/refreshtoken`, `/two-factor-auth`, `/resend-otp-auth`, `/Certificates/by-userId`, `/Signing/hash`, `/Signing/status` — unchanged. Only the `/documents/hash` and `/documents/attachment` handlers are extended.

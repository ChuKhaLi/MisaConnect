# Public surface contract — `MisaConnect.ESign` slice 3 additions

This file pins the slice-3 deltas on the public types consumers depend on. Per Constitution Principle II, every change here is a semver event for the `MisaConnect.ESign` NuGet package. Slice 3 is purely additive — no breaking changes — so it ships as a minor pre-release bump (`2.0.0-preview.3`).

The slice-1 public surface listed in [specs/001-misa-esign-pdf-sign-flow/contracts/public-surface.md](../../001-misa-esign-pdf-sign-flow/contracts/public-surface.md) and the slice-2 surface listed in [specs/002-misa-esign-2fa-otp/contracts/public-surface.md](../../002-misa-esign-2fa-otp/contracts/public-surface.md) remain authoritative for everything not enumerated here.

---

## 1. Facade additions — `IMisaESignClient`

`namespace MisaConnect.ESign.Client`

Three new methods on the existing interface:

```csharp
public interface IMisaESignClient
{
    // (existing slice-1: SignPdfAsync)
    // (existing slice-2: SignInWithOtpAsync, ResendOtpAsync)

    /// <summary>
    /// Sign an XML document end-to-end via MISA eSign RemoteSigning. Mirrors
    /// SignPdfAsync's orchestration (login or cached-token reuse, certificate
    /// selection, server-side hashing, signing, status polling, signature
    /// attachment). The request DTO accepts either `Xml` (string — primary;
    /// matches MISA's "text content" semantics for FileToSign per §4.1.2) or
    /// `XmlUtf8Bytes` (byte[] — secondary; UTF-8-decoded by the SDK). Returns
    /// the signed XML bytes (UTF-8 encoded) in SignXmlResultDto.SignedXml.
    /// </summary>
    /// <exception cref="ESignGeneralException">Validation failure, hash rejection, attachment rejection. Format == Xml.</exception>
    /// <exception cref="ESignTransportException">Transport-retry budget exhausted. Format == Xml.</exception>
    /// <exception cref="AuthenticationFailedException">Cached token invalid; if Requires2FA, complete via SignInWithOtpAsync. Format == Xml.</exception>
    /// <exception cref="NoActiveCertificateException">No ACTIVE certificate found for the configured account. Format == Xml.</exception>
    /// <exception cref="SignTerminalStateException">Signing reached FAILED or CANCELLED. Format == Xml.</exception>
    /// <exception cref="SignTimeoutException">Polling timeout exceeded before terminal state. Format == Xml.</exception>
    Task<SignXmlResultDto> SignXmlAsync(SignXmlRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Sign a Word (OOXML .docx) document end-to-end. Uses MISA's wordDocs
    /// per-format array on /documents/hash and /documents/attachment per
    /// §4.1.1 / §4.6 / §4.15. Returns the signed Word bytes (OOXML) in
    /// SignWordResultDto.SignedWord. Visual-position fields on SignWordRequestDto
    /// are forwarded to MISA verbatim per FR-059.
    /// </summary>
    Task<SignWordResultDto> SignWordAsync(SignWordRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Sign an Excel (OOXML .xlsx) document end-to-end. Symmetric with
    /// SignWordAsync; uses MISA's excelDocs per-format array.
    /// </summary>
    Task<SignExcelResultDto> SignExcelAsync(SignExcelRequestDto request, CancellationToken ct = default);
}
```

The 2FA-captured-userName `AsyncLocal<string?>` flow added in slice 2 carries over to the three new facade methods unchanged — `AuthenticationFailedException(Requires2FA = true)` raised by any of the four facades sets the captured userName, which `SignInWithOtpAsync` then consumes.

---

## 2. New domain enum — `DocumentFormat`

`namespace MisaConnect.ESign.Domain.Documents`

```csharp
public enum DocumentFormat : byte
{
    /// <summary>Sentinel — reserved for failures surfaced outside any specific facade call.</summary>
    Unknown = 0,

    /// <summary>The consumer called SignPdfAsync.</summary>
    Pdf = 1,

    /// <summary>The consumer called SignXmlAsync (either overload).</summary>
    Xml = 2,

    /// <summary>The consumer called SignWordAsync.</summary>
    Word = 3,

    /// <summary>The consumer called SignExcelAsync.</summary>
    Excel = 4,
}
```

Closed enum — the MISA-supported format set is fixed at four. Adding a new value is a binary-breaking semver event by definition (Principle VII).

The enum has no wire-level serialization — it never appears in any MISA request or response body. It lives entirely inside the SDK as a result-typing and exception-typing discriminator.

---

## 3. New domain record — `XmlSignatureContext`

`namespace MisaConnect.ESign.Domain.Signing`

Slim signing-context record for the XML facade. Exposes ONLY the XAdES-meaningful fields.

```csharp
public sealed record XmlSignatureContext(
    string SignatureName,
    HashAlgorithm HashAlgorithm,
    SignatureDescription SignatureDescription);
```

| Field | Type | Required? | Notes |
|---|---|---|---|
| `SignatureName` | `string` | yes | MISA §4.2 requires it. |
| `HashAlgorithm` | `HashAlgorithm` | yes | Slice-1 enum; defaults to `SHA256` when constructed via the Client DTO mapper. |
| `SignatureDescription` | `SignatureDescription` | yes | Slice-1 record (`SignedBy`, `ShowSignedDate`, `Location`, `Reason`, `Contact`, `DisplayText`). All XAdES-meaningful. |

The record does NOT expose any visual-positioning or rendering fields (`PositionX`, `PositionY`, `Width`, `Height`, `Page`, `SignaturePosInfos`, `FontSize`, `FontData`, `SignatureImage`, `LogoImage`, `RenderingMode`, `TextColor`). The type system forbids consumers from supplying them via the XML facade. The Infrastructure-layer `XmlSignatureContextMapper` fills MISA-documented "no signature visualization" defaults for these on the wire so the request stays wire-compatible with MISA's published `SignatureInfo` shape (§4.2 + §4.1.2).

---

## 4. New Client DTOs — request, result, and XML signature context

`namespace MisaConnect.ESign.Client.Dtos`

### 4.1. `SignXmlRequestDto`

```csharp
public sealed record SignXmlRequestDto(
    string? Xml,                          // primary input — exactly one of (Xml, XmlUtf8Bytes) must be non-null
    byte[]? XmlUtf8Bytes,                 // secondary input — UTF-8-decoded by the SDK
    XmlSignatureContextDto SignatureContext,
    string DocumentName,
    string DataToBeDisplayed,
    string? DocumentId = null);
```

Plus two factory helpers in the same file:

```csharp
public static class SignXmlRequest
{
    public static SignXmlRequestDto FromString(string xml, XmlSignatureContextDto ctx, string documentName, string dataToBeDisplayed, string? documentId = null);
    public static SignXmlRequestDto FromUtf8Bytes(byte[] xmlUtf8Bytes, XmlSignatureContextDto ctx, string documentName, string dataToBeDisplayed, string? documentId = null);
}
```

### 4.2. `SignWordRequestDto`

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
```

Mirrors slice 1's `SignPdfRequestDto` shape because Word and PDF share MISA's `SignatureInfo` shape (per FR-059).

### 4.3. `SignExcelRequestDto`

Symmetric with `SignWordRequestDto` — same field set, with `Excel` in place of `Word`.

### 4.4. `SignXmlResultDto`

```csharp
public sealed record SignXmlResultDto(
    byte[] SignedXml,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Xml);
```

### 4.5. `SignWordResultDto`

```csharp
public sealed record SignWordResultDto(
    byte[] SignedWord,
    string TransactionId,
    string CertificateKeyAlias,
    DateTimeOffset CompletedAtUtc,
    DocumentFormat Format = DocumentFormat.Word);
```

### 4.6. `SignExcelResultDto`

Symmetric with `SignWordResultDto`.

### 4.7. `XmlSignatureContextDto`

Public, consumer-facing mirror of `Domain.Signing.XmlSignatureContext`. Lives in `Client.Dtos` so consumers do not need `using MisaConnect.ESign.Domain.Signing;` to construct an XML signing request.

```csharp
public sealed record XmlSignatureContextDto(
    string SignatureName,
    string HashAlgorithm,                  // "SHA256" — kept as string for forward compatibility
    SignatureDescriptionDto SignatureDescription);
```

### 4.8. `SignatureDescriptionDto`

If slice 1 does not already expose a `SignatureDescriptionDto` record (it currently inlines the fields onto `SignPdfRequestDto`), slice 3 introduces it additively in `Client.Dtos`:

```csharp
public sealed record SignatureDescriptionDto(
    string SignedBy,
    bool? ShowSignedDate,
    string Location,
    string Reason,
    string Contact,
    string? DisplayText);
```

Mirrors `Domain.Signing.SignatureDescription`. Used by `XmlSignatureContextDto`. Slice-1 `SignPdfRequestDto` keeps its flat-field shape; slice 3 does not change that surface (FR-065).

### 4.9. `SignaturePosInfoDto`

Slice 1 already exposes this — slice 3 reuses it verbatim for `SignWordRequestDto.AdditionalSignaturePositions` and `SignExcelRequestDto.AdditionalSignaturePositions`.

---

## 5. Additive `Format` property on every typed exception

`namespace MisaConnect.ESign.Domain.Errors`

The base `ESignException` class gains a `DocumentFormat Format { get; }` property, populated via a new optional constructor parameter at the end:

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
    { /* … */ }

    public DocumentFormat Format { get; }   // NEW
    // (existing properties unchanged)
}
```

Every existing subclass forwards `format` to the base via an additive constructor parameter at the end. The change is binary-compatible because every new parameter is optional with default `DocumentFormat.Unknown`.

**Property-population contract** (per FR-062, enforced by `tests/MisaConnect.ESign.UnitTests/Errors/FormatPropertyContractTests.cs`):

| Exception source | Format value |
|---|---|
| Raised inside `SignPdfAsync` orchestration (any reason — auth, cert, hash, sign, status, attachment, transport, terminal-state, timeout) | `Pdf` |
| Raised inside `SignXmlAsync` orchestration (any reason) | `Xml` |
| Raised inside `SignWordAsync` orchestration (any reason) | `Word` |
| Raised inside `SignExcelAsync` orchestration (any reason) | `Excel` |
| Raised outside any facade call (background refresh in `RemoteSigningAuthHandler` reaching a 401-refresh-fail, DI-time validation by `MisaESignOptionsValidator`, `OtpErrorMapper.MapResendResult(...)` typed-failure constructed outside a facade) | `Unknown` |

Slice-1 PDF construction sites are updated to pass `DocumentFormat.Pdf` — this is the only slice-1-touching change FR-065 permits (no wire shape change, no public method signature change, no observable behavior change beyond the new property carrying a non-`Unknown` value on existing PDF paths).

---

## 6. New use cases registered via DI — no new public DI signature

`namespace MisaConnect.ESign.Infrastructure.DependencyInjection`

`ServiceCollectionExtensions.AddMisaConnectESign(...)` signatures (both overloads) are unchanged. Internally the extension registers (in addition to slice-1 / slice-2 registrations):

| Type | Lifetime |
|---|---|
| `HashXmlDocument` | scoped |
| `HashWordDocument` | scoped |
| `HashExcelDocument` | scoped |
| `AttachSignatureToXml` | scoped |
| `AttachSignatureToWordExcel` | scoped |
| `SignXmlRequestValidator` | scoped |
| `SignWordRequestValidator` | scoped |
| `SignExcelRequestValidator` | scoped |
| `SignXml` | scoped (lambda mirrors the existing `SignPdf` registration; resolves `IOtpProvider?` from the service provider) |
| `SignWord` | scoped (same shape) |
| `SignExcel` | scoped (same shape) |

No new public DI method introduced.

---

## 7. New methods on `IMisaESignWireClient` — Application-layer port additions

`namespace MisaConnect.ESign.Application.Abstractions`

Five new methods on the existing interface. All five are additive on a not-yet-1.0 interface; documented in CHANGELOG.

```csharp
public interface IMisaESignWireClient
{
    // (existing slice-1 + slice-2 methods unchanged)

    Task<XmlHashOutput> HashXmlAsync(string accessToken, Certificate cert, string xmlContent, string documentId, XmlSignatureContext signatureContext, CancellationToken ct);
    Task<WordExcelHashOutput> HashWordAsync(string accessToken, Certificate cert, byte[] wordBytes, string documentId, SignatureInfo signatureInfo, CancellationToken ct);
    Task<WordExcelHashOutput> HashExcelAsync(string accessToken, Certificate cert, byte[] excelBytes, string documentId, SignatureInfo signatureInfo, CancellationToken ct);
    Task<byte[]> AttachSignatureToXmlAsync(string accessToken, Certificate cert, XmlHashOutput hash, string signatureData, CancellationToken ct);
    Task<byte[]> AttachSignatureToWordExcelAsync(string accessToken, Certificate cert, WordExcelHashOutput hash, string signatureData, DocumentFormat format, CancellationToken ct);
}
```

Plus the two new Application-layer per-format hash-output records:

```csharp
public sealed record XmlHashOutput(string DocumentId, string Document, string SignatureId, string Digest, string Sh);
public sealed record WordExcelHashOutput(string DocumentId, string DocumentBytes, string SignatureId, string Digest, string MainDom);
```

---

## 8. Options — no new public types

Slice 3 explicitly introduces NO new options (FR-066). The existing `MisaESignOptions.Polling`, `MisaESignOptions.TransportRetry`, `MisaESignOptions.Errors`, `MisaESignOptions.Otp` blocks all apply to the new format paths unchanged.

---

## 9. Semver impact summary

| Change | Semver classification |
|---|---|
| `IMisaESignClient.SignXmlAsync(...)` added | Minor (additive) |
| `IMisaESignClient.SignWordAsync(...)` added | Minor (additive) |
| `IMisaESignClient.SignExcelAsync(...)` added | Minor (additive) |
| `DocumentFormat` enum added | Minor (additive) |
| `XmlSignatureContext` Domain record added | Minor (additive) |
| `SignXmlRequestDto`, `SignWordRequestDto`, `SignExcelRequestDto`, `SignXmlResultDto`, `SignWordResultDto`, `SignExcelResultDto`, `XmlSignatureContextDto` added | Minor (additive) |
| `SignatureDescriptionDto` added (if not present from slice 1) | Minor (additive) |
| `ESignException.Format` property added (constructor gains optional `DocumentFormat format` parameter at end) | Minor (additive — default value preserves all existing call-site compatibility) |
| Every `ESignException` subclass constructor gains optional `format` parameter | Minor (additive) |
| `SignPdfResultDto.Format` property added (default `Pdf`) | Minor (additive) |
| `IMisaESignWireClient.HashXmlAsync(...)`, `HashWordAsync(...)`, `HashExcelAsync(...)`, `AttachSignatureToXmlAsync(...)`, `AttachSignatureToWordExcelAsync(...)` added | Minor (additive on a not-yet-1.0 interface; documented in CHANGELOG) |
| `XmlHashOutput`, `WordExcelHashOutput` Application records added | Minor (additive) |

Package version: `2.0.0-preview.2` → `2.0.0-preview.3`. `CHANGELOG.md` entry under `[Unreleased]` enumerates each addition with a one-line description.

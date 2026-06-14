# Phase 1 Data Model: Fix ESRM empty-doc-array rejection

This slice changes the **nullability** of existing internal request DTO members, adds one internal
response DTO field, and changes one wire-mapping default. No public type, no Domain type, and no
MISA wire field casing/shape changes (Principle IV preserved).

## 1. Request DTOs — document-type arrays become nullable

Both DTOs are `internal` in `MisaConnect.ESign.Infrastructure.ESign.Wire`.

### `HashRequestDto`

| Member | Before | After | Notes |
|--------|--------|-------|-------|
| `Certificate` | `string = string.Empty` | *(unchanged)* | always assigned |
| `CertificateChain` | `List<string> = new()` | *(unchanged)* | always assigned |
| `PdfDocs` | `List<PdfDocRequestDto> = new()` | `List<PdfDocRequestDto>?` | null ⇒ omitted |
| `XmlDocs` | `List<XmlHashDocRequestDto> = new()` | `List<XmlHashDocRequestDto>?` | null ⇒ omitted |
| `WordDocs` | `List<WordHashDocRequestDto> = new()` | `List<WordHashDocRequestDto>?` | null ⇒ omitted |
| `ExcelDocs` | `List<ExcelHashDocRequestDto> = new()` | `List<ExcelHashDocRequestDto>?` | null ⇒ omitted |

### `AttachmentRequestDto`

| Member | Before | After | Notes |
|--------|--------|-------|-------|
| `Certificate` / `CertificateChain` | non-null | *(unchanged)* | always assigned |
| `PdfDocs` | `List<AttachmentPdfDocRequestDto> = new()` | `List<AttachmentPdfDocRequestDto>?` | null ⇒ omitted |
| `XmlDocs` | `List<XmlAttachmentDocRequestDto> = new()` | `List<XmlAttachmentDocRequestDto>?` | null ⇒ omitted |
| `WordDocs` | `List<WordExcelAttachmentDocRequestDto> = new()` | `List<WordExcelAttachmentDocRequestDto>?` | null ⇒ omitted |
| `ExcelDocs` | `List<WordExcelAttachmentDocRequestDto> = new()` | `List<WordExcelAttachmentDocRequestDto>?` | null ⇒ omitted |

**Serialization rule** (`ESignJsonOptions.Wire`, `WhenWritingNull`): a `null` array is omitted from
the JSON body; an assigned array serializes as before. Each build site assigns exactly one array, so
a single-format request now emits exactly that one key.

**Build-site → emitted array** (all in `MisaESignWireClient`):

| Method | Assigns | Body contains | Body omits |
|--------|---------|---------------|------------|
| `HashPdfAsync` | `PdfDocs` | `pdfDocs` | `xmlDocs`, `wordDocs`, `excelDocs` |
| `HashXmlAsync` | `XmlDocs` | `xmlDocs` | `pdfDocs`, `wordDocs`, `excelDocs` |
| `HashWordOrExcelAsync` (Word) | `WordDocs` | `wordDocs` | `pdfDocs`, `xmlDocs`, `excelDocs` |
| `HashWordOrExcelAsync` (Excel) | `ExcelDocs` | `excelDocs` | `pdfDocs`, `xmlDocs`, `wordDocs` |
| `AttachSignatureAsync` | `PdfDocs` | `pdfDocs` | other three |
| `AttachSignatureToXmlAsync` | `XmlDocs` | `xmlDocs` | other three |
| `AttachSignatureToWordExcelAsync` (Word) | `WordDocs` | `wordDocs` | other three |
| `AttachSignatureToWordExcelAsync` (Excel) | `ExcelDocs` | `excelDocs` | other three |

## 2. `SignatureInfoDto.Page` — wire default

| Field | Type | Wire behavior before | Wire behavior after |
|-------|------|----------------------|---------------------|
| `Page` | `int?` | `source.Page` copied verbatim; `null` ⇒ omitted (`WhenWritingNull`) ⇒ MISA 400 | `source.Page ?? 1`; when source was null, send `1` **and** log the default |

- Domain `SignatureInfo.Page` (`int? = null`) is unchanged. The default is applied only at the
  PDF/Word/Excel wire-mapping boundary (`ToWireSignatureInfo`).
- Client-side validation unchanged: `SignatureInfo.Page < 1` (when explicitly set) is still rejected
  pre-flight by `SignPdfRequestValidator`.
- XML path exempt (`XmlSignatureContext` has no `Page`).

## 3. `ResponseErrorDto.ValidationFailures` — new internal field

`ResponseErrorDto` is `internal` (Infrastructure). New nested DTO mirrors MISA's response field
names verbatim (Principle IV).

```csharp
internal sealed class ResponseErrorDto
{
    // existing: error / errorCode / devMsg / userMsg ...
    [JsonPropertyName("validationFailures")]
    public List<ValidationFailureDto>? ValidationFailures { get; set; }
}

internal sealed class ValidationFailureDto
{
    [JsonPropertyName("property")]      public string? Property { get; set; }
    [JsonPropertyName("failureReason")] public string? FailureReason { get; set; }
}
```

- The domain `ResponseError` record is **not** changed (stays a 4-member public record).
- The entries are rendered to a sanitized string in the wire client and threaded to `BuildDetail`
  via an internal `ESignErrorMapper.Map` overload — never onto a public type, never into the
  code-synthesis probe.

## 4. Error-detail rendering (no new domain/public type)

| Input | `IncludeRawErrorMessage = false` | `IncludeRawErrorMessage = true` |
|-------|----------------------------------|---------------------------------|
| 400 with `validationFailures` | detail = existing summary only (no failures) | summary + `userMsg`/`devMsg` (existing) + `validationFailures: [<property>] <failureReason>; …` |
| 400 without `validationFailures` | unchanged | unchanged |

- Synthesized `RawCode` / `Category` are **identical** in all cases (FR-007): the failures string is
  passed only to `BuildDetail`, not to `SynthesizeHashCode*`.
- Rendered failures contain only MISA field names + reason strings (no PII/tokens).

## 5. State / transitions

None. This slice is stateless serialization + error-rendering behavior; no entities with lifecycle.

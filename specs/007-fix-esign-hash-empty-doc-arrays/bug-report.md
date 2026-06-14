# Bug: ESRM `documents/hash` rejects every PDF-only sign — empty `xmlDocs`/`wordDocs`/`excelDocs` arrays are serialized

**Component:** `MisaConnect.ESign` (Infrastructure wire layer)
**Affected version:** 2.1.0 (current). Latent in earlier versions; **exposed** by the 2.1.0 ESRM routing fix.
**Severity:** High — remote PDF signing via `SignPdfAsync` fails 100% against the sandbox ESRM endpoint.
**Endpoints:** `POST external/esrm/service/document/api/v1/documents/hash` (and, by inspection, `…/documents/attachment`).

---

## Summary

A PDF-only remote sign sends a `HashRequestDto` in which `pdfDocs` is populated but
`xmlDocs`, `wordDocs`, and `excelDocs` are **empty (non-null) lists**. Because the wire
serializer only drops `null` (not empty collections), the request body contains
`"xmlDocs":[],"wordDocs":[],"excelDocs":[]`. MISA's ESRM `documents/hash` endpoint validates
each of those arrays as "must contain at least one document" and returns **HTTP 400** before
any signing happens. The SDK maps this to `ESignGeneralException` / `HashRejected` with
`errorCode=<none>`, so the caller sees a generic failure.

## Root cause (source-verified)

1. **Doc arrays default to empty, non-null lists** —
   `src/MisaConnect.ESign.Infrastructure/ESign/Wire/HashDtos.cs`:

   ```csharp
   internal sealed class HashRequestDto
   {
       [JsonPropertyName("pdfDocs")]   public List<PdfDocRequestDto>       PdfDocs   { get; set; } = new();
       [JsonPropertyName("xmlDocs")]   public List<XmlHashDocRequestDto>   XmlDocs   { get; set; } = new();   // ← empty []
       [JsonPropertyName("wordDocs")]  public List<WordHashDocRequestDto>  WordDocs  { get; set; } = new();   // ← empty []
       [JsonPropertyName("excelDocs")] public List<ExcelHashDocRequestDto> ExcelDocs { get; set; } = new();   // ← empty []
   }
   ```

2. **`HashPdfAsync` only assigns `PdfDocs`** (the others keep their empty-list default) —
   `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs` (`HashPdfAsync`, ~line 272):

   ```csharp
   var body = new HashRequestDto
   {
       Certificate = ...,
       PdfDocs = new List<PdfDocRequestDto> { /* the one doc */ },
       // XmlDocs / WordDocs / ExcelDocs left at `new()` → serialized as []
   };
   ```

3. **The wire serializer drops only nulls, not empty collections** —
   `src/MisaConnect.ESign.Infrastructure/ESign/ESignJsonOptions.cs`:

   ```csharp
   DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,   // empty [] is NOT null → still emitted
   ```

Net wire body for a PDF sign:

```jsonc
{
  "certificate": "…",
  "pdfDocs":   [ { /* document */ } ],
  "xmlDocs":   [],   // rejected
  "wordDocs":  [],   // rejected
  "excelDocs": []    // rejected
}
```

## Why it surfaced in 2.1.0

Before 2.1.0, ESRM requests were routed under the configured base path (e.g.
`…/webdev/external/esrm/…`) and came back as the SPA `index.html` — they never reached the
real ESRM service, so its request-body validation never ran. The 2.1.0 "ESRM routing fix"
(normalize base URL to origin) made the call reach the genuine `documents/hash` endpoint,
which then rejects the empty arrays. So 2.1.0 didn't introduce the empty arrays — it made
them reach a server that validates them.

## Steps to reproduce

1. Configure a sandbox org service account (`Environment=Sandbox`, valid `ClientId`/`ClientKey`/`UserName`/`Password`).
2. Call `IMisaESignClient.SignPdfAsync(...)` with any valid PDF and a `SignatureInfo` that has `Page >= 1` (see secondary finding below).
3. The call fails. The outbound `documents/hash` request returns HTTP 400.

## Expected vs actual

- **Expected:** a PDF-only sign omits the document-type arrays it isn't using; `documents/hash` returns `200` with `pdfDocs[*].documentHash`.
- **Actual:** HTTP 400; the SDK throws `ESignGeneralException` (`HashRejected`, `errorCode=<none>`).

## Evidence — MISA 400 response body

```json
{
  "error": null,
  "errorCode": "e400",
  "devMsg": "Dữ liệu truyền lên không chính xác, vui lòng xem trong trường ValidationFailures.",
  "userMsg": "Dữ liệu truyền lên không chính xác, vui lòng xem trong trường ValidationFailures.",
  "moreInfo": null,
  "traceId": "<misa-trace-id>",
  "validationFailures": [
    { "property": "XmlDocs",   "failureReason": "Phải có ít nhất 1 tài liệu" },
    { "property": "WordDocs",  "failureReason": "Phải có ít nhất 1 tài liệu" },
    { "property": "ExcelDocs", "failureReason": "Phải có ít nhất 1 tài liệu" }
  ]
}
```

(Captured by tapping the SDK's `HttpClient` pipeline with a body-logging `DelegatingHandler`;
the raw body is otherwise not surfaced because `ThrowMappedAsync` only forwards a parsed
`errorCode`, which is empty here.)

## Also affected: `documents/attachment`

`src/MisaConnect.ESign.Infrastructure/ESign/Wire/AttachmentDtos.cs` (`AttachmentRequestDto`,
~lines 13–23) has the identical empty-list-default pattern for `pdfDocs`/`xmlDocs`/`wordDocs`/`excelDocs`,
so the attachment-signing flow is very likely rejected the same way. (Not independently reproduced.)

## Proposed fix

Make the unused document-type arrays omit-when-absent. Cleanest option: make them **nullable
with a `null` default** so `WhenWritingNull` drops them, and assign only the array in use.

```csharp
[JsonPropertyName("pdfDocs")]   public List<PdfDocRequestDto>?       PdfDocs   { get; set; }
[JsonPropertyName("xmlDocs")]   public List<XmlHashDocRequestDto>?   XmlDocs   { get; set; }
[JsonPropertyName("wordDocs")]  public List<WordHashDocRequestDto>?  WordDocs  { get; set; }
[JsonPropertyName("excelDocs")] public List<ExcelHashDocRequestDto>? ExcelDocs { get; set; }
```

(`HashPdfAsync`/`HashXmlAsync`/`HashWordOrExcelAsync` already assign exactly one array.) Apply
the same change to `AttachmentRequestDto`. Alternatively keep them non-null and add a
serialization condition that skips empty collections — but nullable + `WhenWritingNull` is the
smallest change and matches the existing response DTOs (`HashResponseDto.XmlDocs` is already
`List<…>?`).

### Suggested test

Add a wire-serialization unit test (alongside the existing `Signing/*/Hash*DocumentTests`)
asserting that a PDF-only `HashRequestDto` serializes **without** `xmlDocs`/`wordDocs`/`excelDocs`
keys (and the Word/Excel/Xml paths omit the arrays they don't use).

## Secondary finding — `SignatureInfo.Page` must be `>= 1`, undocumented

The same endpoint also rejects requests whose `SignatureInfo.Page` is omitted:

```json
{ "property": "PdfDocs[0].SignatureInfo.Page",
  "failureReason": "Invalid value for Page, must be a value greater than or equal to 1." }
```

`SignatureInfoDto.Page` is `int?` and is dropped when null (`WhenWritingNull`), but MISA
requires `Page >= 1`. Consider one of: defaulting `Page` to `1` when the caller leaves it
unset, validating it client-side with a clear exception, or at minimum documenting that
`Page >= 1` is mandatory. (A consumer presently has to default `Page` to `1` themselves to get
past this.)

## Consumer-side workaround (until fixed)

Consumers calling through the SDK cannot change the request body. A temporary
`DelegatingHandler` on the named `HttpClient` (`MisaESignWireClient`) that strips empty
`xmlDocs`/`wordDocs`/`excelDocs` from the outbound `documents/hash` body unblocks signing, but
it patches a third-party contract and should be removed once the SDK is fixed.

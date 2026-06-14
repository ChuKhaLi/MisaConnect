# Quickstart: ESRM empty-doc-array fix (slice 007)

## For consumers (after upgrading to `MisaConnect.ESign 2.1.1`)

**No configuration or code change is required.** Remote signing that previously failed against the
live ESRM service now works:

```csharp
// PDF/Word/Excel/XML signing — previously rejected with an opaque HTTP 400, now succeeds.
var signed = await client.SignPdfAsync(pdfBytes, signatureInfo, ct);
```

### `SignatureInfo.Page` is now optional

If you do not set `Page`, the SDK now sends `Page = 1` (first page) and logs that it applied the
default. Set `Page` explicitly to place the signature elsewhere:

```csharp
var signatureInfo = new SignatureInfo(/* … */, Page: 2 /* explicit; or omit for page 1 */);
```

A `Page` of `0` or negative is still rejected client-side with a clear validation error.

### Seeing why MISA rejected a request

If you opt into raw error detail, rejection reasons from MISA (per-field `validationFailures`) now
appear in the exception `Detail`:

```jsonc
"Misa": { "ESign": { "Errors": { "IncludeRawErrorMessage": true } } }
```

```text
MISA returned errorCode=e400 on …/documents/hash. | userMsg=… | devMsg=… |
validationFailures: [XmlDocs] Phải có ít nhất 1 tài liệu; [WordDocs] Phải có ít nhất 1 tài liệu
```

With the flag left at its default (`false`), error detail is unchanged — no raw vendor text is
surfaced.

## For maintainers — verifying the fix

```bash
# Wire-serialization unit tests (a PDF-only hash body omits xml/word/excel arrays, etc.)
dotnet test tests/MisaConnect.ESign.UnitTests --filter "FullyQualifiedName~Wire"

# Page default + validationFailures detail gating
dotnet test tests/MisaConnect.ESign.UnitTests --filter "FullyQualifiedName~PageDefault|FullyQualifiedName~ValidationFailures"

# End-to-end regression against the in-repo fake (400 on empty arrays → 200 when omitted)
dotnet test tests/MisaConnect.ESign.IntegrationTests
```

Expected: the PDF/Word/Excel/XML hash and attachment bodies omit every unused document-type array;
an unset `Page` serializes as `1`; `validationFailures` appear in `Detail` only under
`IncludeRawErrorMessage = true`; synthesized error codes are unchanged.

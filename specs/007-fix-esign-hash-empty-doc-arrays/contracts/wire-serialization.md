# Contract: Request wire-body serialization

Governs the JSON body the SDK emits to `documents/hash` and `documents/attachment`. Verified by
unit tests that serialize the request DTO with `ESignJsonOptions.Wire` and assert key presence.

## C1 — Unused document-type arrays are omitted

For every single-format flow, the request body MUST contain exactly the one document-type array in
use and MUST NOT contain the other three keys (neither as `null` nor as `[]`).

| Flow | MUST contain key | MUST NOT contain keys |
|------|------------------|-----------------------|
| PDF hash (`HashPdfAsync`) | `pdfDocs` | `xmlDocs`, `wordDocs`, `excelDocs` |
| XML hash (`HashXmlAsync`) | `xmlDocs` | `pdfDocs`, `wordDocs`, `excelDocs` |
| Word hash (`HashWordOrExcelAsync` Word) | `wordDocs` | `pdfDocs`, `xmlDocs`, `excelDocs` |
| Excel hash (`HashWordOrExcelAsync` Excel) | `excelDocs` | `pdfDocs`, `xmlDocs`, `wordDocs` |
| PDF attach (`AttachSignatureAsync`) | `pdfDocs` | `xmlDocs`, `wordDocs`, `excelDocs` |
| XML attach (`AttachSignatureToXmlAsync`) | `xmlDocs` | `pdfDocs`, `wordDocs`, `excelDocs` |
| Word attach (`AttachSignatureToWordExcelAsync` Word) | `wordDocs` | `pdfDocs`, `xmlDocs`, `excelDocs` |
| Excel attach (`AttachSignatureToWordExcelAsync` Excel) | `excelDocs` | `pdfDocs`, `xmlDocs`, `wordDocs` |

## C2 — Always-present fields unchanged

`certificate` and `certificateChain` MUST remain present and non-null in every request body (no
behavior change). The change is scoped strictly to the four document-type arrays.

## C3 — `SignatureInfo.Page` is always a value ≥ 1 on the wire

For PDF/Word/Excel flows, the serialized `SignatureInfo.Page`:
- equals the caller's value when set (≥ 1, enforced pre-flight);
- equals `1` when the caller left `Page` unset (`null`), and a structured log records the default.

The `Page` key is therefore always present with a value ≥ 1 on visible-signature flows. (XML flows
do not carry `Page`.)

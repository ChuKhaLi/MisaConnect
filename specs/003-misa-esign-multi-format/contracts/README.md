# Slice 3 contracts — `MisaConnect.ESign` multi-format document signing

This directory pins the three contracts that slice 3 of `MisaConnect.ESign` extends. Each is an additive delta on top of the corresponding slice-1 + slice-2 contracts.

| File | Scope |
|---|---|
| [public-surface.md](./public-surface.md) | Public API additions: the three new facade methods on `IMisaESignClient` (`SignXmlAsync` + two overloads, `SignWordAsync`, `SignExcelAsync`), the new `DocumentFormat` enum, the new `XmlSignatureContext` Domain record + `XmlSignatureContextDto` Client mirror, the new per-format request / result DTOs, the additive `Format` property on every existing typed exception. |
| [wire-envelopes.md](./wire-envelopes.md) | The two strengthened MISA HTTP request/response shapes — E6.x `/documents/hash` (now carrying typed `xmlDocs` / `wordDocs` / `excelDocs` per-format entries per §4.1 / §4.15) and E7.x `/documents/attachment` (now carrying typed per-format entries per §4.6) — mirroring the MISA doc verbatim per Constitution Principle IV (including the misspelling `Doc_Attackment`). |
| [error-mapping.md](./error-mapping.md) | The per-format hybrid mapping for `/documents/hash` and `/documents/attachment` rejections, the synthesized per-format `RawCode` values (`InvalidXmlInput`, `MissingMainDom`, `MissingSignatureId`, `UnsupportedDocumentVariant`, `IncompleteHashResponse`, `MissingSignedDocument`), and the `DocumentFormat`-property contract on every surfaced exception. |

All three are **incremental** — they list only the slice-3 additions. The slice-1 contracts under [specs/001-misa-esign-pdf-sign-flow/contracts/](../../001-misa-esign-pdf-sign-flow/contracts/) and the slice-2 contracts under [specs/002-misa-esign-2fa-otp/contracts/](../../002-misa-esign-2fa-otp/contracts/) remain authoritative for everything they already covered.

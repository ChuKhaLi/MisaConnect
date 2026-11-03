# Changelog

All notable changes to `@misaconnect/einvoice` are documented here.

## [0.1.0] - 2026-11-03

### Added

- Initial release: TypeScript port of `MisaConnect.EInvoice` with all ten operations — `listTemplates`, `preview`, `saveDraft`, `getDraftPdf`, `deleteDraft`, `lookupByRefId`, `lookupStandard`, `lookupCalculating`, `issueReplacement`, `issueAdjustment`.
- Token caching with single-flight acquisition, retry with backoff, `AbortSignal` support, structured per-call logging (`fromPino` adapter), and `MeInvoiceException` / `MeInvoiceErrorCategory` error model.
- ESM + CJS builds with type declarations; Node >= 20.

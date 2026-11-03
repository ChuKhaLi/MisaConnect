# Changelog

All notable changes to `@misaconnect/einvoice` are documented here.

## [0.1.0] - 2026-11-03

### Added

- Initial release: TypeScript port of `MisaConnect.EInvoice` with all ten operations — `listTemplates`, `preview`, `saveDraft`, `getDraftPdf`, `deleteDraft`, `lookupByRefId`, `lookupStandard`, `lookupCalculating`, `issueReplacement`, `issueAdjustment`.
- Token caching with single-flight acquisition, retry with backoff, `AbortSignal` support, structured per-call logging (`fromPino` adapter), and `MeInvoiceException` / `MeInvoiceErrorCategory` error model.
- `saveDraft` reports per-invoice results in input order, as .NET `SaveDraftInvoices`: an invalid invoice (validator failures, or values that cannot be mapped to the wire such as a malformed money string or an impossible date) becomes its own `Validation` / `ValidationFailed` result with `invoices[i].`-prefixed failures and is not sent; only an empty batch (`Validation`) or one of more than 30 invoices (`BatchOrSize` / `InvoiceQuantityTooLarge`) throws.
- Exported collaborator ports (`TokenCache`, `AccessToken`, `Clock`, `RefIdGenerator`, `InvoiceValidator`, `TemplateResolver`, `TemplateResolution`), the defaults `defaultInvoiceValidator` and `resolveTemplate`, and the `isMeInvoiceException` type guard (safe across the ESM/CJS dual-package hazard).
- The default `fetch` is looked up on every call, so mocks installed after client creation apply. A token cache returning a string or number `expiresAt` is accepted.
- A throwing logger never changes a call's result; a caller abort is logged as `outcome: 'Cancelled'`.
- `createMisaEInvoiceClient` always pins `baseUrl` to https and the MISA host of the chosen environment; no option key can disable it.
- Caller-input `Validation` errors use `rawErrorCode: 'ValidationFailed'` throughout (preview, paged lookups, empty batch), rule ids stay in `failures[].ruleId`.
- ESM + CJS builds with type declarations; Node >= 20.

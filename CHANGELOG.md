# Changelog

All notable changes to MisaConnect will be documented here. This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.0.0] - 2026-05-14

### Added
- Initial public release of `MisaConnect.EInvoice` targeting .NET 8.
- Use cases: `EnsureAccessToken`, `ListActiveTemplates`, `PreviewInvoice`, `SaveDraftInvoices`, `GetDraftPdfByRefId`, `DeleteDraftInvoice`, `LookupByRefIds`, `LookupStandard`, `LookupCalculating`, `IssueReplacementInvoice`, `IssueAdjustmentInvoice`.
- Port-and-adapter interfaces: `IMeInvoiceClient`, `ITokenCache`, `IInvoiceValidator`, `IRefIdGenerator`, `ISystemClock`, `ICorrelationIdAccessor`, `ITemplateResolver`, `IDeleteOptionsAccessor`.
- DI entry point `AddMisaConnectEInvoice(IConfiguration)` binding the `Misa:EInvoice` configuration section.
- Throttle retry, bearer-token auth, in-memory token cache, call-logging decorator.
- Sample console (`samples/MisaConnect.Samples.Console`) and minimal-API host (`samples/MisaConnect.Samples.Api`).
- 315 unit tests; integration tests for MISA sandbox and a fake server.

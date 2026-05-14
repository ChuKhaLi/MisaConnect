# Changelog

All notable changes to MisaConnect will be documented here. This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.1.0] - 2026-05-15

### Added
- Package icon (`icon.png`, 256x256) embedded in the NuGet listing.

### Changed
- Build SDK pinned to .NET 10 LTS via `global.json` and `setup-dotnet` workflows. Target framework remains `net8.0` for broad consumer reach (still under LTS through Nov 2026).

### Breaking
- `AmendmentDtoMapper` demoted from `public` to `internal`. Only intended for Client-internal use; external callers should use `IMisaEInvoiceClient.IssueReplacementAsync` / `IssueAdjustmentAsync`. Caught immediately after v1.0.0 — no known external consumers.

## [1.0.0] - 2026-05-14

### Added
- Initial public release of `MisaConnect.EInvoice` targeting .NET 8.
- Use cases: `EnsureAccessToken`, `ListActiveTemplates`, `PreviewInvoice`, `SaveDraftInvoices`, `GetDraftPdfByRefId`, `DeleteDraftInvoice`, `LookupByRefIds`, `LookupStandard`, `LookupCalculating`, `IssueReplacementInvoice`, `IssueAdjustmentInvoice`.
- Port-and-adapter interfaces: `IMeInvoiceClient`, `ITokenCache`, `IInvoiceValidator`, `IRefIdGenerator`, `ISystemClock`, `ICorrelationIdAccessor`, `ITemplateResolver`, `IDeleteOptionsAccessor`.
- DI entry point `AddMisaConnectEInvoice(IConfiguration)` binding the `Misa:EInvoice` configuration section.
- Throttle retry, bearer-token auth, in-memory token cache, call-logging decorator.
- Sample console (`samples/MisaConnect.Samples.Console`) and minimal-API host (`samples/MisaConnect.Samples.Api`).
- 315 unit tests; integration tests for MISA sandbox and a fake server.

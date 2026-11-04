# Data Model: Fix MisaConnect.EInvoice inventory defects

No new entities and no wire/DTO changes. The slice touches three existing shapes.

## MisaEInvoiceOptions (unchanged shape)

| Property | Type | Carried by factory today | After |
|---|---|---|---|
| `Environment` | `MeInvoiceEnvironment` | yes | yes |
| `BaseUrl` | `string` | yes | yes |
| `TaxCode` | `string` | yes | yes |
| `UserName` | `string` | yes | yes |
| `Password` | `string` | yes | yes |
| `AppId` | `string` | yes | yes |
| `Delete.IncludeRawErrorMessage` | `bool` | **no** | yes (copied by value) |

Rule: every public settable property, recursively, must round-trip through `MisaEInvoiceClientFactory.Create(options)` (FR-001, FR-002).

## Call log entry (field set)

Before: `endpoint, correlationId, customerTaxCode, templateId, refId, eInvoiceStatus?, orgRefId?, changeReason?` (start) and `endpoint, outcome, errorCategory, durationMs, refId, customerTaxCode, templateId, …, errorMessage?` (complete).

After: identical minus `customerTaxCode` (FR-003, FR-004).

## Error category for batch-size conditions

| Condition | Before | After |
|---|---|---|
| MISA `InvoiceQuantityTooLarge` | `Validation` | `BatchOrSize` |
| MISA `XMLTooLong` | `Validation` | `BatchOrSize` |
| Client-side: batch > 30 | `Validation` / `InvoiceQuantityTooLarge` | `BatchOrSize` / `InvoiceQuantityTooLarge` |
| Client-side: batch == 0 | `Validation` / `InvoiceQuantityTooLarge`, message "exceeds" | `Validation` / `ValidationFailed`, message "empty" |

Raw codes are preserved; failure rule id `FR-030` is kept on both client-side rejections.

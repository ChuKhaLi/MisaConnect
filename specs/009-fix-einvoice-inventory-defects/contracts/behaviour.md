# Contract: observable behaviour changes (MisaConnect.EInvoice)

No public type or member is added, removed or renamed. These are the observable changes integrators can see; each needs a CHANGELOG line.

## C1 — Factory honours delete options

```csharp
var client = MisaEInvoiceClientFactory.Create(new MisaEInvoiceOptions
{
    /* connection settings */
    Delete = { IncludeRawErrorMessage = true },
});
// Before: raw MISA messages never surfaced on delete/lookup failures.
// After: surfaced, same as a client built with AddMisaConnectEInvoice.
```

## C2 — Call logs no longer carry the tax code

Structured property `TaxCode` (rendered as `customerTaxCode=…`) is removed from every `MeInvoice call started` / `MeInvoice call completed` entry. Log queries filtering on it must switch to `CorrelationId`.

## C3 — Batch-size errors use `BatchOrSize`

```csharp
try { await client.SaveDraftAsync(thirtyOneInvoices, invoiceWithCode: true, ct); }
catch (MeInvoiceException ex)
{
    // Before: ex.Category == Validation,  ex.RawErrorCode == "InvoiceQuantityTooLarge"
    // After:  ex.Category == BatchOrSize, ex.RawErrorCode == "InvoiceQuantityTooLarge"
}
```

Same for MISA-returned `InvoiceQuantityTooLarge` / `XMLTooLong`. Empty batch: `Validation` / `ValidationFailed` (was `Validation` / `InvoiceQuantityTooLarge`).

**Behaviour change** — integrators that branch on `Validation` for oversize batches must add `BatchOrSize`.

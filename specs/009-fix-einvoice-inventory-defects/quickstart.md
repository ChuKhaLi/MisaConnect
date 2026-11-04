# Quickstart: verify slice 009

```
dotnet build MisaConnect.slnx
dotnet test tests/dotnet/MisaConnect.EInvoice.UnitTests --filter "FullyQualifiedName~ClientFactoryOptionsRoundTrip|FullyQualifiedName~CallLoggerTaxCode|FullyQualifiedName~MeInvoiceErrorMapperTests|FullyQualifiedName~SaveDraftInvoicesTests"
dotnet test tests/dotnet/MisaConnect.EInvoice.UnitTests
dotnet test tests/dotnet/MisaConnect.EInvoice.IntegrationTests
npm test -w @misaconnect/einvoice        # drift guard: no BatchOrSize deviation left
dotnet format MisaConnect.slnx --verify-no-changes   # CI on Linux is the gate (CRLF noise on Windows)
```

Expected:

1. Factory round-trip test passes; removing any line from the factory copy makes it fail.
2. No captured call-log entry contains the configured tax code.
3. 31 invoices → `BatchOrSize` / `InvoiceQuantityTooLarge`; 0 invoices → `Validation` / `ValidationFailed`.
4. `docs/architecture.md` no longer says "(you must register)".

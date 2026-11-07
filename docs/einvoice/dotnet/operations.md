---
title: Operations
parent: .NET
grand_parent: EInvoice
nav_order: 3
---

# Operations

Every operation is a method of `IMisaEInvoiceClient` (namespace `MisaConnect.EInvoice.Client`);
request and result types are in `MisaConnect.EInvoice.Client.Dtos`. Get the client from DI with
`AddEInvoiceClient` (see [Getting started](./getting-started)) or from
`MisaEInvoiceClientFactory.Create`. The short examples below assume a `client` variable of type
`IMisaEInvoiceClient` and, where needed, the `invoice` built in
[A complete invoice](#a-complete-invoice).

Every method takes a final `CancellationToken ct = default`. Cancelling it throws
`OperationCanceledException`, which is never retried or wrapped. Each call signs in first if no
valid token is cached (see [Tax code and AppID](../index#tax-code-and-appid)), and each HTTP request
is retried once on 429, 5xx, timeout or connection failure. Errors are described on
[Errors](./errors); the categories on the [EInvoice page](../index#error-categories).

| Method | MISA endpoint | Returns |
| --- | --- | --- |
| [`ListTemplatesAsync`](#listtemplatesasync) | `POST /webapp/templates` | `IReadOnlyList<TemplateDto>` |
| [`ListTicketTemplatesAsync`](#listtickettemplatesasync) | `POST /webapp/templates/ticket` | `IReadOnlyList<TicketTemplateDto>` |
| [`PreviewAsync`](#previewasync) | `POST /webapp/preview` | `PdfDocumentDto` |
| [`SaveDraftAsync`](#savedraftasync) | `POST /webapp/insert` | `IReadOnlyList<SaveResultDto>` |
| [`GetDraftPdfAsync`](#getdraftpdfasync) | `GET /webapp/viewrefid` | `PdfDocumentDto` |
| [`DeleteDraftAsync`](#deletedraftasync) | `DELETE /webapp/delete` | `DeleteDraftOutcome` |
| [`LookupByRefIdAsync`](#lookupbyrefidasync) | `POST /webapp/getlist` | `LookupBatchOutcomeDto` |
| [`LookupStandardAsync`](#lookupstandardasync) | `POST /webapp/paging` | `PagedLookupResultDto` |
| [`LookupCalculatingAsync`](#lookupcalculatingasync) | `POST /webapp/paging/calculating` | `PagedLookupResultDto` |
| [`IssueReplacementAsync`](#issuereplacementasync) | `POST /webapp/insert` (`EInvoiceStatus` 3) | `AmendmentResultDto` |
| [`IssueAdjustmentAsync`](#issueadjustmentasync) | `POST /webapp/insert` (`EInvoiceStatus` 4) | `AmendmentResultDto` |

## ListTemplatesAsync

<!-- snippet: partial -->
```csharp
Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(bool withCode = true, CancellationToken ct = default);
```

Lists the issuer's **active** invoice templates. Calls `POST /webapp/templates?invoiceWithCode=…&isTicket=false`
with the sign-in credentials in the body; MISA's inactive templates are filtered out. Pass
`withCode: false` for without-code templates (see
[With-code and without-code invoices](../index#with-code-and-without-code-invoices)).

**Result:** one `TemplateDto` per active template, empty when MISA has none:
`IPTemplateID`, `InvSeries`, `TemplateName`, `InvTemplateNo`, `TemplateType`,
`UsesTaxAuthorityCode` (from the series' second character, `C`/`K`), `IsActive`, `IsMoreVATRate`.
Keep `IPTemplateID` and `InvSeries`: they are what an invoice's `Template` refers to.

**Errors:** `MeInvoiceException` with MISA's category, e.g. `Authentication` or `Configuration` for
bad credentials, `TransportFailed` when MISA cannot be reached.

<!-- snippet: partial -->
```csharp
var templates = await client.ListTemplatesAsync(withCode: true);
foreach (var t in templates) Console.WriteLine($"{t.InvSeries} {t.IPTemplateID} {t.TemplateName}");
```

## ListTicketTemplatesAsync

<!-- snippet: partial -->
```csharp
Task<IReadOnlyList<TicketTemplateDto>> ListTicketTemplatesAsync(
    TicketTemplateListRequestDto? request = null,
    CancellationToken ct = default);
```

Since **2.10.0**. Lists one page of e-ticket templates (*mẫu vé điện tử*). Calls
`POST /webapp/templates/ticket?invoiceWithCode=…&start=…&length=…&invoiceCalcu=…` with the sign-in
credentials in the body.

**Input:** `TicketTemplateListRequestDto(bool WithCode = true, int Start = 0, int Length = 20, bool FromCashRegister = false)`;
`null` uses those defaults. `FromCashRegister` is MISA's `invoiceCalcu` (ticket templates from a
cash register). `Start` must be ≥ 0 and `Length` 1–100; otherwise the call throws
`MeInvoiceException` (`Validation`, `RawErrorCode` `FR-047`, `Field` `Start` or `Length`) before
any request.

**Result:** the rows MISA returns, **active and inactive** (the endpoint is paged, so the SDK does
not filter): `TicketTemplateID`, `TemplateName`, `InvSeries`, `IsActive`, `TicketType`,
`TemplateType`, `ServiceName`, `IsInheritFromOldTemplate`, `IsTicketCode`. Rows without a
`TicketTemplateID` are skipped. A page shorter than `Length` is the last one (see
[Paging](../index#paging)).

**Errors:** as for `ListTemplatesAsync`; a payload the SDK cannot read becomes `MisaUnknown`
(`InvalidResponse`).

**Notes:** the request body and the ticket field names follow MISA's API document; they have been
tested only against the in-repo fake server, not yet against live MISA data. The interface member
has a default implementation that throws `NotImplementedException`, so a custom
`IMisaEInvoiceClient` written for 2.9.0 still compiles. A `MisaEInvoiceClient` built by hand with
its older 10-argument constructor throws `InvalidOperationException` here; resolve it from DI.

<!-- snippet: partial -->
```csharp
var page = await client.ListTicketTemplatesAsync(new TicketTemplateListRequestDto(Start: 0, Length: 100));
var usable = page.Where(t => t.IsActive).ToList();
```

## PreviewAsync

<!-- snippet: partial -->
```csharp
Task<PdfDocumentDto> PreviewAsync(InvoiceDto invoice, bool withCode = true, CancellationToken ct = default);
```

Renders the PDF of an invoice **without saving it**. Calls `POST /webapp/preview?invoiceWithCode=…`
with the invoice as MISA `InvoiceData`.

**Steps and validation:**

1. Resolves the template (lists active templates first). No template on the invoice and none
   active throws `TemplateState` (`NoActiveTemplate`); several active throws `Configuration`
   (`AmbiguousTemplate`); a template that matches no active one throws `TemplateState`
   (`UnknownTemplate`). See [Templates and series](../index#templates-and-series).
2. Assigns a generated RefId if `RefId` is empty.
3. Runs the [invoice validation rules](#validation-rules); any failure throws `Validation` with
   every problem in `Failures`.
4. Sends the invoice.

**Result:** `PdfDocumentDto(byte[] Content, string ContentType = "application/pdf")`.

**Errors:** the exceptions above, MISA's errors (e.g. `Validation` for `InvalidInvoiceData`), and
transport categories. A `null` invoice throws `ArgumentNullException`; an unknown `VatRateName` or
a `RefId` over 64 characters throws `ArgumentException` while mapping (see
[Errors](./errors#exceptions-that-are-not-meinvoiceexception)).

<!-- snippet: partial -->
```csharp
PdfDocumentDto pdf = await client.PreviewAsync(invoice);
await File.WriteAllBytesAsync("preview.pdf", pdf.Content);
```

## SaveDraftAsync

<!-- snippet: partial -->
```csharp
Task<IReadOnlyList<SaveResultDto>> SaveDraftAsync(
    IReadOnlyList<InvoiceDto> invoices,
    bool withCode = true,
    CancellationToken ct = default);
```

Saves 1–30 invoices as **unpublished drafts** in one call to `POST /webapp/insert?invoiceWithCode=…`.
See [RefId and drafts](../index#refid-and-drafts).

**Steps and validation:**

- The batch: an empty list throws `Validation` (`ValidationFailed`, failure `$batch.Count`); more
  than 30 throws `BatchOrSize` (`InvoiceQuantityTooLarge`). Nothing is sent.
- Each invoice: a RefId is generated if missing; the template is resolved; the
  [validation rules](#validation-rules) run. An invoice that fails either step gets an `Error`
  result and is not sent; the others still go out together.
- If no invoice is left to send, no MISA insert is made.

**Result:** one `SaveResultDto` per input invoice, **in input order**:
`RefId`, `Outcome` (`SaveOutcomeDto.Success` / `Error`), and on error `ErrorCategory` (a category
name such as `"Validation"` or `"DuplicateOrUniqueness"`), `RawErrorCode`, `ErrorMessage` (MISA's
text, always included) and `Failures` (local validation failures: `FieldPath`, `Message`,
`RuleId`). Typical per-invoice errors: `Validation` / `ValidationFailed` (local),
`TemplateState` / `NoActiveTemplate` or `UnknownTemplate`, `Configuration` / `AmbiguousTemplate`,
MISA codes such as `DuplicateInvoiceRefID`, and `MisaUnknown` / `NoEchoedResult` when MISA's
response does not mention the invoice.

**Errors (thrown):** the two batch-size errors above, authentication and transport failures, and a
MISA response that fails as a whole without per-invoice entries.

**Retries:** the insert is retried once after a timeout, network error, 429 or 5xx. If the first
attempt was stored, the retry reports `DuplicateInvoiceRefID` for that invoice: check with
[`LookupByRefIdAsync`](#lookupbyrefidasync) before saving it again.

### A complete invoice

A minimal VND invoice for an organisation buyer with one goods line, saved as a draft:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Infrastructure.Configuration;

IMisaEInvoiceClient client = MisaEInvoiceClientFactory.Create(o =>
{
    o.Environment = MeInvoiceEnvironment.Sandbox;
    o.BaseUrl = "https://testapi.meinvoice.vn/api/integration";
    o.TaxCode = "0000000000";
    o.UserName = "your-misa-user";
    o.Password = "your-misa-password";
    o.AppId = "your-app-id";
});

var now = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(7));

var invoice = new InvoiceDto(
    RefId: "order-2026-0001",                          // your idempotency key; null = generated GUID
    Template: new TemplateRefDto("ipt-active-1", "1C26TAA"), // null if you have one active template
    InvDate: new DateOnly(2026, 11, 3),
    CreatedDate: now,
    ModifiedDate: now,
    Currency: "VND",
    ExchangeRate: 1m,
    PaymentMethod: "TM/CK",
    BuyerType: 1,                                       // 1 = organisation (Buyer.TaxCode required), 2 = individual
    Buyer: new BuyerInfoDto(
        Name: "Công ty TNHH Ví Dụ",
        TaxCode: "0312345678",
        Address: "1 Lê Lợi, Quận 1, TP. Hồ Chí Minh",
        Email: "ketoan@example.com"),
    Lines: new[]
    {
        new InvoiceLineDto(
            InventoryItemType: 0,                       // 0 = goods
            SortOrder: 1,
            Description: "Bàn phím cơ",
            UnitName: "cái",
            Quantity: 2m,
            UnitPrice: 500_000m,
            AmountOC: 1_000_000m,
            Amount: 1_000_000m,
            AmountWithoutVATOC: 1_000_000m,
            AmountWithoutVAT: 1_000_000m,
            VatRateName: "8%",
            VATAmountOC: 80_000m,
            VATAmount: 80_000m),
    },
    Totals: new InvoiceTotalsDto(
        TotalSaleAmountOC: 1_000_000m,
        TotalSaleAmount: 1_000_000m,
        TotalDiscountAmountOC: 0m,
        TotalDiscountAmount: 0m,
        TotalAmountWithoutVATOC: 1_000_000m,
        TotalAmountWithoutVAT: 1_000_000m,
        TotalVATAmountOC: 80_000m,
        TotalVATAmount: 80_000m,
        TotalAmountOC: 1_080_000m,
        TotalAmount: 1_080_000m,
        TotalAmountInWords: "Một triệu không trăm tám mươi nghìn đồng"));

IReadOnlyList<SaveResultDto> results = await client.SaveDraftAsync(new[] { invoice });
foreach (var r in results)
{
    if (r.Outcome == SaveOutcomeDto.Success) Console.WriteLine($"saved {r.RefId}");
    else Console.WriteLine($"{r.RefId}: {r.ErrorCategory} {r.RawErrorCode} {r.ErrorMessage}");
}
```

Field notes (names and casing are MISA's, see `docs/misa-api-reference/`):

- `InventoryItemType`: `0` goods, `2` promotion, `3` note, `4` trade discount, `6` special goods.
- `BuyerType`: `1` organisation, `2` individual.
- `VatRateName`: `KCT`, `KKKNT`, `0%`, `5%`, `8%`, `10%`, or `KHAC:<rate>%` with a positive rate
  (e.g. `KHAC:3.5%`). Leave it `null` for lines without VAT.
- Amounts are `decimal`; `…OC` fields are in the invoice currency, the others in VND.
- `CustomFields`: only key `1` is sent (as MISA's `CustomField1`).

### Validation rules

`PreviewAsync`, `SaveDraftAsync` and the amendment methods run the default `IInvoiceValidator`
before any insert or preview call (replace it via DI, see [Configuration](./configuration#other-ports)).
Each failure carries `FieldPath`, `Message` and `RuleId`:

| Rule | Check |
| --- | --- |
| `FR-021` | `RefId` at most 64 characters. |
| `FR-006` | `Currency` present; at least one line; totals present. |
| `FR-023` | `ExchangeRate` > 0 when `Currency` is not `VND`. |
| `FR-024` | `Buyer.TaxCode` present when `BuyerType` is 1 (organisation). |
| `FR-027` | `PaymentMethod` present. |
| `FR-028` | No emoji (supplementary-plane characters) in `Buyer.Name`, `Buyer.ContactName`, `Buyer.Address`, line `Description` and `UnitName` (MISA rejects them with `InvalidXMLContainEmoji`). |
| `FR-029` | `VatRateName` in the accepted set. |
| `FR-030` | At most 200 lines per invoice. |
| `FR-025` | `Totals.TotalDiscountAmountOC` equals the sum of goods lines' `DiscountAmountOC`, within ±0.5. |

## GetDraftPdfAsync

<!-- snippet: partial -->
```csharp
Task<PdfDocumentDto> GetDraftPdfAsync(string refId, bool withCode = true, CancellationToken ct = default);
```

Renders a saved draft's PDF by RefId. Calls `GET /webapp/viewrefid?invoiceWithCode=…&refid=…`.

**Validation:** a `null`, empty or whitespace `refId` throws `Validation` (`RefIdRequired`); a
RefId over 64 characters throws `ArgumentException`.

**Result:** `PdfDocumentDto` with the PDF bytes.

**Errors:** a RefId MISA does not know (MISA answers with no data) throws `ResourceNotFound`
(`RefIdNotFound`, `RefId` set on the exception); a response that is not a PDF throws `MisaUnknown`
(`InvalidPdfContent`). Use the same `withCode` as when saving.

<!-- snippet: partial -->
```csharp
var draftPdf = await client.GetDraftPdfAsync("order-2026-0001");
await File.WriteAllBytesAsync("order-2026-0001.pdf", draftPdf.Content);
```

## DeleteDraftAsync

<!-- snippet: partial -->
```csharp
Task<DeleteDraftOutcome> DeleteDraftAsync(string refId, bool invoiceWithCode, CancellationToken ct = default);
```

Deletes an unpublished draft. Calls `DELETE /webapp/delete?invoiceWithCode=…&refid=…`.
`invoiceWithCode` is required.

**Validation:** an empty RefId or one over 64 characters throws `MeInvoiceException`
(`Validation`, `RefIdInvalid`). That is the only exception besides cancellation.

**Result (outcome-returning):** `DeleteDraftOutcome` (namespace
`MisaConnect.EInvoice.Application.Results`): `Status`, `RawErrorCode`, `Message`, `Field`, `RefId`.
Every MISA, sign-in and transport failure becomes a `Status` instead of an exception:
`Deleted`, `NotFound`, `NotDeletable` (signed or published), `AuthFailed`, `Configuration`,
`MisaThrottled`, `MisaUnavailable`, `TransportFailed`, `MisaUnknown`. `Message` is MISA's text only
when `Misa:EInvoice:Delete:IncludeRawErrorMessage` is `true`, otherwise `null`. See
[Errors → Outcome-returning operations](./errors#outcome-returning-operations).

<!-- snippet: partial -->
```csharp
using MisaConnect.EInvoice.Application.Results;

DeleteDraftOutcome del = await client.DeleteDraftAsync("order-2026-0001", invoiceWithCode: true);
if (del.Status is not DeleteDraftStatus.Deleted)
    Console.WriteLine($"not deleted: {del.Status} {del.RawErrorCode}");
```

## LookupByRefIdAsync

<!-- snippet: partial -->
```csharp
Task<LookupBatchOutcomeDto> LookupByRefIdAsync(
    IReadOnlyList<string> refIds,
    bool invoiceWithCode,
    CancellationToken ct = default);
```

Looks invoices up by RefId. Calls `POST /webapp/getlist?invoiceWithCode=…` with chunks of at most
50 RefIds, one after the other (see [Paging](../index#paging)).

**Validation:** an empty list throws `Validation` (`RefIdsEmpty`); an empty RefId or one over 64
characters throws `Validation` (`RefIdInvalid`); a `null` list throws `ArgumentNullException`.

**Result (outcome-returning):** `LookupBatchOutcomeDto(string Status, IReadOnlyList<LookupOutcomeDto>? Outcomes, string? Reason)`.
`Status` is `"Completed"` with one `LookupOutcomeDto` per input RefId, in input order:
`RefId`, `Status` (`Found`, `NotFound`, `Validation`, `AuthFailed`, `Configuration`,
`MisaThrottled`, `MisaUnavailable`, `TransportFailed`, `MisaUnknown`), `Snapshot` (when `Found`),
`ErrorCode`, `Message` (MISA text only with `IncludeRawErrorMessage`). A failing chunk marks its
RefIds with that status and the next chunk is still sent, except after `AuthFailed` or
`Configuration`, which mark all remaining RefIds without further calls. A sign-in failure marks
every RefId.

`InvoiceSnapshotDto` carries `RefId`, `InvoiceTemplateID`, `InvSeries`, `InvDate`, `InvNo`,
`AccountObjectTaxCode`, `AccountObjectName`, the sale / VAT / total amounts (VND and `…OC`),
`RawEInvoiceStatus`, `RawPublishStatus`, the unified `Status` (`Draft`, `Signed`, `Issued`,
`Cancelled`, `Replaced`, `Adjusted`), `OrgRefID`, `CreatedDate`, `ModifiedDate`.
`AccountObjectName` is buyer data: do not log it.

<!-- snippet: partial -->
```csharp
var found = await client.LookupByRefIdAsync(new[] { "order-2026-0001" }, invoiceWithCode: true);
foreach (var o in found.Outcomes ?? Array.Empty<LookupOutcomeDto>())
    Console.WriteLine($"{o.RefId}: {o.Status} {o.Snapshot?.Status} {o.Snapshot?.InvNo}");
```

## LookupStandardAsync

<!-- snippet: partial -->
```csharp
Task<PagedLookupResultDto> LookupStandardAsync(
    PagedLookupRequestDto request,
    bool invoiceWithCode,
    CancellationToken ct = default);
```

Pages through **ordinary** invoices (series position 5 = `T`) by invoice date. Calls
`POST /webapp/paging?invoiceWithCode=…`.

**Input:** `PagedLookupRequestDto(int Start, int Length, string? Sort, DateOnly FromDate, DateOnly ToDate, int? PublishStatus)`.
`Sort` defaults to `InvDate` when `null` or blank. `PublishStatus` is `null` (all) or `0`, `4`, `6`,
`7` (see [Paging](../index#paging)).

**Validation:** `Start` < 0, `Length` outside 1–100, `FromDate` after `ToDate` or another
`PublishStatus` throws `MeInvoiceException` (`Validation`, `RawErrorCode` `FR-047`, `Field` naming
the field, one entry in `Failures`) before any request.

**Result:** `PagedLookupResultDto(IReadOnlyList<InvoiceSnapshotDto> Items, int Start, int Length, int ReturnedCount)`.
The last page has `ReturnedCount < Length`.

**Errors:** MISA and transport failures throw `MeInvoiceException`.

<!-- snippet: partial -->
```csharp
var request = new PagedLookupRequestDto(
    Start: 0, Length: 100, Sort: null,
    FromDate: new DateOnly(2026, 11, 1), ToDate: new DateOnly(2026, 11, 30),
    PublishStatus: null);

while (true)
{
    var page = await client.LookupStandardAsync(request, invoiceWithCode: true);
    foreach (var s in page.Items) Console.WriteLine($"{s.RefId} {s.InvNo} {s.Status}");
    if (page.ReturnedCount < page.Length) break;
    request = request with { Start = request.Start + request.Length };
}
```

## LookupCalculatingAsync

<!-- snippet: partial -->
```csharp
Task<PagedLookupResultDto> LookupCalculatingAsync(
    PagedLookupRequestDto request,
    bool invoiceWithCode,
    CancellationToken ct = default);
```

The same as [`LookupStandardAsync`](#lookupstandardasync) for **cash-register** invoices (*hóa đơn
từ máy tính tiền*, series position 5 = `M`). Calls `POST /webapp/paging/calculating?invoiceWithCode=…`.
Same input, validation, result and errors.

<!-- snippet: partial -->
```csharp
var posPage = await client.LookupCalculatingAsync(
    new PagedLookupRequestDto(0, 50, null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), PublishStatus: 6),
    invoiceWithCode: true);
```

## IssueReplacementAsync

<!-- snippet: partial -->
```csharp
Task<AmendmentResultDto> IssueReplacementAsync(ReplacementRequestDto request, CancellationToken ct = default);
```

Issues a **replacement** for an issued invoice: a complete new invoice with absolute totals that
supersedes the original. Calls `POST /webapp/insert?invoiceWithCode=…` with one invoice carrying
`EInvoiceStatus` `3`, the original's reference fields and `ChangeReason`.

**Input:** `ReplacementRequestDto(InvoiceDto Invoice, OriginalInvoiceReferenceDto OriginalRef, string ChangeReason, bool InvoiceWithCode = true)`,
with `OriginalInvoiceReferenceDto(string OrgRefID, string OrgInvNo, string OrgInvTemplateNo, string OrgInvSeries, DateOnly OrgInvDate)`.
For an original with series `1C26TAA`, `OrgInvTemplateNo` is `1` and `OrgInvSeries` is `C26TAA`
(see [Templates and series](../index#templates-and-series)).

**Validation (all throw, nothing is sent):**

1. Every `OriginalRef` field and `ChangeReason` must be present: `Validation` (`ValidationFailed`)
   with one failure per missing field (`$originalRef.OrgRefID` … `$originalRef.OrgInvDate`,
   `$changeReason`).
2. Template resolution, as for [`PreviewAsync`](#previewasync).
3. The [invoice validation rules](#validation-rules): `Validation` (`ValidationFailed`).

A `null` request or `OriginalRef` throws `ArgumentNullException`; a `null` `Invoice` fails while
mapping with a `NullReferenceException`.

**Result:** `AmendmentResultDto`: `RefId` (the new invoice), `Outcome`, `ErrorCategory`,
`RawErrorCode`, `ErrorMessage`, `Failures`, and `OrgRefId` (echoed from the request). MISA's
refusal (e.g. `Replacement` / `InvoiceCannotReplace`) comes back as `Outcome` `Error`, not as an
exception.

<!-- snippet: partial -->
```csharp
var replacement = await client.IssueReplacementAsync(new ReplacementRequestDto(
    Invoice: invoice with { RefId = "order-2026-0001-r1" },
    OriginalRef: new OriginalInvoiceReferenceDto(
        OrgRefID: "order-2026-0001",
        OrgInvNo: "00000012",
        OrgInvTemplateNo: "1",
        OrgInvSeries: "C26TAA",
        OrgInvDate: new DateOnly(2026, 11, 3)),
    ChangeReason: "Sai địa chỉ người mua"));

if (replacement.Outcome == SaveOutcomeDto.Error)
    Console.WriteLine($"{replacement.ErrorCategory} {replacement.RawErrorCode} {replacement.ErrorMessage}");
```

## IssueAdjustmentAsync

<!-- snippet: partial -->
```csharp
Task<AmendmentResultDto> IssueAdjustmentAsync(AdjustmentRequestDto request, CancellationToken ct = default);
```

Issues an **adjustment** for an issued invoice. Calls `POST /webapp/insert?invoiceWithCode=…` with
one invoice carrying `EInvoiceStatus` `4`, the original's reference fields and `ChangeReason`.
The invoice's lines and totals are the **difference** to apply: positive, negative or zero-net. The
SDK does not enforce a direction.

**Input:** `AdjustmentRequestDto(InvoiceDto Invoice, OriginalInvoiceReferenceDto OriginalRef, string ChangeReason, bool InvoiceWithCode = true)`.

**Validation, result and errors:** as for [`IssueReplacementAsync`](#issuereplacementasync). MISA
refusals such as `InvoiceCannotAdjust` or `HasAdjustmentInvoice` come back as an `Error` outcome
in category `Replacement`.

<!-- snippet: partial -->
```csharp
var adjustment = await client.IssueAdjustmentAsync(new AdjustmentRequestDto(
    Invoice: adjustmentDelta, // an InvoiceDto whose lines and totals hold the change only
    OriginalRef: new OriginalInvoiceReferenceDto("order-2026-0001", "00000012", "1", "C26TAA", new DateOnly(2026, 11, 3)),
    ChangeReason: "Điều chỉnh giảm số lượng"));
```

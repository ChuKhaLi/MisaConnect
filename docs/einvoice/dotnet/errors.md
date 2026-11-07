---
title: Errors
parent: .NET
grand_parent: EInvoice
nav_order: 4
---

# Errors

The SDK reports failures in two ways: operations that cannot produce their result **throw
`MeInvoiceException`**, and a few operations whose failures are expected business results
**return outcome values**. Both use the categories described on the
[EInvoice page](../index#error-categories).

## MeInvoiceException

`MeInvoiceException` (namespace `MisaConnect.EInvoice.Domain.Errors`) carries:

| Property | Type | Meaning |
| --- | --- | --- |
| `Category` | `MeInvoiceErrorCategory` | One of `Configuration`, `Authentication`, `TemplateState`, `DeclarationState`, `Validation`, `BatchOrSize`, `DuplicateOrUniqueness`, `ResourceNotFound`, `MisaThrottled`, `MisaUnavailable`, `Signing`, `Replacement`, `OtherWorkflow`, `MisaUnknown`, `NotDeletable`, `TransportFailed`. See [Error categories](../index#error-categories). |
| `RawErrorCode` | `string?` | MISA's `errorCode` (e.g. `InvalidInvoiceData`), or an SDK code (`ValidationFailed`, `RefIdInvalid`, `NoActiveTemplate`, `Timeout`, …). |
| `Message` | `string` | MISA's description when there is one, otherwise the SDK's. Not gated by the raw-message opt-in: do not log it if MISA text must stay out of your logs. |
| `Field`, `Component` | `string?` | `Field` names the request field of an SDK paging check (`Start`, `Length`, …). MISA codes such as `InvoiceDetail_Quantity_Line3` keep the field and line inside `RawErrorCode`; .NET does not split them into `Field` / `Component` (the TypeScript SDK does). |
| `RefId` | `string?` | The invoice concerned, when known (preview, get draft PDF). |
| `Failures` | `IReadOnlyList<ValidationFailure>?` | Local validation problems: `FieldPath`, `Message`, `RuleId` (e.g. `FR-024`). |

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Domain.Errors;
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

try
{
    var page = await client.LookupStandardAsync(
        new PagedLookupRequestDto(0, 100, null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null),
        invoiceWithCode: true);
    Console.WriteLine($"{page.ReturnedCount} invoice(s)");
}
catch (MeInvoiceException ex) when (ex.Category == MeInvoiceErrorCategory.Validation)
{
    foreach (var f in ex.Failures ?? Array.Empty<ValidationFailure>())
        Console.WriteLine($"{f.FieldPath}: {f.Message} ({f.RuleId})");
}
catch (MeInvoiceException ex) when (ex.Category is MeInvoiceErrorCategory.MisaThrottled
                                        or MeInvoiceErrorCategory.MisaUnavailable
                                        or MeInvoiceErrorCategory.TransportFailed)
{
    Console.WriteLine($"MISA unavailable ({ex.Category}, {ex.RawErrorCode}); retry later");
}
catch (MeInvoiceException ex)
{
    Console.WriteLine($"{ex.Category} {ex.RawErrorCode}");
}
```

### Validation errors raised by the SDK

The SDK checks input before calling MISA. Where the problem is reported differs per operation:

| Operation | Check | Thrown as |
| --- | --- | --- |
| `PreviewAsync` | [Invoice rules](./operations#validation-rules) | `Validation`, `RawErrorCode` `null`, all problems in `Failures`, `RefId` set. |
| `SaveDraftAsync` | Empty batch / more than 30 invoices | `Validation` `ValidationFailed` / `BatchOrSize` `InvoiceQuantityTooLarge`, failure `$batch.Count`. Per-invoice problems are **not** thrown: see [below](#outcome-returning-operations). |
| `IssueReplacementAsync`, `IssueAdjustmentAsync` | Request fields, then invoice rules | `Validation` `ValidationFailed`, `Failures` such as `$originalRef.OrgInvNo`, `$changeReason`. |
| `LookupStandardAsync`, `LookupCalculatingAsync`, `ListTicketTemplatesAsync` | Paging fields | `Validation`, `RawErrorCode` = the rule id `FR-047`, `Field` (`Start`, `Length`, `FromDate`, `PublishStatus`), one entry in `Failures`. |
| `GetDraftPdfAsync` | Empty RefId | `Validation` `RefIdRequired`. |
| `DeleteDraftAsync`, `LookupByRefIdAsync` | Empty or over-long RefId; empty list (lookup) | `Validation` `RefIdInvalid` / `RefIdsEmpty`. |

Template resolution errors (`TemplateState` `NoActiveTemplate` / `UnknownTemplate`, `Configuration`
`AmbiguousTemplate`) are thrown by preview and the amendments, and reported per invoice by
`SaveDraftAsync`.

### Exceptions that are not MeInvoiceException

- `OperationCanceledException` when your `CancellationToken` is cancelled (never retried or
  wrapped).
- `OptionsValidationException` for invalid `Misa:EInvoice` configuration (see
  [Configuration](./configuration#validator-behaviour)).
- `ArgumentNullException` for a `null` argument (invoice, list, request, `OriginalRef`).
- `ArgumentException` while mapping an `InvoiceDto`: an unknown `VatRateName` (e.g. `7%`), a
  `KHAC:` rate ≤ 0, or a `RefId` over 64 characters; also `GetDraftPdfAsync` with a RefId over 64
  characters. Validate these values yourself if you build invoices from user input.
- `InvalidOperationException` from `ListTicketTemplatesAsync` on a `MisaEInvoiceClient` built by
  hand with its pre-2.10 constructor.

## Outcome-returning operations

These operations return failures as values, so you branch on a status instead of catching:

| Operation | Returns | Still throws |
| --- | --- | --- |
| `DeleteDraftAsync` | `DeleteDraftOutcome` | Invalid RefId (`RefIdInvalid`), cancellation. |
| `LookupByRefIdAsync` | `LookupBatchOutcomeDto` with one `LookupOutcomeDto` per RefId | Empty list / invalid RefId, cancellation. |
| `SaveDraftAsync` | One `SaveResultDto` per invoice | Batch size, sign-in, transport, a MISA response that fails as a whole. |
| `IssueReplacementAsync`, `IssueAdjustmentAsync` | `AmendmentResultDto` | Request validation, template resolution, sign-in, transport. |

### DeleteDraftOutcome

`DeleteDraftOutcome(DeleteDraftStatus Status, string? RawErrorCode, string? Message, string? Field, RefId? RefId)`
in `MisaConnect.EInvoice.Application.Results`. Sign-in failures, MISA errors, throttling and
transport failures all become a status:

| `DeleteDraftStatus` | When |
| --- | --- |
| `Deleted` | MISA deleted the draft. |
| `NotFound` | MISA does not know the RefId (`InvalidTransactionID` with *không tồn tại*, an unrecognised message, or `RefIdNotFound`). Check `invoiceWithCode`. |
| `NotDeletable` | The invoice exists but was signed or published (`InvalidTransactionID` with *đã phát hành* / *đã ký*). |
| `AuthFailed` | Sign-in or token rejected. |
| `Configuration` | Configuration / account error (e.g. `InvalidAppID`). |
| `MisaThrottled`, `MisaUnavailable`, `TransportFailed` | 429, 5xx or no response after one retry. |
| `MisaUnknown` | Any other MISA error; see `RawErrorCode`. |

When MISA's `InvalidTransactionID` message matches none of the known phrases, the SDK reports
`NotFound` and logs a warning with event id `ARCH-DEL-001`, so that a change in MISA's wording is
noticed.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Application.Results;
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

DeleteDraftOutcome outcome = await client.DeleteDraftAsync("order-2026-0001", invoiceWithCode: true);
var message = outcome.Status switch
{
    DeleteDraftStatus.Deleted => "deleted",
    DeleteDraftStatus.NotFound => "nothing to delete",
    DeleteDraftStatus.NotDeletable => "already signed or published: cancel or amend it in MISA",
    DeleteDraftStatus.MisaThrottled or DeleteDraftStatus.MisaUnavailable or DeleteDraftStatus.TransportFailed
        => "MISA unavailable, retry later",
    _ => $"failed: {outcome.Status} {outcome.RawErrorCode}",
};
Console.WriteLine(message);
```

### LookupBatchOutcome

`LookupBatchOutcomeDto.Status` is `"Completed"` for every call that reaches MISA, with one
`LookupOutcomeDto` per input RefId in `Outcomes`. (`"Validation"` with a `Reason` exists in the
type, but the facade throws `RefIdsEmpty` for an empty list before it could occur.) Each
`LookupOutcomeDto.Status` is one of `Found` (with `Snapshot`), `NotFound`, `Validation`,
`AuthFailed`, `Configuration`, `MisaThrottled`, `MisaUnavailable`, `TransportFailed`,
`MisaUnknown`, with MISA's code in `ErrorCode`. An `AuthFailed` or `Configuration` chunk stops
further MISA calls: the remaining RefIds get the same status.

### Save and amendment results

`SaveResultDto` and `AmendmentResultDto` have `Outcome` (`SaveOutcomeDto.Success` / `Error`) and,
on error, `ErrorCategory` (the category name as a string), `RawErrorCode`, `ErrorMessage` and
`Failures`. `ErrorMessage` is MISA's text and is **always** included (it is not gated by the opt-in
below). A local validation failure has `ErrorCategory` `"Validation"`, `RawErrorCode`
`"ValidationFailed"` and the problems in `Failures`.

## Raw error messages

MISA's error text can contain buyer data or other vendor detail, so by default the SDK keeps it out
of outcomes and logs. Opt in with:

```json
{
  "Misa": {
    "EInvoice": {
      "Delete": { "IncludeRawErrorMessage": true }
    }
  }
}
```

`Misa:EInvoice:Delete:IncludeRawErrorMessage` (`MisaEInvoiceOptions.Delete.IncludeRawErrorMessage`,
default `false`) controls:

- `DeleteDraftOutcome.Message` (otherwise `null`);
- `LookupOutcomeDto.Message` on lookup outcomes (otherwise `null`);
- the MISA text excerpt in the `ARCH-DEL-001` warning;
- logging of an amendment's `ChangeReason` and of the exception message on a failed amendment
  call, with credential-like fragments redacted.

It does not affect `MeInvoiceException.Message` or `SaveResultDto` / `AmendmentResultDto.ErrorMessage`.
To read the flag from somewhere other than options at runtime, replace `IDeleteOptionsAccessor`
(see [Configuration](./configuration#other-ports)). An unknown key under `Misa:EInvoice:Delete`
fails startup, so a typo cannot silently leave the flag off.

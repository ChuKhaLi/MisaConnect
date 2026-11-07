---
title: Operations
parent: .NET
grand_parent: ESign
nav_order: 3
---

# Operations
{: .no_toc }

Every operation is a method on `IMisaESignClient` (namespace `MisaConnect.ESign.Client`), registered **scoped** by `AddMisaConnectESign`. Request and result DTOs are in `MisaConnect.ESign.Client.Dtos` (webhook DTOs in `MisaConnect.ESign.Client.Dtos.Webhook`); exceptions are in `MisaConnect.ESign.Domain.Errors`. Every method takes an optional `CancellationToken ct = default`.

1. TOC
{:toc}

The snippets on this page assume an `IMisaESignClient esign` resolved from DI, as in [Getting started](./getting-started):

<!-- snippet: partial -->
```csharp
await using var scope = provider.CreateAsyncScope();
var esign = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
```

Errors common to every signing method (sync and begin): `ESignGeneralException` with category `Validation` for a bad request (no HTTP call is made), `AuthenticationFailedException` (with `Requires2FA` for an OTP challenge — see [Two-factor sign-in](./two-factor)), `NoActiveCertificateException`, `ESignGeneralException` with `CertificateLookupFailed` / `HashRejected` / `AttachmentRejected` / `StatusLookupFailed`, `SignRejectedException`, and `ESignTransportException`. Each exception's `Format` names the document format of the call. Details: [Errors](./errors).

## SignPdfAsync

<!-- snippet: partial -->
```csharp
Task<SignPdfResultDto> SignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default)
```

Signs a PDF end-to-end and waits for the signer: sign-in (or cached token) → certificate selection → `documents/hash` → `Signing/hash` → `Signing/status` polling → `documents/attachment`. See [How signing works](../index#how-signing-works).

**Input — `SignPdfRequestDto`** (positional record; the trailing parameters are optional):

| Parameter | Type | Notes |
| --- | --- | --- |
| `Pdf` | `byte[]` | Required, non-empty. |
| `DocumentName` | `string` | Required, at most 100 characters. |
| `SignerName` | `string` | Required. Used as MISA's `signatureName` and `signedBy`. |
| `Location`, `Reason`, `Contact` | `string` | Required. Shown in the signature description. |
| `LogoImageBase64` | `string` | Required, non-empty: base64 of a PNG shown in the signature appearance. |
| `DataToBeDisplayed` | `string` | Required. Text shown to the signer in the MISA eSign app. |
| `Page` | `int?` | 1-based page of the signature box; ≥ 1 when set. |
| `PositionX`, `PositionY`, `Width`, `Height` | `int?` | Signature box position and size. |
| `RenderingMode` | `int?` | `0` description only (default), `1` description and image, `2` image only. |
| `DocumentId` | `string?` | At most 36 characters; a GUID is generated when omitted. |
| `DisplayText` | `string?` | Optional text for the signature appearance. |
| `ShowSignedDate` | `bool` | Default `true`. |
| `AdditionalSignaturePositions` | `IReadOnlyList<SignaturePosInfoDto>?` | Extra signature boxes: `SignaturePosInfoDto(PositionX, PositionY, Width, Height, Page)`. |

**Result — `SignPdfResultDto`**: `SignedPdf` (`byte[]`), `TransactionId`, `CertificateKeyAlias`, `CompletedAtUtc` (`DateTimeOffset`), `Format` (`DocumentFormat.Pdf`).

**Errors**, besides the common ones: `SignTerminalStateException` (MISA reported `FAILED` / `CANCELLED`; `TerminalStatus`, `TransactionId`), `SignTimeoutException` (no approval within `Polling:TotalTimeout`; `TransactionId`, `ElapsedTime`), and `InvalidOperationException` when `Webhook:Mode` is `Webhook`.

<!-- snippet: partial -->
```csharp
var result = await esign.SignPdfAsync(new SignPdfRequestDto(
    Pdf: await File.ReadAllBytesAsync("contract.pdf"),
    DocumentName: "contract.pdf",
    SignerName: "Nguyễn Văn A",
    Location: "Hà Nội",
    Reason: "Phê duyệt hợp đồng",
    Contact: "nguyenvana@example.com",
    LogoImageBase64: logoPngBase64,
    DataToBeDisplayed: "Hợp đồng số 2026/05/18-01",
    Page: 1, PositionX: 100, PositionY: 100, Width: 200, Height: 80,
    RenderingMode: 1));
await File.WriteAllBytesAsync("contract.signed.pdf", result.SignedPdf);
```

## SignXmlAsync

<!-- snippet: partial -->
```csharp
Task<SignXmlResultDto> SignXmlAsync(SignXmlRequestDto request, CancellationToken ct = default)
```

Signs an XML document (XAdES) end-to-end, with the same flow as `SignPdfAsync`. There is one method; the request carries the XML either as a **string** or as **UTF-8 bytes**. Build it with one of the two factories on the static class `SignXmlRequest`:

<!-- snippet: partial -->
```csharp
SignXmlRequestDto SignXmlRequest.FromString(string xml, XmlSignatureContextDto signatureContext, string documentName, string dataToBeDisplayed, string? documentId = null)
SignXmlRequestDto SignXmlRequest.FromUtf8Bytes(byte[] xmlUtf8Bytes, XmlSignatureContextDto signatureContext, string documentName, string dataToBeDisplayed, string? documentId = null)
```

**Input — `SignXmlRequestDto`**:

| Parameter | Type | Notes |
| --- | --- | --- |
| `Xml` | `string?` | The XML text (matches MISA's "text content" semantics for `FileToSign`). |
| `XmlUtf8Bytes` | `byte[]?` | The XML as UTF-8 bytes; the SDK decodes it (a leading BOM is kept). Exactly one of `Xml` / `XmlUtf8Bytes` must be set, otherwise `ArgumentException`. |
| `SignatureContext` | `XmlSignatureContextDto` | Required: `SignatureName`, `HashAlgorithm` (`"SHA256"`; empty or unknown values fall back to SHA256), `SignatureDescription`. |
| `DocumentName` | `string` | Required, at most 100 characters. |
| `DataToBeDisplayed` | `string` | Required. |
| `DocumentId` | `string?` | At most 36 characters; a GUID is generated when omitted. |

`SignatureDescriptionDto(SignedBy, Location, Reason, Contact, ShowSignedDate = null, DisplayText = null)` — the first four are required.

**Result — `SignXmlResultDto`**: `SignedXml` (`byte[]`, UTF-8), `TransactionId`, `CertificateKeyAlias`, `CompletedAtUtc`, `Format` (`DocumentFormat.Xml`).

**Errors**: as `SignPdfAsync`. XML-specific rejections use synthesized raw codes such as `InvalidXmlInput`, `MissingMainDom`, `MissingSignatureId`.

From a string:

<!-- snippet: partial -->
```csharp
var context = new XmlSignatureContextDto(
    SignatureName: "Seller",
    HashAlgorithm: "SHA256",
    SignatureDescription: new SignatureDescriptionDto(
        SignedBy: "ACME Co.", Location: "Hà Nội", Reason: "Issued", Contact: "billing@example.com"));

var signed = await esign.SignXmlAsync(SignXmlRequest.FromString(
    "<Invoice>…</Invoice>", context, documentName: "invoice.xml", dataToBeDisplayed: "Invoice 0001"));
Console.WriteLine(System.Text.Encoding.UTF8.GetString(signed.SignedXml));
```

From UTF-8 bytes:

<!-- snippet: partial -->
```csharp
var signed = await esign.SignXmlAsync(SignXmlRequest.FromUtf8Bytes(
    await File.ReadAllBytesAsync("invoice.xml"), context, "invoice.xml", "Invoice 0001"));
await File.WriteAllBytesAsync("invoice.signed.xml", signed.SignedXml);
```

## SignWordAsync

<!-- snippet: partial -->
```csharp
Task<SignWordResultDto> SignWordAsync(SignWordRequestDto request, CancellationToken ct = default)
```

Signs a Word (OOXML `.docx`) document end-to-end, using MISA's `wordDocs` arrays on `documents/hash` and `documents/attachment`.

**Input — `SignWordRequestDto`**: the same parameters as `SignPdfRequestDto`, with `Word` (`byte[]`, required, non-empty) in place of `Pdf`. `Page`, when set, is not range-checked by the SDK.

**Result — `SignWordResultDto`**: `SignedWord` (`byte[]`), `TransactionId`, `CertificateKeyAlias`, `CompletedAtUtc`, `Format` (`DocumentFormat.Word`).

**Errors**: as `SignPdfAsync`; an unsupported document variant is reported with raw code `UnsupportedDocumentVariant`.

<!-- snippet: partial -->
```csharp
var result = await esign.SignWordAsync(new SignWordRequestDto(
    Word: await File.ReadAllBytesAsync("contract.docx"),
    DocumentName: "contract.docx",
    SignerName: "Nguyễn Văn A", Location: "Hà Nội", Reason: "Phê duyệt", Contact: "nguyenvana@example.com",
    LogoImageBase64: logoPngBase64,
    DataToBeDisplayed: "Hợp đồng số 2026/05/18-01"));
await File.WriteAllBytesAsync("contract.signed.docx", result.SignedWord);
```

## SignExcelAsync

<!-- snippet: partial -->
```csharp
Task<SignExcelResultDto> SignExcelAsync(SignExcelRequestDto request, CancellationToken ct = default)
```

Signs an Excel (OOXML `.xlsx`) workbook end-to-end, using MISA's `excelDocs` arrays.

**Input — `SignExcelRequestDto`**: the same parameters as `SignPdfRequestDto`, with `Excel` (`byte[]`, required, non-empty) in place of `Pdf`. `Page`, when set, is not range-checked by the SDK.

**Result — `SignExcelResultDto`**: `SignedExcel` (`byte[]`), `TransactionId`, `CertificateKeyAlias`, `CompletedAtUtc`, `Format` (`DocumentFormat.Excel`).

**Errors**: as `SignWordAsync`.

<!-- snippet: partial -->
```csharp
var result = await esign.SignExcelAsync(new SignExcelRequestDto(
    Excel: await File.ReadAllBytesAsync("report.xlsx"),
    DocumentName: "report.xlsx",
    SignerName: "Nguyễn Văn A", Location: "Hà Nội", Reason: "Xác nhận", Contact: "nguyenvana@example.com",
    LogoImageBase64: logoPngBase64,
    DataToBeDisplayed: "Báo cáo tháng 10"));
await File.WriteAllBytesAsync("report.signed.xlsx", result.SignedExcel);
```

## SignInWithOtpAsync

<!-- snippet: partial -->
```csharp
Task SignInWithOtpAsync(string otpCode, OtpDeliveryChannel otpType, bool remember, CancellationToken ct = default)
```

Explicit two-factor sign-in: answers the OTP challenge that a preceding call on the **same `IMisaESignClient` instance** raised as `AuthenticationFailedException` with `Requires2FA = true`, for the current principal (`ClientId` + `UserName`). On success the tokens are cached and the next sign call proceeds without another prompt. It does not repeat the sign call for you.

**Inputs**: `otpCode` (required, non-empty), `otpType` — `OtpDeliveryChannel.SmsOrEmail` (0) or `OtpDeliveryChannel.Authenticator` (1), namespace `MisaConnect.ESign.Domain.Authentication` — and `remember` (MISA's "remember this device").

**Result**: none.

**Errors**: `InvalidOperationException` when no challenge is pending for the current principal; `ESignGeneralException` (`Validation`, raw code `InvalidOtpSubmission`) for an empty code; `InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`, `OtpRejectedException` (all subclasses of `AuthenticationFailedException`); `AuthenticationFailedException` when MISA re-issues the challenge; `ESignTransportException`.

<!-- snippet: partial -->
```csharp
await esign.SignInWithOtpAsync(codeFromUser, OtpDeliveryChannel.Authenticator, remember: true);
var result = await esign.SignPdfAsync(request); // repeat the call that asked for the OTP
```

Full flow: [Two-factor sign-in](./two-factor).

## ResendOtpAsync

<!-- snippet: partial -->
```csharp
Task<OtpResendResultDto> ResendOtpAsync(string? language = null, CancellationToken ct = default)
```

Asks MISA to send the OTP of the pending challenge again (`resend-otp-auth`).

**Input**: `language` — passed to MISA; `null` uses `Misa:ESign:Otp:DefaultResendLanguage` (`en-US`).

**Result — `OtpResendResultDto`**: `Success` (`bool`), `RawCode`, `UserMsg`, `DevMsg`, `CorrelationId`. A documented MISA rejection comes back as `Success = false` rather than an exception.

**Errors**: `InvalidOperationException` when no challenge is pending for the current principal; `ESignTransportException`.

<!-- snippet: partial -->
```csharp
var resend = await esign.ResendOtpAsync("vi-VN");
if (!resend.Success) Console.WriteLine($"Resend refused: {resend.RawCode}");
```

## BeginSignPdfAsync

<!-- snippet: partial -->
```csharp
Task<BeginResultDto> BeginSignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default)
```

Webhook mode: runs sign-in → certificate → `documents/hash` → `Signing/hash`, records a signing session and returns without polling. MISA later POSTs the outcome to your webhook, and [`HandleWebhookAsync`](#handlewebhookasync) finalizes the document. See [Webhook mode](./webhooks).

**Input**: `SignPdfRequestDto`, as for [`SignPdfAsync`](#signpdfasync).

**Result — `BeginResultDto`**: `TransactionId`, `Format`.

**Errors**: the common signing errors, and `InvalidOperationException` when `Webhook:Mode` is `Polling`.

<!-- snippet: partial -->
```csharp
BeginResultDto begun = await esign.BeginSignPdfAsync(request);
await SavePendingSigningAsync(begun.TransactionId); // your code: remember what is waiting for the signer
```

## BeginSignXmlAsync

<!-- snippet: partial -->
```csharp
Task<BeginResultDto> BeginSignXmlAsync(SignXmlRequestDto request, CancellationToken ct = default)
```

Webhook-mode start for XML. Input as for [`SignXmlAsync`](#signxmlasync) (build it with `SignXmlRequest.FromString` / `FromUtf8Bytes`); result and errors as for [`BeginSignPdfAsync`](#beginsignpdfasync).

<!-- snippet: partial -->
```csharp
var begun = await esign.BeginSignXmlAsync(SignXmlRequest.FromString(xml, context, "invoice.xml", "Invoice 0001"));
```

## BeginSignWordAsync

<!-- snippet: partial -->
```csharp
Task<BeginResultDto> BeginSignWordAsync(SignWordRequestDto request, CancellationToken ct = default)
```

Webhook-mode start for Word (`.docx`). Input as for [`SignWordAsync`](#signwordasync); result and errors as for [`BeginSignPdfAsync`](#beginsignpdfasync).

<!-- snippet: partial -->
```csharp
var begun = await esign.BeginSignWordAsync(wordRequest);
```

## BeginSignExcelAsync

<!-- snippet: partial -->
```csharp
Task<BeginResultDto> BeginSignExcelAsync(SignExcelRequestDto request, CancellationToken ct = default)
```

Webhook-mode start for Excel (`.xlsx`). Input as for [`SignExcelAsync`](#signexcelasync); result and errors as for [`BeginSignPdfAsync`](#beginsignpdfasync).

<!-- snippet: partial -->
```csharp
var begun = await esign.BeginSignExcelAsync(excelRequest);
```

## HandleWebhookAsync

<!-- snippet: partial -->
```csharp
Task<WebhookHandleResultDto> HandleWebhookAsync(WebhookEnvelopeDto envelope, CancellationToken ct = default)
```

Processes one inbound MISA webhook delivery: validates the envelope against the recorded session, confirms the outcome with MISA (`Signing/status`), attaches the signature on success, records the terminal state, calls your `IWebhookDeliveryHook`, and returns the ACK to send back to MISA.

**Input — `WebhookEnvelopeDto`**: MISA's envelope, deserialized from the request body (camelCase JSON names): `MessageId`, `ClientId`, `ExtraData` (`Dictionary<string, JsonElement>?`), `Status`, `ErrorCode`, `TransactionId`, `Signatures` (`List<WebhookSignatureDto>`: `DocumentId`, `Signature`).

**Result — `WebhookHandleResultDto`**: `Ack` (`WebhookAckDto`: `ErrorCode`, `DevMsg`, `UserMsg`; serialize it as the HTTP response body) and `Outcome` (`WebhookOutcomeDto`), one of:

- `WebhookOutcomeDto.SuccessWithSignedBytes` — `TransactionId`, `Format`, `SignedBytes`, `CorrelationId`.
- `WebhookOutcomeDto.TerminalWithoutFinalize` — `TransactionId`, `Format`, `Status` (`WebhookStatusDto.Failed` / `Cancelled`), `MisaErrorCode`, `CorrelationId`.
- `WebhookOutcomeDto.FailureWithError` — `TransactionId?`, `Format`, `CategoryName` (`MalformedEnvelope`, `ClientIdMismatch`, `UnknownTransaction`, `IncompleteSuccessEnvelope`, `DocumentIdMismatch`, or `None` for a finalize failure), `MisaErrorCode`, `CorrelationId`.

**Errors**: it does not throw for malformed input or finalize failures — those come back as a failure ACK (codes on [Webhook mode](./webhooks#webhook-ack-codes)). Two exceptions propagate: caller cancellation, and an exception thrown by **your** `IWebhookDeliveryHook` while delivering a success or terminal outcome. `ArgumentNullException` for a null envelope.

<!-- snippet: partial -->
```csharp
app.MapPost("/esign/webhook/{secret}", async (string secret, WebhookEnvelopeDto envelope,
    IMisaESignClient esign, CancellationToken ct) =>
{
    if (!SecretMatches(secret)) return Results.NotFound(); // authenticate the route yourself
    var result = await esign.HandleWebhookAsync(envelope, ct);
    return Results.Json(result.Ack);
});
```

Endpoint setup, delivery hook and processing rules: [Webhook mode](./webhooks).

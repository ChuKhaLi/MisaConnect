---
title: Errors
parent: .NET
grand_parent: ESign
nav_order: 7
---

# Errors
{: .no_toc }

Every SDK failure is an `ESignException` (namespace `MisaConnect.ESign.Domain.Errors`) or a subclass. The categories are shared with the TypeScript SDK and explained on the [ESign page](../index#error-categories).

1. TOC
{:toc}

## `ESignException`

| Property | Type | Meaning |
| --- | --- | --- |
| `Category` | `ESignErrorCategory` | What failed (table below). |
| `RawCode` | `string?` | MISA's `errorCode`, or a code the SDK synthesized (e.g. `InvalidSignPdfRequest`, `NoActiveCertificate`, `SignTimeout`, `UnexpectedContentType`). |
| `Detail` | `string` | Description. MISA's raw `userMsg` / `devMsg` (and per-property `validationFailures`) are included only with `Misa:ESign:Errors:IncludeRawErrorMessage = true`. Also the exception `Message`. |
| `CorrelationId` | `string` | The call's correlation ID (from `ICorrelationIdAccessor`). |
| `Format` | `DocumentFormat` | `Pdf`, `Xml`, `Word`, `Excel`, or `Unknown`. |

## Exception types

| Exception | Category | Extra members | Raised when |
| --- | --- | --- | --- |
| `ESignGeneralException` | any (`Validation`, `CertificateLookupFailed`, `HashRejected`, `AttachmentRejected`, `StatusLookupFailed`, `MisaUnknown`, …) | — | The request failed validation before any HTTP call, MISA refused a step, or a response was unusable (e.g. a non-JSON success from an ESRM endpoint). |
| `AuthenticationFailedException` | `Authentication` | `Requires2FA`, `Username` | Sign-in failed or the token was rejected after refresh and re-login. `Requires2FA = true` means MISA needs an OTP — see [Two-factor sign-in](./two-factor). |
| `InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`, `OtpRejectedException` | `Authentication` | — | The OTP exchange was refused. Subclasses of `AuthenticationFailedException`. |
| `NoActiveCertificateException` | `NoActiveCertificate` | — | The account has no `ACTIVE` certificate. Raised before any document is sent. |
| `SignRejectedException` | `SignRejected` | `RequiresUserCertSetup` | MISA refused the sign submission. `RequiresUserCertSetup = true` when MISA says the user's remote-signing account is not connected / set up. |
| `SignTerminalStateException` | `SignTerminalFailed`, `SignTerminalCancelled`, `SignTerminalUnknown` | `TerminalStatus`, `TransactionId` | The transaction ended `FAILED` / `CANCELLED` (for example the signer declined). |
| `SignTimeoutException` | `SignTimeout` | `TransactionId`, `ElapsedTime` | The signer did not approve within `Polling:TotalTimeout`. |
| `ESignTransportException` | `Transport` | `LastStatusCode`, `AttemptCount` | Network failure, timeout, 429 or 5xx after the `TransportRetry` budget. |
| `WebhookValidationException` and subclasses (`MalformedEnvelopeException`, `ClientIdMismatchException`, `UnknownTransactionException`, `IncompleteSuccessEnvelopeException`, `DocumentIdMismatchException`) | `WebhookValidation` | `WebhookCategory`, `MatchedTransactionId` | Used inside webhook handling. `HandleWebhookAsync` turns them into ACK codes and does not throw them — see [Webhook ACK codes](./webhooks#webhook-ack-codes). |

`ESignErrorCategory` also has `WebhookFinalize`; a finalize failure surfaces as the `webhook.finalize_failed` ACK, not as an exception.

Exceptions that are not `ESignException`:

- `InvalidOperationException` — a disabled `Webhook:Mode` (`Sign*Async` in `Webhook` mode, `BeginSign*Async` in `Polling` mode); `SignInWithOtpAsync` / `ResendOtpAsync` with no pending challenge; an empty `UserName` / `ClientId` from a dynamic credentials accessor.
- `ArgumentException` / `ArgumentNullException` — a null request, or a `SignXmlRequestDto` with both or neither of `Xml` / `XmlUtf8Bytes`.
- `OperationCanceledException` — your `CancellationToken` was cancelled.
- `OptionsValidationException` — invalid `Misa:ESign` options, at startup (see [Configuration → Validator behaviour](./configuration#validator-behaviour)).

## Handling errors

```csharp
using MisaConnect.ESign.Domain.Errors;

static string Describe(Exception e) => e switch
{
    AuthenticationFailedException { Requires2FA: true } a => $"OTP required for {a.Username}",
    InvalidOtpException or ExpiredOtpException => "Ask the user for a new code",
    NoActiveCertificateException => "The account has no active certificate; contact the MISA administrator",
    SignRejectedException { RequiresUserCertSetup: true } => "The user's remote-signing account is not set up",
    SignTerminalStateException t => $"Signing ended: {t.TerminalStatus} ({t.TransactionId})",
    SignTimeoutException t => $"No approval after {t.ElapsedTime} ({t.TransactionId})",
    ESignTransportException t => $"MISA unreachable after {t.AttemptCount} attempts ({t.LastStatusCode}); retry later",
    ESignException x => $"{x.Category} {x.RawCode} (correlation {x.CorrelationId})",
    _ => e.Message,
};

Console.WriteLine(Describe(new SignTimeoutException("tx-1", TimeSpan.FromMinutes(1), "timeout", "corr-1")));
```

Log `Category`, `RawCode` and `CorrelationId`; avoid logging `Detail` when `IncludeRawErrorMessage` is on, since MISA's messages can contain user data. The SDK's own logs never contain tokens, credentials, document bytes or signatures.

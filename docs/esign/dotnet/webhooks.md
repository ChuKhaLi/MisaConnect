---
title: Webhook mode
parent: .NET
grand_parent: ESign
nav_order: 5
---

# Webhook mode
{: .no_toc }

Webhook mode starts a signing transaction and returns at once; MISA reports the result to your HTTP endpoint later. The concept and the rules shared with the TypeScript SDK are on the [ESign page](../index#webhook-mode).

1. TOC
{:toc}

## Overview

1. Allow webhook mode: `Misa:ESign:Webhook:Mode` is `Both` (default) or `Webhook`. With `Webhook`, the blocking `Sign*Async` methods throw `InvalidOperationException`; with `Polling`, the `BeginSign*Async` methods do.
2. Register an `IWebhookDeliveryHook` that stores the signed document.
3. Host an HTTP endpoint for MISA's POST and register its public URL with MISA (out of band — the SDK does not register it).
4. Call `BeginSign{Pdf,Xml,Word,Excel}Async`. It returns a `BeginResultDto` (`TransactionId`, `Format`) once the signer's approval request is sent.
5. When MISA POSTs the envelope, pass it to `HandleWebhookAsync` and return `result.Ack` as JSON.

The signing session (the hashed document data and the principal's `clientId` / `userName`, never secrets) lives in `ISigningSessionStore` for `Webhook:Session:Ttl` (24 h by default). The default store is in-memory: a webhook must reach the same process that began the transaction, so a host with several instances needs a [persistent store](#custom-isigningsessionstore).

## Delivery hook

`IWebhookDeliveryHook` (namespace `MisaConnect.ESign.Client.Webhook`) receives the typed `WebhookOutcomeDto` of each processed delivery. The default logs at INFO and does nothing else. Register yours before or after `AddMisaConnectESign`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Client.Dtos.Webhook;
using MisaConnect.ESign.Client.Webhook;

var services = new ServiceCollection();
services.AddSingleton<IWebhookDeliveryHook, SaveSignedDocumentHook>();
services.AddMisaConnectESign(o =>
{
    /* … */
    o.Webhook.DeliverValidationFailuresToHook = false; // the 3.0 default; see below
});

internal sealed class SaveSignedDocumentHook : IWebhookDeliveryHook
{
    public async Task DeliverAsync(WebhookOutcomeDto outcome, CancellationToken ct)
    {
        switch (outcome)
        {
            case WebhookOutcomeDto.SuccessWithSignedBytes ok:
                await File.WriteAllBytesAsync($"{ok.TransactionId}.signed", ok.SignedBytes, ct);
                break;
            case WebhookOutcomeDto.TerminalWithoutFinalize ended:
                Console.WriteLine($"{ended.TransactionId} ended: {ended.Status} ({ended.MisaErrorCode})");
                break;
            case WebhookOutcomeDto.FailureWithError failed:
                Console.WriteLine($"Webhook rejected: {failed.CategoryName} (correlation {failed.CorrelationId})");
                break;
        }
    }
}
```

The hook is registered as a singleton; resolve scoped services (a `DbContext`, …) through `IServiceScopeFactory` inside it.

When the hook is called:

- `SuccessWithSignedBytes` — once per transaction, after MISA confirmed `SUCCESS` and the signature was attached.
- `TerminalWithoutFinalize` — once per transaction, after MISA confirmed `FAILED` or `CANCELLED`.
- `FailureWithError` — for validation failures (malformed envelope, client id mismatch, unknown transaction, incomplete or mismatched success) **only while `Webhook:DeliverValidationFailuresToHook` is `true`**, the 2.x default. Anyone who reaches your endpoint can send one, so they say nothing reliable about a transaction. **The default becomes `false` in 3.0**; set it to `false` now unless your hook needs those failures. A finalize failure (`webhook.finalize_failed`) never reaches the hook; MISA's retry finishes the job.

Also keep the bytes from `HandleWebhookAsync`'s own result if your hook can fail: see the exception rule under [How a webhook is processed](#how-a-webhook-is-processed).

## Webhook endpoint

The SDK does not authenticate the HTTP request. Protect the route yourself with a long secret path segment compared in constant time and/or an IP allowlist. The sample API reads `Webhook:Path`, `Webhook:Secret` (≥ 32 characters) and `Webhook:AllowedIps` for this, and logs a startup warning when webhook mode is on with neither configured. A minimal ASP.NET Core endpoint:

<!-- snippet: partial -->
```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.Dtos.Webhook;
using MisaConnect.ESign.Infrastructure.Configuration;

app.MapPost("/esign/webhook/{secret}", async (
    string secret,
    WebhookEnvelopeDto envelope,
    IMisaESignClient esign,
    IOptions<MisaESignOptions> options,
    CancellationToken ct) =>
{
    var expected = Encoding.UTF8.GetBytes(options.Value.Webhook.Secret ?? "");
    var actual = Encoding.UTF8.GetBytes(secret);
    if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(actual, expected))
        return Results.NotFound();

    var result = await esign.HandleWebhookAsync(envelope, ct);
    return Results.Json(result.Ack); // MISA expects the ACK JSON: { "errorCode", "devMsg", "userMsg" }
});
```

The complete version, with the CIDR allowlist, is `ESignWebhookEndpoint.MapESignWebhookEndpoint` in [`samples/dotnet/MisaConnect.Samples.Api`](https://github.com/ChuKhaLi/MisaConnect/tree/main/samples/dotnet/MisaConnect.Samples.Api/Endpoints). The archived design walkthrough (mode configuration, transport-layer auth, ngrok-based local development) is [`specs/004-misa-esign-webhook/quickstart.md`](https://github.com/ChuKhaLi/MisaConnect/blob/main/specs/004-misa-esign-webhook/quickstart.md). For the sandbox end-to-end test, see [Sandbox setup → Webhook sandbox](./sandbox-setup#webhook-sandbox).

## How a webhook is processed

The webhook is only a trigger. For each accepted webhook the SDK calls MISA's `Signing/status` (one extra call per webhook) and decides from MISA's answer, not from the webhook body: on `SUCCESS` it attaches MISA's signature; on `FAILED` / `CANCELLED` the outcome carries MISA's status and error code. The first terminal state of a transaction (success, failed or cancelled) is stored once; later webhooks for it return the originally returned ACK and do not call the hook again. Finalize never prompts for an OTP: a two-factor requirement, an authentication error or a certificate error during finalize becomes a `webhook.finalize_failed` ACK.

`HandleWebhookAsync` does not throw for malformed input or finalize failures; it returns an ACK and an outcome. Two exceptions propagate: caller cancellation, and exceptions thrown by **your** `IWebhookDeliveryHook` while delivering a success or terminal outcome. The success is cached before the hook runs, so MISA's retry receives the cached ACK and the hook is not called again for that transaction. (Exceptions thrown by the hook while delivering a validation-failure outcome are logged and swallowed.)

Concurrent deliveries for one transaction are serialized (single-flight finalize), so the document is attached once.

## Webhook ACK codes

`HandleWebhookAsync` returns a `WebhookAckDto` (`ErrorCode`, `DevMsg`, `UserMsg`, serialized as `errorCode` / `devMsg` / `userMsg`). Success — including a confirmed `FAILED` / `CANCELLED` outcome — uses `errorCode` `"0"`. Failures use these codes:

| ACK `errorCode` | Returned when |
| --- | --- |
| `webhook.malformed` | The body is missing or has an empty `messageId`, `clientId` or `transactionId`, an unknown `status` (matched case-insensitively), or a null entry in `signatures` (a non-array `signatures` value fails JSON binding before the SDK sees it). Also the fallback for an unspecified validation failure. |
| `webhook.client_id_mismatch` | The webhook's `clientId` differs from the configured `ClientId`. |
| `webhook.unknown_transaction` | No signing session is recorded for that `transactionId` (never begun, or expired after `Webhook:Session:Ttl`). |
| `webhook.incomplete_success` | `status` is `SUCCESS` but `signatures[]` is empty or invalid. |
| `webhook.document_id_mismatch` | A `signatures[].documentId` does not match the session. |
| `webhook.finalize_failed` | Confirming or attaching the signature failed (MISA status call, credentials, two-factor, certificate, a non-final or signature-less status, or any other unexpected error). MISA may retry. |

## Dynamic mode and webhooks

- **Client id check.** In `Dynamic` mode the webhook `clientId` check is skipped only when `ClientId` is left blank. If a Dynamic configuration also sets `ClientId`, webhooks for any other `clientId` are rejected with `webhook.client_id_mismatch`. With a blank `ClientId`, the session lookup by `(clientId, transactionId)` is the spoof check.
- **Credentials.** A Dynamic-mode session records the `clientId` and `userName` it was started with (never secrets). On the webhook, the SDK asks `IWebhookCredentialsResolver.ResolveAsync(clientId, userName, ct)` for that principal's credentials. The default resolver returns `null` in Dynamic mode (so finalize answers `webhook.finalize_failed`); register your own **before** `AddMisaConnectESign`. The credentials it returns must belong to the same `clientId` and `userName`, otherwise finalize fails (`CredentialsMismatch`). In `Static` mode the options credentials are used and the resolver is never called, even a custom one. Example: [Credentials → Webhook credentials resolver](./credentials#webhook-credentials-resolver).
- **Custom `ISigningSessionStore`.** See below.

## Custom `ISigningSessionStore`

Replace the in-memory store (register before `AddMisaConnectESign`) to run several instances or survive restarts. `ISigningSessionStore` (`MisaConnect.ESign.Application.Abstractions`) has `RegisterAsync`, `TryGetByTransactionIdAsync`, `RecordObservedMessageIdAsync`, `CacheSuccessAsync` and `CacheTerminalAsync`. The finalize lock is a separate port, `IFinalizeLockOwner` (`AcquireFinalizeLockAsync(clientId, transactionId, ct)` returning an `IAsyncDisposable`); its default is the in-memory store, which locks within one process only. With a distributed store, register an `IFinalizeLockOwner` too (typically the same class), which serializes finalizers of one `(clientId, transactionId)` across instances with the store's own locking (Redlock, `SELECT … FOR UPDATE`, …).

- A persistent store must persist and restore `SigningSession.UserName` and `SigningSession.CachedTerminal` (both init-only properties). A store that rebuilds sessions through the constructor drops `UserName`, and Dynamic-mode webhooks then fail with `MissingPrincipal`.
- `CacheTerminalAsync` has a default no-op implementation: a store that does not implement it keeps the 2.4 behaviour (failed / cancelled terminals are confirmed with MISA and re-delivered on every webhook).
- Failure ACKs are never cached.

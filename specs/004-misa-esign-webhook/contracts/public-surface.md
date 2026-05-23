# Public surface — slice 4 additions

The four production projects (`MisaConnect.ESign.{Domain,Application,Infrastructure,Client}`) and the sample API (`samples/MisaConnect.Samples.Api/`) expose the following new public types under slice 4. All additions are semver-additive on the `2.0.0-preview.N` line; the package version after slice 4 is `2.0.0-preview.4`.

## 1. `IMisaESignClient` additions (Client layer)

Five new methods on the existing facade interface.

```csharp
namespace MisaConnect.ESign.Client;

public interface IMisaESignClient
{
    // existing slice-1/2/3 methods unchanged

    /// <summary>
    /// Initiate a PDF sign in webhook mode. Runs login → cert select → /documents/hash →
    /// register session in ISigningSessionStore → /Signing/hash, then returns. The signed
    /// PDF arrives asynchronously when MISA POSTs the webhook envelope to the consumer's
    /// HTTP endpoint; the consumer's endpoint passes the envelope to
    /// <see cref="HandleWebhookAsync"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when MisaESignOptions.Webhook.Mode == Polling (FR-093).</exception>
    /// <exception cref="ESignException">Standard slice-1 + slice-3 error surface (Format = Pdf).</exception>
    Task<BeginSignPdfResultDto> BeginSignPdfAsync(BeginSignPdfRequestDto request, CancellationToken ct = default);

    /// <summary>Same as <see cref="BeginSignPdfAsync"/> for XML. Accepts both string + byte[] overloads on the request DTO.</summary>
    Task<BeginSignXmlResultDto> BeginSignXmlAsync(BeginSignXmlRequestDto request, CancellationToken ct = default);

    /// <summary>Same as <see cref="BeginSignPdfAsync"/> for Word.</summary>
    Task<BeginSignWordResultDto> BeginSignWordAsync(BeginSignWordRequestDto request, CancellationToken ct = default);

    /// <summary>Same as <see cref="BeginSignPdfAsync"/> for Excel.</summary>
    Task<BeginSignExcelResultDto> BeginSignExcelAsync(BeginSignExcelRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Handle an inbound MISA webhook delivery. The consumer's HTTP endpoint deserializes
    /// MISA's POST body into <see cref="WebhookEnvelopeDto"/> and passes it here. The SDK
    /// validates the envelope (FR-082), runs single-flight finalize via /documents/attachment
    /// (FR-081), invokes the registered <see cref="IWebhookDeliveryHook"/> on success exactly
    /// once (per (clientId, transactionId)), and returns the <see cref="WebhookAckDto"/> for
    /// the consumer's endpoint to return to MISA.
    /// </summary>
    /// <remarks>Always returns a non-null result; thrown exceptions reach the consumer's
    /// endpoint only on infrastructure errors (e.g. ISigningSessionStore down). Validation
    /// failures are returned as failure ACKs, not thrown.</remarks>
    Task<WebhookHandleResultDto> HandleWebhookAsync(WebhookEnvelopeDto envelope, CancellationToken ct = default);

    // Existing slice-1 SignPdfAsync / slice-3 SignXmlAsync/SignWordAsync/SignExcelAsync remain
    // unchanged in shape but now enforce the FR-093 Mode guard: throw "polling mode disabled"
    // when MisaESignOptions.Webhook.Mode == Webhook.
}
```

## 2. Client-layer DTOs (under `MisaConnect.ESign.Client.Dtos.Webhook`)

### 2.1 Begin request DTOs (one per format)

Each mirrors its slice-1 / slice-3 blocking counterpart one-to-one. Only `BeginSignPdfRequestDto` is shown; the three non-PDF variants follow the slice-3 shape of `SignXmlRequestDto` / `SignWordRequestDto` / `SignExcelRequestDto`.

```csharp
namespace MisaConnect.ESign.Client.Dtos.Webhook;

public sealed record BeginSignPdfRequestDto(
    byte[] Pdf,
    SignatureInfoDto SignatureContext,
    string DocumentName,
    string DataToBeDisplayed,
    string? DocumentId = null);

public sealed record BeginSignXmlRequestDto(
    string? Xml,                           // exactly one of (Xml, XmlUtf8Bytes) non-null — mirrors slice-3 SignXmlRequestDto
    byte[]? XmlUtf8Bytes,
    XmlSignatureContextDto SignatureContext,
    string DocumentName,
    string DataToBeDisplayed,
    string? DocumentId = null);

public sealed record BeginSignWordRequestDto(
    byte[] Word,
    SignatureInfoDto SignatureContext,
    string DocumentName,
    string DataToBeDisplayed,
    string? DocumentId = null);

public sealed record BeginSignExcelRequestDto(
    byte[] Excel,
    SignatureInfoDto SignatureContext,
    string DocumentName,
    string DataToBeDisplayed,
    string? DocumentId = null);
```

### 2.2 Begin result DTOs

```csharp
public sealed record BeginSignPdfResultDto(string TransactionId, DocumentFormat Format);
public sealed record BeginSignXmlResultDto(string TransactionId, DocumentFormat Format);
public sealed record BeginSignWordResultDto(string TransactionId, DocumentFormat Format);
public sealed record BeginSignExcelResultDto(string TransactionId, DocumentFormat Format);
```

Each carries the MISA-issued `transactionId` and the `DocumentFormat` matching the called facade. No `MessageId` field per Clarifications Q1 (MISA generates `messageId` server-side and only delivers it on the inbound webhook).

### 2.3 Webhook envelope + ACK + outcome DTOs

```csharp
public sealed record WebhookEnvelopeDto(
    string MessageId,
    string ClientId,
    IReadOnlyDictionary<string, JsonElement>? ExtraData,
    WebhookStatusDto Status,
    string? ErrorCode,
    string TransactionId,
    IReadOnlyList<WebhookSignatureDto> Signatures);

public sealed record WebhookSignatureDto(string DocumentId, string Signature);

public enum WebhookStatusDto : byte { Success = 1, Failed = 2, Cancelled = 3 }

public sealed record WebhookAckDto(string ErrorCode, string DevMsg, string UserMsg);

public abstract record WebhookOutcomeDto(string CorrelationId)
{
    public sealed record SuccessWithSignedBytes(
        string TransactionId,
        DocumentFormat Format,
        byte[] SignedBytes,
        string CorrelationId) : WebhookOutcomeDto(CorrelationId);

    public sealed record FailureWithError(
        string? TransactionId,
        DocumentFormat Format,
        WebhookValidationCategoryDto Category,
        string? MisaErrorCode,
        string CorrelationId) : WebhookOutcomeDto(CorrelationId);

    public sealed record TerminalWithoutFinalize(
        string TransactionId,
        DocumentFormat Format,
        WebhookStatusDto Status,
        string? MisaErrorCode,
        string CorrelationId) : WebhookOutcomeDto(CorrelationId);
}

public enum WebhookValidationCategoryDto : byte
{
    None = 0,
    MalformedEnvelope = 1,
    ClientIdMismatch = 2,
    UnknownTransaction = 3,
    IncompleteSuccessEnvelope = 4,
    DocumentIdMismatch = 5,
}

public sealed record WebhookHandleResultDto(WebhookAckDto Ack, WebhookOutcomeDto Outcome);
```

The DTO shapes mirror the Domain types one-to-one — same field set, same naming. Two namespaces exist (`Domain.Webhook.*` for typed Domain logic; `Client.Dtos.Webhook.*` for the public-surface DTOs) so consumers don't need a `Domain.Webhook` using directive at their HTTP-endpoint call sites.

## 3. Consumer-implementable interfaces (Client layer)

```csharp
namespace MisaConnect.ESign.Client.Webhook;

public interface IWebhookDeliveryHook
{
    /// <summary>
    /// Invoked once per successful finalize (per (clientId, transactionId)), and once
    /// per FAILED/CANCELLED webhook envelope. NOT invoked for cached-success short-circuits
    /// or for failure ACKs (validation failures, transient finalize failures). The consumer
    /// dispatches the outcome to its downstream (queue, database, SignalR, etc.).
    /// </summary>
    Task DeliverAsync(WebhookOutcomeDto outcome, CancellationToken ct);
}

/// <summary>The default registered via TryAddSingleton; logs at INFO and returns.</summary>
internal sealed class NullWebhookDeliveryHook : IWebhookDeliveryHook { … }
```

Consumers register their own implementation BEFORE `AddMisaConnectESign(IConfiguration)` runs (see research R-9).

## 4. Application-layer port (the only new port)

```csharp
namespace MisaConnect.ESign.Application.Abstractions;

public interface ISigningSessionStore
{
    ValueTask RegisterAsync(SigningSession session, CancellationToken ct);
    ValueTask<SigningSession?> TryGetByTransactionIdAsync(string clientId, string transactionId, CancellationToken ct);
    ValueTask RecordObservedMessageIdAsync(string clientId, string transactionId, string messageId, CancellationToken ct);
    ValueTask CacheSuccessAsync(string clientId, string transactionId, string triggeringMessageId, WebhookAck ack, byte[] signedBytes, CancellationToken ct);
}
```

Adapters MUST guarantee single-flight `CacheSuccessAsync` per `(clientId, transactionId)` (port docstring documents this; the in-memory adapter realizes it via a per-key `SemaphoreSlim`). Distributed adapters use their native locking primitives.

## 5. Domain types (under `MisaConnect.ESign.Domain.{Sessions, Webhook, Errors}`)

See [data-model.md §1](../data-model.md) for the full record definitions. Public types:

- `Sessions.SigningSession` (record)
- `Sessions.PerFormatHashPayload` (abstract closed union: `Pdf` / `Xml` / `Word` / `Excel` subclasses)
- `Sessions.SigningSessionCachedSuccess` (record)
- `Webhook.WebhookEnvelope` (record)
- `Webhook.WebhookSignature` (record)
- `Webhook.WebhookStatus` (enum)
- `Webhook.WebhookAck` (record)
- `Webhook.WebhookOutcome` (abstract closed union: `SuccessWithSignedBytes` / `FailureWithError` / `TerminalWithoutFinalize` subclasses)
- `Webhook.BeginResult` (record)
- `Webhook.WebhookValidationCategory` (enum)
- `Errors.WebhookValidationException` (abstract) + 5 sealed subclasses (`MalformedEnvelopeException`, `ClientIdMismatchException`, `UnknownTransactionException`, `IncompleteSuccessEnvelopeException`, `DocumentIdMismatchException`)

Every typed exception extends slice 1's `ESignException` base — the slice-3 `Format` property contract (FR-062) carries through. Pre-session-resolution exceptions (`MalformedEnvelope`, `ClientIdMismatch`, `UnknownTransaction`) carry `Format == DocumentFormat.Unknown`; post-session-resolution exceptions (`IncompleteSuccessEnvelope`, `DocumentIdMismatch`) carry the session's resolved `Format`.

## 6. Configuration additions (DI-touching; under `MisaConnect.ESign.Infrastructure.Configuration`)

```csharp
public sealed class MisaESignOptions
{
    // existing slice-1/2/3 properties unchanged
    public MisaESignWebhookOptions Webhook { get; set; } = new();
}

public sealed class MisaESignWebhookOptions
{
    public WebhookMode Mode { get; set; } = WebhookMode.Both;        // FR-093
    public MisaESignWebhookSessionOptions Session { get; set; } = new();
    public string? Path { get; set; } = "/esign/webhook";            // sample-API-only; FR-094
    public string? Secret { get; set; }                               // sample-API-only; FR-099
    public string[]? AllowedIps { get; set; }                         // sample-API-only; FR-100
}

public sealed class MisaESignWebhookSessionOptions
{
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(24);      // FR-093 / Assumption 4 / research R-3
}

public enum WebhookMode : byte { Polling = 1, Webhook = 2, Both = 3 }
```

## 7. Slice-1/2/3 surface changes (additive only)

Per FR-089 / FR-091, the existing public surfaces from slices 1+2+3 are byte-identical post-slice-4 except for:

| Type | Change | Compatibility |
|---|---|---|
| `IMisaESignClient.SignPdfAsync` / `SignXmlAsync` / `SignWordAsync` / `SignExcelAsync` | Added a FR-093 mode-guard: throws `InvalidOperationException("polling mode disabled")` when `MisaESignOptions.Webhook.Mode == Webhook`. | Source-compatible; behavior-compatible for the default `Mode = Both` and the legacy implicit `Mode = Polling`. Only consumers who explicitly set `Mode = Webhook` are affected, which is opt-in behavior. |
| (no other public-surface edits) | | |

## 8. CHANGELOG.md entry (under `[Unreleased]`)

```markdown
## MisaConnect.ESign — 2.0.0-preview.4 (slice 4 — webhook receiver)

### Added
- `IMisaESignClient.BeginSignPdfAsync` / `BeginSignXmlAsync` / `BeginSignWordAsync` / `BeginSignExcelAsync` — webhook-mode initiation facades per format.
- `IMisaESignClient.HandleWebhookAsync` — entry point for inbound MISA webhook deliveries.
- `Domain.Sessions.SigningSession` + `PerFormatHashPayload` + `SigningSessionCachedSuccess` — typed bookkeeping records.
- `Domain.Webhook.WebhookEnvelope` / `WebhookSignature` / `WebhookStatus` / `WebhookAck` / `WebhookOutcome` / `BeginResult` / `WebhookValidationCategory` — typed Domain models.
- `Domain.Errors.WebhookValidationException` + 5 sealed subclasses for typed validation failures.
- `Application.Abstractions.ISigningSessionStore` — new port with in-memory default adapter (`Infrastructure.Sessions.InMemorySigningSessionStore`).
- `Client.Webhook.IWebhookDeliveryHook` — consumer-implementable callback for successful finalizes.
- `MisaESignOptions.Webhook` sub-section: `Mode` (`Polling | Webhook | Both`, default `Both`), `Session.Ttl` (default `24:00:00`), sample-API `Path` / `Secret` / `AllowedIps`.
- Sample API: `samples/MisaConnect.Samples.Api/Endpoints/ESignWebhookEndpoint.cs` with configurable shared-secret URL segment + optional CIDR allowlist + startup WARN per FR-099/100/101.
- Per-format Client DTOs: `BeginSign{Pdf,Xml,Word,Excel}RequestDto` / `BeginSign{Pdf,Xml,Word,Excel}ResultDto` / `WebhookEnvelopeDto` / `WebhookAckDto` / `WebhookOutcomeDto` / `WebhookHandleResultDto`.

### Changed
- `Sign{Pdf,Xml,Word,Excel}Async` blocking facades now refuse to run when `Webhook.Mode == Webhook` (throws `InvalidOperationException`); behavior unchanged for the default `Mode = Both`.
- Internal refactoring: blocking orchestrators now compose `BeginSign{Format} → PollSignStatus → AttachSignature{Format}` for shared plumbing with the webhook path. Observable behavior is byte-identical (FR-089 / FR-091; verified by regression tests).

### Notes
- No new NuGet dependencies on the SDK or sample API. CIDR matching uses `System.Net.IPNetwork` (built into .NET 8).
- The sample API's webhook endpoint always returns HTTP `200` per FR-085; failure is reported via the ACK body's `errorCode`.
- Webhook registration with MISA is consumer-side, out-of-band per Assumption 5. The `[SandboxFact]` test requires `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL` to be configured AND MISA to be configured to POST to that URL.
```

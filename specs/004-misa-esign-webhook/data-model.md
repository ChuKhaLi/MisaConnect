# Phase 1 — Data Model: MISA eSign Webhook Receiver

This document catalogues the new Domain entities, value objects, ports, and DTOs slice 4 introduces, plus the state transitions that govern a `SigningSession`'s lifecycle and the webhook handler's validation pipeline. Slice 4 reuses every type from slices 1+2+3 — only the entries below are new (or are existing types receiving slice-4 additions).

---

## 1. Domain layer (`MisaConnect.ESign.Domain`)

### 1.1 `Domain.Sessions.SigningSession` (NEW record)

The authoritative bookkeeping unit for an in-flight webhook-mode sign. One record per `BeginSign{Format}Async` call.

```csharp
namespace MisaConnect.ESign.Domain.Sessions;

public sealed record SigningSession(
    string ClientId,                                  // matches Misa:ESign:ClientId on the session-creating process; part of the primary key
    string TransactionId,                             // MISA-issued, returned by /Signing/hash; part of the primary key
    DocumentFormat Format,                            // slice-3 enum {Unknown, Pdf, Xml, Word, Excel}; never Unknown for a Session
    PerFormatHashPayload HashPayload,                 // closed discriminated union; carries the bytes finalize needs
    IReadOnlyList<string> RecordedDocumentIds,        // the documentIds passed to /documents/hash; webhook.signatures[].documentId is matched against this
    DateTimeOffset CreatedAtUtc,                      // ISystemClock.UtcNow at registration; TTL is computed from this
    TimeSpan Ttl,                                     // snapshot of MisaESignOptions.Webhook.Session.Ttl at registration time
    IReadOnlySet<string> ObservedMessageIds,          // populated on every webhook delivery; immutable snapshot semantics — replace via .With(NewObserved)
    SigningSessionCachedSuccess? CachedSuccess);      // null until the first /documents/attachment succeeds; set exactly once
```

#### `PerFormatHashPayload` (closed discriminated union)

```csharp
public abstract record PerFormatHashPayload
{
    public sealed record Pdf(PdfHashOutput Output) : PerFormatHashPayload;
    public sealed record Xml(XmlHashOutput Output) : PerFormatHashPayload;
    public sealed record Word(WordExcelHashOutput Output) : PerFormatHashPayload;
    public sealed record Excel(WordExcelHashOutput Output) : PerFormatHashPayload;
}
```

The four `PdfHashOutput` / `XmlHashOutput` / `WordExcelHashOutput` types already exist in `MisaConnect.ESign.Application.Abstractions` (from slices 1+3). The discriminated wrapper lives in Domain because session records are Domain entities — the union forces all four formats to be represented uniformly and matches slice 3's closed-format convention.

#### `SigningSessionCachedSuccess`

```csharp
public sealed record SigningSessionCachedSuccess(
    string TriggeringMessageId,             // the messageId on the webhook delivery that completed the first successful finalize
    WebhookAck Ack,                         // the ACK returned to MISA on the first success; reused for all subsequent dupes
    byte[] SignedBytes);                    // the final signed document; not re-emitted to the delivery hook on dupes
```

Note: `SignedBytes` lives in Domain even though it's bulk data, because the cached success is part of the session identity and Constitution Principle I forbids Application reaching into Infrastructure for storage. The in-memory adapter holds the byte array directly; distributed adapters can serialize it (e.g. base64-encoded in Redis).

#### Lifecycle — see §4 below.

#### Equality

The record uses structural equality on all properties. The store's lookup key is `(ClientId, TransactionId)`; equality on the full record is for testing-helper convenience only.

#### Validation invariants

- `ClientId` non-empty.
- `TransactionId` non-empty.
- `Format != DocumentFormat.Unknown`.
- `HashPayload`'s discriminated case matches `Format` (e.g. `Format == Pdf` ⇒ `HashPayload is PerFormatHashPayload.Pdf`). Enforced at session-construction time; tests in `tests/MisaConnect.ESign.UnitTests/Sessions/SigningSessionEquivalenceTests.cs` lock this in.
- `RecordedDocumentIds` non-empty (every signing call records at least one document).
- `Ttl > TimeSpan.Zero`.
- `ObservedMessageIds` may be empty (pre-first-delivery).
- `CachedSuccess` is null pre-finalize and non-null post-finalize-success; once set, it never transitions back to null (success is permanent within the session lifetime).

---

### 1.2 `Domain.Webhook.WebhookEnvelope` (NEW record)

The inbound payload's typed representation. Constructed by the wire DTO's mapping extension; immutable.

```csharp
namespace MisaConnect.ESign.Domain.Webhook;

public sealed record WebhookEnvelope(
    string MessageId,                                          // MISA-generated; primary dedupe key (NOT session-lookup key)
    string ClientId,                                           // must match configured Misa:ESign:ClientId
    IReadOnlyDictionary<string, JsonElement>? ExtraData,       // opaque per Assumption 9
    WebhookStatus Status,                                      // {Success, Failed, Cancelled}
    string? ErrorCode,                                         // MISA-supplied error code; populated when Status != Success
    string TransactionId,                                      // session-lookup key (paired with ClientId)
    IReadOnlyList<WebhookSignature> Signatures);               // empty unless Status == Success
```

#### `WebhookSignature`

```csharp
public sealed record WebhookSignature(
    string DocumentId,        // matches one of SigningSession.RecordedDocumentIds
    string Signature);        // the raw signature payload; never logged
```

#### `WebhookStatus` (NEW enum)

```csharp
public enum WebhookStatus : byte
{
    Success = 1,
    Failed = 2,
    Cancelled = 3,
}
```

No `Unknown = 0` sentinel — an unparseable status fails the shape validator (FR-082 step 1) with `MalformedEnvelopeException` before the typed envelope is constructed.

---

### 1.3 `Domain.Webhook.WebhookAck` (NEW record)

The ACK returned to MISA. Field names mirror MISA §4.9 verbatim.

```csharp
public sealed record WebhookAck(
    string ErrorCode,        // "0" on success (per Assumption 6 / research R-3); "webhook.<category>" on failure
    string DevMsg,           // developer-facing message; safe to log (no secrets)
    string UserMsg);         // user-facing message; safe to display
```

Two factory constants live on the Infrastructure-layer `MisaWebhookAckCodes`:
- `MisaWebhookAckCodes.Success` (currently `"0"`; subject to Postman verification per R-3).
- The five failure categories map to namespaced codes:
  - `MalformedEnvelope` → `"webhook.malformed"`
  - `ClientIdMismatch` → `"webhook.client_id_mismatch"`
  - `UnknownTransaction` → `"webhook.unknown_transaction"`
  - `IncompleteSuccessEnvelope` → `"webhook.incomplete_success"`
  - `DocumentIdMismatch` → `"webhook.document_id_mismatch"`

The full mapping table lives in [contracts/error-mapping.md A.11](./contracts/error-mapping.md).

---

### 1.4 `Domain.Webhook.WebhookOutcome` (NEW discriminated union)

The result the consumer's `IWebhookDeliveryHook` receives. Closed hierarchy.

```csharp
public abstract record WebhookOutcome(string CorrelationId)
{
    public sealed record SuccessWithSignedBytes(
        string TransactionId,
        DocumentFormat Format,
        byte[] SignedBytes,
        string CorrelationId) : WebhookOutcome(CorrelationId);

    public sealed record FailureWithError(
        string? TransactionId,           // null when the failure is pre-session-resolution (Malformed / ClientIdMismatch)
        DocumentFormat Format,           // Unknown when pre-session-resolution
        WebhookValidationCategory Category,
        string? MisaErrorCode,           // null for SDK-synthesized failures; populated when MISA's envelope itself carried an errorCode
        string CorrelationId) : WebhookOutcome(CorrelationId);

    public sealed record TerminalWithoutFinalize(
        string TransactionId,
        DocumentFormat Format,
        WebhookStatus Status,            // Failed or Cancelled
        string? MisaErrorCode,
        string CorrelationId) : WebhookOutcome(CorrelationId);
}
```

The delivery hook is invoked once per webhook delivery on the `HandleWebhook` orchestrator's success path (FR-081 / FR-083). On idempotent short-circuit (cached success), the hook is NOT re-invoked per FR-080.

---

### 1.5 `Domain.Webhook.BeginResult` (NEW record)

The value returned by `BeginSign{Format}Async`.

```csharp
public sealed record BeginResult(
    string TransactionId,        // MISA-issued; the consumer correlates the future webhook against this
    DocumentFormat Format);      // matches the called facade
```

No `MessageId` field per spec (Clarifications Q1 / Assumption 8): MISA generates `messageId` server-side and the SDK first observes it on the inbound webhook.

---

### 1.6 `Domain.Webhook.WebhookValidationCategory` (NEW enum)

```csharp
public enum WebhookValidationCategory : byte
{
    None = 0,                            // sentinel; not used on the wire
    MalformedEnvelope = 1,
    ClientIdMismatch = 2,
    UnknownTransaction = 3,
    IncompleteSuccessEnvelope = 4,
    DocumentIdMismatch = 5,
}
```

Used by `WebhookOutcome.FailureWithError` and by the typed `WebhookValidationException` subclasses to discriminate failure type without string-matching.

---

### 1.7 `Domain.Errors.WebhookValidationException` family (NEW)

```csharp
public abstract class WebhookValidationException : ESignException
{
    public WebhookValidationCategory Category { get; }
    public string CorrelationId { get; }
    public string? MatchedTransactionId { get; }

    protected WebhookValidationException(
        WebhookValidationCategory category,
        string correlationId,
        string? matchedTransactionId,
        DocumentFormat format,
        string message)
        : base(message, format)
    {
        Category = category;
        CorrelationId = correlationId;
        MatchedTransactionId = matchedTransactionId;
    }
}

public sealed class MalformedEnvelopeException : WebhookValidationException { /* category = MalformedEnvelope, format = Unknown, matchedTxId = null */ }
public sealed class ClientIdMismatchException : WebhookValidationException { /* category = ClientIdMismatch, format = Unknown */ }
public sealed class UnknownTransactionException : WebhookValidationException { /* category = UnknownTransaction, format = Unknown, matchedTxId = the unmatched envelope txId */ }
public sealed class IncompleteSuccessEnvelopeException : WebhookValidationException { /* category = IncompleteSuccessEnvelope, format = resolved-from-session */ }
public sealed class DocumentIdMismatchException : WebhookValidationException { /* category = DocumentIdMismatch, format = resolved-from-session */ }
```

`ESignException` (the slice-1 base) already carries the `Format` property (slice-3 FR-062 addition). The validation exceptions extend it with the webhook-specific `Category` + `CorrelationId` + `MatchedTransactionId` properties.

---

## 2. Application layer (`MisaConnect.ESign.Application`)

### 2.1 `Application.Abstractions.ISigningSessionStore` (NEW port)

The only new port slice 4 introduces. See [research.md R-2](./research.md#r-2) for the design discussion.

```csharp
namespace MisaConnect.ESign.Application.Abstractions;

public interface ISigningSessionStore
{
    /// <summary>Registers a new session after /Signing/hash succeeds (FR-076).</summary>
    ValueTask RegisterAsync(SigningSession session, CancellationToken ct);

    /// <summary>Returns the session matching (clientId, transactionId), or null if no record exists or the record has expired per its TTL.</summary>
    ValueTask<SigningSession?> TryGetByTransactionIdAsync(string clientId, string transactionId, CancellationToken ct);

    /// <summary>Appends messageId to the session's ObservedMessageIds set. No-op if the messageId is already present.</summary>
    ValueTask RecordObservedMessageIdAsync(string clientId, string transactionId, string messageId, CancellationToken ct);

    /// <summary>Caches the success ACK + signed bytes on the session for idempotent dedupe (FR-080). Failures MUST NOT be cached (FR-081); there is no CacheFailure method.</summary>
    ValueTask CacheSuccessAsync(string clientId, string transactionId, string triggeringMessageId, WebhookAck ack, byte[] signedBytes, CancellationToken ct);
}
```

**Single-flight contract** (port docstring expands this): adapters MUST guarantee that for any `(clientId, transactionId)` pair, at most one in-flight `CacheSuccessAsync` call is permitted at a time. The `HandleWebhook` orchestrator relies on this to enforce FR-078 (single-flight finalize). The in-memory adapter implements this via a per-key `SemaphoreSlim`; distributed adapters use their native locking primitives (Redlock, `SELECT ... FOR UPDATE`, etc.).

---

### 2.2 New use cases under `Application.UseCases`

#### 2.2.1 `BeginSignPdf` / `BeginSignXml` / `BeginSignWord` / `BeginSignExcel` (NEW)

Each refactored from the existing `Sign{Format}` blocking orchestrator's pre-`/Signing/hash` half. Body shape (template; each format substitutes its own hash call):

```csharp
public sealed class BeginSignPdf
{
    public BeginSignPdf(
        EnsureAccessToken ensureAccessToken,
        ListActiveCertificates listActiveCertificates,
        ICertificateSelector certificateSelector,
        HashPdfDocument hashPdfDocument,
        SubmitSignHash submitSignHash,
        ISigningSessionStore sessionStore,             // NEW dependency vs. slice 1's SignPdf
        IOptions<MisaESignOptions> options,
        ISystemClock systemClock,
        ICorrelationIdAccessor correlationAccessor,
        ILogger<BeginSignPdf> logger) { … }

    public async Task<BeginResult> RunAsync(BeginSignPdfWorkRequest request, CancellationToken ct)
    {
        GuardModeIsNotPolling(options);                            // FR-093: throw "webhook mode disabled" when Mode == Polling
        var session = await ensureAccessToken.RunAsync(ct);
        var cert = certificateSelector.Select(await listActiveCertificates.RunAsync(session.AccessToken, ct));
        var hashOutput = await hashPdfDocument.RunAsync(session.AccessToken, cert, request.PdfBytes, request.DocumentId, request.SignatureInfo, ct);
        var signTx = await submitSignHash.RunAsync(session.AccessToken, cert, hashOutput, request.DataToBeDisplayed, request.DocumentName, ct);
        var sessionRecord = new SigningSession(
            ClientId: options.Value.ClientId,
            TransactionId: signTx.TransactionId,
            Format: DocumentFormat.Pdf,
            HashPayload: new PerFormatHashPayload.Pdf(hashOutput),
            RecordedDocumentIds: [request.DocumentId],
            CreatedAtUtc: systemClock.UtcNow,
            Ttl: options.Value.Webhook.Session.Ttl,
            ObservedMessageIds: new HashSet<string>(),
            CachedSuccess: null);
        await sessionStore.RegisterAsync(sessionRecord, ct);
        return new BeginResult(signTx.TransactionId, DocumentFormat.Pdf);
    }
}
```

Per-format equivalents (`BeginSignXml`, `BeginSignWord`, `BeginSignExcel`) substitute the format-specific hash use case + work-request shape + `PerFormatHashPayload` variant. Per research R-6, session registration happens AFTER `/Signing/hash` returns; no placeholder/update/evict dance.

#### 2.2.2 `HandleWebhook` (NEW)

The Application-layer entry point for inbound webhook deliveries. Used by the sample API (and any consumer-built endpoint).

```csharp
public sealed class HandleWebhook
{
    public HandleWebhook(
        WebhookEnvelopeValidator validator,
        ISigningSessionStore sessionStore,
        FinalizeFromWebhook finalizeFromWebhook,
        IWebhookDeliveryHook deliveryHook,
        IOptions<MisaESignOptions> options,
        ICorrelationIdAccessor correlationAccessor,
        ILogger<HandleWebhook> logger) { … }

    public async Task<WebhookHandleResult> HandleAsync(WebhookEnvelope envelope, CancellationToken ct)
    {
        var correlationId = correlationAccessor.GetOrGenerate();
        logger.LogWebhookReceived(correlationId, envelope.TransactionId, envelope.Status, MaskMessageId(envelope.MessageId));

        // FR-082 step 1: shape — already validated by deserialization (envelope is non-null)
        // FR-082 step 2: ClientId match
        validator.AssertClientIdMatch(envelope, options.Value.ClientId, correlationId);

        // FR-082 step 3: session existence
        var session = await sessionStore.TryGetByTransactionIdAsync(envelope.ClientId, envelope.TransactionId, ct)
            ?? throw new UnknownTransactionException(correlationId, envelope.TransactionId, DocumentFormat.Unknown, "Session not found");

        await sessionStore.RecordObservedMessageIdAsync(envelope.ClientId, envelope.TransactionId, envelope.MessageId, ct);

        // FR-082 step 4: status-specific shape
        if (envelope.Status == WebhookStatus.Success)
        {
            validator.AssertSuccessShapeIsValid(envelope, session, correlationId);
        }
        else
        {
            // FR-083: FAILED/CANCELLED short-circuit
            await deliveryHook.DeliverAsync(new WebhookOutcome.TerminalWithoutFinalize(envelope.TransactionId, session.Format, envelope.Status, envelope.ErrorCode, correlationId), ct);
            return new WebhookHandleResult(Ack: WebhookAck.Success(MisaWebhookAckCodes.Success), Outcome: new WebhookOutcome.TerminalWithoutFinalize(envelope.TransactionId, session.Format, envelope.Status, envelope.ErrorCode, correlationId));
        }

        // Single-flight finalize per (clientId, transactionId)
        // Re-check cache inside the lock
        if (session.CachedSuccess is { } cached)
        {
            logger.LogDuplicateDeliveryShortCircuit(correlationId, envelope.TransactionId, cached.Ack.ErrorCode, isMessageIdNew: !session.ObservedMessageIds.Contains(envelope.MessageId));
            return new WebhookHandleResult(Ack: cached.Ack, Outcome: new WebhookOutcome.SuccessWithSignedBytes(envelope.TransactionId, session.Format, cached.SignedBytes, correlationId));
        }

        return await finalizeFromWebhook.RunAsync(session, envelope, correlationId, ct);
    }
}
```

#### 2.2.3 `FinalizeFromWebhook` (NEW)

```csharp
public sealed class FinalizeFromWebhook
{
    public FinalizeFromWebhook(
        AttachSignaturePdf attachPdf,
        AttachSignatureToXml attachXml,
        AttachSignatureToWordExcel attachWordExcel,
        ISigningSessionStore sessionStore,
        IWebhookDeliveryHook deliveryHook,
        EnsureAccessToken ensureAccessToken,
        ListActiveCertificates listActiveCertificates,
        ICertificateSelector certificateSelector,
        IOptions<MisaESignOptions> options,
        ILogger<FinalizeFromWebhook> logger) { … }

    public async Task<WebhookHandleResult> RunAsync(SigningSession session, WebhookEnvelope envelope, string correlationId, CancellationToken ct)
    {
        // Acquire single-flight lock (delegated to the session store — see research R-5)
        await using var _ = await sessionStore.AcquireFinalizeLockAsync(envelope.ClientId, envelope.TransactionId, ct);

        // Re-check cache inside the lock
        var refreshed = await sessionStore.TryGetByTransactionIdAsync(envelope.ClientId, envelope.TransactionId, ct)
            ?? throw new UnknownTransactionException(correlationId, envelope.TransactionId, DocumentFormat.Unknown, "Session evicted while awaiting finalize lock");

        if (refreshed.CachedSuccess is { } cached)
        {
            return new WebhookHandleResult(Ack: cached.Ack, Outcome: new WebhookOutcome.SuccessWithSignedBytes(envelope.TransactionId, refreshed.Format, cached.SignedBytes, correlationId));
        }

        // Re-establish auth + cert for the outbound /documents/attachment call (slice 1's plumbing)
        var auth = await ensureAccessToken.RunAsync(ct);
        var cert = certificateSelector.Select(await listActiveCertificates.RunAsync(auth.AccessToken, ct));

        try
        {
            var signedBytes = refreshed.Format switch
            {
                DocumentFormat.Pdf => await attachPdf.RunAsync(auth.AccessToken, cert, ((PerFormatHashPayload.Pdf)refreshed.HashPayload).Output, envelope.Signatures.Single().Signature, ct),
                DocumentFormat.Xml => await attachXml.RunAsync(auth.AccessToken, cert, ((PerFormatHashPayload.Xml)refreshed.HashPayload).Output, envelope.Signatures.Single().Signature, ct),
                DocumentFormat.Word => await attachWordExcel.RunAsync(auth.AccessToken, cert, ((PerFormatHashPayload.Word)refreshed.HashPayload).Output, DocumentFormat.Word, envelope.Signatures.Single().Signature, ct),
                DocumentFormat.Excel => await attachWordExcel.RunAsync(auth.AccessToken, cert, ((PerFormatHashPayload.Excel)refreshed.HashPayload).Output, DocumentFormat.Excel, envelope.Signatures.Single().Signature, ct),
                _ => throw new InvalidOperationException("Unreachable — session.Format is closed"),
            };

            var ack = WebhookAck.Success(MisaWebhookAckCodes.Success);
            await sessionStore.CacheSuccessAsync(envelope.ClientId, envelope.TransactionId, envelope.MessageId, ack, signedBytes, ct);
            await deliveryHook.DeliverAsync(new WebhookOutcome.SuccessWithSignedBytes(envelope.TransactionId, refreshed.Format, signedBytes, correlationId), ct);
            logger.LogFinalizeSuccess(correlationId, envelope.TransactionId, refreshed.Format, signedBytes.Length);
            return new WebhookHandleResult(Ack: ack, Outcome: new WebhookOutcome.SuccessWithSignedBytes(envelope.TransactionId, refreshed.Format, signedBytes, correlationId));
        }
        catch (ESignException ex)
        {
            // FR-081: failure NOT cached; ACK returned to MISA; delivery hook NOT invoked
            logger.LogFinalizeFailure(correlationId, envelope.TransactionId, refreshed.Format, ex.GetType().Name);
            var failureAck = WebhookAck.Failure("webhook.finalize_failed", "Finalize attempt failed; MISA may retry.", "Signing finalization failed; please wait for retry.");
            return new WebhookHandleResult(Ack: failureAck, Outcome: new WebhookOutcome.FailureWithError(envelope.TransactionId, refreshed.Format, WebhookValidationCategory.None, MisaErrorCode: null, correlationId));
        }
    }
}
```

`AcquireFinalizeLockAsync` is an `internal` extension method on `ISigningSessionStore` realized as a method on the in-memory adapter; consumer-supplied distributed adapters MUST implement equivalent locking per the port contract.

#### 2.2.4 `Application.Validation.WebhookEnvelopeValidator` (NEW)

The four-step typed validator per FR-082. Static-ish helper class; no state.

```csharp
public sealed class WebhookEnvelopeValidator
{
    public void AssertClientIdMatch(WebhookEnvelope envelope, string configuredClientId, string correlationId)
    {
        if (!StringComparer.Ordinal.Equals(envelope.ClientId, configuredClientId))
        {
            throw new ClientIdMismatchException(correlationId, matchedTransactionId: null, DocumentFormat.Unknown, $"clientId '{envelope.ClientId}' does not match configured ClientId");
        }
    }

    public void AssertSuccessShapeIsValid(WebhookEnvelope envelope, SigningSession session, string correlationId)
    {
        if (envelope.Signatures.Count == 0)
        {
            throw new IncompleteSuccessEnvelopeException(correlationId, envelope.TransactionId, session.Format, "status=SUCCESS but signatures[] is empty");
        }
        var recordedIds = new HashSet<string>(session.RecordedDocumentIds, StringComparer.Ordinal);
        foreach (var sig in envelope.Signatures)
        {
            if (!recordedIds.Contains(sig.DocumentId))
            {
                throw new DocumentIdMismatchException(correlationId, envelope.TransactionId, session.Format, $"signatures[].documentId '{sig.DocumentId}' not in recorded session documents");
            }
        }
    }
}
```

Shape validation (step 1) happens earlier — at JSON deserialization time on the wire boundary. A null `WebhookEnvelope` or any required-field violation throws `MalformedEnvelopeException` before this validator runs.

---

### 2.3 New Application-layer abstractions

#### `Application.Webhook.IWebhookDeliveryHook` (NEW)

```csharp
public interface IWebhookDeliveryHook
{
    Task DeliverAsync(WebhookOutcome outcome, CancellationToken ct);
}
```

Consumers implement this; the SDK ships `NullWebhookDeliveryHook` as a default. See research R-9 for registration semantics.

---

## 3. Infrastructure layer (`MisaConnect.ESign.Infrastructure`)

### 3.1 `Infrastructure.ESign.Webhook.WebhookEnvelopeDto` (NEW wire DTO; internal)

Mirrors MISA §3.8 / §4.9 verbatim — camelCase field names per Constitution Principle IV.

```csharp
internal sealed record WebhookEnvelopeDto(
    [property: JsonPropertyName("messageId")] string MessageId,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("extraData")] Dictionary<string, JsonElement>? ExtraData,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("transactionId")] string TransactionId,
    [property: JsonPropertyName("signatures")] List<WebhookSignatureDto> Signatures)
{
    public WebhookEnvelope ToDomain() => new(
        MessageId, ClientId,
        ExtraData is null ? null : new ReadOnlyDictionary<string, JsonElement>(ExtraData),
        ParseStatus(Status),
        ErrorCode, TransactionId,
        Signatures.Select(s => new WebhookSignature(s.DocumentId, s.Signature)).ToList());

    private static WebhookStatus ParseStatus(string value) => value switch
    {
        "SUCCESS" => WebhookStatus.Success,
        "FAILED" => WebhookStatus.Failed,
        "CANCELLED" => WebhookStatus.Cancelled,
        _ => throw new MalformedEnvelopeException(/* correlationId provided by caller via ctor */),
    };
}

internal sealed record WebhookSignatureDto(
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("signature")] string Signature);
```

The public-surface DTO mirror is `Client.Dtos.Webhook.WebhookEnvelopeDto` (record with the same shape) for consumers that need to deserialize themselves or wire it into a typed minimal-API endpoint. The Infrastructure DTO is `internal` and used only by the SDK's wire-layer mapping.

### 3.2 `Infrastructure.ESign.Webhook.WebhookAckDto` (NEW wire DTO; internal)

Mirrors MISA §4.9 verbatim.

```csharp
internal sealed record WebhookAckDto(
    [property: JsonPropertyName("errorCode")] string ErrorCode,
    [property: JsonPropertyName("devMsg")] string DevMsg,
    [property: JsonPropertyName("userMsg")] string UserMsg)
{
    public static WebhookAckDto FromDomain(WebhookAck ack) => new(ack.ErrorCode, ack.DevMsg, ack.UserMsg);
}
```

### 3.3 `Infrastructure.Sessions.InMemorySigningSessionStore` (NEW adapter; internal)

```csharp
internal sealed class InMemorySigningSessionStore : ISigningSessionStore
{
    private readonly ConcurrentDictionary<(string ClientId, string TransactionId), SessionEntry> store = new();
    private readonly ISystemClock systemClock;
    private readonly ILogger<InMemorySigningSessionStore> logger;

    private sealed class SessionEntry
    {
        public SigningSession Record;
        public readonly SemaphoreSlim FinalizeLock = new(1, 1);

        public SessionEntry(SigningSession record) { Record = record; }
    }

    public ValueTask RegisterAsync(SigningSession session, CancellationToken ct)
    {
        store[(session.ClientId, session.TransactionId)] = new SessionEntry(session);
        return ValueTask.CompletedTask;
    }

    public ValueTask<SigningSession?> TryGetByTransactionIdAsync(string clientId, string transactionId, CancellationToken ct)
    {
        if (!store.TryGetValue((clientId, transactionId), out var entry))
        {
            return ValueTask.FromResult<SigningSession?>(null);
        }
        if (systemClock.UtcNow >= entry.Record.CreatedAtUtc + entry.Record.Ttl)
        {
            store.TryRemove((clientId, transactionId), out _);
            return ValueTask.FromResult<SigningSession?>(null);
        }
        return ValueTask.FromResult<SigningSession?>(entry.Record);
    }

    public ValueTask RecordObservedMessageIdAsync(string clientId, string transactionId, string messageId, CancellationToken ct)
    {
        if (!store.TryGetValue((clientId, transactionId), out var entry))
        {
            return ValueTask.CompletedTask; // already evicted; no-op
        }
        var current = entry.Record;
        var observed = new HashSet<string>(current.ObservedMessageIds, StringComparer.Ordinal) { messageId };
        entry.Record = current with { ObservedMessageIds = observed };
        return ValueTask.CompletedTask;
    }

    public ValueTask CacheSuccessAsync(string clientId, string transactionId, string triggeringMessageId, WebhookAck ack, byte[] signedBytes, CancellationToken ct)
    {
        if (!store.TryGetValue((clientId, transactionId), out var entry))
        {
            return ValueTask.CompletedTask;
        }
        entry.Record = entry.Record with { CachedSuccess = new SigningSessionCachedSuccess(triggeringMessageId, ack, signedBytes) };
        return ValueTask.CompletedTask;
    }

    // Extension surface for FinalizeFromWebhook's single-flight locking
    internal async ValueTask<IAsyncDisposable> AcquireFinalizeLockAsync(string clientId, string transactionId, CancellationToken ct)
    {
        if (!store.TryGetValue((clientId, transactionId), out var entry))
        {
            throw new UnknownTransactionException(/* correlationId from accessor */, transactionId, DocumentFormat.Unknown, "Session evicted before lock acquisition");
        }
        await entry.FinalizeLock.WaitAsync(ct);
        return new ReleaseLock(entry.FinalizeLock);
    }

    private sealed class ReleaseLock(SemaphoreSlim sem) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { sem.Release(); return ValueTask.CompletedTask; }
    }
}
```

The `AcquireFinalizeLockAsync` extension is internal; distributed adapters expose equivalent semantics via their own contract (the public `ISigningSessionStore` port's docstring documents the single-flight requirement without prescribing a primitive).

### 3.4 `Infrastructure.ESign.Webhook.MisaWebhookAckCodes` (NEW; internal)

```csharp
internal static class MisaWebhookAckCodes
{
    public const string Success = "0";   // per Assumption 6 / research R-3 — subject to Postman verification at tasks time
    public const string MalformedEnvelope = "webhook.malformed";
    public const string ClientIdMismatch = "webhook.client_id_mismatch";
    public const string UnknownTransaction = "webhook.unknown_transaction";
    public const string IncompleteSuccessEnvelope = "webhook.incomplete_success";
    public const string DocumentIdMismatch = "webhook.document_id_mismatch";
    public const string FinalizeFailed = "webhook.finalize_failed";
}
```

### 3.5 `Infrastructure.Configuration.MisaESignOptions` (EDITED)

Adds the `Webhook` sub-section.

```csharp
public sealed class MisaESignOptions
{
    // existing slice-1/2/3 properties unchanged
    public MisaESignWebhookOptions Webhook { get; set; } = new();   // NEW
}

public sealed class MisaESignWebhookOptions
{
    public WebhookMode Mode { get; set; } = WebhookMode.Both;       // FR-093 default
    public MisaESignWebhookSessionOptions Session { get; set; } = new();
    public string? Path { get; set; } = "/esign/webhook";           // sample-API-only; FR-094
    public string? Secret { get; set; }                              // sample-API-only; FR-099
    public string[]? AllowedIps { get; set; }                        // sample-API-only; FR-100
}

public sealed class MisaESignWebhookSessionOptions
{
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(24);     // FR-093 / Assumption 4
}

public enum WebhookMode : byte
{
    Polling = 1,
    Webhook = 2,
    Both = 3,
}
```

`MisaESignOptionsValidator` (existing, slice-1) gains startup-time validation rules:
- `Webhook.Session.Ttl > TimeSpan.Zero` (host fails fast on zero or negative).
- `Webhook.Secret` either null OR `Length >= 32` (FR-099; rejects shorter secrets).
- `Webhook.AllowedIps` entries each parse as valid CIDR via `IPNetwork.Parse(...)` (FR-100; rejects unparseable entries).
- `Webhook.Mode != Polling || Webhook.Secret == null && Webhook.AllowedIps is null/empty` — no warning here (the WARN is emitted by the sample API per FR-101, not by SDK validation).

---

## 4. State transitions

### 4.1 `SigningSession` lifecycle

```text
                              (consumer-supplied delivery
   ┌──────────────────┐       hook invoked, ACK cached)
   │   NOT_PRESENT    │ ◄──────────────────────────────────┐
   └────────┬─────────┘                                    │
            │ RegisterAsync                                │
            │ (after /Signing/hash returns;                │
            │  FR-076 / research R-6)                      │
            ▼                                              │
   ┌──────────────────┐                                    │
   │   PRE_FINALIZE   │                                    │
   │ (no CachedSuccess)│                                   │
   └────────┬─────────┘                                    │
            │                                              │
            │ webhook arrives → HandleWebhook              │
            │ → validation OK                              │
            │ → finalize attempted                         │
            ▼                                              │
   ┌──────────────────────────┐                            │
   │  FINALIZE_IN_FLIGHT      │  ◄────────┐                │
   │  (per-key SemaphoreSlim  │           │                │
   │   held by 1 caller;      │           │ concurrent     │
   │   others wait per FR-078)│           │ deliveries     │
   └────────┬─────────┬───────┘           │ await          │
            │         │                   │                │
            │         │                   │                │
   on success│         │ on failure       │                │
            ▼         ▼                   │                │
   ┌────────────────┐ ┌────────────────┐  │                │
   │   SUCCESS_     │ │  PRE_FINALIZE  │──┘                │
   │   CACHED       │ │  (unchanged;   │                   │
   │ (CachedSuccess │ │   no failure   │                   │
   │  set; ACK +    │ │   cached per   │                   │
   │  signedBytes)  │ │   FR-081;      │                   │
   └────────┬───────┘ │   delivery hook│                   │
            │         │   NOT invoked) │                   │
            │         └────────┬───────┘                   │
            │                  │                           │
            │                  │ MISA retries delivery;    │
            │                  │ session remains live      │
            │                  │ within Ttl                │
            │                  └───────────────────────────┤
            │                                              │
            │ duplicate delivery → cache hit;              │
            │ short-circuit; ACK returned;                 │
            │ delivery hook NOT re-invoked                 │
            │                                              │
            ▼                                              │
   ┌──────────────────────┐                                │
   │  TERMINAL (CACHED)   │ TTL elapses → NOT_PRESENT  ────┘
   └──────────────────────┘ (on next read, store evicts)
```

State invariants:

| State | `CachedSuccess` | Delivery hook invoked? | Reachable from |
|---|---|---|---|
| `NOT_PRESENT` | n/a | n/a | initial; post-eviction |
| `PRE_FINALIZE` | `null` | no | post-`RegisterAsync` |
| `FINALIZE_IN_FLIGHT` | `null` | no | post-webhook-arrival, lock acquired |
| `SUCCESS_CACHED` | non-null | yes (exactly once at transition) | post-finalize-success |
| `TERMINAL (CACHED)` | non-null | no (subsequent dupes short-circuit) | post-`SUCCESS_CACHED`, all subsequent dupes |

`FAILED` and `CANCELLED` MISA statuses do NOT advance the session state past `PRE_FINALIZE` (no finalize is attempted per FR-083). The delivery hook is invoked with a `TerminalWithoutFinalize` outcome, and the session record stays in `PRE_FINALIZE` until TTL evicts it.

### 4.2 Webhook envelope validation pipeline (FR-082)

```text
inbound POST
    │
    ▼
[shape validation: deserialize WebhookEnvelopeDto, assert required fields, parse status]
    │ fail → throw MalformedEnvelopeException
    ▼
[clientId match: envelope.ClientId == options.Value.ClientId]
    │ fail → throw ClientIdMismatchException
    ▼
[session lookup: ISigningSessionStore.TryGetByTransactionIdAsync(envelope.ClientId, envelope.TransactionId)]
    │ null → throw UnknownTransactionException
    ▼
[record observed messageId on session — happens always, even on later failures]
    │
    ▼
[status branch]
    │
    ├── status == SUCCESS
    │      │
    │      ▼
    │   [signatures[] non-empty]
    │      │ fail → throw IncompleteSuccessEnvelopeException
    │      ▼
    │   [every signatures[].documentId in session.RecordedDocumentIds]
    │      │ fail → throw DocumentIdMismatchException
    │      ▼
    │   [single-flight finalize via AcquireFinalizeLockAsync]
    │      │
    │      ▼
    │   [re-check session.CachedSuccess inside lock]
    │      ├── cached → return cached ACK + outcome.SuccessWithSignedBytes; delivery hook NOT invoked
    │      └── not cached → finalize via AttachSignature{Format}
    │          │
    │          ├── success → cache success ACK + signed bytes; invoke delivery hook with outcome.SuccessWithSignedBytes; return success ACK
    │          └── failure → return failure ACK; do NOT cache; do NOT invoke delivery hook
    │
    └── status == FAILED || CANCELLED
           │
           ▼
        [no finalize]
           │
           ▼
        [invoke delivery hook with outcome.TerminalWithoutFinalize]
           │
           ▼
        [return success ACK to MISA per FR-083]
```

### 4.3 Mode-guard enforcement (FR-093)

| Mode | `BeginSign{Format}Async` | `Sign{Format}Async` (blocking) |
|---|---|---|
| `Polling` (default-off) | throws `"webhook mode disabled"` | works |
| `Webhook` | works | throws `"polling mode disabled"` |
| `Both` (default) | works | works |

Guards are enforced at the top of each facade method (after argument validation, before `EnsureAccessToken`).

---

## 5. Wire-format mapping summary

The slice's only new wire shapes are inbound (consumer-side):

| Direction | Endpoint | Wire DTO | Domain type | Spec ref |
|---|---|---|---|---|
| Inbound to sample API | `POST {Path}/{secret?}` | `Infrastructure.ESign.Webhook.WebhookEnvelopeDto` | `Domain.Webhook.WebhookEnvelope` | MISA §3.8 / §4.9 |
| Outbound from sample API to MISA | (HTTP body of the above response) | `Infrastructure.ESign.Webhook.WebhookAckDto` | `Domain.Webhook.WebhookAck` | MISA §4.9 |

The slice does NOT change the existing outbound wire shapes for `/Signing/hash` / `/Signing/status` (which the webhook path skips) / `/documents/attachment` (which the webhook path reuses verbatim from slice 3's `FR-056`).

---

## 6. Public-surface delta summary

See [contracts/public-surface.md](./contracts/public-surface.md) for the full catalogue. Cross-reference table:

| Public type | Layer | Purpose |
|---|---|---|
| `IMisaESignClient.BeginSignPdfAsync` / `BeginSignXmlAsync` / `BeginSignWordAsync` / `BeginSignExcelAsync` | Client | Initiate webhook-mode sign per format (FR-075) |
| `IMisaESignClient.HandleWebhookAsync` | Client | Entry point for the consumer's HTTP framework |
| `BeginSign{Pdf,Xml,Word,Excel}RequestDto` | Client.Dtos.Webhook | Per-format Begin request shape (mirrors blocking-path DTOs) |
| `BeginSign{Pdf,Xml,Word,Excel}ResultDto` | Client.Dtos.Webhook | `{ TransactionId, Format }` |
| `WebhookEnvelopeDto` / `WebhookSignatureDto` / `WebhookStatusDto` / `WebhookAckDto` / `WebhookOutcomeDto` / `WebhookHandleResultDto` | Client.Dtos.Webhook | Public DTOs for the consumer's HTTP wiring + delivery hook |
| `IWebhookDeliveryHook` | Client.Webhook | Consumer-implemented callback |
| `Domain.Sessions.SigningSession` + `PerFormatHashPayload` + `SigningSessionCachedSuccess` | Domain | Session record + closed-format hash payload + cached-success snapshot |
| `Domain.Webhook.WebhookEnvelope` + `WebhookSignature` + `WebhookStatus` + `WebhookAck` + `WebhookOutcome` + `BeginResult` + `WebhookValidationCategory` | Domain | Typed Domain models |
| `Domain.Errors.WebhookValidationException` + 5 sealed subclasses | Domain | Typed exception family for validation failures |
| `ISigningSessionStore` | Application | The single new port |
| `MisaESignOptions.Webhook` + `MisaESignWebhookOptions` + `MisaESignWebhookSessionOptions` + `WebhookMode` | Infrastructure (DI-touching) | New configuration sub-section |

All additions are semver-additive — slice 4 ships as `2.0.0-preview.4`.

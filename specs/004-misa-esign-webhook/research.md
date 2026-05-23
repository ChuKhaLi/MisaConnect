# Phase 0 — Research: MISA eSign Webhook Receiver

This document resolves the open questions left by the Technical Context above. Spec-level clarifications (MISA-generated `messageId`, sample-API shared-secret URL + CIDR allowlist, failure-not-cached, default `Session.Ttl = 24h`) landed in [spec.md §Clarifications](./spec.md#clarifications) on 2026-05-23; the entries below pick up where the spec stopped.

Each entry follows: **Decision** → **Rationale** → **Alternatives considered**.

---

## R-1. Two-step orchestrator factoring — refactor `SignPdf` / `SignXml` / `SignWord` / `SignExcel` into `BeginSign{Format}` + finalize?

**Decision**: Yes. Each existing slice-1 / slice-3 blocking orchestrator (`Application.UseCases.SignPdf`, `SignXml`, `SignWord`, `SignExcel`) is refactored into a composition of three steps:

1. `BeginSign{Format}` — runs login → cert select → format-specific hash → `RegisterAsync` on `ISigningSessionStore` → submit `/Signing/hash`. Returns `Domain.Webhook.BeginResult { TransactionId, Format }`. This is the new use case slice 4 introduces.
2. `PollSignStatus` — slice 1's existing polling state machine. Unchanged.
3. `AttachSignature{Format}` — slice 3's existing per-format finalize. Unchanged.

The blocking orchestrators become thin compositions: `Sign{Format} = BeginSign{Format} → PollSignStatus → AttachSignature{Format}`. The webhook-mode path is `BeginSign{Format}` (returning early) + the webhook handler running `AttachSignature{Format}` later via `FinalizeFromWebhook`.

**Rationale**:
- 100% plumbing reuse between blocking-mode and webhook-mode signing. Auth, token cache, cert selection, transport retry, single-flight refresh, correlation-ID propagation, error mapping, log scrubbing — all live in the shared `BeginSign{Format}` and `AttachSignature{Format}` use cases. The webhook path inherits everything without duplication.
- FR-076's "session MUST be registered BEFORE `/Signing/hash` is issued" cleanly belongs inside `BeginSign{Format}` — there's exactly one place to enforce the ordering invariant, and the blocking orchestrator inherits it (registering a session it never reads is harmless and matches the cross-mode bookkeeping spec calls out in Edge Case "Consumer mixes polling and webhook").
- FR-089 / FR-091 (slice-1 / slice-3 byte-identical behavior) are verified by re-running the existing integration tests unchanged. The factoring is an internal refactor with no observable surface change.

Wait — does the blocking path actually need to register a `SigningSession`? The spec's Edge Case "Consumer mixes polling and webhook for the same `transactionId`" says the slice-1 polling facade does NOT register a webhook session. So the factoring needs a flag: `BeginSign{Format}` takes a `RegisterSession: bool` parameter, set to `true` when called from the webhook-mode `BeginSign{Format}Async` facade and `false` when called from the blocking `Sign{Format}Async` facade. The blocking path bypasses `ISigningSessionStore` entirely; the webhook path runs through it. This keeps the cross-mode "webhook for polling-initiated tx → `UnknownTransaction`" semantics correct (FR-077).

Adjustment: `BeginSignPdf.RegisterSession` is a constructor-time mode flag, not a per-call argument — `Application.UseCases` registers two DI instances (or one parameterized at registration time). The simpler shape is two thin orchestrator pairs: `BeginSignPdfForPolling` (no `ISigningSessionStore` interaction) and `BeginSignPdfForWebhook` (with `ISigningSessionStore` interaction), composed via a shared private `BeginSignPdfCore` helper. The blocking `SignPdf` orchestrator composes `BeginSignPdfForPolling → PollSignStatus → AttachSignaturePdf`. The webhook-mode `BeginSignPdfAsync` facade calls `BeginSignPdfForWebhook` and returns.

**Alternatives considered**:
- *Keep `Sign{Format}` monolithic; the webhook path duplicates auth + cert select + hash + submit*: rejected — duplicates ~150 LoC per format (600 LoC total) with no benefit; any future change to the pre-`/Signing/hash` pipeline would need to be applied twice.
- *Have the webhook handler call `Sign{Format}` itself with a short-circuit when no polling is needed*: rejected — Sign{Format} returns signed bytes synchronously; rewiring it to "return early after `/Signing/hash`" would require the blocking path to thread that mode in too, polluting the slice-1 surface for no consumer benefit.
- *Introduce a generic `BeginSign<TFormat>` use case (one type, four parameterizations)*: rejected per slice 3's `R-1` — generic orchestrators trade clarity for tiny amounts of duplication. Four named use cases match the format-per-method convention slice 1+3 established and stay easy to audit.

---

## R-2. `ISigningSessionStore` adapter shape — what method set does the port expose?

**Decision**: The port exposes exactly four methods, all `ValueTask`-returning to keep the in-memory adapter allocation-free:

```csharp
internal interface ISigningSessionStore  // public per Principle II's port carve-out
{
    ValueTask RegisterAsync(SigningSession session, CancellationToken ct);
    ValueTask<SigningSession?> TryGetByTransactionIdAsync(string clientId, string transactionId, CancellationToken ct);
    ValueTask RecordObservedMessageIdAsync(string clientId, string transactionId, string messageId, CancellationToken ct);
    ValueTask CacheSuccessAsync(string clientId, string transactionId, string triggeringMessageId, WebhookAck ack, byte[] signedBytes, CancellationToken ct);
}
```

The store also internally owns a single-flight semaphore per `(clientId, transactionId)` key — this is NOT exposed on the port interface; it is a contract that the adapter implements internally per FR-078. Consumer-supplied distributed adapters (Redis, SQL) must replicate the single-flight guarantee (e.g. via `SET NX` or a row-level lock). The port's docstring spells this out.

**Rationale**:
- Each method is the natural unit of work the webhook handler needs. Conflating them (e.g. one `ProcessWebhookAsync(session, envelope, finalizer)` method that calls back into the handler) couples the port too tightly to the handler's control flow and makes it impossible to test the store in isolation.
- `RecordObservedMessageIdAsync` is separate from `CacheSuccessAsync` because the handler records the observed `messageId` on every delivery (including failed-finalize deliveries) but caches the success ACK only when finalize succeeds (per FR-080 / FR-081). Two methods, two responsibilities.
- No `EvictAsync` method on the port — TTL eviction is the in-memory adapter's responsibility (read-time eviction; the port contract says "lookups MAY return null for an expired entry"). Consumer-supplied distributed adapters handle their own eviction (Redis TTL, SQL job, etc.).
- `ValueTask` over `Task` because the in-memory adapter's most common path (lookup hit) is synchronous; `ValueTask` avoids the allocation. Consumer-supplied async adapters return a `Task`-wrapped `ValueTask` with no penalty.

**Alternatives considered**:
- *One method `ReconcileAsync(envelope, finalizer)` that calls back into the finalize function*: rejected — couples store to handler control-flow; impossible to unit-test the store without the handler.
- *Split into `ISigningSessionWriter` + `ISigningSessionReader`*: rejected — premature CQRS; no consumer asked for write-only or read-only adapters; one port matches the operational reality (every adapter implements all four methods).
- *Expose the per-key semaphore as a port-level concern (`AcquireFinalizeLockAsync` etc.)*: rejected — leaks adapter-specific concurrency primitives onto the port; makes it impossible to implement on a distributed store that uses different locking semantics (Redlock, etc.). The contract says "single-flight per key"; how each adapter achieves that is its own business.

---

## R-3. Success ACK `errorCode` sentinel — confirm `"0"` is the right value?

**Decision**: Use `"0"` as the success-ACK `errorCode` per the spec's Assumption 6, **subject to Postman-collection verification before tasks land**. The implementation contract publishes the value as a single constant `MisaWebhookAckCodes.Success = "0"` in the Infrastructure layer so any future correction is a single-character change.

The MISA API doc (`docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md`) does NOT enumerate canonical ACK codes in §4.9; it only describes the envelope shape `{ errorCode, devMsg, userMsg }`. The Postman collection at `https://drive.usercontent.google.com/u/0/uc?id=1E4GNoFrsU5UDModXx10lzm8c4jegggHn&export=download` (per the parent plan's tie-breaker rule) is the authoritative source — if it shows a different success sentinel (e.g. empty string, `null`, `"OK"`), the implementation reconciles at tasks-generation time.

**Rationale**:
- `"0"` matches the success-code convention used throughout MISA's eInvoice docs (the existing `MisaConnect.EInvoice` package treats `errorCode == "0"` as success consistently) and is the most likely value MISA picked for eSign.
- Centralizing the sentinel in a single constant means any future change is a one-line patch, not a code-search-and-replace.
- The failure-ACK `errorCode` values use a namespaced format (`webhook.client_id_mismatch`, `webhook.unknown_transaction`, `webhook.malformed`, etc.) per Assumption 6. These are namespaced to ensure they cannot collide with any legitimate MISA-canonical code (MISA's codes are flat integers or short strings; the `webhook.` prefix is reserved for SDK-synthesized values).

**Alternatives considered**:
- *Empty string `""` as success*: rejected — no evidence either way; `"0"` matches MISA's other surface conventions.
- *`null` errorCode for success*: rejected — `null` in JSON is semantically distinct from "no error" in MISA's other APIs and would risk MISA's parser rejecting the ACK.
- *Use the documented `Sign_status` enum value (`SUCCESS`) from §4.8 as the success errorCode*: rejected — confuses status (a property of the signing transaction) with errorCode (a property of the ACK envelope's outcome). Different semantic spaces.

**Open**: If the Postman collection contradicts this, update the `MisaWebhookAckCodes.Success` constant and re-run unit tests; no other change is required.

---

## R-4. Sample-API HTTP-layer auth — how to mount the optional secret-bearing route?

**Decision**: The sample API uses **ASP.NET Core 8's optional route parameter** with a runtime-side enforcement layer:

```csharp
// In Endpoints/ESignWebhookEndpoint.cs:
public static IEndpointRouteBuilder MapESignWebhookEndpoint(this IEndpointRouteBuilder app, IConfiguration configuration)
{
    var webhookOptions = configuration.GetSection("Misa:ESign:Webhook").Get<MisaESignWebhookOptions>() ?? new();
    var basePath = webhookOptions.Path ?? "/esign/webhook";

    if (!string.IsNullOrEmpty(webhookOptions.Secret))
    {
        // Mount ONLY the secret-bearing route — the bare basePath returns 404 by default
        app.MapPost(basePath + "/{secret}", HandleAsync)
           .WithMetadata(new WebhookSecretMetadata(webhookOptions.Secret));
    }
    else
    {
        app.MapPost(basePath, HandleAsync);
    }

    return app;
}

private static async Task<IResult> HandleAsync(HttpContext context, IMisaESignClient client, IOptions<MisaESignOptions> options, [FromBody] WebhookEnvelopeDto envelope, CancellationToken ct)
{
    // Step 1: validate the secret if metadata is present
    var secretMetadata = context.GetEndpoint()?.Metadata.GetMetadata<WebhookSecretMetadata>();
    if (secretMetadata is not null)
    {
        var routeSecret = context.GetRouteValue("secret") as string;
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(routeSecret ?? string.Empty),
            Encoding.UTF8.GetBytes(secretMetadata.Secret)))
        {
            // Route matched but secret mismatch — return 404 to keep the URL un-enumerable
            return Results.NotFound();
        }
    }

    // Step 2: validate the CIDR allowlist if configured
    var allowedIps = options.Value.Webhook?.AllowedIps;
    if (allowedIps is { Length: > 0 })
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null || !IsInAllowlist(remoteIp, allowedIps))
        {
            LogAllowlistRejection(context, remoteIp);
            return Results.Forbid();
        }
    }

    // Step 3: invoke the SDK
    var result = await client.HandleWebhookAsync(envelope, ct);
    return Results.Json(result.Ack);
}
```

`CryptographicOperations.FixedTimeEquals` (built into .NET 8) eliminates timing-attack surface on the secret comparison even though the route segment already gates access. The 404-on-mismatch (rather than 401/403) preserves the spec's "secret URL un-enumerable" property (FR-099): attackers can't distinguish "wrong secret" from "no webhook endpoint at this path".

For the CIDR allowlist, the sample uses `System.Net.IPNetwork` (built into .NET 8 — `IPNetwork.Parse("10.0.0.0/8")` returns an `IPNetwork` whose `Contains(IPAddress)` method does the bitwise comparison). No third-party dependency.

For the IP source: the sample reads `context.Connection.RemoteIpAddress` unmodified; consumers running the sample behind a reverse proxy MUST configure ASP.NET's `ForwardedHeadersOptions` themselves (per FR-100). The sample API's `Program.cs` does NOT auto-enable `UseForwardedHeaders` because the trust-boundary configuration is consumer-specific.

**Rationale**:
- Route-segment-based secret keeps the routing layer doing the routing (the `404` is implicit when the secret-bearing route doesn't match) without an additional middleware layer.
- `FixedTimeEquals` guards against the theoretical timing-attack vector even though it's a low-priority concern at this layer.
- Reusing `System.Net.IPNetwork` over a third-party CIDR library (NetTools.IPAddressRange, IPNetwork2, etc.) keeps the "no new NuGet deps" promise from Technical Context.
- The 403-on-IP-rejection + structured-WARN-log-with-IP-class pattern matches Stripe/GitHub conventions and keeps the consumer-side audit trail useful without leaking remote IPs at INFO/DEBUG.

**Alternatives considered**:
- *Header-based secret (`X-Webhook-Secret`)*: rejected — MISA's doc doesn't document a header-shipping mechanism, and forging a header is trivially easy compared to enumerating a URL. URL-segment secret has the same security properties but matches how Stripe / Twilio / GitHub manage webhook secrets when no signature header is available.
- *ASP.NET middleware that validates the secret + IP before routing*: rejected — more code than the route-segment approach, more layers to debug, and harder to test in isolation. The route-segment approach naturally fails-closed (no route → no handler).
- *Third-party CIDR library (NetTools.IPAddressRange)*: rejected — `System.Net.IPNetwork` in .NET 8 does everything needed (handles both IPv4 and IPv6, parses standard CIDR notation). No external dep needed.

---

## R-5. Concurrent webhook deliveries — how to coordinate single-flight finalize per `(clientId, transactionId)`?

**Decision**: The `InMemorySigningSessionStore` owns one `SemaphoreSlim(1, 1)` per session record, stored as a non-public field on the session entry. The `HandleWebhook` orchestrator's finalize path acquires the semaphore around the `AttachSignature{Format}` call:

```csharp
// Inside HandleWebhook.HandleAsync(...) after session resolution:
var session = await sessionStore.TryGetByTransactionIdAsync(envelope.ClientId, envelope.TransactionId, ct);
if (session is null) throw new UnknownTransactionException(...);

await sessionStore.RecordObservedMessageIdAsync(envelope.ClientId, envelope.TransactionId, envelope.MessageId, ct);

// Single-flight finalize via the store's per-key semaphore (see ISigningSessionStore.GetFinalizeLockAsync — internal extension)
using (await sessionStore.AcquireFinalizeLockAsync(envelope.ClientId, envelope.TransactionId, ct))
{
    // Re-check cache inside the lock — a concurrent delivery may have just finalized
    var refreshed = await sessionStore.TryGetByTransactionIdAsync(envelope.ClientId, envelope.TransactionId, ct);
    if (refreshed?.CachedSuccess is { } cached)
    {
        return new WebhookHandleResult(Ack: cached.Ack, Outcome: WebhookOutcome.SuccessWithSignedBytes(...));
    }

    // Finalize for real
    var attachmentResult = await finalizeFromWebhook.RunAsync(refreshed!, envelope, ct);
    return attachmentResult;
}
```

The `AcquireFinalizeLockAsync` extension is an internal contract between the orchestrator and the in-memory adapter. Consumer-supplied distributed adapters (Redis, SQL) implement equivalent locking via their native primitives (Redlock, `SELECT ... FOR UPDATE`, etc.); the port docstring documents the single-flight requirement clearly.

**Rationale**:
- Per-key `SemaphoreSlim` matches the slice-1 single-flight-refresh pattern (`SingleFlightRefresh` from slice 1 uses the same shape).
- Re-checking the cache inside the lock handles the race where two parallel deliveries arrive, both miss the cache, both wait on the semaphore, the first finalizes + caches, and the second now needs to short-circuit without re-finalizing.
- The `using` block guarantees the semaphore is released even on exception, so a finalize that throws doesn't leak a stuck lock.
- The semaphore lives on the in-memory adapter, not on the Domain `SigningSession` record — the record stays a pure data shape (per Principle I; Domain has no concurrency primitives).

**Alternatives considered**:
- *One global lock around all finalize calls*: rejected — serializes every webhook handler invocation, killing throughput.
- *`ConcurrentDictionary.GetOrAdd` with an `AsyncLazy<...>`-style memoizer*: works but requires an extra allocation per session and a custom `AsyncLazy` helper; `SemaphoreSlim` is the simpler primitive when the work isn't naturally a one-shot lazy-init.
- *Optimistic-concurrency via ETag on `CacheSuccessAsync`*: works for distributed adapters but adds complexity (callers must retry on conflict); single-flight via lock is simpler and matches the SDK's existing pattern.

---

## R-6. What does `BeginSign{Format}Async` return when MISA's `/Signing/hash` succeeds but the SDK's session-register call fails?

**Decision**: If `ISigningSessionStore.RegisterAsync` throws or returns a transport error (e.g. distributed adapter is unreachable), the `BeginSign{Format}Async` orchestrator MUST NOT proceed to `/Signing/hash`. The session-register happens BEFORE the `/Signing/hash` call per FR-076 — so a register-failure surfaces as a typed `SessionStoreException` to the consumer, no MISA transaction is created, and there's no cleanup to do.

If, hypothetically, `RegisterAsync` succeeded but `/Signing/hash` then failed, the orchestrator MUST evict the pre-registered session via `RemoveAsync(clientId, transactionId)` (note: no `transactionId` is known yet — MISA hasn't issued one — so the eviction key is actually the pre-registration token; this is a known awkwardness handled by registering with a sentinel `TransactionId = ""` and updating it post-`/Signing/hash`).

Cleanest shape: the session is registered with a placeholder, then `UpdateTransactionIdAsync(clientId, sessionId, transactionId)` is called after `/Signing/hash` succeeds. On `/Signing/hash` failure, the placeholder is evicted via `EvictPlaceholderAsync(clientId, sessionId)`. This adds two methods to the port (`UpdateTransactionIdAsync` + `EvictPlaceholderAsync`).

Wait — this is over-engineered for a case that's vanishingly rare. Alternative shape: register the session AFTER `/Signing/hash` returns, accepting the tiny race window where a webhook arriving during the SDK's `/Signing/hash` response processing would see `UnknownTransaction`. The race is bounded by the time between MISA returning the `/Signing/hash` response and the SDK calling `RegisterAsync` — typically microseconds in-process. The spec's "Webhook arrives before BeginSignPdfAsync returns" edge case already documents this: "If the webhook still arrives before the session is recorded, it is treated as `UnknownTransaction` and the consumer is expected to redeliver via MISA's natural retry."

**Decision (revised)**: Register the session **AFTER** `/Signing/hash` returns successfully. The ISigningSessionStore port stays at four methods (no placeholder/update/evict trio needed). The race window is acceptable because (a) it's measured in microseconds; (b) the spec already documents `UnknownTransaction` as the correct response in that window; (c) MISA's natural webhook retry will resolve it in the next delivery.

**Rationale**:
- Eliminates the placeholder bookkeeping complexity entirely.
- Matches the spec's documented race-window semantics.
- Trades a microsecond-scale failure mode (very rare) for a vastly simpler port surface.
- If a future requirement demands zero-race-window registration, a port extension can be added without breaking existing consumers.

**Alternatives considered**:
- *Pre-register with a placeholder transactionId, then update*: rejected per above — over-engineered for an edge case the spec already accepts.
- *Synchronous register at the top of `BeginSign{Format}` before login*: rejected — session needs the `transactionId` to be useful, and `transactionId` is only known after `/Signing/hash`.

---

## R-7. Re-validating Assumption 11 — does the Postman collection reveal a MISA-side webhook signature header?

**Decision**: Tasks-time pre-implementation check: **download the Postman collection** at `https://drive.usercontent.google.com/u/0/uc?id=1E4GNoFrsU5UDModXx10lzm8c4jegggHn&export=download`, grep it for `signature`, `hmac`, `x-misa-signature`, or any other inbound-header convention on a `/webhook`-like endpoint. If found, the implementation adds an `IWebhookSignatureVerifier` port and the corresponding validation step (step 1.5 in FR-082, after shape validation and before clientId check). If not found, Assumption 11 stands as-is and the sample-API auth layers (FR-099 / FR-100) remain the only transport-layer defense.

**Rationale**:
- The parent plan's tie-breaker rule says the Postman collection is authoritative when the doc and wire disagree. The webhook spec is exactly such a case — the doc says nothing about authentication, but the Postman collection might.
- Adding `IWebhookSignatureVerifier` later (if discovered) is a semver-additive change and won't break consumers of this slice.

**Open**: Run this check during `/speckit-tasks` generation; record the finding inline in the task that creates the `WebhookEnvelopeValidator`.

---

## R-8. JSON deserialization of `extraData` — preserve as `JsonElement`, `JsonObject`, or `Dictionary<string, object?>`?

**Decision**: Preserve `extraData` as `IReadOnlyDictionary<string, JsonElement>` on the public `WebhookEnvelopeDto`. The `JsonElement` value type lets consumers introspect heterogeneous content (strings, numbers, nested objects, arrays) without the SDK guessing at a CLR shape.

**Rationale**:
- `extraData` is opaque per Assumption 9 — the SDK doesn't know what MISA puts there. `JsonElement` is the lowest-friction "I have some JSON but I don't want to decide what shape it is" type in `System.Text.Json`.
- `JsonObject` would force a node-tree allocation per delivery; `JsonElement` is a struct over the original document's byte range.
- `Dictionary<string, object?>` would force the SDK to deserialize into CLR types (string vs number vs nested) before forwarding, which both costs perf and impose a "what does a JSON number become?" decision the SDK shouldn't make.
- Consumers needing typed access can `.Deserialize<TMyShape>()` on the `JsonElement` themselves.

**Alternatives considered**:
- *Raw `string` of the JSON sub-document*: rejected — pushes parsing onto the consumer twice (once when their HTTP framework deserialized the envelope, once when they parse `extraData`).
- *`JsonObject` (the writable variant)*: rejected — mutable types on a webhook DTO is wrong (the consumer shouldn't be able to "edit" what MISA sent); also costs allocation.
- *`object?` typed as `Dictionary<string, object?>` after `System.Text.Json` deserialization*: rejected — pushes a low-quality CLR-typing decision into the wire layer.

---

## R-9. How does the consumer register a custom `IWebhookDeliveryHook` adapter?

**Decision**: The consumer registers their implementation BEFORE `AddMisaConnectESign(builder.Configuration)` runs:

```csharp
// In the consumer's Program.cs:
builder.Services.AddSingleton<IWebhookDeliveryHook, MyDeliveryHook>();  // consumer's implementation
builder.Services.AddMisaConnectESign(builder.Configuration);             // sets the no-op NullWebhookDeliveryHook only if no prior registration exists
```

The SDK's `AddMisaConnectESign` extension uses `TryAddSingleton<IWebhookDeliveryHook, NullWebhookDeliveryHook>()` so the consumer's prior `AddSingleton` registration wins. The sample API ships a `LoggingWebhookDeliveryHook` (logs the outcome at INFO + dispatches to a channel for downstream consumption) as a reference.

**Rationale**:
- Standard `TryAdd*` pattern across the slice-1/2/3 SDK — consumers always have a way to override the default adapter.
- The hook is `singleton` because it's stateless and idempotent — the same instance serves every webhook delivery. Consumers needing per-request state inject `IServiceProvider` and resolve scoped services from there.
- `NullWebhookDeliveryHook` as the default keeps the SDK runnable out-of-the-box without forcing a consumer-side decision; the Application-layer handler always invokes the hook (even when it's the null impl) so the call graph is identical regardless of consumer choice.

**Alternatives considered**:
- *Required registration (throw at startup if no `IWebhookDeliveryHook` is registered)*: rejected — defeats "the sample runs out of the box" use case. The null implementation is harmless.
- *Per-call `deliveryHook` parameter on `HandleWebhookAsync(envelope, deliveryHook, ct)`*: rejected — verbose; forces consumers to pass the same dependency on every call when DI is the natural fit.
- *Static event (`MisaESignClient.OnWebhookCompleted += …`)*: rejected — anti-pattern in DI-based hosts; impossible to unit-test cleanly.

---

## R-10. Sample-API webhook URL — does the sandbox `[SandboxFact]` require local HTTP server reachability from MISA's network?

**Decision**: Yes — and this is documented as a hard prerequisite, not an SDK responsibility. The sandbox `[SandboxFact]` skip-conditions (FR-097) check three env vars:

1. `MISACONNECT_ESIGN_SANDBOX_USERNAME` / `MISACONNECT_ESIGN_SANDBOX_PASSWORD` etc. — slice-1 sandbox creds.
2. `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL` — the publicly-reachable URL that MISA has been told to POST to (registered out-of-band per Assumption 5).
3. `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT` — optional; default `00:05:00` (5 minutes) for the consumer's poll-loop to wait for the webhook to arrive (since the test process owns the webhook endpoint via `WebApplicationFactory<Program>` listening on the configured URL).

If `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL` is unset, the fact skips with the documented message "set MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL to a URL MISA is configured to POST to; see docs/sandbox-setup.md". Local-developer environments typically use ngrok or similar tunneling tools to expose `localhost` to MISA's network — this is documented in `docs/sandbox-setup.md` as part of the slice-4 onboarding instructions (an update slice 4 ships as part of its task list).

**Rationale**:
- Webhook delivery requires MISA to be able to reach the consumer's endpoint; the SDK can't fake that with a `[SandboxFact]`. Either the test environment exposes a reachable URL or the fact skips.
- Skipping cleanly when prerequisites are missing matches the slice-1 sandbox-test convention exactly (Constitution Principle VI).
- The 5-minute default timeout is generous for sandbox-side processing variability; configurable for slower or faster sandbox profiles.

**Alternatives considered**:
- *Use a mocked "webhook arrival" via direct SDK-internal API*: rejected — defeats the point of the sandbox test (which is end-to-end MISA reachability).
- *Have the SDK self-spin up a webhook receiver*: rejected — duplicates the sample API's endpoint logic and complicates the SDK with HTTP-server concerns.

---

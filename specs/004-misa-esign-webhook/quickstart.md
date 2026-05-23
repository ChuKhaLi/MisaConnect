# Quickstart — MISA eSign webhook-mode signing (slice 4)

This walks through wiring up a webhook-mode signing flow in a .NET 8 ASP.NET Core service that consumes `MisaConnect.ESign` ≥ `2.0.0-preview.4`. The blocking polling-mode facades (`SignPdfAsync` / `SignXmlAsync` / `SignWordAsync` / `SignExcelAsync`) from slices 1+3 continue to work unchanged — webhook mode is an additive alternative, not a replacement.

## Prerequisites

1. MISA eSign sandbox or production credentials (see [docs/sandbox-setup.md](../../docs/sandbox-setup.md)).
2. A publicly-reachable HTTPS URL that MISA can POST to. For local development, use ngrok or similar tunneling: `ngrok http https://localhost:7099` exposes your local sample API.
3. Out-of-band registration of your webhook URL with MISA (per [spec.md Assumption 5](./spec.md#assumptions) — MISA does not provide a registration API).

## 1. Configure the SDK

Add the webhook sub-section to your `appsettings.json` (or environment variables / user-secrets):

```json
{
  "Misa": {
    "ESign": {
      "Environment": "Sandbox",
      "BaseUrl": "https://esignapp.misa.vn/",
      "UserName": "your-user",
      "ClientId": "your-client-id",
      "ClientKey": "your-client-key",
      "Webhook": {
        "Mode": "Both",                          // "Polling" | "Webhook" | "Both" (default "Both")
        "Session": { "Ttl": "24:00:00" },        // session record lifetime; default 24h
        "Path": "/esign/webhook",                // sample-API URL path; default "/esign/webhook"
        "Secret": null,                          // sample-API shared-secret URL segment; set via user-secrets in production
        "AllowedIps": null                       // sample-API CIDR allowlist; e.g. ["203.0.113.0/24"]
      }
    }
  }
}
```

Password lives in user-secrets (not JSON):

```bash
dotnet user-secrets set "Misa:ESign:Password" "your-password"
```

For production, also configure the webhook auth layers (see step 4 below):

```bash
dotnet user-secrets set "Misa:ESign:Webhook:Secret" "$(openssl rand -base64 32 | tr -d '=' | tr '/+' '_-')"
```

## 2. Register DI in `Program.cs`

```csharp
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.Webhook;
using MisaConnect.ESign.Infrastructure.DependencyInjection;
using MisaConnect.Samples.Api.Endpoints;
using MisaConnect.Samples.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Register your delivery hook BEFORE AddMisaConnectESign so it wins TryAddSingleton
builder.Services.AddSingleton<IWebhookDeliveryHook, MyWebhookDeliveryHook>();

// Wire up the SDK
builder.Services.AddMisaConnectESign(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.MapHealthChecks("/health");

// Mount the webhook endpoint (sample-API helper)
app.MapESignWebhookEndpoint(builder.Configuration);

// Emit FR-101 startup WARN if Webhook.Mode != Polling and no auth layer is configured
WebhookStartupValidator.EmitWarnIfPubliclyReachable(app.Services, builder.Configuration);

app.Run();
```

## 3. Implement `IWebhookDeliveryHook`

Your hook receives the signed bytes (on success) or a typed terminal-state result (on `FAILED` / `CANCELLED`):

```csharp
public sealed class MyWebhookDeliveryHook : IWebhookDeliveryHook
{
    private readonly ISignedDocumentSink sink;          // your downstream (queue, DB, SignalR, etc.)
    private readonly ILogger<MyWebhookDeliveryHook> logger;

    public MyWebhookDeliveryHook(ISignedDocumentSink sink, ILogger<MyWebhookDeliveryHook> logger)
    {
        this.sink = sink;
        this.logger = logger;
    }

    public async Task DeliverAsync(WebhookOutcomeDto outcome, CancellationToken ct)
    {
        switch (outcome)
        {
            case WebhookOutcomeDto.SuccessWithSignedBytes success:
                logger.LogInformation("Webhook finalize succeeded for {TransactionId} ({Format}); {ByteCount} bytes",
                    success.TransactionId, success.Format, success.SignedBytes.Length);
                await sink.AcceptAsync(success.TransactionId, success.Format, success.SignedBytes, ct);
                break;

            case WebhookOutcomeDto.TerminalWithoutFinalize terminal:
                logger.LogWarning("Signing transaction {TransactionId} reached terminal state {Status} (errorCode = {ErrorCode}); no signed document",
                    terminal.TransactionId, terminal.Status, terminal.MisaErrorCode);
                await sink.MarkFailedAsync(terminal.TransactionId, terminal.Status.ToString(), terminal.MisaErrorCode, ct);
                break;

            case WebhookOutcomeDto.FailureWithError failure:
                logger.LogWarning("Webhook validation failed: {Category} (correlationId = {CorrelationId})",
                    failure.Category, failure.CorrelationId);
                // No downstream dispatch — the SDK already returned the failure ACK to MISA
                break;
        }
    }
}
```

> **The delivery hook is invoked exactly once per `(clientId, transactionId)` for successful finalizes.** Duplicate webhook deliveries hit the cached success and do NOT re-invoke the hook (per FR-080). Don't bake retry logic into the hook for the success path — the SDK already handled idempotency.

## 4. Configure the webhook endpoint's transport-layer auth (production)

MISA's documented webhook contract has no signature header or HMAC scheme. The sample API ships two opt-in transport-layer auth layers — configure at least one for production deployments.

### 4a. Shared-secret URL segment (FR-099)

Set `Misa:ESign:Webhook:Secret` to a 32+ character URL-safe string:

```bash
dotnet user-secrets set "Misa:ESign:Webhook:Secret" "Q9aBcD3fGhIjKlMnOpQrStUvWxYz123456"
```

The endpoint mounts at `{Path}/{secret}`. Register the **full URL including the secret segment** with MISA out-of-band (e.g. `https://your-host/esign/webhook/Q9aBcD3fGhIjKlMnOpQrStUvWxYz123456`). Any POST to the bare `{Path}` or to `{Path}/{wrong-secret}` returns `404 Not Found`.

The secret comparison uses `CryptographicOperations.FixedTimeEquals` to eliminate timing-attack surface. The secret value is NEVER logged at any level — `ESignLogScrubber` redacts it from any captured log line.

### 4b. CIDR allowlist (FR-100)

Set `Misa:ESign:Webhook:AllowedIps` to MISA's documented egress CIDR ranges (consult MISA support for the current list):

```json
"AllowedIps": ["203.0.113.0/24", "198.51.100.0/24"]
```

POSTs from any IP outside the allowlist return `403 Forbidden` before the webhook handler runs. The structured WARN log records only the IP class (`"public"` / `"private-rfc1918"` / etc.) — never the full IP at INFO/DEBUG.

If the sample runs behind a reverse proxy, **configure `ForwardedHeadersOptions` yourself** — the sample does not auto-enable forwarded-headers processing.

### 4c. Both layers (defense in depth)

Combine the secret URL with the CIDR allowlist for layered defense. Configure both options simultaneously — the endpoint enforces secret-then-IP in that order.

## 5. Initiate a sign in webhook mode

```csharp
public sealed class SigningController : ControllerBase
{
    private readonly IMisaESignClient esign;

    public SigningController(IMisaESignClient esign) => this.esign = esign;

    [HttpPost("/api/sign-pdf")]
    public async Task<IActionResult> SignPdf([FromBody] SignRequest request, CancellationToken ct)
    {
        var beginResult = await esign.BeginSignPdfAsync(new BeginSignPdfRequestDto(
            Pdf: request.PdfBytes,
            SignatureContext: new SignatureInfoDto(
                SignatureName: "Demo Signer",
                HashAlgorithm: "SHA256",
                LogoImage: request.LogoImageBase64,
                SignatureDescription: new SignatureDescriptionDto(
                    SignedBy: "Demo Signer",
                    Location: "Hà Nội",
                    Reason: "Demo signing",
                    Contact: "demo@example.com")),
            DocumentName: "demo.pdf",
            DataToBeDisplayed: "Demo signing"), ct);

        // Persist the transactionId alongside the user's request so your delivery hook can correlate later
        await myRequestStore.RecordAsync(request.UserId, beginResult.TransactionId, ct);

        // Return 202 — the actual signed PDF arrives via the webhook → delivery hook later
        return Accepted(new { transactionId = beginResult.TransactionId });
    }
}
```

`BeginSignPdfAsync` runs login → cert select → `/documents/hash` → register session → `/Signing/hash` and returns the MISA-issued `transactionId`. It does NOT poll `/Signing/status` (the webhook will deliver completion).

The non-PDF formats use the per-format `BeginSign{Xml,Word,Excel}Async` facades with the same shape as their slice-3 blocking counterparts.

## 6. Verify locally with the in-repo fake server

The integration test suite ships an in-process `EsignFake` server that you can drive end-to-end without MISA reachability. Run the sample API with the fake-server profile:

```bash
dotnet run --project samples/MisaConnect.Samples.Api --launch-profile FakeMisa
```

Then in a separate terminal, drive a Begin + synthetic-webhook round-trip via the test driver:

```bash
dotnet test tests/MisaConnect.ESign.IntegrationTests \
    --filter "FullyQualifiedName~BeginAndWebhookHappyPathFakeServerTests"
```

This verifies the full pipeline (Begin → session register → synthetic webhook POST → finalize → delivery hook → success ACK) against deterministic fake-server responses. No sandbox credentials needed.

## 7. Verify against the MISA sandbox

Set the sandbox env vars per [docs/sandbox-setup.md](../../docs/sandbox-setup.md) AND register your webhook URL with MISA out-of-band. Then:

```bash
export MISACONNECT_ESIGN_SANDBOX_USERNAME="your-sandbox-user"
export MISACONNECT_ESIGN_SANDBOX_PASSWORD="your-sandbox-password"
export MISACONNECT_ESIGN_SANDBOX_CLIENT_ID="your-sandbox-client-id"
export MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY="your-sandbox-client-key"
export MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL="https://your-ngrok-host/esign/webhook/your-secret"
# Optional: extend the poll-loop budget if your sandbox is slow
export MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT="00:10:00"

dotnet test tests/MisaConnect.ESign.IntegrationTests \
    --filter "FullyQualifiedName~SignViaWebhookSandboxTests"
```

The `[SandboxFact]` test:
- Skips cleanly when any required env var is missing.
- Exercises a real Begin + waits up to `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT` for MISA's push.
- Asserts the delivery hook received the signed PDF bytes within the timeout.
- Fails loudly when credentials are explicitly rejected by the sandbox.

## What's NOT in scope for slice 4

- **Webhook registration with MISA** — out-of-band per Assumption 5; the SDK does not register, discover, or validate the configured webhook URL against MISA.
- **HMAC / signature verification at the SDK layer** — MISA's documented contract has no signature header per Assumption 11. The sample API's shared-secret URL segment + CIDR allowlist (steps 4a/4b above) are consumer-side defense-in-depth, not MISA-mandated.
- **Durable webhook queueing** — the default `InMemorySigningSessionStore` is in-process only. For multi-instance deployments (load-balanced web farm), implement and register a distributed `ISigningSessionStore` adapter (Redis, SQL, etc.) per the port contract in [contracts/public-surface.md](./contracts/public-surface.md).
- **Mixing webhook and polling modes for the same `transactionId`** — each session is one mode; a webhook for a polling-initiated transaction surfaces as `UnknownTransaction` (per spec.md Edge Case "Consumer mixes polling and webhook").

## Cross-mode operation

The default `Webhook.Mode = Both` lets webhook-mode and polling-mode coexist:

```csharp
// Use webhook mode for long-document signing (multi-MB Word doc):
var begin = await esign.BeginSignWordAsync(largeWordRequest, ct);
return Accepted(new { transactionId = begin.TransactionId });

// In the same process, use blocking mode for tiny PDFs where polling is fine:
var result = await esign.SignPdfAsync(smallPdfRequest, ct);
return File(result.SignedPdf, "application/pdf");
```

Both modes share the same auth token (one cached login per user), the same certificate selection, the same transport plumbing. Switching modes per call site has no setup overhead.

If you've finished migrating every call site to webhook mode, you can set `Webhook.Mode = Webhook` to refuse any accidental polling-mode calls (the blocking facades throw `InvalidOperationException("polling mode disabled")` per FR-093). Vice versa for `Mode = Polling`.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Sample API logs FR-101 WARN at startup | Webhook endpoint mounted with no transport-layer auth | Configure `Misa:ESign:Webhook:Secret` and/or `:AllowedIps` (see step 4). |
| `BeginSignPdfAsync` throws `InvalidOperationException("webhook mode disabled")` | `Webhook.Mode == Polling` | Set `Mode = Webhook` or `Mode = Both`. |
| `SignPdfAsync` throws `InvalidOperationException("polling mode disabled")` | `Webhook.Mode == Webhook` | Set `Mode = Both` to allow both, or use `BeginSignPdfAsync` instead. |
| Webhook handler returns `webhook.unknown_transaction` for every delivery | Session expired (TTL elapsed), or webhook arrived for a polling-initiated transaction | Confirm `BeginSignPdfAsync` was called (not `SignPdfAsync`); increase `Webhook.Session.Ttl` if MISA's retry window exceeds your default. |
| Webhook handler returns `webhook.client_id_mismatch` | Configured `ClientId` differs from MISA's webhook payload | Verify `Misa:ESign:ClientId` matches the MISA-registered value for your account. |
| Delivery hook invoked twice for the same `transactionId` | Bug or distributed-store misconfiguration | The default in-memory adapter guarantees exactly-once on success (FR-080). If using a custom `ISigningSessionStore`, verify it implements the single-flight contract per the port docstring. |
| Sandbox `[SandboxFact]` times out without receiving the webhook | MISA's webhook URL registration mismatch | Verify the URL registered with MISA matches `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL` exactly (including the secret segment if configured); verify your ngrok tunnel is alive. |

## Reference

- Specification: [spec.md](./spec.md)
- Implementation plan: [plan.md](./plan.md)
- Public surface: [contracts/public-surface.md](./contracts/public-surface.md)
- Wire envelopes: [contracts/wire-envelopes.md](./contracts/wire-envelopes.md)
- Error mapping: [contracts/error-mapping.md](./contracts/error-mapping.md)
- Sample API contract: [contracts/sample-api.md](./contracts/sample-api.md)
- Data model: [data-model.md](./data-model.md)
- Slice-1 quickstart (blocking PDF signing): [../001-misa-esign-pdf-sign-flow/quickstart.md](../001-misa-esign-pdf-sign-flow/quickstart.md)
- Slice-3 quickstart (multi-format blocking signing): [../003-misa-esign-multi-format/quickstart.md](../003-misa-esign-multi-format/quickstart.md)

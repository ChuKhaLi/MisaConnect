# Sample API — slice 4 webhook endpoint contract

This file pins the contract for the sample API's webhook endpoint at `samples/MisaConnect.Samples.Api/Endpoints/ESignWebhookEndpoint.cs`. The sample is a reference implementation, not part of the SDK's public surface — but the behavior documented here is testable (SC-035, SC-036, SC-037) and consumers using the sample as a template inherit these guarantees.

## 1. Endpoint mounting (FR-094 + FR-099)

```text
configuration:
  Misa:ESign:Webhook:Path         (default: "/esign/webhook")
  Misa:ESign:Webhook:Secret       (no default; opt-in)
  Misa:ESign:Webhook:AllowedIps   (no default; opt-in)
```

| `Secret` configured? | Mounted route | Behavior on `POST {Path}` | Behavior on `POST {Path}/{secret}` |
|---|---|---|---|
| No (null or empty) | `POST {Path}` | reaches handler | n/a (route doesn't exist) |
| Yes | `POST {Path}/{secret}` only | returns `404 Not Found` (routing-layer; no log line that leaks the secret) | route segment matched → secret compared via `CryptographicOperations.FixedTimeEquals` → `404 Not Found` on mismatch / proceeds on match |

The `404`-on-mismatch (rather than `401` or `403`) preserves the spec's "secret URL un-enumerable" property per FR-099. Attackers cannot distinguish "wrong secret" from "no webhook endpoint at this path" by status code, body, or timing (the constant-time comparison eliminates the timing side-channel).

### Secret validation at startup (FR-099)

`MisaESignOptionsValidator` (extended in slice 4) asserts at startup:

| Rule | Action on violation |
|---|---|
| `Webhook.Secret == null || Webhook.Secret.Length >= 32` | Host fails fast with a descriptive exception identifying the option key and the minimum length. |
| `Webhook.Secret` characters all in `[A-Za-z0-9_-]` (URL-safe) | Host fails fast — the secret WILL be a URL segment, so non-URL-safe characters would cause routing failures. |

A configured secret MUST be loadable from .NET user-secrets / environment variables / a secret manager, NEVER from a committed `appsettings.json` file. The sample API's `appsettings.json` does NOT include the `Misa:ESign:Webhook:Secret` key by design — consumers configure it via `dotnet user-secrets set "Misa:ESign:Webhook:Secret" "<32+-char-url-safe-value>"` for dev or via their host's secret manager for production.

## 2. CIDR allowlist (FR-100)

```text
configuration:
  Misa:ESign:Webhook:AllowedIps   string[] of CIDR ranges (default: not configured)
```

When the allowlist is configured (non-empty array), every inbound webhook POST is matched against the remote IP **before** the webhook handler is invoked. A request from an IP outside every configured CIDR range MUST return `403 Forbidden` per FR-100.

### Implementation

```csharp
private static bool IsInAllowlist(IPAddress remote, string[] allowedCidrRanges)
{
    foreach (var cidr in allowedCidrRanges)
    {
        if (IPNetwork.TryParse(cidr, out var network) && network.Contains(remote))
        {
            return true;
        }
    }
    return false;
}
```

Uses `System.Net.IPNetwork` (built into .NET 8 — no third-party dependency per research R-4 / Technical Context). Handles IPv4 and IPv6.

### Reverse-proxy / X-Forwarded-For handling

The sample reads `context.Connection.RemoteIpAddress` unmodified. Consumers running the sample behind a reverse proxy MUST configure ASP.NET's `ForwardedHeadersOptions` themselves (`builder.Services.Configure<ForwardedHeadersOptions>(...)` + `app.UseForwardedHeaders()`). The sample API's `Program.cs` does NOT auto-enable forwarded-headers processing because the trust-boundary configuration is consumer-specific (the trusted-proxy CIDR list, the header name, the limit) — auto-enabling would silently trust forged `X-Forwarded-For` headers from any upstream.

### Startup validation (FR-100)

`MisaESignOptionsValidator` asserts at startup:

| Rule | Action on violation |
|---|---|
| Every entry in `Webhook.AllowedIps` parses via `IPNetwork.TryParse(entry, out _)` | Host fails fast — invalid CIDR ranges are typos waiting to silently allow more (or less) than intended. |

### IP-class log reduction (FR-100)

On `403 Forbidden` rejection, the endpoint emits a structured log line at `WARN` level with these fields ONLY:

| Field | Example value | Why |
|---|---|---|
| `CorrelationId` | `"abc-123-xyz"` | Same as the request's correlation ID |
| `RemoteIpClass` | `"public"` / `"private-rfc1918"` / `"private-rfc6598"` / `"loopback"` / `"ipv6-link-local"` | Class label only; the full IP is NEVER logged at INFO/DEBUG/WARN |
| `ConfiguredCidrRangeCount` | `3` | How many ranges were tried |

The class-label reduction is implemented as a small helper in `Infrastructure/Logging/IpClassifier.cs`:

```csharp
internal static string ClassifyIp(IPAddress? ip) => ip switch
{
    null => "unknown",
    { IsIPv4MappedToIPv6: true } => ClassifyIp(ip.MapToIPv4()),
    _ when IPAddress.IsLoopback(ip) => "loopback",
    _ when ip.AddressFamily == AddressFamily.InterNetwork && IsRfc1918(ip) => "private-rfc1918",
    _ when ip.AddressFamily == AddressFamily.InterNetwork && IsRfc6598(ip) => "private-rfc6598",
    _ when ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv6LinkLocal => "ipv6-link-local",
    _ when ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv6SiteLocal => "ipv6-site-local",
    _ => "public",
};
```

This rule is enforced by `tests/MisaConnect.ESign.UnitTests/Webhook/ExtraDataNotLoggedTests.cs` (which also covers FR-087's other no-log rules) — the test scans captured log output for any IPv4 dotted-quad pattern (`\d+\.\d+\.\d+\.\d+`) or IPv6 colon pattern and asserts zero hits.

## 3. HTTP response shape (FR-085)

Every well-formed POST to the webhook endpoint returns HTTP `200 OK` with the `WebhookAckDto` as the JSON body, regardless of the validation/finalize outcome. Failure is reported via the body's `errorCode` field per the A.11 mapping (see [error-mapping.md](./error-mapping.md)).

| Outcome | HTTP status | Body |
|---|---|---|
| Validation passes + finalize succeeds | `200 OK` | `{ "errorCode": "0", "devMsg": "...", "userMsg": "..." }` |
| Validation fails (any A.11.1 row) | `200 OK` | `{ "errorCode": "webhook.<category>", "devMsg": "...", "userMsg": "..." }` |
| Validation passes + finalize fails (A.11.2) | `200 OK` | `{ "errorCode": "webhook.finalize_failed", "devMsg": "...", "userMsg": "..." }` |
| `Status = FAILED / CANCELLED` (A.11.3) | `200 OK` | `{ "errorCode": "0", "devMsg": "...", "userMsg": "..." }` |
| Secret URL segment doesn't match (FR-099) | `404 Not Found` | (empty body — handled by ASP.NET's default 404 response) |
| Source IP not in allowlist (FR-100) | `403 Forbidden` | (empty body — the WARN log is emitted server-side) |
| JSON body that doesn't parse (FR-085 edge case) | `200 OK` | `{ "errorCode": "webhook.malformed", "devMsg": "...", "userMsg": "..." }` — the handler catches `JsonException` from `System.Text.Json` deserialization and returns `MalformedEnvelopeException`'s ACK |
| Transport-level failure (host unhealthy, handler crashed unexpectedly) | `500 Internal Server Error` or similar | ASP.NET's default error response |

The `200`-on-every-well-formed-POST convention matches MISA's documented contract (per Assumption 7): MISA's documented behavior uses the ACK body as the retry signal, not HTTP status. Non-`200` responses provoke MISA's transport-level retry rather than a typed application-level acknowledgement.

## 4. Startup-time WARN emission (FR-101)

The sample API's `Program.cs` emits a startup-time WARN log line when both conditions hold:

1. `Misa:ESign:Webhook:Mode` is `Webhook` or `Both` (i.e. the webhook endpoint will be reachable).
2. Neither `Misa:ESign:Webhook:Secret` NOR `Misa:ESign:Webhook:AllowedIps` is configured (both are null/empty/unset).

The WARN line:

```text
Webhook endpoint is publicly reachable with no transport-layer auth; configure Misa:ESign:Webhook:Secret and/or :AllowedIps for production.
```

This is **informational** — the host MUST NOT fail-start on the WARN. Dev / sandbox environments remain runnable without configuring either auth layer (a common scenario for local-machine testing through ngrok or similar tunneling).

The WARN line is enforced by `tests/MisaConnect.ESign.IntegrationTests/SampleApi/WebhookStartupWarnTests.cs` (SC-037):

- With `Mode = Both` + no secret + no allowlist → exactly one WARN line containing the verbatim string above.
- With `Mode = Both` + secret configured → zero WARN lines.
- With `Mode = Both` + allowlist configured → zero WARN lines.
- With `Mode = Both` + both configured → zero WARN lines.
- With `Mode = Polling` (webhook endpoint not mounted) → zero WARN lines regardless of secret/allowlist.

## 5. Sample API `Program.cs` integration

The sample's `Program.cs` wires the webhook endpoint after the existing eInvoice endpoints + the new `IWebhookDeliveryHook` registration:

```csharp
// Existing slice-1 eInvoice DI
builder.Services.AddMisaConnectEInvoice(builder.Configuration);

// NEW slice-4 eSign DI
builder.Services.AddSingleton<IWebhookDeliveryHook, SampleWebhookDeliveryHook>();  // sample's reference impl
builder.Services.AddMisaConnectESign(builder.Configuration);                        // sets default NullWebhookDeliveryHook only if no prior registration

// ... existing CorrelationIdMiddleware + MapHealthChecks + MapTemplateEndpoints + MapInvoiceEndpoints ...

// NEW slice-4 webhook endpoint
app.MapESignWebhookEndpoint(builder.Configuration);

// NEW slice-4 startup WARN per FR-101
WebhookStartupValidator.EmitWarnIfPubliclyReachable(app.Services, builder.Configuration);

app.Run();
```

The `SampleWebhookDeliveryHook` is a reference implementation that:
- Logs the outcome at INFO via `ILogger<SampleWebhookDeliveryHook>`.
- Dispatches success-with-signed-bytes to an in-memory `Channel<SignedDocument>` that a downstream consumer (e.g. a worker service or another endpoint on the sample) reads from.
- Returns immediately (the delivery hook is invoked synchronously by the webhook handler but should not block — long-running work belongs in the consumer's downstream).

## 6. Sandbox `[SandboxFact]` contract (FR-097)

The sandbox-gated end-to-end test under `tests/MisaConnect.ESign.IntegrationTests/Sandbox/SignViaWebhookSandboxTests.cs` exercises a real MISA round-trip. Skip-conditions:

| Env var | Required? | Purpose |
|---|---|---|
| `MISACONNECT_ESIGN_SANDBOX_USERNAME` | yes | slice-1 sandbox username |
| `MISACONNECT_ESIGN_SANDBOX_PASSWORD` | yes | slice-1 sandbox password |
| `MISACONNECT_ESIGN_SANDBOX_CLIENT_ID` | yes | slice-1 sandbox clientId |
| `MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY` | yes | slice-1 sandbox clientKey |
| `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL` | **NEW (slice 4)** | The publicly-reachable URL that the test process listens on AND that MISA is configured to POST to (registered out-of-band per Assumption 5) |
| `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT` | optional | The poll-loop duration before the test fails. Default `00:05:00`. |

When `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL` is unset, the fact skips cleanly with the documented message. Local-developer environments typically use ngrok or similar tunneling tools to expose `localhost` — this is documented in `docs/sandbox-setup.md` (an update slice 4 ships as a task).

Failure modes:

| Condition | Test outcome |
|---|---|
| Any required env var unset | `SkipResult` with descriptive message — no failure |
| `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL` set but MISA hasn't been configured to POST to it | Fails after `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT` elapses with "no webhook delivery received within timeout — verify MISA-side webhook URL registration" |
| All env vars set + MISA configured + sandbox returns success | Passes — asserts the test's `IWebhookDeliveryHook` received `SuccessWithSignedBytes` with the matching `transactionId` and the signed PDF bytes |
| Credentials explicitly rejected by sandbox | Fails loudly (matches slice-1 `SC-003` extended convention) |

# Sandbox setup

To run integration tests or `samples/MisaConnect.Samples.Api` against the MISA eSign sandbox, you need sandbox credentials from MISA.

## 1. Request sandbox access

Contact MISA (https://www.misa.vn) and request **eSign RemoteSigning** integration sandbox credentials. You should receive:

- A sandbox **client ID** and **client key**.
- A **user name** and **password**.
- A sandbox **base URL**.

The production base URL is `https://esignapp.misa.vn/`. The sandbox URL is issued alongside your sandbox credentials.

At least one `ACTIVE` remote-signing certificate must be provisioned on the sandbox account (via MISA's eSign portal). Without it, `SignPdfAsync` throws `NoActiveCertificateException` immediately — no MISA call is wasted.

## 2. Local development — user secrets

For `samples/MisaConnect.Samples.Api`:

```bash
cd samples/MisaConnect.Samples.Api
dotnet user-secrets set "Misa:ESign:Environment" "Sandbox"
dotnet user-secrets set "Misa:ESign:BaseUrl" "https://<sandbox-host>/"
dotnet user-secrets set "Misa:ESign:ClientId" "<your-clientId>"
dotnet user-secrets set "Misa:ESign:ClientKey" "<your-clientKey>"
dotnet user-secrets set "Misa:ESign:UserName" "<your-misa-user>"
dotnet user-secrets set "Misa:ESign:Password" "<your-misa-password>"
```

## 3. Integration test secrets

Sandbox tests use `[SandboxFact]`, which reads the `MISACONNECT_ESIGN_SANDBOX_*` env vars and skips cleanly when any are missing or the sandbox host is unreachable.

```bash
# Sandbox credentials read by [SandboxFact]
export MISACONNECT_ESIGN_SANDBOX_BASE_URL=https://<sandbox-host>/
export MISACONNECT_ESIGN_SANDBOX_CLIENT_ID=...
export MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY=...
export MISACONNECT_ESIGN_SANDBOX_USERNAME=...
export MISACONNECT_ESIGN_SANDBOX_PASSWORD=...

dotnet test tests/MisaConnect.ESign.IntegrationTests
```

### Two-factor sandbox

For slice-2 two-factor tests (`[SandboxFact(SandboxRequirement.TwoFactorAuth)]`), additionally set:

```bash
# Either a static OTP value (sandbox-only — never use in production) OR an
# absolute path to a console binary that prints the current OTP on stdout.
export MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER=123456
# Confirms the sandbox account is enrolled in 2FA (any non-empty value suffices).
export MISACONNECT_ESIGN_SANDBOX_USER_2FA_ENABLED=1
```

When either of the two-factor env vars is absent the 2FA sandbox test skips cleanly with `ESign sandbox not configured. Missing: ...`. The test never throws on missing credentials — failures only surface when MISA itself rejects the configured account.

### Webhook sandbox

For the slice-4 webhook end-to-end test (`[SandboxFact(SandboxRequirement.Webhook)]`), additionally set:

```bash
# Publicly reachable URL that MISA has been configured to POST webhook deliveries to.
# For local development, ngrok or similar tunneling tools expose localhost to MISA's network.
export MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL=https://your-tunnel.ngrok.io/esign/webhook
# Optional — how long the sandbox test waits for MISA to POST back. Default 00:05:00 (5 min).
export MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT=00:05:00
```

The webhook URL must be registered with MISA out-of-band (the SDK does not register webhook destinations on the consumer's behalf). Local-developer workflow: start the sample API behind ngrok, set `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL` to the ngrok-public URL, and register that URL with MISA's webhook configuration. The sandbox test then drives `BeginSignPdfAsync` and waits for MISA to push the completion envelope back to the running sample API endpoint, which routes through `IMisaESignClient.HandleWebhookAsync` and surfaces the signed bytes to the test's delivery hook.

## 4. In-repo fake server (no MISA reachability needed)

`tests/MisaConnect.ESign.IntegrationTests/EsignFake/` is a deterministic in-process HTTP host. Integration tests can point at it for fully offline E2E coverage (refresh-stampede counting, polling state machine, terminal-state mapping, webhook round-trips). Use this when you don't have sandbox credentials or want fast, hermetic CI runs.

## 5. Production cutover

Flip:

- `Misa:ESign:Environment` → `Production`
- `Misa:ESign:BaseUrl` → `https://esignapp.misa.vn/`
- Replace sandbox credentials with production credentials.

The options validator refuses to start if the environment and base-URL host disagree, so misconfiguration fails fast.

> Looking for the `MisaConnect.EInvoice` sandbox setup? See [docs/einvoice/sandbox-setup.md](../einvoice/sandbox-setup.md).

# Sandbox setup

To run integration tests or `samples/MisaConnect.Samples.Console` against MISA's sandbox, you need sandbox credentials from MISA.

## 1. Request sandbox access

Contact MISA (https://www.misa.vn) and request MeInvoice integration sandbox credentials. You should receive:

- A sandbox **tax code** (mã số thuế).
- A **user name** and **password**.
- An **app ID**.

The sandbox base URL is `https://testapi.meinvoice.vn/api/integration`.

## 2. Local development — user secrets

For `samples/MisaConnect.Samples.Console`:

```bash
cd samples/MisaConnect.Samples.Console
dotnet user-secrets set "Misa:EInvoice:Environment" "Sandbox"
dotnet user-secrets set "Misa:EInvoice:BaseUrl" "https://testapi.meinvoice.vn/api/integration"
dotnet user-secrets set "Misa:EInvoice:TaxCode" "0000000000"
dotnet user-secrets set "Misa:EInvoice:UserName" "your-misa-user"
dotnet user-secrets set "Misa:EInvoice:Password" "your-misa-password"
dotnet user-secrets set "Misa:EInvoice:AppId" "your-misa-app-id"
```

Same pattern for `samples/MisaConnect.Samples.Api`.

## 3. Integration test secrets

Sandbox tests use `[SandboxFact]`, which reads the `MISACONNECT_SANDBOX_*` env vars and skips cleanly when any are missing or the sandbox host is unreachable. The `Misa__EInvoice__*` vars feed the API/Client wiring inside the integration project — set both pairs to actually exercise live sandbox calls:

```bash
# Configuration for the Misa:EInvoice options binder
export Misa__EInvoice__Environment=Sandbox
export Misa__EInvoice__BaseUrl=https://testapi.meinvoice.vn/api/integration
export Misa__EInvoice__TaxCode=0000000000
export Misa__EInvoice__UserName=...
export Misa__EInvoice__Password=...
export Misa__EInvoice__AppId=...

# Sandbox credentials read by [SandboxFact]
export MISACONNECT_SANDBOX_TAXCODE=0000000000
export MISACONNECT_SANDBOX_USERNAME=...
export MISACONNECT_SANDBOX_PASSWORD=...
export MISACONNECT_SANDBOX_APPID=...

dotnet test tests/MisaConnect.EInvoice.IntegrationTests
```

In GitHub Actions, set environment secrets `MISA_TAX_CODE`, `MISA_USERNAME`, `MISA_PASSWORD`, `MISA_APP_ID` on the `misa-sandbox` environment; `.github/workflows/integration.yml` maps them into both env-var sets.

## 3a. MisaConnect.ESign sandbox

The `MisaConnect.ESign` integration suite reads its own `MISACONNECT_ESIGN_SANDBOX_*` env-var family. `[SandboxFact]` skips cleanly when any of these are absent:

```bash
export MISACONNECT_ESIGN_SANDBOX_BASE_URL=https://esign-sandbox.example.com/
export MISACONNECT_ESIGN_SANDBOX_CLIENT_ID=...
export MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY=...
export MISACONNECT_ESIGN_SANDBOX_USERNAME=...
export MISACONNECT_ESIGN_SANDBOX_PASSWORD=...
```

For slice-2 two-factor tests (`[SandboxFact(SandboxRequirement.TwoFactorAuth)]`), additionally set:

```bash
# Either a static OTP value (sandbox-only — never use in production) OR an
# absolute path to a console binary that prints the current OTP on stdout.
export MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER=123456
# Confirms the sandbox account is enrolled in 2FA (any non-empty value suffices).
export MISACONNECT_ESIGN_SANDBOX_USER_2FA_ENABLED=1
```

When either of the two-factor env vars is absent the 2FA sandbox test skips cleanly with `ESign sandbox not configured. Missing: ...`. The test never throws on missing credentials — failures only surface when MISA itself rejects the configured account.

### Slice-4 webhook sandbox

For the slice-4 webhook end-to-end test (`[SandboxFact(SandboxRequirement.Webhook)]`), additionally set:

```bash
# Publicly reachable URL that MISA has been configured to POST webhook deliveries to.
# For local development, ngrok or similar tunneling tools expose localhost to MISA's network.
export MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL=https://your-tunnel.ngrok.io/esign/webhook
# Optional — how long the sandbox test waits for MISA to POST back. Default 00:05:00 (5 min).
export MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT=00:05:00
```

The webhook URL must be registered with MISA out-of-band (the SDK does not register webhook destinations on the consumer's behalf). Local-developer workflow: start the sample API behind ngrok, set `MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL` to the ngrok-public URL, and register that URL with MISA's webhook configuration. The sandbox test then drives `BeginSignPdfAsync` and waits for MISA to push the completion envelope back to the running sample API endpoint, which routes through `IMisaESignClient.HandleWebhookAsync` and surfaces the signed bytes to the test's delivery hook.

## 4. Production cutover

Flip:

- `Misa:EInvoice:Environment` → `Production`
- `Misa:EInvoice:BaseUrl` → `https://api.meinvoice.vn/api/integration`
- Replace sandbox credentials with production credentials.

The options validator refuses to start if the environment and base-URL host disagree, so misconfiguration fails fast.

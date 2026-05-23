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

> Looking for the `MisaConnect.ESign` sandbox setup? See [docs/esign/sandbox-setup.md](../esign/sandbox-setup.md).

## 4. Production cutover

Flip:

- `Misa:EInvoice:Environment` → `Production`
- `Misa:EInvoice:BaseUrl` → `https://api.meinvoice.vn/api/integration`
- Replace sandbox credentials with production credentials.

The options validator refuses to start if the environment and base-URL host disagree, so misconfiguration fails fast.

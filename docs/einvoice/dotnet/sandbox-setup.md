---
title: Sandbox setup
parent: .NET
grand_parent: EInvoice
nav_order: 5
---

# Sandbox setup

To run the integration tests or `samples/dotnet/MisaConnect.Samples.Console` against MISA's
sandbox, you need sandbox credentials from MISA.

## 1. Request sandbox access

Contact MISA (<https://www.misa.vn>) and register for the MeInvoice integration API. You receive:

- A sandbox **tax code** (mã số thuế). If it looks like `6868686868-267`, it is
  `<TaxCode>-<AppId>`: split it (see [Tax code and AppID](../index#tax-code-and-appid)).
- A **user name** and **password** (a sandbox meInvoice web account).
- An **app ID** (the same for sandbox and production).

The sandbox base URL is `https://testapi.meinvoice.vn/api/integration`. Sign in to the sandbox web
app at <https://testapp3.meinvoice.vn> to set up a template and declaration and to see the drafts
you create; production is <https://app3.meinvoice.vn>.

If your machine is outside Vietnam, read
[Calling MISA from outside Vietnam](../index#calling-misa-from-outside-vietnam) first: without an
MTU change, authenticated sandbox calls can hang until they time out.

## 2. Local development — user secrets

For `samples/dotnet/MisaConnect.Samples.Console`:

```bash
cd samples/dotnet/MisaConnect.Samples.Console
dotnet user-secrets set "Misa:EInvoice:Environment" "Sandbox"
dotnet user-secrets set "Misa:EInvoice:BaseUrl" "https://testapi.meinvoice.vn/api/integration"
dotnet user-secrets set "Misa:EInvoice:TaxCode" "0101243150"
dotnet user-secrets set "Misa:EInvoice:UserName" "your-misa-user"
dotnet user-secrets set "Misa:EInvoice:Password" "your-misa-password"
dotnet user-secrets set "Misa:EInvoice:AppId" "your-app-id"
dotnet run
```

Same pattern for `samples/dotnet/MisaConnect.Samples.Api`.

## 3. Integration test secrets

Sandbox tests use `[SandboxFact]`, which reads the `MISACONNECT_SANDBOX_*` env vars and skips
cleanly when any of them is missing or the sandbox host is unreachable. The `Misa__EInvoice__*`
vars feed the options binder inside the integration test project. Set both groups to exercise live
sandbox calls:

```bash
# Configuration for the Misa:EInvoice options binder
export Misa__EInvoice__Environment=Sandbox
export Misa__EInvoice__BaseUrl=https://testapi.meinvoice.vn/api/integration
export Misa__EInvoice__TaxCode=0101243150
export Misa__EInvoice__UserName=your-misa-user
export Misa__EInvoice__Password=your-misa-password
export Misa__EInvoice__AppId=your-app-id

# Sandbox credentials read by [SandboxFact]
export MISACONNECT_SANDBOX_TAXCODE=0101243150
export MISACONNECT_SANDBOX_USERNAME=your-misa-user
export MISACONNECT_SANDBOX_PASSWORD=your-misa-password
export MISACONNECT_SANDBOX_APPID=your-app-id

dotnet test --project tests/dotnet/MisaConnect.EInvoice.IntegrationTests
```

The same four `MISACONNECT_SANDBOX_*` variables drive the TypeScript sandbox tests (see
[TypeScript sandbox setup](../typescript/sandbox-setup)). Without them, the integration project
still runs its tests against the in-repo fake MISA server
(`tests/dotnet/MisaConnect.EInvoice.IntegrationTests/MisaFake/`).

In GitHub Actions, set the environment secrets `MISA_TAX_CODE`, `MISA_USERNAME`, `MISA_PASSWORD`
and `MISA_APP_ID` on the `misa-sandbox` environment; `.github/workflows/integration.yml` maps them
into both groups of variables and lowers the runner's MTU to 1280 before the tests.

> Looking for the `MisaConnect.ESign` sandbox setup? See
> [ESign for .NET → Sandbox setup](../../esign/dotnet/sandbox-setup).

## 4. Production cutover

Change:

- `Misa:EInvoice:Environment` → `Production`
- `Misa:EInvoice:BaseUrl` → `https://api.meinvoice.vn/api/integration`
- The sandbox tax code, user name and password → the production ones (from the issuer's
  accounting team). The app ID stays the same.

The options validator refuses to start if the environment and the base-URL host disagree, so a
half-done cutover fails fast. In production, a template must be in use and its declaration
accepted by the tax authority before MISA accepts invoices on it (see
[Templates and series](../index#templates-and-series)).

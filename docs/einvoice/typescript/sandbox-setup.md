---
title: Sandbox setup
parent: TypeScript
grand_parent: EInvoice
nav_order: 5
---

# Sandbox setup

## Credentials

Ask MISA for meInvoice integration sandbox credentials: a sandbox **tax code**, a **user name**, a
**password** and an **app ID**. The sandbox base URL is
`https://testapi.meinvoice.vn/api/integration` (`environment: 'sandbox'`). Keep the tax code and
the AppID separate ([Tax code and AppID](../index#tax-code-and-appid)).

The quick start, the samples (`samples/typescript/express`, `samples/typescript/nestjs`) and the
sandbox tests all read the same variables, the names the .NET SDK uses:

```bash
export MISACONNECT_SANDBOX_TAXCODE=0000000000
export MISACONNECT_SANDBOX_USERNAME=your-misa-user
export MISACONNECT_SANDBOX_PASSWORD=your-misa-password
export MISACONNECT_SANDBOX_APPID=your-misa-app-id
```

## Sandbox tests

Sandbox tests skip cleanly unless all four variables are set (`MISACONNECT_SANDBOX_TAXCODE`,
`MISACONNECT_SANDBOX_USERNAME`, `MISACONNECT_SANDBOX_PASSWORD`, `MISACONNECT_SANDBOX_APPID`) and
`testapi.meinvoice.vn:443` accepts a TCP connection within 5 s. From the repository root:

```bash
pnpm --filter @misaconnect/einvoice run test:sandbox
```

The tests create drafts in the sandbox account and delete them again; they run serially because
the sandbox tenant is shared and rate limited. With `MISACONNECT_SANDBOX_REQUIRED=1` a skip becomes
a failure, so a green run means the live tests actually ran (the repository's `ts-sandbox`
workflow sets it). The reachability probe dials the host directly, so behind a proxy-only network
the suite skips even if a proxied `fetch` would work.

The regular `pnpm test` never touches the network: the integration tests run against an in-repo
fake MISA server (`src/typescript/einvoice/test/fake/`).

## Outside Vietnam

From hosts outside Vietnam, authenticated calls to the sandbox hang and are reset unless the
interface MTU is lowered (the CI does `sudo ip link set dev eth0 mtu 1280` before the tests). See
[Network / TLS](./configuration#network--tls) and
[Calling MISA from outside Vietnam](../index#calling-misa-from-outside-vietnam).

## In CI

The `ts-sandbox` workflow (`.github/workflows/ts-sandbox.yml`, manual or weekly) runs the suite on
Node 22 and 24 with the `misa-sandbox` environment secrets `MISA_TAX_CODE`, `MISA_USERNAME`,
`MISA_PASSWORD` and `MISA_APP_ID`, mapped to the `MISACONNECT_SANDBOX_*` variables.

## Production

Switch to `environment: 'production'`, `baseUrl: 'https://api.meinvoice.vn/api/integration'` and
your production credentials. A host that does not match the environment is rejected by
`createMisaEInvoiceClient` (`Configuration`), so a mixed-up configuration fails before any call.

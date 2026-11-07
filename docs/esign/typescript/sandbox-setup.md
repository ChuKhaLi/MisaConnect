---
title: Sandbox setup
parent: TypeScript
grand_parent: ESign
nav_order: 8
---

# Sandbox setup

The quick start, the Express sample (`samples/typescript/express`) and the sandbox tests read the
same `MISACONNECT_ESIGN_SANDBOX_*` variables as the .NET SDK:

| Variable                               | Value                                                              |
| -------------------------------------- | ------------------------------------------------------------------ |
| `MISACONNECT_ESIGN_SANDBOX_BASE_URL`   | The MISA eSign sandbox origin (`https`, not `esignapp.misa.vn`).   |
| `MISACONNECT_ESIGN_SANDBOX_CLIENT_ID`  | Your application's client id.                                      |
| `MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY` | Your application's client key (secret).                            |
| `MISACONNECT_ESIGN_SANDBOX_USERNAME`   | The sandbox signer's user name.                                    |
| `MISACONNECT_ESIGN_SANDBOX_PASSWORD`   | The sandbox signer's password (secret).                            |

Keep the values in your shell or a secret store, never in the repository.

## Sandbox tests

The sandbox suite skips cleanly unless `MISACONNECT_ESIGN_SANDBOX_BASE_URL`, `_CLIENT_ID`,
`_CLIENT_KEY`, `_USERNAME` and `_PASSWORD` are all set (`MISACONNECT_ESIGN_SANDBOX_REQUIRED=1` turns a
skip into a failure):

```bash
pnpm --filter @misaconnect/esign run test:sandbox
```

How the suite behaves:

- With the variables set, it first probes the sandbox host with a short TCP connect; an unreachable
  host also skips the suite.
- It signs a PDF and an XML document. A sign test that hits `NoActiveCertificateException` (login and
  the certificate listing worked, but the account has no ACTIVE certificate) is skipped too, unless
  `MISACONNECT_ESIGN_SANDBOX_REQUIRED=1`.
- Tests run one at a time: the sandbox tenant is shared and rate limited.

The unit and integration tests (`pnpm --filter @misaconnect/esign test`) never touch the network:
they run against an in-process fake MISA server.

## From outside Vietnam

Requests to MISA from outside Vietnam can hang and end in `ECONNRESET` (path-MTU blackhole). The
CI sandbox jobs lower the runner's MTU first (`sudo ip link set dev eth0 mtu 1280`). See
[Calling MISA from outside Vietnam](../index#calling-misa-from-outside-vietnam) and
[Configuration → Network / TLS](./configuration#network--tls). With MTU 1280 the login works from
GitHub runners; signing has not yet been verified live, because the sandbox account used so far
has no ACTIVE certificate.

---
title: TypeScript
parent: ESign
nav_order: 2
has_children: true
permalink: /esign/typescript/
---

# ESign for TypeScript

```bash
npm install @misaconnect/esign
```

`@misaconnect/esign` is the TypeScript SDK for **MISA eSign RemoteSigning**, a port of the .NET
`MisaConnect.ESign` package. It signs PDF, XML, Word and Excel documents with a remote certificate,
including two-factor (OTP) sign-in and a non-blocking webhook mode. It has zero runtime
dependencies: HTTP goes through Node's built-in `node:https` (or a `fetch` you supply). It ships ESM
and CommonJS builds with full type declarations, and requires **Node.js >= 22**.

The MISA concepts behind every page here (certificates, the sign-hash flow, OTP, webhook ACKs,
credentials modes, error categories) are described once on the [ESign](../index#how-signing-works)
page.

- [Getting started](./getting-started)
- [Configuration](./configuration)
- [Operations](./operations)
- [Two-factor sign-in](./two-factor)
- [Webhook mode](./webhooks)
- [Credentials](./credentials)
- [Errors](./errors)
- [Sandbox setup](./sandbox-setup)

## Names: .NET to TypeScript

| .NET                                           | TypeScript                                                       |
| ---------------------------------------------- | ---------------------------------------------------------------- |
| `IMisaESignClient`                             | `IMisaESignClient` / `createMisaESignClient(options)`            |
| `SignPdfAsync` / `SignXmlAsync` / ...          | `signPdf` / `signXml` / `signWord` / `signExcel`                 |
| `BeginSignPdfAsync` / ...                      | `beginSignPdf` / ...                                             |
| `HandleWebhookAsync`                           | `handleWebhook`                                                  |
| `SignInWithOtpAsync`, `ResendOtpAsync`         | `signInWithOtp`, `resendOtp`                                     |
| `MisaESignOptions`                             | `MisaESignOptions` (camelCase)                                   |
| `CredentialsMode` + `IMisaCredentialsAccessor` | `credentials` (static or provider) + `opts.credentials`          |
| `ITokenCache`, `ISigningSessionStore`          | `TokenCache`, `SigningSessionStore`                              |
| `CancellationToken`                            | `opts.signal` (`AbortSignal`)                                    |
| `byte[]`                                       | `Uint8Array`                                                     |
| `ESignException` and subclasses                | same names; `Category` / `RawCode` become `category` / `rawCode` |

DTO property names are the camelCase of the .NET names; wire field names match MISA's API verbatim.

## Differences from the .NET SDK

Since .NET `MisaConnect.ESign` 2.5.0 the two SDKs agree on token handling, retries, response
guards, 2FA detection and webhook handling. Three differences remain: the default of the
validation-failure delivery option, how webhook credentials are found in static credentials
mode, and the webhook `clientId` check — .NET compares the envelope's `clientId` with the
configured `ClientId` (`client_id_mismatch`), while TypeScript looks the session up by the
envelope's `clientId`, so a foreign `clientId` usually answers `webhook.unknown_transaction`
(see the ACK tables in [Webhook mode](./webhooks) and the [.NET page](../dotnet/webhooks)).

- **Validation-failure delivery default.** `handleWebhook` answers validation failures (malformed
  body, unknown transaction, client id or document mismatch) to the caller only and never calls
  your delivery hook. .NET 2.x delivers them to `IWebhookDeliveryHook` by default
  (`Webhook.DeliverValidationFailuresToHook = true`) so a minor release does not change behaviour;
  set it to `false` for the TypeScript behaviour. The .NET default becomes `false` in 3.0.
- **Webhook credentials in static mode.** When `credentials` is a provider function,
  `handleWebhook` always calls it with the session's `clientId` and `userName` to finalize a
  webhook. .NET in `CredentialsMode.Static` never calls `IWebhookCredentialsResolver`, even a
  custom one; it uses the credentials from its options. Only `CredentialsMode.Dynamic` calls the
  resolver.

### Fixed in .NET 2.5.0

These were .NET defects or deliberate TypeScript choices that .NET 2.5.0 now matches:

1. **Token after a 401 refresh.** The rest of the flow uses the refreshed token.
2. **Login fallback.** If refreshing fails on a 401, the SDK evicts the token and logs in once
   more before failing with `Authentication`.
3. **`Signing/hash` retries.** Retried only on 429 and on errors where the connection was never
   established (`ECONNREFUSED`, `ENOTFOUND`, `EAI_AGAIN`), never on 5xx, timeouts or resets,
   because a retry could start a second signing transaction (a second approval on the signer's
   phone). The other calls keep their retry rules.
4. **Content-type guard on every eSign (ESRM) JSON response** (`UnexpectedContentType` /
   `UnparseableResponse`; the error reports the content type and body length, never the body).
   The auth calls (login, two-factor, refresh, resend OTP) parse JSON whatever the content type.
5. **`error` field typed loosely.** MISA's `error` accepts `boolean | string | null` (`"true"` /
   `"false"` are parsed), so a string never drops the envelope.
6. **Malformed webhooks** are answered with ACK `webhook.malformed` and a `FailureWithError`
   outcome instead of escaping as an exception (`messageId`, `clientId`, `transactionId`, `status`
   (case-insensitive) and `signatures` are validated).
7. **2FA code `122`** is recognised on any HTTP status, in both the nested `{status:{...}}` and the
   flat body shape.
8. **Proactive token refresh** happens when the token has under 30 s left.
9. **Dynamic credentials work in webhook mode**: the session stores `clientId` and `userName`, and
   finalize calls your provider with them (.NET: `IWebhookCredentialsResolver`).

The webhook rulings also match: webhooks are a trigger, not a source of truth (confirmed with
`Signing/status`); the terminal state is stored once; finalize never prompts for OTP; OTP
challenges are per principal.

### Further behaviour decided while implementing

- **Token binding.** Cached tokens carry a salted fingerprint of password + clientKey; see
  [Configuration → Token cache](./configuration#token-cache).
- **XML bytes keep a BOM.** XML passed as bytes keeps a leading UTF-8 BOM, the same as .NET.
- **No DI scope.** One client object plays the role of one .NET DI scope; use per-call credentials
  to serve several users from one client.

## License and design

MIT. Design:
[docs/superpowers/specs/2026-11-04-ts-esign-design.md](https://github.com/ChuKhaLi/MisaConnect/blob/main/docs/superpowers/specs/2026-11-04-ts-esign-design.md).

---
title: Credentials
parent: TypeScript
grand_parent: ESign
nav_order: 6
---

# Credentials

`MisaCredentials` is `{ clientId, clientKey, userName, password }`: the application's client id and
key plus the signer's MISA account. `clientKey` and `password` are secrets. The MISA side (one
account per process versus one per user) is described in
[Credentials modes](../index#credentials-modes).

## Three ways to supply them

`credentials` is either a static value or a provider function, and any call can override it:

```ts
import { createMisaESignClient } from '@misaconnect/esign';
import type { MisaCredentials } from '@misaconnect/esign';

declare const baseUrl: string;
declare const mine: MisaCredentials;
declare function lookupSecrets(userName?: string): Promise<MisaCredentials>;

// 1. Static: one account for the whole process.
const staticClient = createMisaESignClient({ baseUrl, credentials: mine });

// 2. Provider: resolved per call (e.g. a per-user vault). The context carries clientId / userName
//    when known (webhook finalize) and the call's AbortSignal.
const providerClient = createMisaESignClient({
  baseUrl,
  credentials: async (ctx) => lookupSecrets(ctx.userName),
});

// 3. Per call: overrides both of the above for this call only.
await staticClient.signPdf(
  {
    pdf: new Uint8Array(),
    documentName: 'a.pdf',
    signerName: 's',
    location: 'l',
    reason: 'r',
    contact: 'c',
    logoImageBase64: 'x',
    dataToBeDisplayed: 'd',
  },
  { credentials: mine },
);
void providerClient;
```

The provider has the type `MisaCredentialsProvider`:
`(ctx: { clientId?, userName?, signal? }) => MisaCredentials | Promise<MisaCredentials>`.

## Resolution

Resolution order: `opts.credentials`, then the provider or static value. Credentials are resolved
once per call and used for sign-in, the `x-clientId` / `x-clientKey` headers and the token-cache
key. `MisaCredentials` carry secrets: keep them out of your logs; the SDK never logs them.

Static credentials are checked when the client is created (`Validation` / `InvalidOptions`). A
provider's result and a per-call `opts.credentials` are checked on each call: a blank field fails
with `Validation` / `InvalidCredentials`.

## Several users on one client

One client object plays the role of one .NET DI scope. To serve several users from one client, pass
per-call credentials or use a provider; tokens are cached per user, client id and host, and
[OTP challenges](./two-factor) are tracked per principal.

## Credentials in webhook mode

The webhook finalize has no caller, so the SDK finds credentials by itself. The session stores
only `clientId` and `userName`; with a provider, the SDK calls it with those. With static
credentials it uses the static value, so a transaction started with a per-call `opts.credentials`
needs the same credentials on `handleWebhook(envelope, { credentials })`, or a provider. See
[Webhook mode](./webhooks).

---
title: Getting started
parent: TypeScript
grand_parent: ESign
nav_order: 1
---

# Getting started

## Requirements

- **Node.js >= 22.** The package ships ESM (`import`) and CommonJS (`require`) builds with type
  declarations, and has no runtime dependencies.
- A MISA eSign RemoteSigning account: a client id and client key for your application, plus the
  signer's user name and password. Signing needs an **ACTIVE** certificate on the account; see
  [Certificates](../index#certificates).

## Install

```bash
npm install @misaconnect/esign
```

## Quick start

Create one client and reuse it (it holds the token cache and the HTTP connections), then sign a
PDF. `signPdf` waits until the signer approves the request in the MISA app; see
[How signing works](../index#how-signing-works).

```ts
import { readFile, writeFile } from 'node:fs/promises';
import { createMisaESignClient, isESignException } from '@misaconnect/esign';

const client = createMisaESignClient({
  environment: 'sandbox', // or 'production'
  baseUrl: process.env.MISACONNECT_ESIGN_SANDBOX_BASE_URL!,
  credentials: {
    clientId: process.env.MISACONNECT_ESIGN_SANDBOX_CLIENT_ID!,
    clientKey: process.env.MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY!,
    userName: process.env.MISACONNECT_ESIGN_SANDBOX_USERNAME!,
    password: process.env.MISACONNECT_ESIGN_SANDBOX_PASSWORD!,
  },
});

try {
  const result = await client.signPdf({
    pdf: new Uint8Array(await readFile('contract.pdf')),
    documentName: 'contract.pdf',
    signerName: 'Nguyen Van A',
    location: 'Ho Chi Minh City',
    reason: 'I approve this document',
    contact: 'a@example.com',
    logoImageBase64: '<base64 PNG>', // required, non-empty
    dataToBeDisplayed: 'Signed by Nguyen Van A',
    page: 1,
    positionX: 50,
    positionY: 50,
    width: 200,
    height: 80,
  });
  await writeFile('contract.signed.pdf', result.signedPdf);
  console.log(result.transactionId, result.certificateKeyAlias, result.completedAtUtc);
} catch (e) {
  if (isESignException(e)) console.error(e.category, e.rawCode, e.detail, e.correlationId);
  else throw e;
}
```

The quick start, the sample (`samples/typescript/express`) and the sandbox tests read the same
`MISACONNECT_ESIGN_SANDBOX_*` variables the .NET SDK uses; see [Sandbox setup](./sandbox-setup).

`baseUrl` must be an `https` URL (`http` only for `localhost`, `127.0.0.1`, `::1`). With
`environment: 'production'` its host must be `esignapp.misa.vn`; with `'sandbox'` it must not be.
Invalid options throw an `ESignGeneralException` with category `Validation` and
`rawCode: 'InvalidOptions'`.

## CommonJS

The same API is available through `require`:

<!-- snippet: partial -->
```ts
const { createMisaESignClient, isESignException } = require('@misaconnect/esign');
```

When both builds can load in one process, check errors with `isESignException(err)` rather than
`instanceof`; see [Errors](./errors).

## Next steps

- [Configuration](./configuration): every option, swappable collaborators, network and logging.
- [Operations](./operations): every client method.
- [Two-factor sign-in](./two-factor), [Webhook mode](./webhooks), [Credentials](./credentials).
- [Calling MISA from outside Vietnam](../index#calling-misa-from-outside-vietnam) if your host is
  not in Vietnam.

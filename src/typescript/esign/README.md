# @misaconnect/esign

TypeScript SDK for **MISA eSign RemoteSigning** (chữ ký số từ xa): sign PDF, XML, Word and Excel documents with a remote certificate, two-factor (OTP) sign-in, non-blocking webhook mode, per-call credentials. Same operations as the .NET `MisaConnect.ESign` package. Zero runtime dependencies, ESM and CommonJS, full type declarations. Requires **Node.js >= 22**.

📖 **Full documentation:** <https://chukhali.github.io/MisaConnect/esign/typescript/>

## Install

```bash
npm install @misaconnect/esign    # or pnpm add / yarn add
```

## Quick start

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
    logoImageBase64: (await readFile('logo.png')).toString('base64'),
    dataToBeDisplayed: 'Signed by Nguyen Van A',
    page: 1,
    positionX: 50,
    positionY: 50,
    width: 200,
    height: 80,
  });
  await writeFile('contract.signed.pdf', result.signedPdf);
} catch (e) {
  if (isESignException(e)) console.error(e.category, e.rawCode, e.detail, e.correlationId);
  else throw e;
}
```

## Operations

| Method                                                               | Purpose                                  |
| -------------------------------------------------------------------- | ---------------------------------------- |
| `signPdf` / `signXml` / `signWord` / `signExcel`                     | Sign and wait for the signer's approval. |
| `signInWithOtp(otpCode, otpType, remember)` / `resendOtp(language?)` | Two-factor sign-in (explicit OTP).       |
| `beginSignPdf` / `beginSignXml` / `beginSignWord` / `beginSignExcel` | Webhook mode: start signing and return.  |
| `handleWebhook(envelope)`                                            | Webhook mode: process one MISA delivery. |

Signing needs an ACTIVE remote-signing certificate on the MISA account. Live signing is not yet verified against MISA (tested against an in-repo fake server).

More: [Operations](https://chukhali.github.io/MisaConnect/esign/typescript/operations) · [Two-factor sign-in](https://chukhali.github.io/MisaConnect/esign/typescript/two-factor) · [Webhook mode](https://chukhali.github.io/MisaConnect/esign/typescript/webhooks) · [Credentials](https://chukhali.github.io/MisaConnect/esign/typescript/credentials) · [Configuration](https://chukhali.github.io/MisaConnect/esign/typescript/configuration) · [Errors](https://chukhali.github.io/MisaConnect/esign/typescript/errors) · [Concepts](https://chukhali.github.io/MisaConnect/esign/#concepts) · [CHANGELOG](https://github.com/ChuKhaLi/MisaConnect/blob/main/src/typescript/esign/CHANGELOG.md).

## License

MIT. Community project, not affiliated with MISA JSC.

# Express sample

```
npm run start -w @misaconnect/sample-express
```

`start` type-checks first (`tsc --noEmit`) and then runs `server.ts` directly with Node's built-in
TypeScript type stripping, so no build step or extra runtime dependency is needed.

Requires **Node.js >= 22.18** (the workspace baseline): type stripping is enabled by default from
that version. On Node 22.6 - 22.17 add `--experimental-strip-types` to the `node` command; on
Node 20 run the file with a loader such as `tsx` instead. The `@misaconnect/einvoice` package
itself supports Node >= 20.

Set `MISACONNECT_SANDBOX_TAXCODE`, `MISACONNECT_SANDBOX_USERNAME`, `MISACONNECT_SANDBOX_PASSWORD`
and `MISACONNECT_SANDBOX_APPID` first.

## eSign endpoints

Set `MISACONNECT_ESIGN_SANDBOX_BASE_URL`, `MISACONNECT_ESIGN_SANDBOX_CLIENT_ID`,
`MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY`, `MISACONNECT_ESIGN_SANDBOX_USERNAME` and
`MISACONNECT_ESIGN_SANDBOX_PASSWORD` (the eSign client is only created on first use, so the
eInvoice endpoints work without them).

- `POST /esign/pdf` with `Content-Type: application/pdf` and the PDF as the raw body returns the
  signed PDF: `curl -H "Content-Type: application/pdf" --data-binary @in.pdf http://localhost:3000/esign/pdf -o out.pdf`.
  It needs an ACTIVE certificate on the account.
- `POST /esign/webhook/:secret` is the endpoint to register with MISA for webhook mode (the sample
  signs in polling mode; use `webhook: { mode: 'webhook' }` and `beginSignPdf` to start transactions).
  The SDK does not authenticate webhook requests, so the sample does: `ESIGN_WEBHOOK_SECRET` (at
  least 32 characters) must equal the `:secret` path segment (constant-time compare, 404 otherwise),
  and an optional `ESIGN_WEBHOOK_ALLOWED_IPS` (comma-separated IPv4 addresses or CIDRs) restricts
  the source address (403 otherwise; behind a proxy configure Express `trust proxy` so `req.ip` is
  the real client). It then calls `client.handleWebhook(req.body)` and answers with the ACK JSON.
  Keep the secret out of access logs.

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

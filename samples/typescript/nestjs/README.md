# NestJS sample

```
pnpm --filter @misaconnect/sample-nestjs run start
```

`start` compiles `src/` to `dist/` with `tsc -p tsconfig.build.json` and then runs
`node dist/main.js`. A build step is needed because Nest relies on decorators, which Node's
built-in TypeScript type stripping does not support. The `@misaconnect/einvoice` workspace
package must be built first (`pnpm --filter @misaconnect/einvoice run build`).

Set `MISACONNECT_SANDBOX_TAXCODE`, `MISACONNECT_SANDBOX_USERNAME`, `MISACONNECT_SANDBOX_PASSWORD`
and `MISACONNECT_SANDBOX_APPID` first; the app listens on http://localhost:3000 (`GET /templates`).

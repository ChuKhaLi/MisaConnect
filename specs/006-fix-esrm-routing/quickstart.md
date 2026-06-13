# Quickstart: ESRM Routing Fix (slice 006)

## For consumers (after upgrading to `MisaConnect.ESign 2.1.0`)

**No configuration change is required.** The SDK now normalizes `Misa:ESign:BaseUrl` to its origin, so both of these behave identically and route ESRM calls to the host root:

```jsonc
// Either of these works — the path is tolerated and ignored for routing.
"Misa": { "ESign": { "BaseUrl": "https://esignapp.misa.vn/" } }
"Misa": { "ESign": { "BaseUrl": "https://<sandbox-host>/webdev/" } }
```

Set `Environment` as you already do; login/two-factor location follows from it:

```jsonc
"Misa": {
  "ESign": {
    "Environment": "Sandbox",        // login/two-factor under /webdev/ (default for Sandbox)
    "BaseUrl": "https://<sandbox-host>/"
  }
}
```

### Override (rare)

Only if your environment serves login/two-factor at a different location than its `Environment` default:

```jsonc
"Misa": { "ESign": { "AuthUnderWebdev": true } }   // force login/two-factor under /webdev/
"Misa": { "ESign": { "AuthUnderWebdev": false } }  // force login/two-factor at host root
```

`AuthUnderWebdev` does **not** affect refresh/resend (always `/webdev/`) or ESRM (always host root).

## Pre-release verification (maintainers)

Before tagging `2.1.0`, confirm the **production** login path, since the default trusts the (internally inconsistent) doc that login is at the host root:

```bash
# Expect: which one returns 200 with tokens?
curl -i -X POST "https://esignapp.misa.vn/api/auth/api/v1/auth/login-api"        # root (doc / default)
curl -i -X POST "https://esignapp.misa.vn/webdev/api/auth/api/v1/auth/login-api" # /webdev/
```

If production login only works under `/webdev/`, ship guidance to set `AuthUnderWebdev = true` for Production (or change the Production default in a follow-up). No code change is needed to correct it in the field — the override covers it.

## Running the tests

```bash
dotnet test tests/MisaConnect.ESign.UnitTests          # fast, no network — route resolution + content-type guard + AuthUnderWebdev derivation
dotnet test tests/MisaConnect.ESign.IntegrationTests   # fake server (ESRM-at-root, 200 text/html guard, bearer assertion); sandbox facts skip without creds
dotnet format MisaConnect.slnx                         # required before PR
```

### What the new tests prove
- ESRM resolves at host root for a `/webdev/` base (Defect A).
- refresh/resend never double-prefix (Defect C).
- a `200 text/html` ESRM response throws a clear, secret-free exception — not "no active certificate" (Defect D).
- the `AuthorizationRM` bearer is the remote-signing token (Defect B guard).
- login/two-factor flip with `Environment` and with `AuthUnderWebdev`.

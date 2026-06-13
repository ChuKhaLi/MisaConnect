# Phase 1 Data Model: Fix ESRM Routing & Silent-Failure Hardening

This slice introduces no DTO/wire-shape changes (Principle IV). The "entities" here are the configuration member, the route-resolution mapping, and the new error condition.

## 1. Configuration: `MisaESignOptions.AuthUnderWebdev`

| Field | Type | Default | Validation | Notes |
|-------|------|---------|------------|-------|
| `AuthUnderWebdev` | `bool?` | `null` | none (any value valid) | Public member on the existing options type (section `Misa:ESign`). `null` ⇒ derive from `Environment`. |

**Effective value** (computed, not stored):

```
effectiveAuthUnderWebdev = AuthUnderWebdev ?? (Environment == ESignEnvironment.Sandbox)
```

- `Environment.Sandbox` (enum value `0`, the default) ⇒ `true` when override unset.
- `Environment.Production` (`1`) ⇒ `false` when override unset.
- Override, when set, always wins.

## 2. Route table: canonical endpoint identifier → request path

Canonical identifiers (`ESignHttpRoutes.*`) are **unchanged** and remain the keys used by `ESignErrorMapper`/`OtpErrorMapper` (exact-match dispatch). The resolver derives the *request path* (resolved against `BaseAddress = origin`).

| Canonical endpoint id (unchanged) | Request path — `effectiveAuthUnderWebdev = false` (Production default) | Request path — `effectiveAuthUnderWebdev = true` (Sandbox default) |
|---|---|---|
| `api/auth/api/v1/auth/login-api` | `api/auth/api/v1/auth/login-api` | `webdev/api/auth/api/v1/auth/login-api` |
| `api/auth/api/v1/auth/two-factor-auth` | `api/auth/api/v1/auth/two-factor-auth` | `webdev/api/auth/api/v1/auth/two-factor-auth` |
| `webdev/api/auth/api/v1/auth/refreshtoken` | `webdev/api/auth/api/v1/auth/refreshtoken` | `webdev/api/auth/api/v1/auth/refreshtoken` |
| `webdev/api/auth/api/v1/auth/resend-otp-auth` | `webdev/api/auth/api/v1/auth/resend-otp-auth` | `webdev/api/auth/api/v1/auth/resend-otp-auth` |
| `external/esrm/…` (5 routes) | `external/esrm/…` | `external/esrm/…` |

**Resolution rule**: only `login-api` and `two-factor-auth` differ between the two columns (prepend `webdev/`). All other rows are identity.

## 3. Base address composition

| Input `Misa:ESign:BaseUrl` | `HttpClient.BaseAddress` (after normalization) |
|---|---|
| `https://host/` | `https://host/` |
| `https://host/webdev/` | `https://host/` |
| `https://host/anything/else/` | `https://host/` |

Resolved absolute URL = `BaseAddress` + request path (relative, no leading slash). Example (Sandbox, base `https://host/webdev/`):
- cert list → `https://host/external/esrm/service/general/api/v1/Certificates/by-userId`
- login → `https://host/webdev/api/auth/api/v1/auth/login-api`

## 4. Error condition: non-JSON 2xx on an ESRM JSON endpoint

| Aspect | Value |
|--------|-------|
| Trigger | HTTP `2xx` on an ESRM JSON endpoint where content type is not JSON (not `application/json` / `*+json`), **or** a non-empty body fails to parse as the expected type. |
| Outcome | Throw `ESignGeneralException` (category `MisaUnknown`, code e.g. `UnexpectedContentType`). |
| Message contents | Endpoint (canonical id) + response content type + truncated body snippet (~256 chars). |
| Must NOT contain | `AuthorizationRM` bearer, credentials, or any request header value. |
| Distinct from | Empty/`[]` body → existing `NoActiveCertificateException` (legitimate "no active certificate"). |

## 5. State / flow (unchanged paths)

- Token acquisition/refresh (`EnsureAccessToken` → `AccessToken.Value = RemoteSigningAccessToken`) — **unchanged** (Defect B refuted).
- `RemoteSigningAuthHandler` `IsAuthEndpoint` matches by path suffix (`/login-api`, `/two-factor-auth`, `/refreshtoken`, `/resend-otp-auth`) — still correct after a `webdev/` prefix (suffix preserved).
- `401` refresh-and-retry flow — unchanged, now operating on corrected routes.

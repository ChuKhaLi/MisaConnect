# MISA eSign RemoteSigning — ESRM base-path & token bug report

> **Status:** confirmed, reproducible. **Component:** `MisaConnect.ESign` SDK (Infrastructure wire client + options).
> **Discovered:** 2026-06-13, while integration-testing a consumer backend against the MISA eSign **sandbox** (`https://testesignappv1.misa.vn`).
> **Impact:** the SDK reports **0 certificates / "no active certificate"** (and remote signing fails) even when the organisation has a valid **ACTIVE** certificate. Two independent defects; both are in the SDK; the consumer needs **no config change**.

> **⚠️ Verification note (2026-06-13, MisaConnect maintainers).** This report was adversarially verified against the SDK source and the official API doc. Findings, with corrections:
> - **Defect A (ESRM base path) — CONFIRMED.**
> - **Defect B (wrong bearer token) — REFUTED.** The SDK *already* sends `remoteSigningAccessToken` as the `AuthorizationRM` bearer: `AccessToken.Value = session.RemoteSigningAccessToken` (`EnsureAccessToken.cs:72`, `RefreshAccessToken.cs:18`), applied via `RemoteSigningAuthHandler` `token.Value`. `session.AccessToken` is parked in the unused `RawAccessToken`. The report's §4/§5 token claim and fix #3 describe behaviour the code already has — **no token change is needed**. The actual runtime failure is Defect A alone, silently masked by Defect D.
> - **Defect C (double `/webdev/`) — CONFIRMED but latent/conditional** (only bites under a `/webdev/` base; correct under a root base).
> - **Defect D (silent deserialize) — CONFIRMED**, with one correction: HTTP status *is* enforced (`IsSuccessStatusCode` + `ThrowMappedAsync`); the real gap is the absence of a **content-type/body-validity check on a 2xx**, not status.
> - **Root cause** is the single-`BaseUrl` model (auth lives under `/webdev/`, ESRM at root). Tracked for fix in the Spec Kit slice; design of the base-URL model is settled there.
> - Credentials in Appendix A have been **redacted**; set them from a local/secret source when reproducing.

---

## 1. TL;DR

The SDK cannot see certificates or sign because of **two** defects in the ESRM call path:

| # | Defect | Effect | Fix location |
|---|--------|--------|--------------|
| **A** | ESRM requests are sent under the auth app's `/webdev/` base path. The ESRM API lives at the **host root**. | `…/webdev/external/esrm/…` returns the SPA `index.html` (`200 text/html`). | SDK |
| **B** | ESRM requests send `data.accessToken`. The ESRM API requires `data.remoteSigningAccessToken`. | When the path is right, returns `401 access_token_invalid` (`e2001`). | SDK |
| **C** *(latent)* | `refreshtoken` / `resend-otp-auth` route constants bake in `webdev/`, but so does the configured base → `…/webdev/webdev/api/auth/…`. | Token refresh / OTP resend are broken. | SDK |
| **D** *(hardening)* | `Deserialize<T>` swallows `JsonException` on a `2xx` and returns `default`. | Defect A degraded **silently** to "0 certs" instead of erroring — this is what cost ~3h of diagnosis and got misread on 2026-06-12 as *"account has no active cert."* | SDK |

**Defect A + B together** mean the only request that actually returns the certificate is **host-root base + `remoteSigningAccessToken`**. This is confirmed against the live sandbox **and** the official `docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md`.

A pure config change (just editing `Misa:ESign:BaseUrl`) **cannot** fix this — see §6.

---

## 2. Symptom

The consumer calls `IMisaESignWireClient.ListCertificatesByUserIdAsync` (via its eSign provider → account-health / cert-list). Result:

```
login OK userId=3a7f29c1-5e84-4b16-9d20-6c8f1a40b7e2 needs2FA=False
certificates returned: 0
health: certCount=0 hasActiveCert=False warning='Tài khoản MISA chưa có chứng thư active …'
```

But the same account, in the MISA eSign web UI (`https://testesignappv1.misa.vn/certificate-management`), shows a valid **ACTIVE** organisation certificate.

---

## 3. Evidence

### 3.1 The certificate exists and is ACTIVE (web UI)

Logged in as `<sandbox-user>` (userId `3a7f29c1-…`). `Danh sách chứng thư số` shows:

| field | value |
|---|---|
| Name / Type | **Test Org** / Tổ chức (organisation) |
| MST | 4815263097-208 |
| Serial | `9C1F47A0DE3B82556614FAC07D9E3B28` |
| certificateId / keyAlias | `b1e9374c-08af-42d6-95b3-7c2e60d18f4a` |
| Validity | 25/03/2025 → **07/10/2026** |
| **Status** | **Đang hoạt động (ACTIVE)** |
| Subject | `E=<sandbox-user>, UID=MST:4815263097-208, CN=Test Org, O=Test Org, L=Quận Hoàn Kiếm, S=Hà Nội, C=VN` |

### 3.2 The ESRM `by-userId` endpoint returns it — under the right base + token

Same request (headers `x-clientId`, `x-clientKey`, `AuthorizationRM`), varying **token × base**:

| token | base | result |
|---|---|---|
| `accessToken` | `…/webdev/external/esrm/…` ← **what the SDK sends today** | `200` **text/html** (SPA `index.html`) |
| `accessToken` | `…/external/esrm/…` (root) | **`401` `access_token_invalid` (`e2001`)** |
| `remoteSigningAccessToken` | `…/webdev/external/esrm/…` | `200` **text/html** (SPA) |
| **`remoteSigningAccessToken`** | **`…/external/esrm/…` (root)** | **`200 application/json`** ✅ |

The only working combination returns exactly what the SDK's `List<WireCertDto>` already expects — a **bare JSON array**:

```json
[{"userId":"3a7f29c1-5e84-4b16-9d20-6c8f1a40b7e2",
  "keyAlias":"b1e9374c-08af-42d6-95b3-7c2e60d18f4a",
  "appName":"","keyStatus":"ACTIVE","certificate":"MIIE1DCC…"}]
```

### 3.3 Auth lives under `/webdev/`, not the root

`POST …/auth/api/v1/auth/login-api`:

| URL | result |
|---|---|
| `https://testesignappv1.misa.vn/api/auth/api/v1/auth/login-api` (root) | **`405 Not Allowed`** (nginx) |
| `https://testesignappv1.misa.vn/webdev/api/auth/api/v1/auth/login-api` | **`200`** JSON (tokens) |

→ **auth = `/webdev/…`, ESRM = root.** Two different base paths on the same host.

### 3.4 Cross-check against the official API doc

`docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md` confirms:

- `APIUrl = https://esignapp.misa.vn/` (production) — the **host root** (sandbox: `https://testesignappv1.misa.vn/`).
- All ESRM endpoints are `{APIUrl}/external/esrm/…` (lines 169-174, 330-400) — at root.
- **Use `remoteSigningAccessToken`** as the credential for the ESRM APIs, in header `AuthorizationRM` (lines 185, 229, 237). On `401`, refresh to obtain a new `remoteSigningAccessToken`.
- `refresh` / `resend-otp` are `{APIUrl}/webdev/api/auth/…` (lines 287, 316).

(The doc's "login token url" at line 165 is listed as `api/auth/…` without the `webdev/` prefix; empirically login is served only at `…/webdev/api/auth/…` — see §3.3. Treat the doc's login path as imprecise; auth clearly lives under `/webdev/`.)

---

## 4. Root-cause analysis (current SDK behaviour)

`MisaESignWireClient` builds every request against a single `HttpClient.BaseAddress = Misa:ESign:BaseUrl`. The consumer's sandbox config (and the SDK's own E2E default) set:

```
Misa:ESign:BaseUrl = https://testesignappv1.misa.vn/webdev/
```

Route constants (`ESignHttpRoutes`) combined with that base:

| route constant | current value | resolves to (base = `…/webdev/`) | correct? |
|---|---|---|---|
| `AuthLoginApi` | `api/auth/api/v1/auth/login-api` | `/webdev/api/auth/…login-api` | ✅ |
| `AuthTwoFactor` | `api/auth/api/v1/auth/two-factor-auth` | `/webdev/api/auth/…two-factor-auth` | ✅ |
| `AuthRefreshToken` | `webdev/api/auth/api/v1/auth/refreshtoken` | `/webdev/**webdev**/api/auth/…` | ❌ (Defect C) |
| `AuthResendOtp` | `webdev/api/auth/api/v1/auth/resend-otp-auth` | `/webdev/**webdev**/api/auth/…` | ❌ (Defect C) |
| `CertificatesByUserId` | `external/esrm/service/general/api/v1/Certificates/by-userId` | `/webdev/external/esrm/…` → SPA HTML | ❌ (Defect A) |
| `DocumentsHash` / `SigningHash` / `SigningStatus` / `DocumentsAttachment` | `external/esrm/…` | `/webdev/external/esrm/…` → SPA HTML | ❌ (Defect A) |

The route table is **internally inconsistent**: `login`/`two-factor` are written assuming the base already contains `/webdev/`, while `refresh`/`resend` and all `external/esrm/…` routes are written assuming a **root** base. There is **no single `BaseUrl` value** that satisfies both groups (see §6).

**Token (Defect B):** every authenticated SDK call is an ESRM call. `ApplyAuth` sets `AuthorizationRM: Bearer <accessToken>` where the token is `AuthSession.AccessToken` (`token.Value`). The ESRM API requires `AuthSession.RemoteSigningAccessToken`. The SDK parses `remoteSigningAccessToken` from the login response (`LoginDataBlockDto.RemoteSigningAccessToken`, mapped into `AuthSession`) but **never uses it** as a bearer.

**Silent failure (Defect D):** `MisaESignWireClient.Deserialize<T>` catches `JsonException` and returns `default`. On Defect A the response is `200 text/html`; `Deserialize<List<WireCertDto>>` throws, is swallowed, and `?? new List<…>()` yields an empty list. The caller (`ListActiveCertificates`) then reports "no ACTIVE certificate". A routing/content-type error masquerades as a business state.

---

## 5. Recommended fix (all in `MisaConnect.ESign`)

Keep `Misa:ESign:BaseUrl` as the **documented value the consumer already has** (`https://testesignappv1.misa.vn/webdev/` sandbox / the prod equivalent). Make the SDK aware that ESRM lives at the host root.

1. **Derive the ESRM base from the origin of `BaseUrl`.**
   - `HttpClient.BaseAddress` stays `BaseUrl` (auth keeps working).
   - For `external/esrm/…` routes, issue **absolute** requests against `origin(BaseUrl)` = `https://{host}/`.
   - Add optional `Misa:ESign:EsrmBaseUrl`, **default null → derived origin**, as an escape hatch if a future environment hosts ESRM elsewhere.
   - Files: `Infrastructure/ESign/MisaESignWireClient.cs` (`NewRequest` / ESRM call sites), `Infrastructure/Configuration/MisaESignOptions.cs`, DI wiring in `Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`.

2. **Harmonise the auth route constants.** Make all four auth routes root-of-`/webdev/` relative so they don't double-prefix:
   - `AuthRefreshToken`: `webdev/api/auth/api/v1/auth/refreshtoken` → `api/auth/api/v1/auth/refreshtoken`
   - `AuthResendOtp`: `webdev/api/auth/api/v1/auth/resend-otp-auth` → `api/auth/api/v1/auth/resend-otp-auth`
   - (`AuthLoginApi`, `AuthTwoFactor` already correct.)
   - File: `Infrastructure/ESign/ESignHttpRoutes.cs`. Fixes Defect C.

3. **Send `RemoteSigningAccessToken` on ESRM calls.** Every authenticated SDK call is ESRM, so the simplest correct change is to make the cached bearer carry `AuthSession.RemoteSigningAccessToken` (not `AccessToken`). The `AuthorizationRM` header then matches the doc.
   - Files: the token-acquisition path that builds the cached `AccessToken` value from `AuthSession` (e.g. `EnsureAccessToken` / the AcquireToken mapping) and/or `AuthSessionMapper`. Verify `RefreshAsync` likewise surfaces the refreshed `remoteSigningAccessToken`.

4. **Content-Type guard (hardening).** In `MisaESignWireClient`, on a `2xx` whose body is not JSON (or whose `Content-Type` is not `application/json`), throw a clear `ESignGeneralException` (include endpoint + content-type + a body snippet) rather than letting `Deserialize` swallow it. Fixes Defect D and would have surfaced Defect A immediately.

### Net effect after the fix

| call | base used | bearer |
|---|---|---|
| `login-api`, `two-factor-auth`, `refreshtoken`, `resend-otp-auth` | `BaseUrl` (`…/webdev/`) | none / refresh token |
| `Certificates/by-userId`, `documents/hash`, `Signing/hash`, `Signing/status`, `documents/attachment` | `origin(BaseUrl)` (host root) | **`remoteSigningAccessToken`** |

---

## 6. Why a config-only fix is impossible

The route table is inconsistent, so neither `BaseUrl` value works for everything:

| `BaseUrl` | login / two-factor | refresh / resend | ESRM (certs, sign) |
|---|---|---|---|
| `…/webdev/` (current) | ✅ | ❌ `/webdev/webdev/…` | ❌ `/webdev/external/esrm/…` → SPA |
| `…/` (root) | ❌ `405` at `/api/auth/…` | ✅ | ✅ |

Best case, flipping the base to root trades "silent 0 certs" for "login broken + 401". And **Defect B (token) has no config lever at all** — it is hardcoded that the bearer is `AccessToken`. So the SDK must change regardless.

---

## 7. Consumer impact

**None.** `Misa:ESign:BaseUrl` stays as-is (`https://testesignappv1.misa.vn/webdev/` sandbox; prod equivalent). After bumping the `MisaConnect.ESign` package, the existing provider works. No appsettings/env edits.

---

## 8. Test guidance (`FakeMisaESignServer`)

- ESRM requests (`external/esrm/…`) are issued to the **origin** (no `/webdev/` segment) and carry `AuthorizationRM: Bearer <remoteSigningAccessToken>` + `x-clientId` / `x-clientKey`.
- Auth requests (`login-api`, `two-factor-auth`, `refreshtoken`, `resend-otp-auth`) are issued under `BaseUrl` (`/webdev/api/auth/…`) with **no double `/webdev/`**.
- A `2xx` response with `Content-Type: text/html` (or a non-JSON body) throws a clear `ESignException` (regression test for Defect D).
- `EsrmBaseUrl` override, when set, takes precedence over the derived origin.

---

## Appendix A — reproduction curls (sandbox)

> Sandbox test credentials (disposable, **not production** — redact before wide sharing). Mirrors the consumer's `.env`.
>
> ```
> x-clientId  : REDACTED-CLIENT-ID
> x-clientKey : REDACTED-CLIENT-KEY
> userName    : <sandbox-user>
> password    : REDACTED-PASSWORD
> host        : https://testesignappv1.misa.vn
> ```

**Login (returns `data.accessToken` and `data.remoteSigningAccessToken`):**

```bash
curl -s -X POST "https://testesignappv1.misa.vn/webdev/api/auth/api/v1/auth/login-api" \
  -H "x-clientId: REDACTED-CLIENT-ID" \
  -H "x-clientKey: REDACTED-CLIENT-KEY" \
  -H "X-Correlation-Id: 11111111-1111-4111-8111-111111111111" \
  -H "Content-Type: application/json" \
  -d '{"userName":"<sandbox-user>","password":"REDACTED-PASSWORD"}'
```

**`Certificates/by-userId` — what the SDK sends today (→ `200` HTML, silently 0 certs):**

```bash
curl -s "https://testesignappv1.misa.vn/webdev/external/esrm/service/general/api/v1/Certificates/by-userId" \
  -H "AuthorizationRM: Bearer <accessToken>" \
  -H "x-clientId: REDACTED-CLIENT-ID" \
  -H "x-clientKey: REDACTED-CLIENT-KEY" \
  -H "X-Correlation-Id: 11111111-1111-4111-8111-111111111111"
```

**`Certificates/by-userId` — corrected (root + `remoteSigningAccessToken` → returns the cert):**

```bash
curl -s "https://testesignappv1.misa.vn/external/esrm/service/general/api/v1/Certificates/by-userId" \
  -H "AuthorizationRM: Bearer <remoteSigningAccessToken>" \
  -H "x-clientId: REDACTED-CLIENT-ID" \
  -H "x-clientKey: REDACTED-CLIENT-KEY" \
  -H "X-Correlation-Id: 11111111-1111-4111-8111-111111111111"
```

**One-shot (login → extract remote-signing token → call):**

```bash
CID=11111111-1111-4111-8111-111111111111
CK=(-H "x-clientId: REDACTED-CLIENT-ID" -H "x-clientKey: REDACTED-CLIENT-KEY" -H "X-Correlation-Id: $CID")
RM=$(curl -s -X POST "https://testesignappv1.misa.vn/webdev/api/auth/api/v1/auth/login-api" "${CK[@]}" \
  -H "Content-Type: application/json" \
  -d '{"userName":"<sandbox-user>","password":"REDACTED-PASSWORD"}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['data']['remoteSigningAccessToken'])")
curl -s "https://testesignappv1.misa.vn/external/esrm/service/general/api/v1/Certificates/by-userId" \
  -H "AuthorizationRM: Bearer $RM" "${CK[@]}"
```

---

## Appendix B — affected source (for the implementer)

- `src/MisaConnect.ESign.Infrastructure/ESign/ESignHttpRoutes.cs` — route constants (Defects A, C).
- `src/MisaConnect.ESign.Infrastructure/ESign/MisaESignWireClient.cs` — `NewRequest` / `ApplyAuth` / `Deserialize` (Defects A, B, D).
- `src/MisaConnect.ESign.Infrastructure/ESign/Mapping/AuthSessionMapper.cs` + the token-acquisition path that produces the cached bearer (Defect B).
- `src/MisaConnect.ESign.Infrastructure/Configuration/MisaESignOptions.cs` + `DependencyInjection/ServiceCollectionExtensions.cs` — `EsrmBaseUrl` option + origin derivation.

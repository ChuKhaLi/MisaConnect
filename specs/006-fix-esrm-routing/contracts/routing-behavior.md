# Contract: Routing & Silent-Failure Behavior

Behavioral guarantees this slice must satisfy. Each maps to acceptance scenarios in [spec.md](../spec.md) and is asserted by tests (see [research.md](../research.md) §D8).

## C1 — ESRM at host root, base-path tolerant (FR-001, FR-002)

Given any configured `Misa:ESign:BaseUrl` of the form `https://host[/anypath]/`, ESRM requests MUST resolve to `https://host/external/esrm/…` (host root).

| `BaseUrl` | Resolved cert-list URL |
|---|---|
| `https://host/` | `https://host/external/esrm/service/general/api/v1/Certificates/by-userId` |
| `https://host/webdev/` | `https://host/external/esrm/service/general/api/v1/Certificates/by-userId` |

Both MUST be byte-identical. (Asserted by unit test on resolved absolute URLs.)

## C2 — Refresh/resend single `/webdev/` (FR-003)

`refreshtoken` and `resend-otp` MUST resolve to exactly one `/webdev/` segment for every accepted base value — never `…/webdev/webdev/…`.

## C3 — Login/two-factor location by environment + override (FR-004, FR-005)

| `Environment` | `AuthUnderWebdev` | login / two-factor resolve to |
|---|---|---|
| Production | `null` | `https://host/api/auth/…` (root) |
| Sandbox | `null` | `https://host/webdev/api/auth/…` |
| Production | `true` | `https://host/webdev/api/auth/…` (override wins) |
| Sandbox | `false` | `https://host/api/auth/…` (override wins) |

Changing this MUST NOT change the resolved URL of any ESRM/refresh/resend route. (Asserted by unit tests across the matrix.)

## C4 — Non-JSON 2xx fails loudly (FR-006, FR-007, FR-008)

| Response on ESRM JSON endpoint | Required outcome |
|---|---|
| `200` + `Content-Type: text/html` (SPA) | Throw `ESignGeneralException` naming endpoint + content type + body snippet. MUST NOT return an empty list or `NoActiveCertificateException`. |
| `200` + `application/json` body `[]` | Empty certificate list → `NoActiveCertificateException` (existing business outcome). |
| `200` + `application/json` valid array | Parsed normally. |
| non-`2xx` | Existing error mapping (unchanged). |

The thrown exception MUST NOT contain the `AuthorizationRM` bearer or any credential. (Asserted by an integration test against a fake endpoint returning `200 text/html`.)

## C5 — Bearer unchanged (FR-009)

The `AuthorizationRM` bearer on ESRM calls MUST remain the remote-signing access token. (Asserted by an integration test that inspects the header value the fake server receives.)

## C6 — No regression (FR-010, SC-005)

The `401` refresh-and-retry flow and all pre-existing unit + integration tests MUST continue to pass against the corrected routes.

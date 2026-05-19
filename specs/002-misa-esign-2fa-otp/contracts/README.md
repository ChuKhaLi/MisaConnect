# Slice 2 contracts — `MisaConnect.ESign` two-factor authentication

This directory pins the three contracts that slice 2 of `MisaConnect.ESign` extends. Each is an additive delta on top of the corresponding slice-1 contract.

| File | Scope |
|---|---|
| [public-surface.md](./public-surface.md) | Public API additions: new methods on `IMisaESignClient`, the new `IOtpProvider` port, the new public records and enum, the new exception types, and the new `MisaESignOptions.Otp` block. |
| [wire-envelopes.md](./wire-envelopes.md) | The two new MISA HTTP request/response shapes — E8 `/two-factor-auth` and E9 `/resend-otp-auth` — mirroring the doc verbatim per Constitution Principle IV. |
| [error-mapping.md](./error-mapping.md) | The hybrid mapping for `/two-factor-auth` rejections (canonical errorCode table first, substring fallback second), the resend-otp typed-result table, and the keyword tables that drive the fallback. |

All three are **incremental** — they list only the slice-2 additions. The slice-1 contracts under [specs/001-misa-esign-pdf-sign-flow/contracts/](../../001-misa-esign-pdf-sign-flow/contracts/) remain authoritative for everything they already covered.

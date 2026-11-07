# Changelog

All notable changes to `@misaconnect/esign` are documented here.

## [0.3.0] - 2026-11-07

### Added

- `TokenCache.deleteIfMatches?(key, value)`, an optional compare-and-delete: removes the entry only while its token value matches. `createInMemoryTokenCache()` implements it. Existing custom caches keep working unchanged (same as .NET `ITokenCache.RemoveIfMatchesAsync` in `MisaConnect.ESign` 2.7.0).

### Fixed

- After a failed refresh, the SDK no longer evicts a token another call stored meanwhile. The first-token and 401-retry paths run in separate shared flights, so one could evict the token the other had just stored, costing an extra login (or a spurious OTP prompt for a two-factor account). It now reuses a fresh token stored by another call, and otherwise evicts only the stale token (through `deleteIfMatches` when the cache has it), as .NET `MisaConnect.ESign` has since 2.6.0.

## [0.2.0] - 2026-11-07

### Changed (breaking)

- Requires **Node.js 22 or later** (`engines.node` `>=22`; was `>=20`). Node.js 20 reached end of life on 2026-04-30. The build now targets `node22`; no API change. CI exercises the built package on Node 22.0.0, 22 and 24.

## [0.1.3] - 2026-11-06

### Fixed

- A refresh result that is already expired (for example `expiresIn: 0`) now counts as a failed refresh and falls back to login, as in .NET `MisaConnect.ESign` since 2.5.0.
- A non-integer `expiresIn` (for example `10.5`) counts as missing (300 s), matching the documented rule and .NET.

## [0.1.2] - 2026-11-06

### Fixed

- `expiresIn` lifetime, same rule as .NET `MisaConnect.ESign`: missing or non-numeric now means 300 s (it was 0, which refreshed the token on every request); 1–59 s is raised to 60 s; an explicit 0 or less is honoured as already expired.

## [0.1.1] - 2026-11-05

First release published from CI through npm trusted publishing, with provenance. 0.1.0 was published manually without provenance.

### Changed

- `package.json` `repository.url` uses the `git+https://` form npm expects.
- CHANGELOG: `tokenFingerprintSecret` is listed under 0.1.0, where it actually shipped.

## [0.1.0] - 2026-11-04

First release: a TypeScript port of the .NET `MisaConnect.ESign` package.

### Added

- `createMisaESignClient(options)` returning `IMisaESignClient` with `signPdf`, `signXml` (string and bytes via `SignXmlRequest`), `signWord`, `signExcel`, `signInWithOtp`, `resendOtp`, `beginSignPdf`, `beginSignXml`, `beginSignWord`, `beginSignExcel` and `handleWebhook`.
- `MisaESignOptions`: environment and base URL checks, static / provider / per-call credentials, polling and transport-retry settings, OTP and webhook settings, raw-error opt-in.
- Swappable ports with defaults: `TokenCache`, `CertificateSelector`, `OtpProvider`, `MisaCredentialsProvider`, `SigningSessionStore`, `WebhookDeliveryHook`, `Clock`, `Delay`, `Logger` (`fromPino`).
- Typed errors (`ESignException` and subclasses, `isESignException`) with the .NET categories and error-code mapping.
- Built-in `node:https` HTTP client with classical TLS key-exchange groups.
- `tokenFingerprintSecret` option (at least 32 characters). The CHANGELOG inside the 0.1.0 tarball lists this under "Unreleased", but it shipped in 0.1.0. Hosts that share a `tokenCache` (e.g. Redis) and set the same secret now reuse each other's cached tokens: the token's credential fingerprint becomes HMAC-SHA256(secret, password + clientKey) instead of a random per-instance salt. Without the option the behaviour is unchanged (no reuse across processes or restarts). A wrong password still never matches a cached token.

### Differences from .NET

See the README section "Differences from the .NET SDK" (token refresh, `Signing/hash` retries, token fingerprint binding, per-principal OTP challenges, webhook confirmation via `Signing/status`, and more).

### Network

- The default HTTP agents allow 2 s per happy-eyeballs address attempt (`autoSelectFamilyAttemptTimeout: 2000`) instead of Node's 250 ms. MISA publishes an AAAA record, and from outside Vietnam the IPv4 TCP handshake alone takes ~210–250 ms, so Node's default timed out intermittently (`ETIMEDOUT`). Same fix as `@misaconnect/einvoice` 0.1.4.

### Known limitation

Signing has not been verified against the live sandbox yet (the sandbox account has no ACTIVE certificate); sign-in works.

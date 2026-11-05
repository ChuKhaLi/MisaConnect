# Changelog

All notable changes to `@misaconnect/esign` are documented here.

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

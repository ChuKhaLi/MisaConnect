---
title: ESign
nav_order: 3
has_children: true
permalink: /esign/
---

# ESign
{: .no_toc }

SDKs for **MISA eSign RemoteSigning (chữ ký số từ xa)**, in two languages with the same operations:

| Language | Package | Start here |
| --- | --- | --- |
| .NET 8 / .NET 10 | [`MisaConnect.ESign`](https://www.nuget.org/packages/MisaConnect.ESign) | [.NET guide](./dotnet/) |
| TypeScript (Node ≥ 22) | [`@misaconnect/esign`](https://www.npmjs.com/package/@misaconnect/esign) | [TypeScript guide](./typescript/) |

MISA eSign RemoteSigning signs documents with a certificate that MISA-CA holds for the signer. Your application sends the document; the signer approves the request on the **MISA eSign** mobile app; MISA returns the signature and the SDK embeds it. Both SDKs sign **PDF, XML (XAdES), Word (`.docx`) and Excel (`.xlsx`)**, either blocking until the signer approves (polling) or non-blocking (webhook mode), and handle two-factor (OTP) sign-in. They call these MISA endpoints, with the wire format kept verbatim: the auth app — `login-api`, `two-factor-auth`, `resend-otp-auth`, `refreshtoken`; and the eSign service (ESRM) — `Certificates/by-userId`, `documents/hash`, `Signing/hash`, `Signing/status`, `documents/attachment`. They also accept MISA's inbound signing-status webhook. `Certificates/by-certId` is not used. The full MISA API document (Vietnamese) is in the repository under [`docs/misa-api-reference/`](https://github.com/ChuKhaLi/MisaConnect/tree/main/docs/misa-api-reference).

{: .note }
Live signing has not been verified against MISA yet: the sandbox account has no ACTIVE remote-signing certificate, so signing, two-factor and webhook flows are tested against an in-repo fake MISA server. Sign-in against the real sandbox works.

## Concepts

These apply to both SDKs. The language guides link here instead of repeating them.

### Certificates

A **certificate** (chứng thư số, CTS) is the remote-signing key MISA-CA issues to a user. MISA lists a user's certificates with a `keyStatus` of `ACTIVE` or `INACTIVE`; only an `ACTIVE` certificate can sign. Before every signing flow the SDK lists the signer's certificates (`Certificates/by-userId`), keeps the active ones, and picks one — by default the **first active certificate**. You can replace that choice with your own certificate selector (by alias, issuer, expiry, …).

If the account has no active certificate, the SDK fails with `NoActiveCertificateException` before any document is sent. Certificates are provisioned on MISA's side (the eSign portal, or a test certificate for a sandbox account); the SDK cannot create them. The signer also needs the MISA eSign app installed and signed in to approve requests (on a test site, hold the eSign logo for about 5 s in the app to switch environment).

### How signing works

MISA signs a **hash** of the document, not the document itself. One sign call runs this sequence:

1. **Sign in** (`login-api`) — or reuse a cached token. MISA returns a `remoteSigningAccessToken`, sent as the `AuthorizationRM` header; every call also carries `x-clientId` / `x-clientKey`.
2. **List certificates** and pick an active one (see [Certificates](#certificates)).
3. **Hash the document** (`documents/hash`) — MISA computes the hash server side, per format (`pdfDocs`, `xmlDocs`, `wordDocs`, `excelDocs`).
4. **Submit the hash for signing** (`Signing/hash`) — this starts a transaction (`transactionId`) and sends an approval request to the signer's phone. Because a repeated call would start a second transaction (a second approval request), both SDKs retry `Signing/hash` only on HTTP 429 or when the connection was never made (DNS or connection failure), never on 5xx, timeouts or resets.
5. **Wait for the signer** — poll `Signing/status` until the transaction is `SUCCESS`, `FAILED` or `CANCELLED`, or until the total timeout (default 60 s). In [webhook mode](#webhook-mode) this step is replaced by MISA's callback.
6. **Attach the signature** (`documents/attachment`) — MISA embeds the signature into the document and the SDK returns the signed bytes.

Tokens are cached per principal (`{userName}|{clientId}|{host}`) and refreshed shortly before they expire (30 s skew). A token whose `expiresIn` is missing is treated as valid for 300 s; 1–59 s is raised to 60 s. On a 401 the SDK refreshes once and retries; if the refresh fails it signs in again once before failing with an authentication error. Concurrent flows for the same principal share one refresh. Transient failures (429, 5xx, timeouts, connection errors) are retried with exponential backoff and jitter, honouring `Retry-After`; `Signing/hash` is retried only when a retry cannot start a second transaction (a second approval on the signer's phone).

Routing: give the SDK the **host root** as base URL (production `https://esignapp.misa.vn/`). The SDK keeps only the origin: ESRM endpoints are always at the host root, `refreshtoken` / `resend-otp-auth` always under `/webdev/`, and `login-api` / `two-factor-auth` under `/webdev/` for the sandbox and at the host root for production (overridable).

### Two-factor sign-in (OTP)

If the account has two-factor authentication, MISA answers sign-in with error code **`122`**. The SDK then needs a one-time code, delivered by SMS/email (`otpType` 0) or the authenticator app (`otpType` 1), and exchanges it at `two-factor-auth`. MISA can be asked to send the code again (`resend-otp-auth`). There are two ways to supply the code:

- **Transparent** — you register an OTP provider. When MISA asks for a code, the SDK calls your provider, completes the exchange and retries the operation once. Your sign call never sees the challenge.
- **Explicit** — without a provider, the sign call fails with `AuthenticationFailedException` whose `Requires2FA` / `requires2FA` is true. You collect the code from the user, call the sign-in-with-OTP operation, then repeat the sign call.

Challenges are tracked **per principal** (`clientId` + `userName`): the OTP and resend calls act on the challenge of the current caller's credentials only. The tokens from a successful exchange are cached like any other token, so the next sign call does not ask again. A webhook finalize never prompts for an OTP.

Language guides: [.NET](./dotnet/two-factor) · [TypeScript](./typescript/two-factor).

### Webhook mode

Instead of blocking until the signer approves, a **begin-sign** operation runs steps 1–4 of [How signing works](#how-signing-works), records a signing session for the `transactionId`, and returns. When the signer approves or declines, MISA POSTs a JSON envelope (`messageId`, `clientId`, `extraData`, `status`, `errorCode`, `transactionId`, `signatures[]`) to the URL you registered with MISA (registration is out of band; the SDK does not register it). Your endpoint passes the envelope to the SDK's webhook handler and returns the **ACK** JSON it produces (`errorCode`, `devMsg`, `userMsg`) to MISA.

Rules both SDKs follow:

- **The webhook is only a trigger.** For every accepted webhook the SDK calls `Signing/status` with an authenticated request (one extra MISA call) and decides from MISA's answer, not from the webhook body. On `SUCCESS` it attaches MISA's signature; on `FAILED` / `CANCELLED` the outcome carries MISA's status and error code.
- **A terminal state is recorded once.** The first success, failure or cancellation of a transaction is stored; later webhooks for it get the stored ACK and do not reach your delivery hook again.
- **Bad input is answered, not thrown.** A malformed or unknown webhook gets a failure ACK (`webhook.malformed`, `webhook.client_id_mismatch`, `webhook.unknown_transaction`, `webhook.incomplete_success`, `webhook.document_id_mismatch`); a failure while confirming or attaching gets `webhook.finalize_failed`, and MISA may retry. Success is ACK `errorCode` `"0"`.
- **Finalize has no caller**, so it never prompts for an OTP and needs the credentials of the principal that began the transaction by itself. The session records that principal's `clientId` and `userName` (never secrets).
- **The SDK does not authenticate the HTTP request.** Protect your webhook route yourself: a long secret path segment compared in constant time, and/or an IP allowlist.
- **Sessions live in a session store.** The default is in-memory and single-process (24 h lifetime). Hosts with several instances need a shared store.

The SDKs differ on one default: .NET 2.x also delivers validation failures to your hook (until 3.0); TypeScript never does. Language guides: [.NET](./dotnet/webhooks) · [TypeScript](./typescript/webhooks).

### Credentials modes

A MISA principal is four values: `clientId` and `clientKey` (the integration, issued by MISA when you register for the API; MISA's API document says the pair serves both sandbox and production) and `userName` and `password` (the signer's MISA ID). The SDK uses them for the sign-in body, the `x-clientId` / `x-clientKey` headers and the token-cache key.

- **Static** — one principal for the whole process, from configuration.
- **Dynamic / per call** — each signing call resolves its own principal, for example per signer from a vault. Tokens and OTP challenges are then kept per principal. In webhook mode the finalize step asks your code for the credentials of the principal recorded on the session.

Credentials are secrets: the SDKs never log them, and neither should you. Language guides: [.NET](./dotnet/credentials) · [TypeScript](./typescript/credentials).

### Error categories

Every SDK failure is an `ESignException` (or a subclass) carrying a **category**, MISA's raw error code (or a code the SDK synthesized), a detail message, the correlation ID and the document format. The categories are the same in both SDKs:

| Category | Meaning |
| --- | --- |
| `Validation` | Bad options or request, rejected before any HTTP call. |
| `Authentication` | Sign-in failed, the token was rejected, or MISA needs an OTP (`122`). OTP rejections (invalid, expired, attempts exhausted, other) are subclasses. |
| `NoActiveCertificate`, `CertificateLookupFailed` | No usable certificate, or the certificate list failed. |
| `HashRejected`, `SignRejected`, `AttachmentRejected`, `StatusLookupFailed` | MISA refused that step of the flow. `SignRejected` can mean the user's remote-signing account is not set up. |
| `SignTerminalFailed`, `SignTerminalCancelled`, `SignTerminalUnknown`, `SignTimeout` | The transaction ended without a signature, or the signer did not approve in time. |
| `Transport` | Network failure, timeout or unusable response after the retry budget. |
| `WebhookValidation`, `WebhookFinalize` | A webhook envelope was rejected, or confirming it failed. These surface as ACK codes, not exceptions. |
| `MisaUnknown` | An error MISA returned that has no better mapping. |

MISA's raw `userMsg` / `devMsg` text is left out of error details unless you opt in (`IncludeRawErrorMessage` / `includeRawErrorMessage`). Language guides: [.NET](./dotnet/errors) · [TypeScript](./typescript/errors).

### Calling MISA from outside Vietnam

From Vietnam, MISA is reached reliably. From abroad, the path from GitHub-hosted runners to MISA's eInvoice sandbox host `testapi.meinvoice.vn` was measured (2026-11-03) as a **path-MTU blackhole**: TCP segments near 1500 bytes are dropped and the ICMP message that would make the sender shrink them never arrives. Any request larger than about 1.4 KB hangs and ends in a connection reset. Authenticated calls carry a large bearer token, and signing calls carry base64 documents, so they are affected; small requests still succeed, which makes the failure look intermittent. Every HTTP client suffers equally (curl too), so no SDK setting fixes it.

- Lower the MTU of the interface that reaches MISA, e.g. `ip link set dev eth0 mtu 1280` on Linux (this is what the repository's CI does), or try TCP MTU probing (`sysctl -w net.ipv4.tcp_mtu_probing=1`; untested against MISA).
- Some hosts also fail to open a TCP connection at all from abroad; retry from another host.

For eSign, sign-in has been verified from GitHub runners with MTU 1280. The hash / sign / attachment calls have so far only been exercised against the in-repo fake server, because the sandbox account used has no active certificate; the same advice very likely applies to them. The TypeScript SDK also pins classical TLS key exchange and lengthens Node's happy-eyeballs connect timeout; see the [TypeScript guide](./typescript/). The same network findings are described for EInvoice under [EInvoice → Calling MISA from outside Vietnam](../einvoice/#calling-misa-from-outside-vietnam).

# Error mapping — slice 4 additions

This file pins the slice 4 mapping table **A.11** — webhook validation failures + finalize failures → typed exception subclass → outbound ACK code. It extends the slice-1 mapping table A.1, the slice-2 OTP mapping A.5, and the slice-3 per-format mapping A.10. All four tables together are the authoritative reference for any `ESignErrorMapper` decision the SDK makes.

## A.11. Webhook handler mappings (FR-082 + FR-081)

The webhook handler's four-step validation per FR-082 + the finalize-failure path per FR-081 produce exactly these typed exceptions and ACK codes. Every mapping is deterministic — no `errorCode + devMsg` substring heuristics (unlike slice 2's A.5 and slice 3's A.10 which deal with MISA-side error codes).

### A.11.1 — Webhook validation failures (FR-082)

| # | Validation step (FR-082) | Failure condition | Typed exception | `WebhookValidationCategory` | ACK `errorCode` | `Format` on exception | Pre/post-session-resolution |
|---|---|---|---|---|---|---|---|
| 1 | Step 1 — shape | JSON deserialization fails OR required field missing OR `status` value not in {`SUCCESS`, `FAILED`, `CANCELLED`} | `MalformedEnvelopeException` | `MalformedEnvelope` | `webhook.malformed` | `Unknown` | pre |
| 2 | Step 2 — clientId match | `envelope.ClientId != options.Value.ClientId` (case-sensitive) | `ClientIdMismatchException` | `ClientIdMismatch` | `webhook.client_id_mismatch` | `Unknown` | pre |
| 3 | Step 3 — session existence | `ISigningSessionStore.TryGetByTransactionIdAsync(envelope.ClientId, envelope.TransactionId)` returns null OR record has expired | `UnknownTransactionException` | `UnknownTransaction` | `webhook.unknown_transaction` | `Unknown` | pre |
| 4 | Step 4a — success-shape, empty signatures | `envelope.Status == SUCCESS && envelope.Signatures.Count == 0` | `IncompleteSuccessEnvelopeException` | `IncompleteSuccessEnvelope` | `webhook.incomplete_success` | session's `Format` | post |
| 5 | Step 4b — success-shape, unknown documentId | `envelope.Status == SUCCESS && exists s in envelope.Signatures where s.DocumentId not in session.RecordedDocumentIds` | `DocumentIdMismatchException` | `DocumentIdMismatch` | `webhook.document_id_mismatch` | session's `Format` | post |

Validation runs in numeric order (1 → 5); the first failure short-circuits — no later check runs once a failure is raised. The validation pipeline diagram is in [data-model.md §4.2](../data-model.md).

### A.11.2 — Finalize-attempt failures (FR-081)

When validation passes and `HandleWebhook` invokes `FinalizeFromWebhook` which calls slice 3's `AttachSignature{Format}` use case, the attempt may fail. Per FR-081, failures are **returned as failure ACKs but NEVER cached** on the session record. The session remains in `PRE_FINALIZE` so MISA's next webhook delivery for the same `transactionId` triggers a fresh attempt.

| # | Failure source | Typed exception caught | ACK `errorCode` | Delivery hook invoked? | Session cache changed? |
|---|---|---|---|---|---|
| 6 | `/documents/attachment` transport failure (transport retry budget exhausted per slice-1 FR-023) | `ESignTransportException` | `webhook.finalize_failed` | no (FR-081) | no |
| 7 | `/documents/attachment` MISA 4xx/5xx mapped by slice-1 `ESignErrorMapper` to one of `SignRejectedException` / `SignTerminalStateException` / `AuthenticationFailedException` | The thrown exception's type (logged for diagnosis) | `webhook.finalize_failed` | no (FR-081) | no |
| 8 | Any unexpected exception (e.g. `OperationCanceledException`, internal SDK bug) | The thrown exception's type | `webhook.finalize_failed` | no (FR-081) | no |

The single `webhook.finalize_failed` code covers all finalize-failure causes because MISA's retry behavior doesn't differentiate by SDK-internal failure type — the consumer can inspect the structured log (FR-088) to diagnose specific finalize failures via the captured exception type.

### A.11.3 — Terminal-status short-circuit (FR-083)

When `envelope.Status == FAILED` or `CANCELLED`, the handler short-circuits before finalize. No exception is thrown; the consumer's delivery hook is invoked with a `TerminalWithoutFinalize` outcome; the ACK returned to MISA is a SUCCESS ACK (the webhook was successfully received and bookkeeping was updated — MISA's transaction-level failure is reported via the delivery hook, not via the ACK).

| Envelope status | ACK `errorCode` | Delivery hook invoked? | Delivery outcome variant |
|---|---|---|---|
| `FAILED` | `"0"` (success ACK) | yes | `WebhookOutcome.TerminalWithoutFinalize` with `Status = Failed` + MISA's `ErrorCode` |
| `CANCELLED` | `"0"` (success ACK) | yes | `WebhookOutcome.TerminalWithoutFinalize` with `Status = Cancelled` + MISA's `ErrorCode` |

## A.11.4 — `DocumentFormat` discriminator on every typed exception (slice 3's FR-062 carries through)

Every typed exception slice 4 introduces inherits the `Format` property contract from slice 3:

| Exception | `Format` value at construction | Why |
|---|---|---|
| `MalformedEnvelopeException` | `DocumentFormat.Unknown` | Pre-session-resolution — the SDK can't know which format the envelope was meant for if the envelope itself is unparseable. |
| `ClientIdMismatchException` | `DocumentFormat.Unknown` | Pre-session-resolution. |
| `UnknownTransactionException` | `DocumentFormat.Unknown` | Pre-session-resolution. The MISA-supplied `transactionId` is preserved on `MatchedTransactionId` so the consumer can correlate, but the session was never registered (or has expired) so the format is genuinely unknown. |
| `IncompleteSuccessEnvelopeException` | session's resolved `Format` | Post-session-resolution; the session record carries the format. |
| `DocumentIdMismatchException` | session's resolved `Format` | Post-session-resolution. |

The `Unknown` sentinel matches slice 3's documented usage ("set on every surfaced exception including failures that occur before the per-format DTO is built" per slice-3 FR-062) — consumers that branch on `(ErrorType, Format)` get `(MalformedEnvelope, Unknown)` for pre-resolution failures and `(IncompleteSuccessEnvelope, Pdf)` (or `Xml` / `Word` / `Excel`) for post-resolution failures. This matches slice 3's "format-aware error branching" UX.

## A.11.5 — Idempotency-vs-mapping interaction (FR-080)

The mapping table above describes what happens when validation runs against an inbound envelope. For deliveries that hit a cached success ACK (FR-080 — duplicate delivery with `session.CachedSuccess != null`), NO mapping decision is made:

- The validation pipeline runs up to step 3 (session lookup succeeds and finds a record with `CachedSuccess != null`).
- The handler returns the cached `WebhookHandleResult` directly — same `WebhookAck.ErrorCode = "0"`, same `WebhookAck.DevMsg / UserMsg` as the first successful delivery.
- The delivery hook is NOT invoked.
- The observed-set is updated to include the new `messageId` per FR-080.

This means a single `transactionId` can produce mapping decisions from table A.11.1–A.11.3 ONLY for the first delivery (or the first delivery after a finalize failure); subsequent SUCCESS deliveries hit the cache and bypass mapping entirely.

## A.11.6 — Mode-guard mapping (FR-093)

The FR-093 mode-guard throws a plain `InvalidOperationException` (not an `ESignException`) because the failure is a host-configuration mistake, not a MISA-protocol failure. No ACK is involved — the exception surfaces synchronously to the Begin/Sign call site at the facade boundary.

| Guard | Mode value | Throws |
|---|---|---|
| `BeginSign{Format}Async` | `MisaESignOptions.Webhook.Mode == Polling` | `InvalidOperationException("webhook mode disabled — set Misa:ESign:Webhook:Mode to Webhook or Both to use Begin facades")` |
| `Sign{Format}Async` | `MisaESignOptions.Webhook.Mode == Webhook` | `InvalidOperationException("polling mode disabled — set Misa:ESign:Webhook:Mode to Polling or Both to use blocking facades")` |

Tests in `tests/MisaConnect.ESign.UnitTests/Webhook/ModeGuardTests.cs` verify both directions.

## Cross-reference with slices 1/2/3 mapping tables

| Table | Source slice | Scope |
|---|---|---|
| A.1 | Slice 1 | Slice-1 endpoint error mapping (login, refresh, /Certificates/by-userId, /documents/hash, /Signing/hash, /Signing/status, /documents/attachment for PDF) |
| A.5 | Slice 2 | OTP-flow hybrid mapping (canonical-code table + substring synthesis on `errorCode + devMsg`) |
| A.10 | Slice 3 | Per-format hash + attachment hybrid mapping (synthesized `InvalidXmlInput`, `MissingMainDom`, `MissingSignatureId`, `UnsupportedDocumentVariant`, `IncompleteHashResponse`, `MissingSignedDocument` codes) |
| A.11 | Slice 4 (this table) | Webhook validation + finalize-failure mapping; deterministic, no substring synthesis |

The `ESignErrorMapper` in `Application/Errors/ESignErrorMapper.cs` is extended with a `WebhookValidationException`-aware dispatch that consults A.11; the existing A.1/A.5/A.10 dispatch paths are unchanged.

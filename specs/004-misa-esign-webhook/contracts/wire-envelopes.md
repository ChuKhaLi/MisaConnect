# Wire envelopes — slice 4 additions

Slice 4 introduces two new MISA wire shapes (E12 inbound + E13 outbound) and reuses every existing envelope from slices 1+3 unchanged on the webhook-mode finalize path. Field names and casing mirror MISA's published shape verbatim per Constitution Principle IV — including the inconsistent casing between the per-format request entries (PascalCase per MISA §4.1.x) and the inbound webhook envelope (camelCase per MISA §3.8 / §4.9).

The Postman collection at `https://drive.usercontent.google.com/u/0/uc?id=1E4GNoFrsU5UDModXx10lzm8c4jegggHn&export=download` is the tie-breaker if the doc and wire disagree (per the parent plan). Research R-3 flags one verification step: confirm `errorCode = "0"` is MISA's success-ACK sentinel before tasks-time implementation.

## E12. Inbound webhook envelope (MISA → consumer's webhook endpoint)

Source: MISA §3.8 ("Webhook nhận trạng thái ký số") + §4.9 (envelope structure).

### Request

| Element | Value |
|---|---|
| Method | `POST` |
| URL | Consumer-configured (registered with MISA out-of-band per Assumption 5). The sample API mounts it at `{Path}` or `{Path}/{secret}` per FR-099. |
| Headers | (none documented as required by MISA; the sample API uses standard ASP.NET correlation-ID processing — FR-086) |
| Body | JSON, see schema below |

### Body schema (camelCase per §4.9)

```json
{
  "messageId": "abc-123-xyz",
  "clientId": "your-client-id",
  "extraData": { "any": "opaque content MISA may attach server-side" },
  "status": "SUCCESS",
  "errorCode": null,
  "transactionId": "tx-987",
  "signatures": [
    { "documentId": "doc-1", "signature": "base64-encoded-signature-payload" }
  ]
}
```

### Field-level wire contract

| Wire field | Type | Required? | MISA-supplied | Notes |
|---|---|---|---|---|
| `messageId` | `string` | yes | yes | MISA-generated server-side per Clarifications Q1 + Assumption 8. The SDK first observes this on the inbound delivery; it is NOT sent on `/Signing/hash`. Used as the post-arrival dedupe key. |
| `clientId` | `string` | yes | yes (echoed) | Must match the configured `Misa:ESign:ClientId` exactly (case-sensitive) per FR-082 step 2. |
| `extraData` | `object` (heterogeneous) | optional | yes | Opaque to the SDK per Assumption 9. Deserialized as `IReadOnlyDictionary<string, JsonElement>`. NEVER logged (FR-087). |
| `status` | `string` enum | yes | yes | Documented values: `SUCCESS`, `FAILED`, `CANCELLED` (per §4.8 `Sign_status`). Any other value triggers `MalformedEnvelopeException` at shape validation. |
| `errorCode` | `string` | optional | yes | Populated when `status != SUCCESS`. Forwarded to the consumer's `IWebhookDeliveryHook` on `TerminalWithoutFinalize` outcomes per FR-083. |
| `transactionId` | `string` | yes | yes | Session-lookup key paired with `clientId` per FR-080. |
| `signatures` | `array` | yes (may be empty when `status != SUCCESS`) | yes | When `status == SUCCESS`, MUST be non-empty AND every entry's `documentId` MUST match one of `SigningSession.RecordedDocumentIds` (FR-082 step 4). Entries are returned in the order the documents were submitted to `/documents/hash`. |
| `signatures[].documentId` | `string` | yes | yes | Matches `SigningSession.RecordedDocumentIds` element. |
| `signatures[].signature` | `string` | yes | yes | The raw signature payload threaded into `/documents/attachment` per slice 3's `FR-056`. NEVER logged (FR-087); `ESignLogScrubber` redacts `$.signatures[*].signature` on any captured log line. |

### Response (E13 below)

The consumer's webhook endpoint MUST return the E13 ACK envelope with HTTP `200 OK` for every well-formed POST per FR-085 — failure is reported via the ACK body's `errorCode`, not via HTTP status (matches MISA's documented contract treating the ACK body as the status signal). Non-`200` responses are reserved for transport-layer failures (host unhealthy, malformed JSON that didn't parse, etc.) and provoke MISA's transport-level retry.

---

## E13. Outbound ACK envelope (consumer's webhook endpoint → MISA)

Source: MISA §4.9 ("Response" of the webhook table).

### Response

| Element | Value |
|---|---|
| Status | `200 OK` (always, per FR-085) |
| `Content-Type` | `application/json; charset=utf-8` |
| Body | JSON, see schema below |

### Body schema (camelCase per §4.9)

```json
{
  "errorCode": "0",
  "devMsg": "Webhook accepted and finalize succeeded.",
  "userMsg": "Signing completed."
}
```

### Field-level wire contract

| Wire field | Type | Required? | Notes |
|---|---|---|---|
| `errorCode` | `string` | yes | `"0"` on success (per Assumption 6 / research R-3; subject to Postman verification). One of the namespaced failure codes on failure (table below). |
| `devMsg` | `string` | yes | Developer-facing English message. Safe to log; contains no secrets. |
| `userMsg` | `string` | yes | User-facing message; typically Vietnamese (matching MISA's locale conventions) but the SDK ships English defaults until localization tooling is added. Consumers can override the messages via DI customization of the `WebhookAckTemplates` Infrastructure-layer record (not part of this slice's public surface; flagged as a follow-up). |

### ACK code mapping (slice 4 table A.11)

| Source typed exception | `errorCode` | Default `devMsg` | Default `userMsg` |
|---|---|---|---|
| (success — no exception) | `"0"` | `"Webhook accepted and finalize succeeded."` | `"Signing completed."` |
| `MalformedEnvelopeException` | `"webhook.malformed"` | `"Inbound payload failed schema validation: <reason>."` | `"Invalid webhook payload."` |
| `ClientIdMismatchException` | `"webhook.client_id_mismatch"` | `"Inbound clientId does not match configured Misa:ESign:ClientId."` | `"Webhook authentication failed."` |
| `UnknownTransactionException` | `"webhook.unknown_transaction"` | `"No in-flight session matches the inbound transactionId; the session may have expired or was never registered."` | `"Unknown signing transaction."` |
| `IncompleteSuccessEnvelopeException` | `"webhook.incomplete_success"` | `"Inbound envelope status = SUCCESS but signatures[] is empty."` | `"Webhook payload incomplete."` |
| `DocumentIdMismatchException` | `"webhook.document_id_mismatch"` | `"Inbound signatures[].documentId is not in the session's recorded document IDs."` | `"Webhook payload references unknown document."` |
| (finalize attempt failed — transport / MISA 4xx-5xx) | `"webhook.finalize_failed"` | `"Finalize attempt against /documents/attachment failed; MISA may retry."` | `"Signing finalization failed; please wait for retry."` |
| (FAILED / CANCELLED status — handled per FR-083) | `"0"` (success ACK) | `"Webhook accepted; signing transaction reached terminal state <FAILED \| CANCELLED>."` | `"Signing did not complete."` |

The full mapping table also lives in [error-mapping.md](./error-mapping.md) as the authoritative reference; this table is the wire-shape view.

---

## E.reuse — `/documents/attachment` from the webhook handler's finalize path

The webhook-mode finalize path reuses slice 3's existing `/documents/attachment` request + response shapes verbatim (no changes). Reference:

- Request shape: see [specs/003-misa-esign-multi-format/contracts/wire-envelopes.md E11](../../003-misa-esign-multi-format/contracts/wire-envelopes.md) for the `Doc_Attackment`-per-format request shape (with the MISA misspelling preserved verbatim).
- Response shape: same reference for the per-format response array extraction.

The slice-4 handler dispatches by `session.Format`:

| `session.Format` | Outbound use case | Slice 3 reference |
|---|---|---|
| `Pdf` | `AttachSignaturePdf` (slice 1) | slice-1 `/documents/attachment` shape, single `pdfDocs[0]` entry |
| `Xml` | `AttachSignatureToXml` | slice-3 FR-056 — single `xmlDocs[0]` entry with `signatureId` populated |
| `Word` | `AttachSignatureToWordExcel(format = Word)` | slice-3 FR-056 — single `wordDocs[0]` entry with `mainDom` + `signatureId` |
| `Excel` | `AttachSignatureToWordExcel(format = Excel)` | slice-3 FR-056 — single `excelDocs[0]` entry with `mainDom` + `signatureId` |

The webhook envelope's `signatures[]` provides the `signature` value that gets threaded into the `Doc_Attackment.signature` field (mapping: `signatures.Single().Signature` per the slice-4 single-document-per-call constraint inherited from slice 3's Assumption 2).

---

## Wire-format compliance checks (extends slice 3's checks)

| Check | How it's enforced | Test location |
|---|---|---|
| Inbound envelope field names match MISA §4.9 camelCase verbatim | `[JsonPropertyName(...)]` attributes on `WebhookEnvelopeDto` + a unit test that snapshots a fixture body and asserts deserialization round-trips. | `tests/MisaConnect.ESign.UnitTests/Webhook/WebhookEnvelopeValidatorTests.cs` |
| Outbound ACK field names match MISA §4.9 camelCase verbatim | `[JsonPropertyName(...)]` attributes on `WebhookAckDto` + a unit test that captures the serialized JSON and asserts the field names. | `tests/MisaConnect.ESign.UnitTests/Webhook/HandleWebhookOrchestratorTests.cs` |
| `signatures[].signature` is never serialized into log output | `ESignLogScrubber` JSON-path rule `$.signatures[*].signature` + a unit test that drives `HandleWebhookAsync` with a populated signature and asserts the captured log contains no occurrence of the signature value. | `tests/MisaConnect.ESign.UnitTests/Logging/ESignLogScrubberTests.cs` |
| `extraData` is never serialized into log output | `ESignLogScrubber` JSON-path rule `$.extraData` + a unit test parallel to the above. | same file |
| `MisaWebhookAckCodes.Success` matches the Postman-collection success sentinel | Manual verification at tasks-time per research R-3; if Postman shows a different value, update the constant + re-run unit tests. | (not a test — a research / tasks-time step) |

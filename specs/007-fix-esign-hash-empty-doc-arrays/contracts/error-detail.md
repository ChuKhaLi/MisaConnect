# Contract: `validationFailures` in error detail

Governs how MISA's per-property `validationFailures` reach the consumer. Verified by unit tests over
the error-mapping path with both flag states.

## C4 — Gated surfacing

Given a MISA error response carrying `validationFailures` (`[{ property, failureReason }]`):

| `Errors.IncludeRawErrorMessage` | Exception `Detail` |
|---------------------------------|--------------------|
| `false` (default) | Existing summary only. MUST NOT contain any `validationFailures` text, `devMsg`, or `userMsg`. |
| `true` | Existing summary + `userMsg`/`devMsg` (as today) + a `validationFailures:` segment listing each `[<property>] <failureReason>`. |

## C5 — Error code/category are invariant

The thrown exception's `Category` and `RawCode` MUST be identical whether or not `validationFailures`
are present, and identical to today's values for the same `errorCode`/`devMsg`/`userMsg`. The
failures feed only the human-readable `Detail` (FR-007). In particular, for the captured 400
(`errorCode = "e400"`), the surfaced `RawCode` remains `"e400"` and `Category` remains `HashRejected`
(PDF/Word/Excel) — the reported `errorCode=<none>` was inaccurate.

## C6 — Defensive rendering & no PII

- Absent `validationFailures` (field missing/empty) ⇒ detail unchanged from today.
- Entries with missing `property` or `failureReason` ⇒ rendered defensively (skipped/empty), no throw.
- Rendered text contains only MISA field names + reason strings — never tokens, credentials, buyer
  PII, or document content (Principle VIII).

## C7 — No public-surface dependency

The contract is delivered without modifying any public type: the failures travel through the
internal `ResponseErrorDto` and an internal `ESignErrorMapper.Map` overload. The public
`ESignErrorMapper.Map` signature and the public `ResponseError` record are unchanged.

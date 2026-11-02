# Contract: `InvalidTransactionID` → `NotFound` vs `NotDeletable` disambiguation

**Spec mapping**: FR-035, Spec Clarifications Session 2026-05-12 Q1
**Constitution mapping**: Principle II (no silently-swallowed errors), Principle IV (warning-logged on default branch)

## Why this contract exists

MISA's error catalogue (see `docs/misa-api-reference/error-codes.md`)
does **not** expose a dedicated `InvoiceCannotDelete` error code. Two
distinct caller-actionable conditions collapse into the single
`InvalidTransactionID` raw code:

1. **The RefID is genuinely unknown to MISA** — never existed, or was
   already deleted (FR-037 idempotency). The caller should treat this as
   a successful no-op and move on.
2. **The RefID exists but the invoice is not in a deletable state** —
   typically because it has already been signed/issued to the tax
   authority. The caller MUST NOT silently move on; this is the route to
   the replacement (slice 4) / adjustment (slice 4) flows.

A delete API that surfaces both conditions identically is dangerous (per
spec User Story 2). The slice therefore disambiguates the two by
inspecting MISA's textual `ErrorMessage` field via a small, documented
allow-list of Vietnamese phrases.

## The allow-list

Matching algorithm (FR-035, research R-DEL-03):

- Compare with `String.Contains(phrase, StringComparison.OrdinalIgnoreCase)`.
- Iterate entries in order; first match wins.
- On empty `ErrorMessage` OR no match against any entry: default to
  `ResourceNotFound` (surfaces as `DeleteDraftStatus.NotFound`) and emit
  a warning-level log entry with event ID `ARCH-DEL-001` so operators can
  observe drift in MISA's text.

### Entries (load-bearing — `EInvoice.UnitTests` asserts the code list matches this table)

| # | Phrase (case-insensitive substring) | Maps to category | Maps to status | Provenance |
|---|---|---|---|---|
| 1 | `không tồn tại` | `ResourceNotFound` | `NotFound` | MISA error catalogue (`Mã lỗi thường gặp.md`) — canonical "does not exist" phrasing for `InvalidTransactionID` |
| 2 | `đã phát hành` | `NotDeletable` | `NotDeletable` | MISA sandbox observed 2026-05-08 — exact phrase returned for attempting to delete an already-issued invoice |
| 3 | `đã ký` | `NotDeletable` | `NotDeletable` | MISA sandbox observed 2026-05-08 — exact phrase returned for attempting to delete a signed-but-not-yet-issued invoice |

**Adding a new entry**: when production logs (or sandbox observation) show
`ARCH-DEL-001` warnings firing with a recurring unmatched phrase, the
following procedure adds an entry:

1. Append a row to this table with the phrase, the desired category, and a
   provenance note (where the phrase was observed, when, by whom).
2. Append the matching `DisambiguationEntry` to
   `InvalidTransactionAllowList.Entries` in
   `EInvoice.Application/Errors/InvalidTransactionDisambiguator.cs` in the
   same source order.
3. Re-run `InvalidTransactionDisambiguatorTests.AllowList_matches_contract`
   — it parses this markdown table and asserts set-equality with the code.

No spec amendment is required for additive allow-list changes — the spec
already commits to the disambiguation behaviour. Re-categorising an
existing entry (e.g., moving a phrase from `NotDeletable` to `NotFound`)
DOES require a spec note in this document.

## Examples

**Example A — issued invoice (the User Story 2 happy path)**

MISA payload:
```json
{
  "success": false,
  "errorCode": "InvalidTransactionID",
  "ErrorMessage": "Hóa đơn đã phát hành nên không thể xóa."
}
```

Matching: substring `đã phát hành` is found (case-insensitive). → Category
`NotDeletable`. → `DeleteDraftStatus.NotDeletable`. → HTTP 409 / library
`outcome.Status == NotDeletable`.

**Example B — unknown RefID (the User Story 3 idempotency path)**

MISA payload:
```json
{
  "success": false,
  "errorCode": "InvalidTransactionID",
  "ErrorMessage": "RefID không tồn tại."
}
```

Matching: substring `không tồn tại` is found. → Category
`ResourceNotFound`. → `DeleteDraftStatus.NotFound`. → HTTP 404 / library
`outcome.Status == NotFound`.

**Example C — empty `ErrorMessage` (the Edge Case "empty `ErrorMessage`" path)**

MISA payload:
```json
{
  "success": false,
  "errorCode": "InvalidTransactionID",
  "ErrorMessage": ""
}
```

Matching: empty string short-circuits to default. → Category
`ResourceNotFound`. → `DeleteDraftStatus.NotFound`. → Warning log
`ARCH-DEL-001` fires carrying the empty-message marker.

**Example D — unmatched non-empty `ErrorMessage` (drift detection)**

MISA payload:
```json
{
  "success": false,
  "errorCode": "InvalidTransactionID",
  "ErrorMessage": "Hóa đơn đang trong trạng thái khóa."
}
```

Matching: no allow-list entry matches. → Default branch → Category
`ResourceNotFound`. → `DeleteDraftStatus.NotFound`. → Warning log
`ARCH-DEL-001` fires; the message excerpt is included **only** when
`MeInvoiceOptions:Delete:IncludeRawErrorMessage == true`. Operators
observing the warning add a new allow-list entry per the procedure above.

## Why substring matching (not regex, not structured lookup)

Per spec Clarifications Session 2026-05-12 Q1:

- **Regex** was rejected because the data does not need pattern
  expressivity. Regex invites operators to author patterns that pass
  tests today and break on a comma-added variant.
- **Structured lookup** (keyed on a hypothetical MISA "errorReason" field)
  was rejected because MISA does not publish such a field, and adding a
  derived structured key per phrase would require maintaining a mapping
  that drifts the same way the substring allow-list would — without the
  benefit of being directly inspectable.
- **Substring matching** is the minimum machinery that satisfies the
  ambiguity, with the warning-on-default emission as the controlled
  release valve when MISA's text changes faster than the allow-list.

## Why case-insensitive

MISA's responses have been observed with inconsistent diacritic
capitalisation. The allow-list entries are stored lowercase; the matcher
uses `OrdinalIgnoreCase` (per R-DEL-03). Vietnamese-specific
case-folding (e.g., `Đ` vs `đ`) is handled correctly by .NET 8's
`OrdinalIgnoreCase` for the ASCII-extended letters in the entries above.

## Why default to `NotFound` (not `NotDeletable`)

Per spec Assumptions, third bullet: "deliberate bias toward the more
common case (unknown RefID) over the rarer case (issued-and-undeletable)".
The warning-level log entry gives operators visibility to flip the default
if production data shows the rare case is more common than expected.

## Acceptance tests

The following unit tests in `EInvoice.UnitTests/Delete/InvalidTransactionDisambiguatorTests.cs`
exercise this contract (test-first per Principle VII):

| Test | Asserts |
|---|---|
| `Classify_returns_ResourceNotFound_on_null_message` | `Classify(null) == ResourceNotFound` |
| `Classify_returns_ResourceNotFound_on_empty_message` | `Classify("") == ResourceNotFound` |
| `Classify_returns_ResourceNotFound_on_whitespace_message` | `Classify("   \t\n") == ResourceNotFound` |
| `Classify_matches_khong_ton_tai_to_ResourceNotFound` | `Classify("RefID không tồn tại.") == ResourceNotFound` |
| `Classify_matches_KHONG_TON_TAI_case_insensitive` | `Classify("REFID KHÔNG TỒN TẠI") == ResourceNotFound` |
| `Classify_matches_da_phat_hanh_to_NotDeletable` | `Classify("Hóa đơn đã phát hành nên không thể xóa.") == NotDeletable` |
| `Classify_matches_da_ky_to_NotDeletable` | `Classify("Hóa đơn đã ký, không thể xóa.") == NotDeletable` |
| `Classify_returns_ResourceNotFound_on_unmatched_text` | `Classify("Some unrelated text") == ResourceNotFound` |
| `AllowList_matches_contract` | Set-equality between code list and the table above (parsed from this file embedded as resource). |

The warning-emission assertion lives in
`DeleteDraftInvoiceTests` T4/T5 (see `contracts/delete-draft.md`), not in
the disambiguator tests, because the disambiguator does not log — the use
case logs based on the disambiguator's return value plus the message
non-emptiness.

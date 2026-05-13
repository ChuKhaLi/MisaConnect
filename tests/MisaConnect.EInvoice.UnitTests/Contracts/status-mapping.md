# Contract: Unified invoice status mapping (FR-048)

**Surface**: `EInvoice.Application` (pure function, consumed by both delivery surfaces)
**Spec mapping**: FR-048, FR-049, FR-050; User Story 1 / 2 / 3 acceptance scenarios that assert `Status`
**Constitution mapping**: Principle II (the unified `Status` is a domain concept, not a MISA-layer detail), Principle VII (test-first — every row in this table is one parameterised test case)

## What this contract pins down

MISA reports invoice state through **two raw axes**:

- `EInvoiceStatus` — string-typed: `"1"` (original), `"3"` (replaced),
  `"4"` (adjusted). Documented in
  `docs/Tài liệu API Tạo hóa đơn - V2.md` §8.3.
- `PublishStatus` — string-typed: `"0"` (chưa phát hành / unpublished),
  `"4"` (chờ cấp mã / awaiting code), `"6"` (đã cấp mã / code allocated),
  `"7"` (từ chối cấp mã / code rejected). Documented in §6.2 (paging
  endpoint description) and `docs/misa-api-reference/curl-examples.md`.

The service surfaces a **unified six-value `InvoiceStatus`** to callers
(`Draft`, `Signed`, `Issued`, `Cancelled`, `Replaced`, `Adjusted`)
derived from those two axes by a **two-axis precedence rule** fixed by
Clarifications Session 2026-05-12 Q1. The snapshot also carries both raw
values verbatim (`RawEInvoiceStatus`, `RawPublishStatus`) so callers can
inspect the wire values when the unified status is insufficient.

This contract is the **load-bearing copy** of the mapping table. A unit
test (`StatusMappingContractTests.MapperMatchesContract`) loads this file
as an embedded resource, parses the table below, and asserts
`InvoiceStatusMapper.Map(...)` produces the same value for every row.
If the table and the code disagree, the test fails red.

## The mapping algorithm

```text
function Map(eInvoiceStatus, publishStatus):
    // Axis 1: EInvoiceStatus overrides PublishStatus for 3 and 4.
    if eInvoiceStatus == 3 → return Replaced
    if eInvoiceStatus == 4 → return Adjusted

    // Axis 2: PublishStatus drives the rest.
    switch publishStatus:
        0    → Draft
        4    → Signed
        6    → Issued
        7    → Issued     (rejection signal preserved via RawPublishStatus)
        null → Issued     (conservative default)
        _    → Issued     (conservative default; raw value preserved)
```

`Cancelled` is **never** returned by `Map(...)` from any of the three
slice-5 lookup endpoints. MISA does not surface a cancellation signal in
`getlist` / `paging` / `paging/calculating` responses. The value exists
in the `InvoiceStatus` enum for forward compatibility with a future
slice that integrates MISA's cancellation API.

## The mapping table (load-bearing)

| `RawEInvoiceStatus` | `RawPublishStatus` | Unified `Status` | Notes |
|---|---|---|---|
| `1` | `0` | `Draft` | Typical: caller's freshly-created draft. |
| `1` | `4` | `Signed` | Awaiting tax-authority code. |
| `1` | `6` | `Issued` | Code allocated. |
| `1` | `7` | `Issued` | Code rejected — raw value preserved for caller inspection. |
| `1` | *(unknown)* | `Issued` | Conservative default; raw value preserved. |
| `1` | `null` | `Issued` | Conservative default; MISA omitted the field. |
| `3` | `0` | `Replaced` | EInvoiceStatus overrides axis 2. |
| `3` | `4` | `Replaced` | EInvoiceStatus overrides axis 2. |
| `3` | `6` | `Replaced` | EInvoiceStatus overrides axis 2. |
| `3` | `7` | `Replaced` | EInvoiceStatus overrides axis 2. |
| `3` | *(unknown)* | `Replaced` | EInvoiceStatus overrides axis 2. |
| `3` | `null` | `Replaced` | EInvoiceStatus overrides axis 2. |
| `4` | `0` | `Adjusted` | EInvoiceStatus overrides axis 2. |
| `4` | `4` | `Adjusted` | EInvoiceStatus overrides axis 2. |
| `4` | `6` | `Adjusted` | EInvoiceStatus overrides axis 2. |
| `4` | `7` | `Adjusted` | EInvoiceStatus overrides axis 2. |
| `4` | *(unknown)* | `Adjusted` | EInvoiceStatus overrides axis 2. |
| `4` | `null` | `Adjusted` | EInvoiceStatus overrides axis 2. |
| *(unknown)* | `0` | `Draft` | Unrecognised EInvoiceStatus falls through to axis 2 as if `1`. |
| *(unknown)* | `4` | `Signed` | Falls through to axis 2. |
| *(unknown)* | `6` | `Issued` | Falls through to axis 2. |
| *(unknown)* | `7` | `Issued` | Falls through to axis 2. |
| *(unknown)* | *(unknown)* | `Issued` | Conservative default on both axes. |
| `null` | `0` | `Draft` | Null EInvoiceStatus treated as unrecognised → axis 2. |
| `null` | `4` | `Signed` | Same. |
| `null` | `6` | `Issued` | Same. |
| `null` | `7` | `Issued` | Same. |
| `null` | `null` | `Issued` | Both axes default to `Issued`. |

`*(unknown)*` in the table means "any integer that is neither the
documented values for that axis nor null". The parameterised unit test
covers a representative unknown value (e.g., `99`) for each axis-unknown
row.

## What MUST be true at all times

- `Cancelled` MUST NOT appear in any row of this table.
- Every row's unified `Status` is reachable by some
  `(EInvoiceStatus, PublishStatus)` tuple a current MISA endpoint can
  produce — except `Cancelled`, which is reserved.
- A snapshot returned by any slice-5 endpoint MUST carry both raw values
  AND the unified `Status`; if the wire DTO is missing a raw value (MISA
  omitted the field), the snapshot's corresponding field is `null` and
  the unified `Status` falls into the appropriate `null` row above.
- If MISA introduces a new `EInvoiceStatus` or `PublishStatus` value not
  in this table, the conservative defaults above ensure the service
  continues to function (the value flows through as raw, and the unified
  `Status` defaults to `Issued`). A spec note SHOULD be added when such a
  value is observed in the wild, and a row appended to this table after
  the appropriate clarification.

## How to update this contract

When MISA introduces a new documented combination (typically observed
via integration-test surfacing of a `(EInvoiceStatus, PublishStatus)`
tuple with an unexpected unified `Status` choice):

1. Add a new row to the table above with the desired unified status.
2. Update `InvoiceStatusMapper.Map(...)` in
   `src/EInvoice.Application/Mapping/InvoiceStatusMapper.cs` to produce
   the same value for that input.
3. Run `dotnet test --filter "InvoiceStatusMapperTests"` and
   `dotnet test --filter "StatusMappingContractTests"` — both must stay
   green.
4. Commit the contract file change and the code change in a single
   commit so the contract/code parity is atomic.

**Re-categorising** an existing row (e.g., flipping `(1, 7)` from
`Issued` to a new `Rejected` value) is a **spec-level change** and
requires a clarification round; do not update the table without one.

## Cross-references

- [`lookup-by-refid.md`](./lookup-by-refid.md) — consumes this mapping
  for batch-lookup snapshots.
- [`lookup-paginated.md`](./lookup-paginated.md) — consumes this
  mapping for paged-lookup snapshots.
- [`../data-model.md`](../data-model.md) — `InvoiceStatusMapper`
  declaration and the `InvoiceStatus` enum definition.
- `docs/Tài liệu API Tạo hóa đơn - V2.md` §6 and §8.3 — MISA's
  documented values for both axes.

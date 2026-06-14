# Contract: Public surface (no change)

This slice is a **patch** (`MisaConnect.ESign 2.1.1`). It makes **no** change to the consumer-facing
public surface (Principle II / Principle VII / FR-009).

## C8 — Unchanged public types & signatures

- `IMisaESignClient` and all facade method signatures — unchanged.
- `MisaESignOptions` (and `Errors.IncludeRawErrorMessage`) — unchanged.
- Domain error types (`ESignException` and subclasses, `ResponseError`, `ESignErrorCategory`) —
  unchanged. In particular `public sealed record ResponseError(bool, string?, string?, string?)`
  keeps its 4-member shape.
- `ESignErrorMapper.Map` public signature — unchanged (a new **internal** overload is added,
  reachable via existing `InternalsVisibleTo`).

## C9 — Internal-only changes

- `HashRequestDto` / `AttachmentRequestDto` document-type arrays: nullability change (`internal`).
- `ResponseErrorDto` + new `ValidationFailureDto`: `internal`.
- `MisaESignWireClient.ToWireSignatureInfo`: internal behavior (Page default + log).

## C10 — Consumer-observable behavior delta

Only two observable changes, both improvements:
1. Requests that previously failed (HTTP 400) now succeed (unused arrays omitted; unset `Page`
   defaults to 1).
2. With `Errors.IncludeRawErrorMessage = true`, error `Detail` strings are richer (include MISA
   `validationFailures`). With the flag false, detail is unchanged.

No recompilation or configuration change is required of consumers.

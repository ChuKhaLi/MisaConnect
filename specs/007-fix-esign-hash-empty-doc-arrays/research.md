# Phase 0 Research: Fix ESRM empty-doc-array rejection

All decisions are grounded in the verified [bug-report.md](./bug-report.md), direct source reading,
and the 6-agent adversarial verification recorded against the spec. No blocking `NEEDS
CLARIFICATION` remains. The one empirical premise — "MISA accepts a body that omits the unused
arrays" — is captured as a managed assumption (substantiated by the report's consumer-side
workaround, which strips the empty arrays and unblocks signing).

---

## D1 — Omit unused document-type arrays via nullable + `WhenWritingNull`

**Decision**: Change the four document-type arrays on `HashRequestDto` and `AttachmentRequestDto`
(`pdfDocs`/`xmlDocs`/`wordDocs`/`excelDocs`) from `List<…> = new()` to `List<…>?` with no
initializer. `ESignJsonOptions.Wire` uses `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`,
so an unassigned (null) array is dropped from the body; an assigned one serializes normally.

**Rationale**: Minimal, type-safe, and matches existing convention — the response DTOs
(`HashResponseDto.XmlDocs/WordDocs/ExcelDocs`, all `AttachmentResponseDto.*`) are already `List<…>?`.
Verification confirmed each of the six build sites (`HashPdfAsync`, `HashXmlAsync`,
`HashWordOrExcelAsync`, `AttachSignatureAsync`, `AttachSignatureToXmlAsync`,
`AttachSignatureToWordExcelAsync`) assigns exactly one array, the request arrays are **never read
back** after construction (so a null default cannot NPE), and the response-side `Count > 0` guards
read the **separate** response DTO types (unaffected).

**Alternatives considered**:
- *Custom converter / global skip-empty-collection condition*. Heavier machinery; changes
  serialization semantics project-wide and risks unintended effects on other empty lists (e.g.
  `CertificateChain`). Rejected.
- *Null the unused arrays at the call site while keeping non-null types*. Fights the type system; a
  non-nullable property cannot hold null without warnings under `Nullable` enabled. Rejected.

**Scope guard**: `Certificate` (string) and `CertificateChain` (`List<string>`) are assigned at
every build site and **stay non-null** — they are explicitly out of this change.

---

## D2 — Default `SignatureInfo.Page` to 1 (logged) when unset

**Decision**: In the wire client's `ToWireSignatureInfo` (PDF/Word/Excel), set
`Page = source.Page ?? 1`; when `source.Page is null`, emit a structured log (Debug/Information,
correlation-scoped) recording that the default was applied. The existing client-side validation
rejecting `Page < 1` (`SignPdfRequestValidator`) is unchanged. Make `ToWireSignatureInfo` an
instance method so it can reach `_logger`/`_correlation` (it is only called from instance methods).

**Rationale**: Verification confirmed `SignatureInfo.Page` is `int? = null`, the validator's
`is < 1` pattern does **not** match null (so a null `Page` silently passes client validation, is
dropped on the wire, then rejected by MISA). Defaulting to 1 makes a visible signature land on the
first page — the safe, expected default — while the log preserves an audit trail (the chosen option
from brainstorming: "default to 1, but log it"). Consumers needing another page still set `Page`
explicitly and that value is sent verbatim.

**Scope**: PDF/Word/Excel only. The XML path uses `XmlSignatureContextMapper`, whose
`XmlSignatureContext` has no `Page` (XML signatures are not page-positioned); it is exempt and
carried through unchanged.

**Alternatives considered**: *Validate and fail fast on null Page* — rejected in brainstorming as
less convenient (forces every caller to set `Page`). *Silent default with no log* — rejected for
lack of audit trail.

---

## D3 — Surface `validationFailures` without changing public surface or error codes

**Decision**: Add an internal `ValidationFailures` field (`List<ValidationFailureDto>?`, nested DTO
`{ [JsonPropertyName("property")] string? Property; [JsonPropertyName("failureReason")] string?
FailureReason; }`) to the internal `ResponseErrorDto`. In the wire client's error path
(`ThrowMappedAsync` / `BuildAuthFailureAsync`), render present entries to a single sanitized string
and pass it to a **new internal** `ESignErrorMapper.Map` overload (reachable via the existing
`InternalsVisibleTo` from Application to Infrastructure). The public `Map` delegates to it with
`null`, so its signature is preserved. `BuildDetail` appends the rendered string **only when
`IncludeRawErrorMessage = true`**.

**Rationale**: Three hard constraints shaped this:
1. **No public-surface change (Principle II / FR-009).** The domain `ResponseError` is a
   `public sealed record`; adding a positional member would change its constructor/`Deconstruct`
   (binary-breaking). So `validationFailures` must travel through **internal** types only — hence
   the wire-DTO field + internal `Map` overload, leaving `ResponseError` untouched.
2. **Error codes must not change (FR-007).** `SynthesizeHashCode*` builds the synthesized code from
   a probe over `rawCode`/`DevMsg`/`UserMsg`. Folding `validationFailures` into `DevMsg` would
   pollute that probe (e.g. the property name "XmlDocs" contains "xml") and could alter the code.
   Passing the failures on a **separate channel that only `BuildDetail` reads** keeps synthesis
   inputs identical.
3. **Opt-in gating (FR-006 / Principle VIII).** `BuildDetail` already suppresses raw detail
   (`devMsg`/`userMsg`) unless `includeRawErrorMessage` is true; the new string follows the same
   gate, so nothing leaks by default.

**Alternatives considered**:
- *Add `ValidationFailures` to the public `ResponseError` record + render in `BuildDetail`*. Cleanest
  data model but a breaking/public change → would force a minor or major bump, contradicting the
  patch target and FR-009. Rejected.
- *Add an optional parameter to the public `ESignErrorMapper.Map`*. Source-compatible but
  binary-breaking on a public method; the internal overload guarantees a zero public delta. Rejected
  in favor of the internal overload.
- *Fold into `DevMsg`*. Rejected — violates FR-007 (pollutes the code-synthesis probe).

**PII/secret check (Principle VIII)**: rendered content is MISA's field names (e.g. `XmlDocs`) plus
its generic reason strings (e.g. `Phải có ít nhất 1 tài liệu`) — no buyer PII, no tokens, no
document content. Entries with missing `property`/`failureReason` are rendered defensively (skipped
or empty) without throwing.

---

## D4 — Correct the misleading mechanism comment

**Decision**: Fix the comment at `XmlSignatureContextMapper.cs:30-32`, which attributes the `Page`
omission to `[JsonIgnore]`. There is no per-property `[JsonIgnore]` on `SignatureInfoDto.Page`; the
omission is the global `DefaultIgnoreCondition = WhenWritingNull` in `ESignJsonOptions.cs`. Behavior
is unchanged — this is a correctness fix to the documentation-in-code.

---

## D5 — Release classification

**Decision**: Patch release `MisaConnect.ESign 2.1.1`. Bug fix only; no public-surface change
(D1/D3 keep all touched types internal and the public `Map`/`ResponseError` signatures intact).
`CHANGELOG` gains `[Unreleased]` "Fixed" entries (empty-array rejection; null-`Page` rejection) and a
short note on the `validationFailures` detail enrichment under `IncludeRawErrorMessage`.

**Rationale**: Principle VII — patch versions are bug fixes only. The consumer-observable surface
(types, method signatures) is unchanged; only the request body (now correct) and the opt-in error
detail text change.

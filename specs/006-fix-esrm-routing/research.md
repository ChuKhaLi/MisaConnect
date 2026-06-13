# Phase 0 Research: Fix ESRM Routing & Silent-Failure Hardening

All decisions below are grounded in the verified [bug-report.md](./bug-report.md), the official `docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md`, and direct source reading. No blocking `NEEDS CLARIFICATION` remains; the one empirical unknown (production login path) is captured as a managed residual risk.

---

## D1 — Normalize `BaseAddress` to the origin

**Decision**: At HttpClient registration, set `http.BaseAddress = new Uri(new Uri(opts.BaseUrl, UriKind.Absolute).GetLeftPart(UriPartial.Authority) + "/")`. The configured path (e.g. `/webdev/`) is discarded for request composition.

**Rationale**: With base = origin and relative routes resolved by `System.Uri` rules, the *current* route constants already produce the doc-correct production URLs: `external/esrm/…` → root (fixes A), `webdev/api/auth/…` → single `/webdev/` (fixes C), `api/auth/…` → root. One change fixes A and C and makes the SDK path-tolerant, so consumers need no config change (FR-002).

**Alternatives considered**:
- *Keep base = `…/webdev/` and special-case ESRM to absolute origin URIs* (the report's suggestion). Works, but leaves `BaseUrl` semantics ambiguous ("auth base") and forces stripping `webdev/` from refresh/resend constants — more churn, and inconsistent with the README's documented host-root base.
- *Add `EsrmBaseUrl`/`AuthBaseUrl` explicit options*. More configuration surface than needed; origin-derivation covers every observed topology. Rejected as YAGNI.

---

## D2 — Separate request-path resolution from endpoint identity

**Decision**: Keep `ESignHttpRoutes.*` constant **values** unchanged; they remain the canonical *endpoint identifiers* passed to `ESignErrorMapper` / `OtpErrorMapper`. Introduce `ESignRouteResolver` (Infrastructure-internal) that maps a canonical route to its *request path*, prepending `webdev/` to **login** and **two-factor** only when auth-under-webdev is in effect. ESRM/refresh/resend resolve to identity.

**Rationale**: The error mappers dispatch on the endpoint string by **exact equality** (`ESignErrorMapper.cs:60-72`, `OtpErrorMapper.cs:16-17`, `EndpointLogin = "api/auth/api/v1/auth/login-api"`, etc.). If we changed the constant values or passed a webdev-prefixed string as the endpoint id, error categorization would silently break. Decoupling request path from endpoint id keeps error mapping correct with **zero changes** to the mapper constants, neutralizing the "keep constants in sync" risk the report flagged.

**Alternatives considered**:
- *Mutate the route constants per environment*. Breaks exact-match error dispatch; rejected.
- *Make error mappers match by suffix (`EndsWith`)*. Larger blast radius into the Application layer for no added value; rejected.

---

## D3 — `AuthUnderWebdev` option and derivation

**Decision**: Add `public bool? AuthUnderWebdev { get; set; }` to `MisaESignOptions` (default `null`). Effective value = `options.AuthUnderWebdev ?? (options.Environment == ESignEnvironment.Sandbox)`. The resolver consumes this single boolean.

**Rationale**: Implements approved option (b) — environment-derived default with an explicit override. `ESignEnvironment.Sandbox = 0` is the enum default, so an unset environment defaults login to `/webdev/` (the only empirically confirmed-working path), which is the safe default; production requires an explicit `Environment = Production`. No validation needed (any `bool?` value is meaningful).

**Alternatives considered**:
- *Environment-only, no override (option a)*. Rejected by the user — leaves no lever if production also serves login under `/webdev/`.
- *String enum for auth location*. Overkill for a binary choice; `bool?` is the minimal expressive surface.

---

## D4 — Defect D: 2xx content-type / JSON guard

**Decision**: At the ESRM JSON-response boundary in `MisaESignWireClient`, after the existing `IsSuccessStatusCode` check and before deserializing, verify the response media type is JSON (`application/json` or a `*+json` suffix). If it is not JSON, or the body fails to parse as the expected type, throw a clear `ESignGeneralException` (e.g. category `MisaUnknown`, code `UnexpectedContentType`) carrying the endpoint, the response content type, and a short body snippet. Do **not** modify the shared `Deserialize<T>` swallow-`JsonException` behavior.

**Rationale**: The silent failure is specific to a `2xx` non-JSON body coalescing into `?? new List<…>()` (`MisaESignWireClient.cs:232`). A scoped guard surfaces routing/gateway anomalies loudly (FR-006) while preserving `Deserialize<T>` for the other callers (login/hash/attachment/status) that rely on its lenient behavior and have their own `EmptyResponse` guards. HTTP status enforcement already exists and is unchanged.

**Alternatives considered**:
- *Rethrow `JsonException` globally in `Deserialize<T>`*. Changes behavior for every caller; several depend on the lenient path. Rejected per the verifier's scoping note.
- *Only `EnsureSuccessStatusCode`*. Redundant — status is already enforced; it would not catch the `200 text/html` case. Rejected.

---

## D5 — Distinguish legitimate empty result from parse failure

**Decision**: An empty/whitespace body or a valid empty JSON array `[]` continues to yield an empty certificate list → existing `NoActiveCertificateException` (FR-008). Only a non-JSON content type or an unparseable non-empty JSON body triggers the D4 exception.

**Rationale**: Preserves the genuine "no active certificate" business outcome and avoids false positives for accounts that truly have no certificate. The guard keys on *content type* + *parse outcome*, not merely "result is empty".

**Alternatives considered**:
- *Treat any empty result as an error*. Would misreport real empty-cert accounts; rejected.

---

## D6 — Body-snippet sanitization (no secret leakage)

**Decision**: The two Defect-D guards treat the response body differently by how sensitive it can be:
- **Non-JSON content-type guard** (`EnsureEsrmJsonResponse`, e.g. `text/html`): includes a truncated (~256 char) body snippet + content type + endpoint. The body here is MISA's SPA `index.html` — non-sensitive — and the snippet is the key routing-diagnosis signal.
- **JSON-but-unparseable guard** (`UnparseableResponse` in the cert-list path): reports only the body **length** + content type + endpoint, **never the body content**. This body is a cert-endpoint JSON payload that can carry PII (e.g. `emailName`) or cert material, so it is not echoed.

Neither path ever includes request headers, the `AuthorizationRM` bearer, or credentials.

**Rationale**: Satisfies Principle VIII / FR-007 while keeping the routing-error case (HTML) diagnosable. (Hardened after code review flagged that the cert-endpoint body could otherwise embed PII into an exception message a consumer might log; the SDK itself never logs `ex.Message` — `ESignCallLogger` logs only category + raw code.)

**Alternatives considered**:
- *Include full body*. Unbounded log/exception size; truncation/length-only preferred.
- *Echo a snippet for the unparseable-JSON case too*. Rejected — the cert-endpoint body may carry PII; length-only is sufficient to diagnose.

---

## D7 — Residual risk: production login path

**Decision**: Default production login/two-factor to the host root per the official doc; rely on `AuthUnderWebdev = true` as the override if production turns out to serve them under `/webdev/`. Record a pre-release verification step (probe the production login endpoint) in the spec Assumptions and quickstart.

**Rationale**: The doc is the agreed source of truth, but it is internally inconsistent (login/two-factor without `webdev/`, refresh/resend with it) and reproduction evidence exists only for the sandbox. The override makes this correctable by configuration without a code change, so the risk does not block the slice.

**Alternatives considered**:
- *Block on live production verification now*. Production credentials are not in hand; the override de-risks shipping. Deferred to a pre-release check.

---

## D8 — Test strategy (close the masking gaps)

**Decision**:
- **Unit**: assert resolved **absolute** request URLs for every route against a `BaseUrl` that includes a path segment (e.g. `https://host/webdev/`) and against a bare-host base — both must yield identical correct URLs; assert login/two-factor flip with `Environment` and with `AuthUnderWebdev`.
- **Integration (fake server)**: serve ESRM **only at the host root**; return `200 text/html` under `/webdev/external/esrm/…` to prove the D4 exception (not "no active cert"); **assert** the `AuthorizationRM` bearer equals the remote-signing token; add coverage for the login-prefix switch (Production root vs Sandbox `/webdev/`).
- **Sandbox `[SandboxFact]`**: unchanged contract — skips cleanly without creds.

**Rationale**: The current `FakeMisaESignServer` structurally masks A/B/C/D (ESRM mapped at root, auth under `/webdev/`, root loopback base, discarded bearer). The tests above invert each masking condition so a regression re-breaks a test.

**Alternatives considered**:
- *Rely on sandbox tests only*. They skip without creds and don't run in CI deterministically; the fake must carry the regression guarantees.

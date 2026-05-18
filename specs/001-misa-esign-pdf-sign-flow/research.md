# Phase 0 — Research: MISA eSign PDF Sign Flow

This document resolves every NEEDS-CLARIFICATION item from the Technical Context, plus the design choices the slice rests on. All spec clarifications from `/speckit-clarify` (Session 2026-05-18) have already landed in [spec.md](./spec.md); the items below pick up where the spec stopped.

Each entry follows: **Decision** → **Rationale** → **Alternatives considered**.

---

## R-1. HTTP plumbing — typed `HttpClient` + custom `DelegatingHandler` pipeline (no Polly)

**Decision**: Register a single typed `HttpClient` for `MisaESignWireClient` via `IHttpClientFactory`. Compose the handler pipeline outer-to-inner as `TransientFailureRetryHandler → RemoteSigningAuthHandler → ClientHeadersHandler → (network)`. All three handlers are hand-rolled `DelegatingHandler` subclasses; no Polly dependency.

**Rationale**:
- `MisaConnect.EInvoice` set the precedent: a hand-rolled `ThrottleRetryHandler` + `BearerTokenHandler` with no Polly. Constitution Principle I keeps Infrastructure dependencies narrow; adding Polly here just for a 3-attempt retry would expand the dep surface for negligible benefit and would diverge from the eInvoice family.
- Splitting headers vs auth vs retry into three handlers keeps each responsibility under ~80 LoC and unit-testable in isolation.
- Ordering matters. Putting retry outermost means a 5xx triggers a fresh auth header on the retry (so a token that turned 401 in the meantime gets refreshed by the inner auth handler on the retried attempt). Putting auth innermost keeps the 401-refresh-then-retry-once budget isolated from the transport-retry budget per FR-024.

**Alternatives considered**:
- *Polly with `AddStandardResilienceHandler`*: rejected — extra dep, opinionated defaults that don't map cleanly to the "auth-401-refresh is a separate budget from transport retries" rule (FR-024). The eInvoice family has shown the hand-rolled path is enough.
- *Single fat handler*: rejected — the three concerns (client headers / bearer auth / transient retry) have different test surfaces and different failure modes. Mixing them into one type makes the unit tests for FR-004, FR-014, and FR-023 entangled.
- *Cross-cutting middleware on `HttpClient` itself (no DelegatingHandler)*: not viable — `IHttpClientFactory` only exposes the handler pipeline as the extension surface.

---

## R-2. Token cache & refresh — single-flight via in-process keyed semaphore

**Decision**: The default `InMemoryTokenCache` is a `ConcurrentDictionary<string, AccessToken>` (parallel to `MisaConnect.EInvoice.Infrastructure.Caching.InMemoryTokenCache`). Single-flight refresh is implemented as a `ConcurrentDictionary<string, Lazy<Task<AccessToken>>>` (the "single-flight refresh slot") keyed by the same cache key. On 401, `RemoteSigningAuthHandler` invalidates the cache entry and calls `SingleFlightRefresh.RefreshAsync(cacheKey, factory)`; the first caller installs the `Lazy<Task<AccessToken>>` and runs the actual `refreshtoken` POST; subsequent callers `await` the same `Task`. The slot is removed once the task completes (success or failure) so a follow-up 401 starts a fresh attempt.

**Rationale**:
- Satisfies FR-028 / SC-007 (exactly 1 outbound `refreshtoken` per cache key under 8+ concurrent 401s) without inventing a global lock.
- Per-key (not global) so independent cache keys — e.g. sandbox + production sessions in the same process — never block each other (FR-027).
- `Lazy<Task<...>>` is the standard .NET idiom for single-flight in-memory work and avoids the double-checked-locking pitfalls.
- Failed refreshes propagate via the awaited `Task`, so every waiter sees the same typed auth error (FR-028 "If the in-flight refresh fails, all waiters MUST surface the same typed auth error").

**Alternatives considered**:
- *Global `SemaphoreSlim`*: rejected — serializes unrelated cache keys (would harm multi-tenant consumers using different selectors).
- *`AsyncLazy<T>` from an external lib*: rejected — adds a dep just for a one-line idiom.
- *Recompute on every 401*: rejected — explicitly forbidden by FR-028 (refresh stampede).

---

## R-3. Transport-retry policy — bounded exponential backoff with full jitter, separate budget from auth refresh

**Decision**: `TransientFailureRetryHandler` retries 5xx, 429, `HttpRequestException` (DNS / TLS / connect refused), and `TaskCanceledException` (operation timeout, NOT user cancel). Default: `MaxAttempts = 3`, `BaseDelay = 200 ms`, `MaxDelay = 2000 ms`, doubling per retry with full jitter (uniform `[0, computed]`). On 429 with a `Retry-After` header (delta-seconds or HTTP-date), use that value clamped to `MaxDelay` instead of the computed backoff. All policies are bound to `MisaESignOptions.TransportRetry` and disable-able via `MaxAttempts = 1`. The handler is exhausted-budget terminal: it throws `ESignTransportException` carrying the last response's status, the correlation ID, and the attempt count (FR-025). Auth-401-refresh-then-retry-once (FR-004) is implemented inside `RemoteSigningAuthHandler` — that retry never enters this handler's loop, satisfying FR-024.

**Rationale**:
- Mirrors FR-023 / FR-024 / FR-025 verbatim. Full jitter (vs. equal or decorrelated) is the AWS-recommended default and is simple enough to hand-roll deterministically with `Random.Shared` (plus an internal-injectable `IDelayer` and `IRandom` seam for unit tests so retries are predictable).
- 429-`Retry-After` honoring is required by FR-023; clamping to `MaxDelay` prevents a malicious or buggy server from holding the SDK hostage.
- "Operation timeout" (`TaskCanceledException` when `CancellationToken.IsCancellationRequested == false`) is treated as transient — matches the eInvoice `ThrottleRetryHandler` convention. Caller-initiated cancellation propagates unchanged.

**Alternatives considered**:
- *Equal-jitter / decorrelated-jitter*: defensible but no clear win for a 3-attempt budget; full jitter is simpler and the spec's example math (200 → 400 → 800 ms) maps onto it cleanly.
- *Retry idempotency check*: rejected — every slice-1 endpoint is either GET (status), DELETE (n/a here), or an idempotent POST where MISA's contract is "submit again with the same body, get the same result" (login/refresh/hash/sign/attach all behave that way in MISA's published doc; for `Signing/hash` specifically, "if a `transactionId` was already produced before the failure, retries resume polling that same `transactionId`" — but that's an orchestrator-level concern, see R-7, not a handler-level concern).

---

## R-4. Configuration shape — `MisaESignOptions` mirrors `MisaEInvoiceOptions`

**Decision**: `MisaESignOptions.SectionName = "Misa:ESign"`. Required fields: `Environment` (`Sandbox`/`Production`), `BaseUrl`, `UserName`, `Password`, `ClientId`, `ClientKey`. Nested classes: `Polling { TimeSpan Interval = 2s; TimeSpan TotalTimeout = 60s; }`, `TransportRetry { int MaxAttempts = 3; TimeSpan BaseDelay = 200ms; TimeSpan MaxDelay = 2000ms; }`, `Errors { bool IncludeRawErrorMessage = false; }`. Sandbox host constant = whatever MISA issues with sandbox creds (no public sandbox host is documented; recorded as a config requirement). Production host constant = `esignapp.misa.vn` per the API doc §1.2.

**Rationale**:
- Same shape as `MisaEInvoiceOptions` and its nested `MisaEInvoiceDeleteOptions` — keeps the two product families easy to learn together.
- `Misa:ESign` mirrors `Misa:EInvoice`, so an integrated host with both products has a clean configuration section per product.
- Splitting Polling / TransportRetry / Errors into nested classes keeps each independently tunable and keeps the binder validation tight (FR-015).
- `MisaESignOptionsValidator : IValidateOptions<MisaESignOptions>` checks: required fields present, `Environment` matches `BaseUrl` host (Sandbox base URL must NOT resolve to `esignapp.misa.vn`; Production base URL MUST), `Polling.Interval > 0`, `Polling.TotalTimeout > Polling.Interval`, `TransportRetry.MaxAttempts ≥ 1`, `TransportRetry.MaxDelay ≥ TransportRetry.BaseDelay`. Validator runs at startup via `.ValidateOnStart()`, matching the eInvoice convention.

**Alternatives considered**:
- *Flatten everything into one class*: rejected — `Misa:ESign:Polling__Interval` is a clearer config key than `Misa:ESign:PollingInterval`, and the nested-options pattern is already the project's house style.
- *Validate at first-use instead of startup*: rejected — explicit constitution preference (eInvoice already uses `.ValidateOnStart()`).

---

## R-5. Sandbox-env-var prefix — `MISACONNECT_ESIGN_SANDBOX_*` (matches the spec)

**Decision**: The integration project reads sandbox creds from `MISACONNECT_ESIGN_SANDBOX_USERNAME`, `MISACONNECT_ESIGN_SANDBOX_PASSWORD`, `MISACONNECT_ESIGN_SANDBOX_CLIENTID`, `MISACONNECT_ESIGN_SANDBOX_CLIENTKEY`, `MISACONNECT_ESIGN_SANDBOX_BASEURL`. The configuration binder reads `Misa__ESign__*` (matching the existing `Misa__EInvoice__*` pair in [docs/sandbox-setup.md](../../docs/sandbox-setup.md)). The `[SandboxFact]` skip predicate requires both: the env vars present AND a TCP probe to the configured base URL host succeeding within ~2 s.

**Rationale**:
- The spec already pins the `MISACONNECT_ESIGN_SANDBOX_*` prefix (FR-021). Parallel pair to `MISACONNECT_SANDBOX_*` for eInvoice is the established convention.
- Splitting "config binding" vs "skip predicate" mirrors what [docs/sandbox-setup.md](../../docs/sandbox-setup.md) already documents for eInvoice — see "Configuration for the Misa:EInvoice options binder" vs "Sandbox credentials read by `[SandboxFact]`".
- Probing host reachability (not just env-var presence) is what makes the suite "skip cleanly when the sandbox is unreachable" (FR-021 / SC-003).
- An update to `docs/sandbox-setup.md` is required as part of this slice's task list to add the `MISACONNECT_ESIGN_SANDBOX_*` table and a "Production cutover" entry for eSign.

**Alternatives considered**:
- *Reuse the existing `MISACONNECT_SANDBOX_*` env vars*: rejected — eInvoice and eSign sandboxes are issued by different MISA teams and have independent credentials; conflating them would force every consumer running both test suites to pick one.
- *Skip on env-var absence only, ignore reachability*: rejected — would make CI red on every sandbox outage. The spec explicitly says "skip cleanly when the sandbox is unreachable."

---

## R-6. Certificate selection default — first ACTIVE in MISA's response order (matches the spec)

**Decision**: `FirstActiveCertificateSelector` iterates the `Certificates/by-userId` response in the order MISA returned, filters to `keyStatus == "ACTIVE"`, and returns the first match. If none, throw `NoActiveCertificateException` (carries the MISA correlation ID and an empty cert list). `ICertificateSelector` is the swap seam — consumers register their own before `AddMisaConnectESign` to e.g. pick by issuer DN, by `keyAlias` allowlist, or by `expirationDate` ordering.

**Rationale**:
- Spec FR-007 + the "Multiple ACTIVE certificates" edge-case pins this exact behavior. The decision recorded here is the "default" half; the port is what makes it swappable.
- MISA's `by-userId` response is documented as an array (`[{...}, {...}, ...]`); preserving server order is the cheapest defensible default and is testable with a fixture.

**Alternatives considered**:
- *Throw on multiple ACTIVE without a selector*: rejected — would force consumers to register a selector just to use the SDK against an account with two valid certs, which is the common reality.
- *Pick the longest-validity-remaining cert*: rejected as the default — opinionated; consumers can register that as their own selector if they want.

---

## R-7. Sign-pipeline orchestration & polling state machine

**Decision**: `SignPdf` use case is the single orchestrator that the facade calls. It does:

```
1. cert = ICertificateSelector.SelectAsync(await ListActiveCertificates(...))
2. hash = await HashPdfDocument(pdf, cert, signatureInfo)
3. tx   = await SubmitSignHash(hash.Digest, cert, request.DataToBeDisplayed)
4. final = await PollSignStatus(tx.TransactionId, options.Polling)
5. signed = await AttachSignature(pdf, cert, hash, final.Signature)
6. return signed
```

The polling loop in `PollSignStatus`:
- `ISystemClock`-driven deadline = `now + options.Polling.TotalTimeout`.
- Each iteration: `GET /Signing/status/{transactionId}` (subject to FR-023 transport retry + FR-004 auth refresh).
- Map `status` → `PENDING` (continue), `SUCCESS` (return with `signatures[0].signature`), `FAILED` (throw `SignTerminalStateException`), `CANCELLED` (throw `SignTerminalStateException`). Unknown values → `SignTerminalStateException` with `errorCode = "UnknownStatus"` (defensive).
- Sleep `options.Polling.Interval` (via injected `IDelayer` so unit tests are time-deterministic) before the next iteration, unless the deadline has passed.
- On deadline expiry → `SignTimeoutException` carrying the `transactionId` so the caller can correlate with MISA's side. Per the spec edge case, the SDK does NOT call any "cancel" endpoint (none documented) — MISA's transaction remains as-is.

The orchestrator is **not** transactional across the seven endpoints. If a transient failure happens *after* `SubmitSignHash` returns a `transactionId` but *before* `PollSignStatus` reaches a terminal state, the transport-retry handler resumes the *current call* (the GET status), not the sign-submit. This is the "no duplicate sign submission" guarantee from the spec's transient-transport edge case.

**Rationale**:
- Spec FR-009 → FR-012 prescribe this exact pipeline. The decision records *which layer owns it* (Application) and *which collaborators it injects* (`IMisaESignWireClient`, `ICertificateSelector`, `ISystemClock`, `IDelayer` internal seam).
- Keeping the orchestrator in Application (not in `MisaESignClient` facade in Client) preserves Principle I — Client just maps DTOs and forwards.
- Injecting `IDelayer` as an internal seam lets `PollSignStatusTests` exercise PENDING→SUCCESS, PENDING→FAILED, and PENDING→timeout deterministically in <100 ms each.

**Alternatives considered**:
- *Put `SignPdf` in Infrastructure as part of the HTTP client*: rejected — Infrastructure types are `internal` per Principle II; the orchestrator must be testable by the Client facade and by the unit-test project, which means it belongs in Application.
- *Per-iteration retry budget separate from FR-023*: rejected — over-design for slice 1; FR-023 transport retries plus the polling deadline already cover transient-during-poll cases.
- *Long-polling instead of polling-with-sleep*: rejected — MISA's `Signing/status` is a plain GET with no long-poll semantics in the doc.

---

## R-8. Error-mapping table — `ResponseError.errorCode` → typed exception

**Decision**: `ESignErrorMapper.Map(string? errorCode, string? userMsg, string? devMsg, string correlationId)` returns an `ESignErrorCode` record `(category, rawCode, detail)` consumed by `ESignException` and its subclasses. Mapping table (slice-1 endpoints only):

| MISA `errorCode` | SDK category | SDK exception |
|---|---|---|
| (anything from `login-api`, 4xx) | `Authentication` | `AuthenticationFailedException` |
| `122` (2FA required) on `login-api` | `Authentication` (with a hint that slice-2 is needed) | `AuthenticationFailedException` carrying `requires2FA = true` so consumers can detect "needs slice 2" without parsing strings |
| Anything from `refreshtoken`, 4xx | `Authentication` | `AuthenticationFailedException` (terminal — no further refresh) |
| `by-userId` returned empty / no ACTIVE | (synthesized client-side) | `NoActiveCertificateException` |
| `documents/hash` 4xx | `HashRejected` | `ESignException` with category `HashRejected` |
| `Signing/hash` 4xx — generic | `SignRejected` | `SignRejectedException` |
| `Signing/hash` "user not connected" (per API doc note) | `SignRejected` | `SignRejectedException` with `requiresUserCertSetup = true` |
| `Signing/status` body has `status = FAILED` | `SignTerminalFailed` | `SignTerminalStateException` |
| `Signing/status` body has `status = CANCELLED` | `SignTerminalCancelled` | `SignTerminalStateException` |
| `Signing/status` poll deadline | `SignTimeout` | `SignTimeoutException` |
| `documents/attachment` 4xx | `AttachmentRejected` | `ESignException` with category `AttachmentRejected` |
| 5xx / 429 / transport exhausted | `Transport` | `ESignTransportException` |
| Unknown / unmapped | `MisaUnknown` | `ESignException` with `rawCode` preserved |

The detailed code list per endpoint (e.g. specific MISA codes like `"InvalidPassword"`, `"AccountLocked"`, `"InvalidCertificate"`, `"InvalidHash"`) is captured in `contracts/error-mapping.md`. Every surfaced exception carries `(category, rawCode, correlationId, detail)`. Raw `userMsg`/`devMsg` is included on the exception only when `MisaESignOptions.Errors.IncludeRawErrorMessage = true` (matches eInvoice's `MisaEInvoiceDeleteOptions.IncludeRawErrorMessage` opt-in).

**Rationale**:
- Spec FR-018 + SC-005 require every documented `errorCode` for the seven slice-1 endpoints to map to a typed exception with `errorCode` + correlation ID retained. The table above is the contract; the per-endpoint codes (in `contracts/error-mapping.md`) are the per-unit-test cases that satisfy SC-005.
- The `requires2FA = true` / `requiresUserCertSetup = true` hints are forward-looking but cheap — they let a consumer integrate slice 1 today and detect "needs slice 2" without a string compare. They cost one extra bool on the exception.
- Opt-in raw error surfacing satisfies Principle VIII (default-safe logs/exceptions) while still giving operators a way to debug production issues.

**Alternatives considered**:
- *One generic exception with a string `errorCode`*: rejected — spec FR-018 explicitly wants typed exceptions ("auth failure / cert missing / hash invalid / sign rejected / transaction terminal-failure").
- *Always include `userMsg`/`devMsg` in exception text*: rejected — leaks raw MISA strings that may contain account names or internal IDs in production logs.

---

## R-9. Log scrubbing — copy the eInvoice playbook, extend the deny-list

**Decision**: `ESignLogScrubber` exposes a `Scrub(string)` helper that replaces by-pattern: bearer token values, refresh-token values, `AuthorizationRM` header value bodies, base64-prefixed cert chunks (`certificate`, `certificateChain[*]`, `pdfDocs.documentBytes`, `pdfDocs.sh`, `pdfDocs.documentHash`, `pdfDocs.digest`), end-user-PII fields from `user` (`email`, `phoneNumber`, `firstName`, `lastName`), and the `signatures[*].signature` field. All log lines emitted by `ESignCallLogger` go through the scrubber. The decorator emits structured fields (`{Endpoint}`, `{Method}`, `{StatusCode}`, `{CorrelationId}`, `{DurationMs}`) — never the body. Default log levels: Information for request start/end, Warning on transient retry, Error on terminal failure. No body logging at any level by default.

**Rationale**:
- Spec FR-019 + SC-006 explicitly require zero tokens / refresh tokens / cert private material / raw doc bytes / PII in logs by default, verifiable by a captured-log scan.
- Mirroring `MeInvoiceLogScrubber` keeps the scan harness consistent across products.
- Structured-field-only logging is what makes the SC-006 scan tractable — there is no body to scan in the default config; the scrubber is the safety net for when consumers crank their logger to trace.

**Alternatives considered**:
- *Log raw bodies at debug*: rejected — even debug logs leak via captured-log pipes (Application Insights, Datadog). The opt-in flag is `MisaESignOptions.Errors.IncludeRawErrorMessage`, not a global "log everything" switch.
- *Use a `JsonElement` redactor instead of regex*: a follow-on improvement; for slice 1 the structured-fields-only default makes regex scrubbing the rare case rather than the common one.

---

## R-10. Concurrency contract — singleton facade, scoped use cases, transient handlers

**Decision**: `IMisaESignClient` → singleton (Client csproj registers it that way). The `SignPdf` orchestrator and the seven sub-use-cases → scoped (parallels the eInvoice convention so `ICorrelationIdAccessor` per-request semantics work for ASP.NET hosts). HTTP handlers (`TransientFailureRetryHandler`, `RemoteSigningAuthHandler`, `ClientHeadersHandler`) → transient. `MisaESignWireClient` → typed-client lifetime (managed by `IHttpClientFactory`). `ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`, `ISystemClock`, `SingleFlightRefresh` → singleton.

**Rationale**:
- Spec FR-027: facade is safe as a DI singleton with multiple in-flight sign calls. Cache and refresh-slot singletons are how the single-flight-per-cache-key guarantee is enforced across the whole process.
- Scoped use cases let ASP.NET hosts attach per-request correlation IDs without leaking state across requests; console / worker hosts use the Client's `LibraryCorrelationIdAccessor` equivalent.
- Identical lifetimes to eInvoice means an integrator using both products has a uniform mental model.

**Alternatives considered**:
- *Singleton everything*: rejected — would force `ICorrelationIdAccessor` to be singleton too, which doesn't fit per-request correlation IDs.
- *Transient everything*: rejected — defeats `ITokenCache` and `SingleFlightRefresh` (would create a fresh refresh slot per DI scope, breaking SC-007).

---

## R-11. Concrete vs swappable defaults — what ships with the SDK and what is consumer-supplied

**Decision** (consolidated for clarity):

| Port | Default adapter | Lifetime | Swap reason for consumers |
|------|-----------------|----------|---------------------------|
| `IMisaESignWireClient` | `MisaESignWireClient` (typed HTTP) + `ESignCallLogger` decorator | typed-client / scoped decorator | Replace with a fake for tests |
| `ITokenCache` | `InMemoryTokenCache` | singleton | Use Redis / distributed cache |
| `ITokenCacheKeySelector` | `DefaultTokenCacheKeySelector` (composes `userName + clientId + base-URL host`) | singleton | Add a tenant discriminator |
| `ICertificateSelector` | `FirstActiveCertificateSelector` | singleton | Pick by issuer DN / allowlist / longest validity |
| `ISystemClock` | `SystemClock` (wraps `TimeProvider.System`) | singleton | Test-time clocks |
| `ICorrelationIdAccessor` | `LibraryCorrelationIdAccessor` (Client) | scoped | ASP.NET vs console vs worker |

**Rationale**: Same column shape as [docs/architecture.md §Port-and-adapter seams](../../docs/architecture.md#port-and-adapter-seams). Keeping the table aligned makes the two products feel like one SDK family.

**Alternatives considered**: *Skip default selectors, force consumer registration*: rejected — every other port has a sensible default; selectors should too. Friction-free DX is a Principle-II side benefit (small public surface).

---

## R-12. Out-of-scope items confirmed deferred

For the record, the following are explicitly deferred and need no slice-1 implementation:

- **2FA / OTP flow** (`auth/two-factor-auth`, `auth/resend-otp-auth`) — slice 2 (`misa-esign-2fa-otp`). Slice 1 surfaces `requires2FA = true` on `AuthenticationFailedException` when MISA returns `errorCode = 122`, so consumers can build slice-2 themselves before the SDK supports it.
- **Non-PDF document types** (XML / Word / Excel) — slice 3 (`misa-esign-multi-format`). Slice 1 wires `documents/hash` and `documents/attachment` with only the `pdfDocs` field populated; the `xmlDocs`/`wordDocs`/`excelDocs` arrays are empty.
- **Webhook receiver** — slice 4 (`misa-esign-webhook`). Slice 1 only polls `Signing/status`.
- **Client-side hashing fallback** (the PHP/NodeJS path called out in the API doc §3.4) — out of scope for the family; MISA's `documents/hash` server-side path is the only one supported per the eSign spec-decomp plan.
- **`Certificates/by-certId` (detail-by-keyAlias)** — not needed for slice 1; `by-userId` returns the full chain.
- **`isAutoSign` cert flag** — surface it on `CertificateDto` for transparency, but the SDK does not branch on it in slice 1.

---

## Summary

No NEEDS-CLARIFICATION items remain. All technical decisions above were either pinned by the spec (which has already been through `/speckit-clarify`) or are the established `MisaConnect.EInvoice` precedent applied to the eSign family. Phase 1 design artifacts (`data-model.md`, `contracts/`, `quickstart.md`) lean directly on the decisions recorded here.

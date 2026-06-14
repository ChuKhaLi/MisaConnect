# Phase 0 Research: Per-User Credentials Seam

All Technical Context items are resolved (no remaining NEEDS CLARIFICATION). The four spec clarifications (Session 2026-06-15) and the change request's decided items (D1–D3) settle the design questions. This file records the decisions, rationale, and rejected alternatives.

## D-A — Seam shape: port-and-adapter + ambient, not per-call parameters

- **Decision**: Add a swappable `IMisaCredentialsAccessor` port (Application) with an options-reading default (Infrastructure), consulted per-call inside the awaited pipeline. The consumer flows the current signer's credentials via an `AsyncLocal` its accessor reads.
- **Rationale**: The SDK already solved "per-call ambient input read deep in the pipeline" with `ICertificateSelector` + cert-selection `AsyncLocal`, proven to flow through the SDK's DI child scopes. Mirroring it keeps the change additive and orthogonal: default consumers are untouched; the consumer swaps one `TryAdd` default.
- **Alternatives rejected**: (1) Add a credentials parameter to every `Sign*`/list-certs/OTP entry point — breaking-shaped, changes the public surface of every method, and does not flow into the internal seams (cache key, headers, login closure) without threading a parameter through every layer. (2) Construct a credential-bound client instance per signer — diverges from the established pattern and complicates DI.

## D-B — Placement of the new types (Constitution Principle I/II)

- **Decision**: `IMisaCredentialsAccessor` + `MisaCredentials` in `MisaConnect.ESign.Application.Abstractions` (beside the public `AccessToken` record and the other ports). The default adapter `OptionsMisaCredentialsAccessor` is `internal sealed` in a dedicated `MisaConnect.ESign.Infrastructure.Credentials` folder/namespace (mirrors `Infrastructure/Caching` + `Infrastructure/Certificates`). `CredentialsMode` is a **public** enum in `Infrastructure/Configuration` next to `ESignEnvironment` (an options value, not an adapter). [Request D1]
- **Rationale**: `MisaCredentials` is a consumer-extension/DI contract type paired with a port — same layer/rationale as `AccessToken`. `Domain` must stay zero-dependency and hold only protocol/entity types; a transport/DI concern there would wrongly elevate it. Public-in-Infrastructure for the bound option/enum follows the exact precedent of the existing public `MisaESignOptions`/`ESignEnvironment`/`AuthUnderWebdev`.
- **Alternatives rejected**: Domain placement (violates zero-dependency + has no consumer needing it); adapter inside `Configuration/` (blurs config-POCO vs adapter convention).

## D-C — `CredentialsMode` is Infrastructure-only (load-bearing invariant)

- **Decision**: `CredentialsMode` is read **only** by `MisaESignOptionsValidator` (and bound on `MisaESignOptions`). No Application type references it. The default `OptionsMisaCredentialsAccessor` does **not** inspect it — it unconditionally returns the option values. Dynamic behavior is achieved purely by the consumer pre-registering its own accessor; the mode flag only relaxes the startup validator.
- **Rationale**: An Application use-case (e.g. `EnsureAccessToken`) referencing the Infrastructure enum would be a forbidden Application→Infrastructure dependency (Principle I). Keeping the mode validator-only preserves layering and keeps the seam orthogonal.
- **Alternatives rejected**: Branching `if (CredentialsMode == Static)` inside any Application consumer or inside the default adapter — a layering violation; rejected in review.

## D-D — Per-call resolution contract: read each time, no SDK caching

- **Decision**: The SDK reads `IMisaCredentialsAccessor.Get()` per-call at each consumer (login closure, headers, wire client, cache key). It does **not** resolve-once-and-cache within a scope or the singleton. Mutual consistency (no torn read) relies on the consumer returning a **stable** value for the call's duration. [Clarification 2026-06-15 #1]
- **Rationale**: An `AsyncLocal` set by the consumer around the awaited call is stable for that call; multiple reads return the same snapshot. This matches the cert-selector pattern (read per-call) and avoids introducing scoped per-call state/threading. All four consumers run in the caller's async-flow, so one ambient scope yields identical values to all four.
- **Alternatives rejected**: Resolve-once-per-scope-and-cache (adds threading/scoped state for no benefit given a stable ambient); equality-assert debug guard (extra complexity, not warranted).

## D-E — Empty-credential fail-fast in `Compose()` [Request D2]

- **Decision**: `DefaultTokenCacheKeySelector.Compose()` throws `InvalidOperationException` when the resolved `UserName` or `ClientId` is null/empty (any cache-key path), rather than collapsing to `||host`.
- **Rationale**: Relaxing the startup validator in Dynamic mode removes the non-empty guarantee. A missing-ambient bug (consumer forgot to set the `AsyncLocal`, or blank static options under a misconfig) would otherwise collapse identities into one shared (Redis) cache slot → cross-user token bleed, the exact failure this feature prevents. The key selector is defensive by construction; correctness does not rely on the consumer.
- **Alternatives rejected**: Silent `||host` fallback (cross-user bleed); relying on the consumer to guarantee non-empty (fragile).

## D-F — Override registration: register-before only, documentation-only enforcement [Clarification #3]

- **Decision**: The supported override is **register the consumer accessor before `AddMisaConnectESign`** (its `TryAdd` then yields). No runtime guard is added against a register-after mistake. The docs correct the misleading "or just after" wording for this secret-bearing port.
- **Rationale**: A plain `AddSingleton` after the SDK's `TryAddSingleton` appends a second descriptor; `IEnumerable<IMisaCredentialsAccessor>` then surfaces both and the options-default stays constructible — strictly worse for a credentials port. Register-before guarantees exactly one accessor. A runtime guard diverges from how every other port behaves and adds SDK complexity for a documentation-addressable mistake.
- **Alternatives rejected**: Fail-fast multi-registration guard; startup warning — both diverge from existing port conventions.

## D-G — `EnsureAccessToken` ctor preserved; port adapted at the DI seam

- **Decision**: Keep `EnsureAccessToken`'s `Func<(string userName, string password)> credentialsAccessor` ctor parameter (type + position) unchanged. Resolve `IMisaCredentialsAccessor` in the DI factory and pass `() => { var c = accessor.Get(); return (c.UserName, c.Password); }`.
- **Rationale**: Both the 5-arg call site (`MisaESignClientUsernameContextTests`) and 6-arg call site (`EnsureAccessTokenTests`) depend on the `Func` parameter. Adapting the port at the DI seam keeps all existing unit tests green (zero fixture change) while making the login body per-call.
- **Alternatives rejected**: Retype the ctor to take the port — breaks both test files for no benefit.

## D-H — Secret redaction on `MisaCredentials.ToString()` [Constitution VIII]

- **Decision**: Override the positional-record auto-`ToString()` to emit `UserName`/`ClientId` plainly and `ClientKey`/`Password` as `<redacted>`.
- **Rationale**: The C# positional-record default `ToString()` prints all members, so `$"{creds}"`, a structured-logging sink, or an exception-context capture would dump both secrets. Redaction guards against accidental leakage.
- **Alternatives rejected**: Rely on callers never stringifying credentials (fragile, violates Principle VIII).

## D-I — Scope: OTP relink in, webhook/begin-sign deferred [Clarification #2 + Request §4]

- **Decision**: The synchronous-poll path (sign + list-certs + token acquisition/refresh) is in scope. OTP relink (`ExchangeOtp`/`ResendOtp`) rides the same header (#1/#2) + cache-key (#4) changes with no new public surface and gets coverage. The begin-sign/webhook `clientId` accessors and `FinalizeFromWebhook` are deferred (the consumer's v1 is sync-poll only; those `clientId`s are correlation/anti-spoof tags, not outbound credentials).
- **Rationale**: Keeps the slice focused on the consumer's actual v1 need while keeping the seam consistent across every path the consumer drives.
- **Alternatives rejected**: Defer relink too (would leave relink behavior unverified for dynamic credentials); pull the webhook path forward (unused by the consumer, adds per-session `clientId` complexity).

## D-J — No extra Dynamic-mode diagnostics [Clarification #4]

- **Decision**: Beyond gating the four credential checks, the validator and the options debug view are unchanged. No `CredentialsMode` debug-view line, no "reject stray static creds in Dynamic mode" check.
- **Rationale**: Keeps the change minimal; no new secret-bearing key is introduced so the redaction map is untouched.
- **Alternatives rejected**: Debug-view line / stray-cred rejection — deferred as non-essential.

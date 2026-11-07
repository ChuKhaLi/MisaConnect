# Changelog

All notable changes to MisaConnect will be documented here. This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [2.10.0] - 2026-11-07

Minor release of both families: EInvoice gains e-ticket template listing. ESign: no change (version moves in lock-step).

### Added

- **EInvoice:** `IMisaEInvoiceClient.ListTicketTemplatesAsync(TicketTemplateListRequestDto? request = null, CancellationToken ct = default)` → `IReadOnlyList<TicketTemplateDto>`, for MISA's `/webapp/templates/ticket` ("Lấy danh sách mẫu vé điện tử", API document §3.2; Ticket object §8.2). The request carries `WithCode`, `Start`, `Length` (1–100, default 20) and `FromCashRegister` (MISA's `invoiceCalcu`). Out-of-range values throw `MeInvoiceException` (`Validation`) before any request. Rows are returned active and inactive (`IsActive`), since the endpoint is paged.
- Application: `ListTicketTemplates` use case, `TicketTemplateListRequest`; Domain: `TicketTemplate`. The `IMeInvoiceClient` port gains `ListTicketTemplatesAsync` with a default implementation (throws `NotImplementedException`, like the lookup and amendment members), so custom implementations keep compiling.
- `MisaEInvoiceClient` keeps its existing public constructor; an instance built with it throws `InvalidOperationException` from `ListTicketTemplatesAsync`. DI uses the new constructor.
- Sample API: `GET /api/templates/ticket?withCode&start&length&fromCashRegister`.

### Repo (TypeScript tooling, nothing shipped)

- The TypeScript workspace moves from npm workspaces to **pnpm 12** (`pnpm-workspace.yaml`, `pnpm-lock.yaml`, `packageManager`). Isolated `node_modules`; only `esbuild` may run install scripts; the `minimatch` override moves to `pnpm-workspace.yaml`. Samples link the packages with `workspace:*`.
- CI (`ts-ci`, `ts-sandbox`, `release-ts`) installs and runs on pnpm. Publishing is unchanged: `npm stage publish` (trusted publishing, staged approval). Published tarballs contain the same files.

## [2.9.0] - 2026-11-07

Minor release of both families. EInvoice DI registration order no longer matters for its swappable ports. ESign: no change (version moves in lock-step).

### Changed

- **EInvoice:** `AddMisaConnectEInvoice` registers its default ports with `TryAdd…` instead of `Add…`: `ISystemClock`, `IRefIdGenerator`, `ITokenCache`, `IInvoiceValidator`, `IDeleteOptionsAccessor` (singleton) and `ITemplateResolver` (scoped); `AddEInvoiceClient` does the same for `ICorrelationIdAccessor` (scoped). A consumer registration now wins whether it is made before or after the call (before 2.9.0, one made before was silently overridden). Matches ESign. Lifetimes are unchanged; use cases, HTTP handlers and the `IMeInvoiceClient` decorator still use `Add…`.
- **Upgrading:** check for port registrations made *before* `AddMisaConnectEInvoice` / `AddEInvoiceClient`. In 2.8.0 they were silently ignored; from 2.9.0 they take effect, with the lifetime you gave them (e.g. a leftover scoped `ITokenCache` would replace the singleton `InMemoryTokenCache`).
- Side effect: with a consumer registration made before the call, `IEnumerable<ITokenCache>` (and the other ports) now holds one entry instead of two; calling `AddMisaConnectEInvoice` twice no longer duplicates the default port registrations listed above (use cases, handlers and the options validator are still added each time).

### Fixed

- `docs/einvoice/configuration.md` ("Custom DI overrides") showed a register-before example that did not take effect; it does now. `docs/architecture.md` updated to match.

### Repo (tests only, nothing shipped)

- Dev toolchain: npm `overrides` pins `minimatch` to `^11.0.5` (GHSA-g3hv-75wg-m249, ReDoS; eslint and typescript-eslint pulled 10.2.6). `npm audit` is clean; eslint lints the same 143 files. Not shipped: both npm packages have zero runtime dependencies.
- Tests move from xunit 2 (deprecated) to **xunit.v3 4.0.2** on **Microsoft Testing Platform** (`global.json` `test.runner`); `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` and `Xunit.SkippableFact` are gone. Run-time sandbox skips use xUnit's built-in `Assert.Skip`.
- Test commands: `dotnet test --project tests/dotnet/<Project>` (a bare directory argument is rejected). `--filter` keeps the VSTest syntax (`FullyQualifiedName~...`); a filter that matches no test exits 8. CI writes TRX through xunit's `--report-xunit-trx` and ignores exit code 8 only when a `test_filter` input is given.
- Sandbox tests with a `Timeout` now pass `TestContext.Current.CancellationToken` to the client, so a timed-out test is actually cancelled.

## [2.8.0] - 2026-11-07

Minor release of both families: adds .NET 10 builds. .NET 8 (and Microsoft.Extensions 8.x) reach end of support on 2026-11-10; the `net8.0` builds stay for apps that have not moved yet. No API or behaviour change.

### Added

- Both packages ship `lib/net10.0` alongside `lib/net8.0`. Both targets depend on Microsoft.Extensions 10.0.14; on `net10.0`, `System.Text.Json` comes from the framework.

### Changed

- Requirement made explicit in the README and getting-started pages: an app must use `Microsoft.Extensions.*` 10.0.14 or later (an 8.x pin fails restore with NU1605). This has been the case since 2.6.0.
- Repo: the unit tests run on both `net8.0` and `net10.0`; integration tests and samples run on `net10.0` (ASP.NET Core 10). The package smoke gate runs its console and ASP.NET Core apps on both targets.

## [2.7.1] - 2026-11-06

Patch release of both families: fixes the packages so that consumer apps can load them (see "Fixed (packaging)"). Upgrade from 2.4.0–2.7.0: those versions crash at startup in console/worker apps, and 2.6.0–2.7.0 also in ASP.NET Core 8 apps. No API or behaviour change.

### Fixed — `MisaConnect.EInvoice` and `MisaConnect.ESign` (packaging)

- **Both packages now declare every dependency their bundled assemblies load.** The Domain, Application and Infrastructure assemblies are bundled into each package, so their own package references never reached the nuspec. As a result, `AddMisaConnectESign` / `AddEInvoiceClient` threw `FileNotFoundException` (first for `Microsoft.Extensions.Options.ConfigurationExtensions`) in:
  - console and worker apps, since at least 2.4.0;
  - ASP.NET Core 8 apps, since 2.6.0 (the move to Microsoft.Extensions 10.x).

  The packages now also depend on `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options.ConfigurationExtensions` and `System.Text.Json` (all 10.0.14). An app that pins one of these Microsoft.Extensions packages below 10.0.14 now gets a restore-time NU1605 downgrade error naming the package, instead of a crash at startup.
- New CI gate (`scripts/package-smoke.sh`, run in `ci.yml` and before the NuGet push in `release.yml`): it packs both packages and runs a net8.0 console app and an ASP.NET Core 8 app against them from a local feed.

### `MisaConnect.ESign` — Docs

- `docs/esign/configuration.md` "`ITokenCache` — custom caches": decorators should forward `RemoveIfMatchesAsync`, and mocks of `ITokenCache` should set it up (an un-configured mock does not run the default body). This applies since 2.7.0.
- New end-to-end test pinning the cached token lifetime for odd `expiresIn` values on login.

## [2.7.0] - 2026-11-06

Minor release of `MisaConnect.ESign`: closes the follow-ups from the 2.6.0 review. It adds one public member, `ITokenCache.RemoveIfMatchesAsync`, a default interface method, so existing custom caches compile and behave unchanged. `MisaConnect.EInvoice` has no code changes; its version is aligned to 2.7.0 because the release pipeline packs both families together.

### `MisaConnect.ESign` — Added

- `ITokenCache.RemoveIfMatchesAsync(string key, string expectedValue, CancellationToken ct)`, a default interface method that removes an entry only while its token value matches. Existing custom caches keep working unchanged; the default implementation reads, compares and removes. The built-in in-memory cache overrides it atomically. The SDK now uses it on every "evict only the stale token" path (401 fallback, failed proactive refresh). See `docs/esign/configuration.md` "`ITokenCache` — custom caches".

### `MisaConnect.ESign` — Fixed

- A shared token refresh that fails after every waiter has cancelled no longer raises `TaskScheduler.UnobservedTaskException`.
- `expiresIn` is read leniently: a numeric string such as `"3600"` or `"-1"` is accepted, and any other odd value (a non-integer number, another string, a boolean, an object or an array) counts as missing (300 s) instead of failing the login. This matches `@misaconnect/esign` for integers and numeric strings; the two still differ on out-of-range numbers and on numbers written with a fraction or exponent (`3600.0`, `1e3`), which .NET treats as missing.
- Docs: the shared-refresh notes now say which calls inside it are bounded (HTTP) and which are not (custom `ITokenCache`), and that its auth calls carry the correlation ID of the flow that started it.

## [2.6.0] - 2026-11-06

Minor release of both families. `MisaConnect.ESign` fixes the token-lifecycle and correlation edge cases left open by the 2.5.0 review. Both packages now depend on Microsoft.Extensions 10.x (see "Dependencies"); that consumer-visible dependency change is why this is a minor release rather than a patch. No public API change in either family. `MisaConnect.EInvoice` has no code changes.

### `MisaConnect.ESign` — Fixed

- A shared token refresh (proactive, 401-driven, or the OTP exchange) is no longer cancelled when the caller that started it cancels. Each waiter now stops on its own `CancellationToken`; the refresh completes for the others and its result is cached.
- When the refresh after a 401 fails, the SDK reuses a fresh token another flow stored meanwhile and evicts the cache entry only while it still holds the token that was rejected, as `EnsureAccessToken` already did. This avoids an extra login (and a spurious OTP prompt for a two-factor principal). The "401 even after a fresh token" eviction follows the same rule.
- Login, refresh and fallback calls made inside the HTTP handler pipeline now carry the flow's `X-Correlation-Id` (in the request header, the call log and exceptions) instead of a handler-scope ID (a gap that dates back to 2.4).
- `expiresIn` lifetime: a missing `expiresIn` now means 300 s (it was 0, which refreshed the token on every request), and 1–59 s is raised to 60 s so the 30 s refresh skew leaves a usable window. An explicit 0 or less is still honoured as already expired, so an expired refresh result still falls back to login. Applies to login, two-factor and refresh, in both response shapes.

### Dependencies — `MisaConnect.EInvoice` and `MisaConnect.ESign`

- The shipped Microsoft.Extensions dependencies move from 8.x to 10.0.14: `Configuration`, `Configuration.Binder`, `DependencyInjection` and `DependencyInjection.Abstractions` (#14, #16, #17), then `Http`, `Logging.Abstractions`, `Options` and `Options.ConfigurationExtensions`. `System.Text.Json` moves from 8.0.5 to 10.0.14 (8.0.6 via #12 on the way). The packages still target `net8.0` (the 10.x packages support it), but a consuming app now resolves these packages at ≥ 10.0.14 and cannot pin them to 8.x.
- Test-only, not shipped: xunit 2.4.2 → 2.9.3, `Microsoft.NET.Test.Sdk` 17.14.1, `Microsoft.AspNetCore.Mvc.Testing` 8.0.33 (#12).

## [2.5.0] - 2026-11-05

### `MisaConnect.ESign` 2.5.0

Minor release of `MisaConnect.ESign`: back-ports the fixes and rulings first made in the TypeScript package `@misaconnect/esign` (see `docs/superpowers/specs/2026-11-05-esign-dotnet-ts-parity-design.md`), and corrects the eSign docs. Public changes are additive except for one constructor: `FinalizeFromWebhook`'s 2.4 constructor is removed (see "Public use-case constructors" under "Behaviour changes"). Code that builds the SDK through `AddMisaConnectESign` is unaffected. `MisaConnect.EInvoice` ships unchanged; its project version is only aligned to 2.5.0 because the release pipeline packs both families together.

#### Fixed

- After a 401-triggered token refresh, the rest of the flow now uses the refreshed token instead of the stale one, and no longer refreshes again on every later call.
- When the refresh after a 401 fails for any reason, the SDK evicts the cached token, logs in once more and retries the request once, and fails with `Authentication` only if that fails too.
- A cached token with 30 s or less left is refreshed proactively (the skew was declared but never used).
- Proactive refreshes run in the same single flight as 401-driven refreshes, keyed by the token cache key, so concurrent flows for one principal make one refresh call. A refresh that fails no longer evicts a token another flow has just stored, and a refresh result that is already expired counts as a failed refresh and falls back to login.
- Any refresh-path failure, including non-SDK exceptions, leaves no stale entry in the token cache.
- `POST Signing/hash` is no longer retried after a 5xx, a timeout, or a failure after the request may have been sent (see "Behaviour changes").
- Every ESRM JSON response goes through the content-type guard, not only the certificate list.
- MISA's `error` field accepts boolean, `"true"`/`"false"` strings (any case) and null, in both the flat and the nested login shape, without dropping the envelope.
- Two-factor code `122` is recognised on any HTTP status that reaches login error mapping, in both the nested and the flat body shape, whatever the `error` flag says (`true`, `false`, null or missing). The same applies to the two-factor endpoint.
- A webhook validation failure whose `IWebhookDeliveryHook` throws `OperationCanceledException` because the caller's token was cancelled now propagates that cancellation instead of swallowing it.
- Docs: `docs/esign/getting-started.md` used a non-existent namespace, a placeholder logo, and listed `SignXmlAsync` overloads that do not exist; `configuration.md` and `sandbox-setup.md` were corrected (URL rule, override ordering, sandbox skip rules, no reachability probe).

#### Added

- `IWebhookCredentialsResolver` (`MisaConnect.ESign.Application.Abstractions`): supplies the credentials of the principal that began a webhook-mode transaction, so `CredentialsMode.Dynamic` now works with webhooks. The session already recorded the `clientId`; it now also records the `userName` (never secrets). The default returns the option credentials in `Static` mode and `null` in `Dynamic` mode; register your own before `AddMisaConnectESign`.
- `MisaESignOptions.Webhook.DeliverValidationFailuresToHook` (default `true`): whether webhook validation failures reach `IWebhookDeliveryHook` as a failure outcome. The default becomes `false` in 3.0.
- `ISigningSessionStore.CacheTerminalAsync` (default interface method, no-op), the `SigningSessionCachedTerminal` record and `SigningSession.CachedTerminal`, so a terminal state can be stored once per transaction. `SigningSession.UserName` records the principal that began the transaction.
- `HandleWebhook.HandleMalformedAsync`: answers a webhook body that failed to parse with ACK `webhook.malformed` and a failure outcome.
- `EnsureAccessToken.ProactiveRefreshSkew` (30 s): a cached token with this much life left or less is refreshed rather than reused.
- An `EnsureAccessToken` constructor overload with a trailing `ExchangeOtp.SingleFlightDelegate? singleFlight` parameter, which coalesces concurrent refreshes for one principal. The existing constructor is unchanged and refreshes without coalescing.

#### Behaviour changes

- **`Signing/hash` retry rule.** It is retried only on 429 or when the connection was never established; never on 5xx, a timeout or a failure after the request may have been sent, because a retry could start a second signing transaction (a second approval on the signer's phone). All other calls keep their retry rules.
- **ESRM error mapping.** An HTML or otherwise non-JSON ESRM response now maps to `UnexpectedContentType`, and an unparseable JSON body to `UnparseableResponse`, instead of `EmptyResponse`. A bodyless 2xx ESRM response without a JSON content type now surfaces as `UnexpectedContentType` (was `EmptyResponse`).
- **Guard message.** The content-type guard message reports the content type and body length and no longer includes a body snippet.
- **Webhook outcomes are confirmed by MISA.** A webhook is a trigger: the SDK calls `Signing/status` for every accepted webhook (one extra call per webhook), and MISA's status, error code and signature decide the outcome. Webhook FAILED/CANCELLED outcomes now report MISA's status and error code (from `Signing/status`), not the webhook's.
- **A terminal state is delivered once.** The first recorded terminal state (success, failed or cancelled) of a transaction is stored once; later webhooks for it return the original ACK and do not call `IWebhookDeliveryHook` again. A custom `ISigningSessionStore` that does not implement `CacheTerminalAsync` keeps the 2.4 behaviour for failed/cancelled terminals.
- **Malformed webhooks.** A malformed body (missing or empty `messageId`/`clientId`/`transactionId`, unknown `status`, a null entry in `signatures`) now gets ACK `webhook.malformed` and a failure outcome instead of an exception. `status` is matched case-insensitively.
- **Finalize errors.** Errors while confirming or finalizing a webhook (including a two-factor requirement, which finalize never prompts for) return ACK `webhook.finalize_failed` instead of throwing. Exceptions thrown by your `IWebhookDeliveryHook` on success/terminal delivery still propagate out of `HandleWebhookAsync`; the success is cached first, so MISA's retry gets the cached ACK.
- **Two-factor challenges are per principal** (`clientId` + `userName`). `SignInWithOtpAsync` and `ResendOtpAsync` act on the current principal's challenge only.
- **3.0 default flip.** `Webhook.DeliverValidationFailuresToHook` defaults to `true` in 2.x so a minor release does not silently change behaviour. It flips to `false` in 3.0, because a public webhook endpoint lets anyone trigger hook calls with forged payloads. Set it to `false` now to opt in.
- **Public use-case constructors (not a supported extension point).** The Application use-case classes are public but built by DI. `HandleWebhook` (new trailing `deliverValidationFailuresAccessor`) and `BeginSign{Pdf,Xml,Word,Excel}` (new optional `userNameAccessor`) keep their 2.4 constructors as compatibility overloads, so 2.4 source and binaries still work: the `HandleWebhook` overload delivers validation failures to the hook, and the `BeginSign*` overloads record no user name. `FinalizeFromWebhook`'s 2.4 constructor is removed, because it now requires the wire client, the credentials resolver, the credentials scope and the mode accessor. That is a source and binary break for code that constructs `FinalizeFromWebhook` directly instead of through `AddMisaConnectESign`.

## [2.4.0] - 2026-11-04

Minor release of `MisaConnect.EInvoice`: slice 009 bug fixes, one of which changes an error category (see "Behaviour change"). `MisaConnect.ESign` 2.4.0 has no code changes from 2.3.0; it ships at the same version because the release pipeline packs both families together.

#### Behaviour change — `MisaConnect.EInvoice` (slice 009)

- Batch-size errors now report `MeInvoiceErrorCategory.BatchOrSize` instead of `Validation`. This covers the MISA codes `InvoiceQuantityTooLarge` and `XMLTooLong` and the SDK's own check for a `SaveDraftInvoices` batch of more than 30 invoices (raw code `InvoiceQuantityTooLarge` and rule `FR-030` unchanged). Code that catches `Validation` to handle oversize batches must also handle `BatchOrSize`.
- An empty `SaveDraftInvoices` batch now throws `Validation` with raw code `ValidationFailed` and the message "Batch is empty; …". It used to reuse `InvoiceQuantityTooLarge` with a misleading "exceeds the limit" message.

#### Fixed — `MisaConnect.EInvoice` (slice 009)

- `MisaEInvoiceClientFactory.Create(MisaEInvoiceOptions)` dropped `Delete.IncludeRawErrorMessage`, so a factory-built client never surfaced raw MISA messages on delete/lookup failures even when opted in. All options are now copied, and a test fails when a new option is not.
- Call logs no longer include `customerTaxCode` (the configured `Misa:EInvoice:TaxCode`). The SDK's own log scrubber treats tax codes as sensitive. Log queries that filtered on it should use `CorrelationId`.
- `docs/architecture.md`: `ICorrelationIdAccessor` has a built-in default (`LibraryCorrelationIdAccessor`); it is not something you must register. The EInvoice port table also said to register overrides *before* `AddMisaConnectEInvoice`, but EInvoice registers its defaults with `Add…`, so overrides must come *after* the call.

#### Changed

- Sample API (`samples/dotnet/MisaConnect.Samples.Api`) wires up eSign only when a `Misa:ESign` configuration section exists. Since the eSign webhook slice it always required eSign credentials, so an eInvoice-only host failed at startup with `OptionsValidationException` — and 52 `MisaConnect.EInvoice.IntegrationTests` (which host the sample) failed unnoticed.
- CI (`ci.yml`) now runs both integration test projects (fake server + sample host; sandbox facts skip) on every push/PR, not only weekly.
- Repository layout: .NET projects moved under `src/dotnet`, `tests/dotnet`, `samples/dotnet` to make room for the TypeScript packages under `src/typescript`. No package changes.
- Sandbox CI (`integration.yml`) lowers the runner's MTU to 1280. Without it, 14 `MisaConnect.EInvoice` sandbox tests hung until `HttpClient.Timeout` and the weekly run had been failing since October.

#### Documentation

- `MisaConnect.EInvoice` README, new section "Calling MISA from outside Vietnam". The network path to MISA's eInvoice API drops TCP segments near 1500 bytes (a path-MTU blackhole). From outside Vietnam, any request over ~1.4 KB therefore times out, and that includes every bearer-authenticated call. Lowering the interface MTU to 1280 fixes it. Hosts in Vietnam are not affected.
- `MisaConnect.EInvoice` README: `TaxCode` is the bare tax code. A "Mã số thuế" such as `6868686868-267` from MISA is `<TaxCode>-<AppId>` and must be split into the two options.

## [2.3.0] - 2026-07-13

First stable release of `MisaConnect.EInvoice` — .NET SDK for MISA MeInvoice
(templates, preview, save draft, draft PDF, delete draft, lookup, replacement /
adjustment issuance). Previously published as `1.0.0-preview.2`.
`MisaConnect.ESign` 2.3.0 is identical to 2.2.0 (shared version line — the
release pipeline packs both packages at one version; nuget push uses
`--skip-duplicate` so republish is safe).

#### Added

- **`MisaConnect.EInvoice` package (stable).** Facade `IMisaEInvoiceClient`:
  `ListTemplatesAsync`, `PreviewAsync`, `SaveDraftAsync`, `GetDraftPdfAsync`,
  `DeleteDraftAsync`, `LookupByRefIdAsync`, `LookupStandardAsync`,
  `LookupCalculatingAsync`, `IssueReplacementAsync`, `IssueAdjustmentAsync`.

## [2.2.0] - 2026-06-15

`MisaConnect.ESign` — adds a per-user credentials seam so a consumer can supply MISA credentials per signing call instead of from static options, without changing any existing signing method. Additive and non-breaking (MINOR); target `2.2.0`. The default path — no accessor registered and `CredentialsMode` unset — is byte-identical to 2.1.1. See `specs/008-esign-per-user-credentials/`.

#### Added

- **`IMisaCredentialsAccessor` (port, `MisaConnect.ESign.Application.Abstractions`) and `MisaCredentials` (record, same namespace).** `IMisaCredentialsAccessor.Get()` resolves the current call's MISA credentials, consulted per-call inside the awaited pipeline. `MisaCredentials(string ClientId, string ClientKey, string UserName, string Password)` carries one signer's full credential set; its `ToString()` redacts `ClientKey` and `Password` so structured logging / interpolation cannot leak the two secrets.
- **Default accessor `OptionsMisaCredentialsAccessor` (internal)** reads the four credential options and is registered via `TryAddSingleton`, so a consumer that pre-registers its own accessor wins. Overrides for this secret-bearing port MUST be **register-before** `AddMisaConnectESign` (a plain `AddSingleton` after the SDK's `TryAddSingleton` appends a second descriptor and leaves the options-default accessor constructible) and MUST be singleton-safe / ambient (`AsyncLocal`)-backed — never scoped (a scoped accessor is a captive-dependency failure).
- **`MisaESignOptions.CredentialsMode` (`CredentialsMode { Static = 0, Dynamic = 1 }`, in `MisaConnect.ESign.Infrastructure.Configuration`), default `Static`**, bound from `Misa:ESign:CredentialsMode`. In `Static` mode the four credential options are required at startup; in `Dynamic` mode the validator skips **only** the four credential checks (BaseUrl / Environment / Polling / transport-retry / OTP / webhook still validate). An absent key resolves to `Static`, so existing appsettings are byte-identical.

#### Changed

- `ClientHeadersHandler`, `MisaESignWireClient` request building, the `EnsureAccessToken` login closure, and `DefaultTokenCacheKeySelector` now resolve credentials through `IMisaCredentialsAccessor` per-call. With the default accessor the resolved values equal the static options, so the `x-clientId`/`x-clientKey` header injection, the login body, and the token-cache-key shape `{UserName}|{ClientId}|{host}` are all unchanged.

#### Notes

- The begin-sign / webhook `clientId` usages (session-correlation tag and inbound anti-spoof match value, not outbound credentials) still read the static option — deferred. Per-user webhook sessions would later need per-session handling.

## [2.1.1] - 2026-06-14

`MisaConnect.ESign` — fixes remote signing against the live MISA ESRM service, latent before 2.1.0 and exposed by the 2.1.0 routing fix. See `specs/007-fix-esign-hash-empty-doc-arrays/`. No public-surface change (patch). Empty-array fix verified against the MISA eSign sandbox.

#### Fixed

- **`documents/hash` and `documents/attachment` no longer ship empty document-type arrays.** A PDF (or Word/Excel/XML) sign populated only its own array but serialized the unused ones as `"xmlDocs":[],"wordDocs":[],"excelDocs":[]`; MISA's ESRM endpoint rejected those with HTTP 400 (`Phải có ít nhất 1 tài liệu`). The four document-type arrays are now omitted when not in use, so each request carries exactly the array it needs. Remote signing now completes against the real ESRM service.
- **An unset `SignatureInfo.Page` no longer fails.** MISA requires a visible signature's `Page >= 1`; a null `Page` was dropped on the wire and rejected. It now defaults to `1` (first page), and the SDK logs that the default was applied. An explicitly set `Page` is sent unchanged; the existing client-side `Page < 1` validation is unchanged.
- **MISA error envelopes with `"error": null` are no longer swallowed.** The `error` field is now nullable, so a 400 body that sends `"error": null` (such as the empty-array rejection) parses correctly and surfaces its `errorCode` (e.g. `e400`) instead of `errorCode=<none>`.

#### Changed

- When `MisaESignOptions.Errors.IncludeRawErrorMessage = true`, MISA's per-property `validationFailures` (`property` + `failureReason`) are now included in the error `Detail`, so a rejected request is self-diagnosing. With the flag left at its default (`false`), error detail is unchanged. Synthesized error codes/categories are unchanged.

## [2.1.0] - 2026-06-13

`MisaConnect.ESign` — fixes MISA eSign RemoteSigning ESRM routing against environments where the auth app and the ESRM microservices live at different base paths (e.g. the auth app under `/webdev/`, ESRM at the host root). See `specs/006-fix-esrm-routing/`.

#### Added

- `MisaESignOptions.AuthUnderWebdev` (`bool?`, default `null`) — overrides where the `login` / `two-factor` endpoints are served. `null` derives from `Environment` (Sandbox ⇒ under `/webdev/`, Production ⇒ host root, per the API doc); `true`/`false` force it. Does not affect refresh/resend (always `/webdev/`) or ESRM (always host root). Additive, non-breaking.

#### Fixed

- **ESRM calls no longer route under the configured base path.** `Misa:ESign:BaseUrl` is now normalized to its origin (scheme + host), so ESRM endpoints (`external/esrm/…`) always resolve at the host root and `refreshtoken`/`resend-otp` resolve with a single `/webdev/` segment — regardless of whether the configured base URL includes a path such as `/webdev/`. Previously a `/webdev/`-suffixed base sent cert-list/signing requests to `…/webdev/external/esrm/…`, which returned the SPA `index.html`. **No consumer configuration change is required** (the path is tolerated).
- **A non-JSON success on an ESRM endpoint now fails loudly.** A `2xx` response whose body is not JSON (e.g. `text/html`) on the certificate-list endpoint raises a clear `ESignGeneralException` naming the endpoint and content type (with a truncated body snippet, no secrets) instead of silently collapsing into an empty list / "no active certificate". A legitimately empty JSON array still surfaces as "no active certificate".

#### Notes

- The remote-signing bearer (`AuthorizationRM` = `remoteSigningAccessToken`) was investigated and confirmed already correct; no token-selection change was made.

## [2.0.2] - 2026-05-23

Documentation patch. No source code or public API changes.

### Fixed

- Per-package NuGet READMEs: replaced the leading `<p align="center"><img></p>` + `<h1 align="center">` HTML block with CommonMark. NuGet.org's Markdig renderer strips raw HTML, so the logo and title were previously appearing as literal `<p align="center">…</p>` text on both package detail pages. The image and heading now render correctly.

### Added

- GitHub Pages documentation site at <https://chukhali.github.io/MisaConnect/>, built from `docs/` via Jekyll + Just-the-Docs (`remote_theme`). Sidebar navigation covers EInvoice, ESign, and Architecture; the existing per-product guides (`getting-started`, `configuration`, `sandbox-setup`) are reused — only Jekyll front matter was added.
- Each per-package NuGet README now links to its docs section on the new Pages site.
- Root `README.md` gains a Docs badge and a direct link to the hosted docs.

## [2.0.1] - 2026-05-23

Documentation patch. No source code or public API changes.

### Changed

- Per-package NuGet READMEs: each `.nupkg` now ships its own focused README (`src/MisaConnect.EInvoice.Client/README.md` and `src/MisaConnect.ESign.Client/README.md`) instead of the shared root README, so the NuGet.org listing pages no longer cross-reference the sibling package.
- Root `README.md` rewritten as a GitHub landing page with shields.io NuGet version + download badges for both packages and the embedded `icon.png` rendered as a centered logo.
- `docs/architecture.md` updated: ESign documented as shipped in v2.0.0 (previously listed as "planned for v2.0"); added the ESign port-and-adapter table; wire-format-fidelity section now references both families' Wire/Mapping namespaces.
- Documentation reorganized into product subfolders: `docs/einvoice/{getting-started,configuration,sandbox-setup}.md` and `docs/esign/{getting-started,configuration,sandbox-setup}.md`. The new ESign guides cover `Misa:ESign` configuration (including the slice-4 `Webhook` block) and the `MISACONNECT_ESIGN_SANDBOX_*` env-var family.

### Removed

- `docs/misa-esign-spec-plan.md` — pre-release planning snapshot superseded by `specs/001`–`005` and the `[2.0.0]` CHANGELOG entry above.

## [2.0.0] - 2026-05-23

Public API is the union of slices 1–4 of the `MisaConnect.ESign` product family, previewed through `2.0.0-preview.1`..`preview.4`. This is the first stable release of `MisaConnect.ESign`.

### Added

#### Slice 1 — PDF signing flow (`MisaConnect.ESign`)

- Public DI entry point `services.AddMisaConnectESign(IConfiguration)` (and `Action<MisaESignOptions>` overload) binding the `Misa:ESign` configuration section.
- Public facade `IMisaESignClient.SignPdfAsync(SignPdfRequestDto, CancellationToken)` — end-to-end PDF signing via MISA eSign RemoteSigning (login → list certs → hash → sign → poll → attach).
- Public port interfaces: `ITokenCache`, `ITokenCacheKeySelector`, `ICertificateSelector`, `ISystemClock`, `ICorrelationIdAccessor`.
- Public options type `MisaESignOptions` (section name `Misa:ESign`) with nested `Polling`, `TransportRetry`, and `Errors` blocks.
- Public typed exception hierarchy: `ESignException` (base), `AuthenticationFailedException` (with `Requires2FA`), `NoActiveCertificateException`, `SignRejectedException` (with `RequiresUserCertSetup`), `SignTerminalStateException` (with `TerminalStatus`/`TransactionId`), `SignTimeoutException` (with `TransactionId`/`ElapsedTime`), `ESignTransportException` (with `LastStatusCode`/`AttemptCount`).
- HTTP handler pipeline: bounded exponential-backoff retry with full jitter (transient 5xx/429/timeouts), `AuthorizationRM` injection with 401-refresh-then-retry-once, single-flight refresh per cache key, `x-clientId`/`x-clientKey` headers on every call.
- In-memory token cache and call-logging decorator with structured log shape — never logs tokens, refresh tokens, `AuthorizationRM`, cert private bytes, raw PDF bytes, or end-user PII by default.
- In-repo `FakeMisaESignServer` (under `tests/MisaConnect.ESign.IntegrationTests/EsignFake/`) for deterministic offline E2E coverage.

#### Slice 2 — Two-factor authentication (OTP)

- `IMisaESignClient.SignInWithOtpAsync(otpCode, otpType, remember, ct)` — completes an in-progress 2FA challenge captured from a preceding `SignPdfAsync`.
- `IMisaESignClient.ResendOtpAsync(language?, ct)` — requests OTP re-delivery; returns a typed `OtpResendResultDto` (does not throw on documented MISA rejections).
- Optional `IOtpProvider` port (`ProvideAsync`, `RequestResendAsync`) — when registered, `SignPdfAsync` transparently consults it on the 2FA-required signal so the challenge never surfaces to the consumer.
- `OtpDeliveryChannel` enum (`SmsOrEmail = 0`, `Authenticator = 1`) in `Domain.Authentication`.
- Typed OTP exceptions in `Domain.Errors`: `InvalidOtpException`, `ExpiredOtpException`, `ExhaustedOtpAttemptsException`, `OtpRejectedException` — all sealed subclasses of `AuthenticationFailedException` (existing catches continue to pick them up).
- `AuthenticationFailedException.Username` property (non-null; populated only on the `errorCode = 122` re-challenge branch). Constructor gains an optional 5th `username` parameter, default `""` — fully backwards compatible.
- `OtpResendResultDto` (Client.Dtos) — 1:1 wrapper over `Application.Abstractions.OtpResendResult`.
- `MisaESignOptions.Otp` block with `DefaultResendLanguage = "en-US"`.
- Two new methods on `IMisaESignWireClient`: `TwoFactorAuthAsync(...)` and `ResendOtpAsync(...)`.

#### Slice 3 — Multi-format document signing (XML, Word, Excel)

- `IMisaESignClient.SignXmlAsync(SignXmlRequestDto, CancellationToken)` — end-to-end XML (XAdES) signing. Accepts either a `string Xml` (primary; matches MISA's "text content" semantics on `FileToSign` per §4.1.2) or `byte[] XmlUtf8Bytes` (secondary; UTF-8-decoded by the SDK). Construct via `SignXmlRequest.FromString(...)` / `FromUtf8Bytes(...)` factories.
- `IMisaESignClient.SignWordAsync(SignWordRequestDto, CancellationToken)` — end-to-end Word (OOXML `.docx`) signing using MISA's `wordDocs` per-format array on `/documents/hash` and `/documents/attachment` per §4.1.1 / §4.6 / §4.15.
- `IMisaESignClient.SignExcelAsync(SignExcelRequestDto, CancellationToken)` — end-to-end Excel (OOXML `.xlsx`) signing using MISA's `excelDocs` per-format array.
- `DocumentFormat` byte-backed closed enum (`Unknown = 0, Pdf = 1, Xml = 2, Word = 3, Excel = 4`) in `Domain.Documents`. Surfaced as the `Format` property on every typed exception and on every result DTO so consumers can branch on `(ExceptionType, Format)` without string-matching MISA's `devMsg`.
- `XmlSignatureContext` slim record (`SignatureName`, `HashAlgorithm`, `SignatureDescription`) in `Domain.Signing` — exposes only XAdES-meaningful fields. Visual-positioning fields are intentionally absent so the type system rejects consumer mistakes at compile time.
- Public client DTOs: `SignXmlRequestDto`, `SignWordRequestDto`, `SignExcelRequestDto`, `SignXmlResultDto`, `SignWordResultDto`, `SignExcelResultDto`, `XmlSignatureContextDto`, `SignatureDescriptionDto`.
- `SignPdfResultDto` gains a `Format = DocumentFormat.Pdf` property (additive; existing call sites unaffected).
- Every existing typed exception (`ESignException` base + every concrete subclass) gains an optional `format` constructor parameter at the end and a `Format` property. Slice-1 PDF call sites pass `DocumentFormat.Pdf`; slice-2 paths reached during a facade call inherit the called facade's format.
- Five new methods on `IMisaESignWireClient` (additive): `HashXmlAsync`, `HashWordAsync`, `HashExcelAsync`, `AttachSignatureToXmlAsync`, `AttachSignatureToWordExcelAsync`.
- Two new Application records: `XmlHashOutput`, `WordExcelHashOutput`. New `SignHashInput(string DocumentId, string Digest)` record — `SubmitSignHash.ExecuteAsync(...)` now takes this in place of the slice-1 `PdfHashOutput hash` parameter (each per-format hash output exposes a `ToSignHashInput()` projection). Wire shape on `/Signing/hash` stays byte-identical (FR-065).
- `ESignErrorMapper.Map(...)` gains a `DocumentFormat requestedFormat = DocumentFormat.Pdf` parameter and new per-format synthesized codes (`InvalidXmlInput`, `MissingMainDom`, `MissingSignatureId`, `UnsupportedDocumentVariant`).
- `ESignLogScrubber` redacts the new sensitive fields on `/documents/*` payloads: `mainDom`, `signatureId`, `document`.

#### Slice 4 — Webhook-mode signing

- `IMisaESignClient.BeginSignPdfAsync` / `BeginSignXmlAsync` / `BeginSignWordAsync` / `BeginSignExcelAsync` — initiate a sign without polling; the SDK runs login → cert → `/documents/hash` → `/Signing/hash`, registers a `SigningSession`, and returns a `BeginResultDto { TransactionId, Format }`. Throws `InvalidOperationException("Webhook-mode signing is disabled when Mode == Polling")` per FR-093.
- `IMisaESignClient.HandleWebhookAsync(WebhookEnvelopeDto, CancellationToken)` — entry point for inbound MISA webhook deliveries. Runs the four-step typed validation pipeline (FR-082), single-flight finalize via `/documents/attachment`, idempotent dedupe on the success path (FR-080), failure-not-cached invariant (FR-081), and short-circuit on terminal `FAILED`/`CANCELLED` envelopes (FR-083). Returns `WebhookHandleResultDto { Ack, Outcome }` for the consumer's HTTP layer to echo back to MISA.
- `IWebhookDeliveryHook` (`Client.Webhook` namespace) — consumer-implementable callback receiving the typed `WebhookOutcomeDto`. Default `NullClientWebhookDeliveryHook` logs at INFO; consumers register their own via `services.AddSingleton<IWebhookDeliveryHook, MyImpl>()` before `AddMisaConnectESign(...)`.
- `Application.Abstractions.ISigningSessionStore` — single new port for session bookkeeping. Default `InMemorySigningSessionStore` uses `ConcurrentDictionary` keyed by `(clientId, transactionId)` with lazy TTL eviction on read; it also implements `IFinalizeLockOwner` for the single-flight finalize lock.
- `Domain.Webhook.*` — new namespace with `WebhookEnvelope`, `WebhookSignature`, `WebhookStatus`, `WebhookAck`, `BeginResult`, `WebhookOutcome` + 3 variants (`SuccessWithSignedBytes`, `FailureWithError`, `TerminalWithoutFinalize`), `WebhookValidationCategory`.
- `Application.Sessions.*` — `SigningSession`, `PerFormatHashPayload` + 4 variants (Pdf / Xml / Word / Excel), `SigningSessionCachedSuccess`. Located in Application (not Domain) because they wrap Application-layer hash output types — staying compatible with Constitution Principle I (Domain has zero external deps).
- `Domain.Errors.WebhookValidationException` (abstract) + 5 sealed subclasses (`MalformedEnvelopeException`, `ClientIdMismatchException`, `UnknownTransactionException`, `IncompleteSuccessEnvelopeException`, `DocumentIdMismatchException`) — extends `ESignException` with `WebhookCategory` / `MatchedTransactionId` properties.
- `MisaESignOptions.Webhook` sub-section: `Mode` (`Polling`/`Webhook`/`Both`, default `Both`), `Session.Ttl` (default 24h), `Path` (default `/esign/webhook`), `Secret` (≥32 chars when set), `AllowedIps` (CIDR strings). Validated at startup by `MisaESignOptionsValidator`.
- `ESignErrorMapper.MapWebhookValidationToAck(WebhookValidationException)` — returns the namespaced ACK `errorCode` (`webhook.malformed`, `webhook.client_id_mismatch`, `webhook.unknown_transaction`, `webhook.incomplete_success`, `webhook.document_id_mismatch`).
- Sample API endpoint `MapESignWebhookEndpoint(IEndpointRouteBuilder, IConfiguration)` — paralleling `MapInvoiceEndpoints` / `MapTemplateEndpoints`. Mounts on the configured `Webhook.Path`, with optional shared-secret URL segment (constant-time compared via `CryptographicOperations.FixedTimeEquals`) and CIDR allowlist enforcement. Emits the FR-101 startup WARN when `Mode != Polling` and neither `Secret` nor `AllowedIps` is configured.
- `ESignLogScrubber` extended: redacts `signature`, `signatureId`, `mainDom`, `extraData`, `secret` JSON field values on captured envelopes (`extraData` object body redacted entirely).

### Changed

- Constitution amended (`.specify/memory/constitution.md` 1.0.0 → 1.1.0): Principle II and Principle VII widened to name `MisaConnect.<Product>.{Client, Domain}` as the public-surface pattern across `EInvoice` and `ESign` families; governance footer now requires major version bumps on every affected product-family package. Amendment is additive — no existing artifact (EInvoice 1.1.0 or any ESign preview) became non-compliant.

## [1.1.0] - 2026-05-15

### Added
- Package icon (`icon.png`, 256x256) embedded in the NuGet listing.

### Changed
- Build SDK pinned to .NET 10 LTS via `global.json` and `setup-dotnet` workflows. Target framework remains `net8.0` for broad consumer reach (still under LTS through Nov 2026).

### Breaking
- `AmendmentDtoMapper` demoted from `public` to `internal`. Only intended for Client-internal use; external callers should use `IMisaEInvoiceClient.IssueReplacementAsync` / `IssueAdjustmentAsync`. Caught immediately after v1.0.0 — no known external consumers.

## [1.0.0] - 2026-05-14

### Added
- Initial public release of `MisaConnect.EInvoice` targeting .NET 8.
- Use cases: `EnsureAccessToken`, `ListActiveTemplates`, `PreviewInvoice`, `SaveDraftInvoices`, `GetDraftPdfByRefId`, `DeleteDraftInvoice`, `LookupByRefIds`, `LookupStandard`, `LookupCalculating`, `IssueReplacementInvoice`, `IssueAdjustmentInvoice`.
- Port-and-adapter interfaces: `IMeInvoiceClient`, `ITokenCache`, `IInvoiceValidator`, `IRefIdGenerator`, `ISystemClock`, `ICorrelationIdAccessor`, `ITemplateResolver`, `IDeleteOptionsAccessor`.
- DI entry point `AddMisaConnectEInvoice(IConfiguration)` binding the `Misa:EInvoice` configuration section.
- Throttle retry, bearer-token auth, in-memory token cache, call-logging decorator.
- Sample console (`samples/MisaConnect.Samples.Console`) and minimal-API host (`samples/MisaConnect.Samples.Api`).
- 315 unit tests; integration tests for MISA sandbox and a fake server.

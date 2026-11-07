---
title: EInvoice
nav_order: 2
has_children: true
permalink: /einvoice/
---

# EInvoice
{: .no_toc }

SDKs for **MISA MeInvoice (hóa đơn điện tử)**, in two languages with the same operations:

| Language | Package | Start here |
| --- | --- | --- |
| .NET 8 / .NET 10 | [`MisaConnect.EInvoice`](https://www.nuget.org/packages/MisaConnect.EInvoice) | [.NET guide](./dotnet/) |
| TypeScript (Node ≥ 22) | [`@misaconnect/einvoice`](https://www.npmjs.com/package/@misaconnect/einvoice) | [TypeScript guide](./typescript/) |

Both SDKs talk to MISA's **meInvoice integration API** (`https://testapi.meinvoice.vn/api/integration`
for the sandbox, `https://api.meinvoice.vn/api/integration` for production). They sign in
(`/webapp/token`) and cache the bearer token for you, list invoice templates (`/webapp/templates`)
and e-ticket templates (`/webapp/templates/ticket`), render a PDF preview of an invoice without
saving it (`/webapp/preview`), save drafts (`/webapp/insert`), fetch a draft's PDF
(`/webapp/viewrefid`), delete a draft (`/webapp/delete`), look invoices up by RefId
(`/webapp/getlist`) or page through them by date (`/webapp/paging`, `/webapp/paging/calculating`),
and issue replacement and adjustment invoices (`/webapp/insert` with `EInvoiceStatus` 3 or 4). In
MISA's integration model an invoice created through the API is a draft: signing and publishing it
happen in the MISA web app (`app3.meinvoice.vn`). Field names, date formats and envelopes follow
MISA's API document verbatim
([`docs/misa-api-reference/`](https://github.com/ChuKhaLi/MisaConnect/tree/main/docs/misa-api-reference)).

## Concepts

These apply to both SDKs. The language guides link here instead of repeating them.

### Tax code and AppID

MISA identifies the calling application by a wire tax code of the form `<TaxCode>-<AppId>`, sent
in the sign-in body and in the `taxcode` header of every call. You configure the two parts
separately (.NET `TaxCode` / `AppId`, TypeScript `taxCode` / `appId`) and the SDK joins them.

MISA often hands out credentials as one "Mã số thuế" such as `6868686868-267`. That value is
`<TaxCode>-<AppId>`: set the tax code to `6868686868` and the AppID to `267`. If you pass the
combined value as the tax code, the SDK sends `6868686868-267-267` and MISA's token endpoint
answers `INVALID_TAXCODE`. MISA issues the AppID once, for sandbox and production alike; the tax
code, user name and password change at go-live.

The token MISA returns is valid for 14 days, and MISA asks integrators not to sign in on every
call. Both SDKs cache the token per wire tax code (in memory by default; the token cache is
swappable) and sign in again shortly before it expires, or once after a 401.

### With-code and without-code invoices

Vietnamese e-invoices are either **with a tax-authority code** (*hóa đơn có mã*: the tax authority
issues a verification code) or **without code** (*không mã*). MISA keeps the two kinds apart: every
endpoint takes an `invoiceWithCode=true|false` query parameter, and a template, draft or lookup
made with one value is not visible with the other.

The SDKs expose it as a `withCode` / `invoiceWithCode` argument. It defaults to `true` for listing
templates and ticket templates, preview, save draft and get draft PDF; it is required for delete
draft and the three lookups; for replacement and adjustment it is a field of the request
(`InvoiceWithCode` / `invoiceWithCode`, default `true`). Use the same value for every call about
one invoice. The second character of the invoice series tells you which kind a template is: `C`
with code, `K` without (see [Templates and series](#templates-and-series)).

### Templates and series

An invoice is issued on a **template** (*mẫu hóa đơn*) set up in the MISA web app, identified by
`IPTemplateID` and its series (*ký hiệu*, `InvSeries`). A full series such as `1C26TAA` reads:

| Position | Example | Meaning |
| --- | --- | --- |
| 1 | `1` | Invoice type: `1` VAT invoice (GTGT), `2` sales invoice, `5` e-ticket, `6` stock-out note. |
| 2 | `C` | `C` with code, `K` without code. |
| 3–4 | `26` | Year. MISA rolls it over from the invoice date (`1C25…` becomes `1C26…`). |
| 5 | `T` | `T` ordinary invoice, `M` invoice from a cash register (*máy tính tiền*). |
| 6–7 | `AA` | Chosen by the issuer. |

Replacement and adjustment requests refer to the original invoice's series in two parts: the
template number is its first character (`OrgInvTemplateNo` = `1`) and the series is the last six
characters (`OrgInvSeries` = `C26TAA`).

Listing templates returns only **active** invoice templates. When an invoice carries no template,
the SDK picks one: the only active template, or an error when there is none (`NoActiveTemplate`,
category `TemplateState`) or more than one (`AmbiguousTemplate`, category `Configuration`). A
template reference (by `IPTemplateID`, `InvSeries` or both) that matches no active template fails
with `UnknownTemplate` (`TemplateState`). MISA accepts invoices on a template only while it is in
use (*Sử dụng*) and its declaration (*tờ khai*) is accepted by the tax authority; otherwise it
answers with a `TemplateState` or `DeclarationState` error.

**E-ticket templates** (*mẫu vé*, `/webapp/templates/ticket`) are listed separately and one page at
a time (see [Paging](#paging)). Rows come back as MISA sends them, active and inactive, so filter on
`IsActive` / `isActive` yourself. The `FromCashRegister` / `fromCashRegister` flag is MISA's
`invoiceCalcu` (ticket templates from a cash register).

### RefId and drafts

`RefId` (MISA `RefID`) is your key for an invoice: you choose it, MISA stores it, and every later
call (fetch the PDF, delete, look up, amend via `OrgRefID`) uses it. It must be non-empty and at
most 64 characters; any format is accepted. Leave it empty and the SDK generates a GUID (the RefId
generator is swappable), returned in the save result.

Use a RefId derived from your own record (an order number, for example) and treat it as an
idempotency key. Every request, inserts included, is retried once after a timeout, a network error,
a 429 or a 5xx. If the first insert was in fact stored, MISA answers the retry with
`DuplicateInvoiceRefID` for that invoice. On that code, or any unclear failure, look the RefId up
before creating the invoice again.

The draft lifecycle:

1. **Preview** renders the PDF of an invoice without storing anything.
2. **Save draft** inserts up to 30 invoices per call (each with at most 200 lines). Each invoice
   gets its own result, in input order: one bad invoice does not fail the others.
3. **Get draft PDF** renders a saved draft by RefId.
4. **Delete draft** removes a draft that has not been signed or published. A signed or published
   invoice cannot be deleted (`NotDeletable`); a RefId MISA does not know gives `NotFound`.
5. Signing and publishing happen in the MISA web app. Lookups report the invoice's unified status
   (`Draft`, `Signed`, `Issued`, `Cancelled`, `Replaced` or `Adjusted`) next to MISA's raw
   `EInvoiceStatus` and `PublishStatus` values.
6. An issued invoice is corrected by a **replacement** (a complete new invoice with absolute totals)
   or an **adjustment** (lines and totals carry only the difference, which may be positive,
   negative or zero; the SDK does not enforce a direction). Both need the original's `OrgRefID`,
   `OrgInvNo`, `OrgInvTemplateNo`, `OrgInvSeries`, `OrgInvDate` and a change reason.

### Paging

MISA caps how much one call can carry, and the SDKs check the limits before sending:

| Operation | Limit | What the SDK does |
| --- | --- | --- |
| Save draft | 1–30 invoices per call, ≤ 200 lines per invoice | An empty batch throws `Validation`; more than 30 throws `BatchOrSize` (`InvoiceQuantityTooLarge`); more than 200 lines fails that invoice with `Validation`. |
| Look up by RefId | 50 RefIds per MISA call | Any number of RefIds is accepted: the SDK sends chunks of 50 one after the other and returns one outcome per RefId, in input order. |
| Paged lookup (standard / calculating) | `Start` ≥ 0, `Length` 1–100 | Out-of-range values, `FromDate` after `ToDate`, or a `PublishStatus` other than 0, 4, 6, 7 throw `Validation` before any request. |
| Ticket templates | `Start` ≥ 0, `Length` 1–100 (default 20) | Out-of-range values throw `Validation` before any request. |

The paged lookups take a date range and an optional `PublishStatus` filter: `0` not published, `4`
waiting for a code, `6` code issued, `7` code refused. `Sort` defaults to `InvDate`. Use
**standard** paging (`/webapp/paging`) for ordinary invoices (series position 5 = `T`) and
**calculating** paging (`/webapp/paging/calculating`) for cash-register invoices (`M`).

A paged result echoes `Start` and `Length` and carries `ReturnedCount`. MISA returns no total, so
you have reached the end when `ReturnedCount < Length`; otherwise request the next page at
`Start + Length`. Ticket-template pages come back as a plain list: a page shorter than `Length` is
the last one.

### Error categories

Every failure is classified into one category, with the same names in .NET
(`MeInvoiceErrorCategory`) and TypeScript. MISA's raw `errorCode` is kept next to it
(`RawErrorCode` / `rawErrorCode`); an unknown code is never dropped: it becomes `MisaUnknown` with
the raw code attached.

| Category | Meaning | Typical raw codes | What to do |
| --- | --- | --- | --- |
| `Configuration` | Your settings or MISA account are wrong. | `InvalidAppID`, `InActiveAppID`, `InvalidTaxCode`, `LicenseInfo_*`, `AmbiguousTemplate`; invalid options in TypeScript (.NET throws `OptionsValidationException` instead) | Fix the configuration; retrying does not help. |
| `Authentication` | Sign-in or the token was rejected. | `UnAuthorize`, `TokenExpiredCode`, `InvalidTokenCode` | Check user name, password, tax code and AppID. |
| `TemplateState` | The template is missing, not in use or not active. | `InvoiceTemplateNotExist`, `TemplateIsNotUsing`, `NoActiveTemplate`, `UnknownTemplate` | Fix the template in the MISA web app or pass another one. |
| `DeclarationState` | The tax declaration (*tờ khai*) does not allow this invoice. | `DeclarationNotExist`, `InvalidDeclaration`, `InvoiceTemplateNotValidInDeclaration` | Fix the declaration in the MISA web app. |
| `Validation` | The request or invoice is invalid (SDK pre-check or MISA). | `ValidationFailed`, `InvalidInvoiceData`, `InvalidXMLContainEmoji`, `InvoiceDetail_*`, `RequireError_*` | Fix the input; `failures` lists the fields. |
| `BatchOrSize` | The batch or payload is too large. | `InvoiceQuantityTooLarge`, `XMLTooLong` | Send smaller batches. |
| `DuplicateOrUniqueness` | MISA already has this RefId or number. | `DuplicateInvoiceRefID`, `InvoiceDuplicated`, `InvoiceNumberNotCotinuous` | Look the RefId up; for `InvoiceNumberNotCotinuous`, issue one request at a time per series. |
| `ResourceNotFound` | The invoice or RefId does not exist. | `RefIdNotFound`, `InvalidTransactionID` | Check the RefId and the with-code flag. |
| `NotDeletable` | The invoice exists but can no longer be deleted (it was signed or published). | `InvalidTransactionID` whose message says *đã phát hành* or *đã ký* | Cancel or amend it in MISA instead. |
| `Replacement` | MISA refuses the replacement or adjustment. | `InvoiceCannotReplace`, `InvoiceCannotAdjust`, `HasAdjustmentInvoice` | Check the original invoice's state. |
| `Signing` | A signing-related MISA error. | `InvalidSignature`, `CertRevocation`, `CallSignServiceFail` | Fix the signing setup in MISA. |
| `OtherWorkflow` | Another MISA business rule. | `InvalidInvNo`, `StockInTaxCode_NotInfo_*` | See MISA's message. |
| `MisaThrottled` | MISA answered 429 twice (one retry is automatic). | HTTP 429 | Back off and retry later. |
| `MisaUnavailable` | MISA answered 5xx twice, or with its catch-all `Exception`. | HTTP 5xx, `Exception` | Retry later. |
| `TransportFailed` | No response at all after one retry (timeout, DNS, TLS, connection reset). | `Timeout`, `ConnectionFailed` | Check the network; from abroad see [below](#calling-misa-from-outside-vietnam). |
| `MisaUnknown` | A response the SDK cannot classify. | unmapped codes, `EmptyResponse`, `NoEchoedResult`, `InvalidPdfContent`, `InvalidResponse` | Log the raw code and report it. |

Some operations report expected business results as values instead of exceptions (delete outcomes,
per-RefId lookup outcomes, per-invoice save and amendment results); they use the same category
names. Each language's Errors page lists them.

### Calling MISA from outside Vietnam

Measured from GitHub-hosted runners (2026-11-03), the network path to `testapi.meinvoice.vn` is a
**path-MTU blackhole**: TCP segments near 1500 bytes are dropped, and the ICMP message that would
make the sender shrink them never arrives. Any request larger than about 1.4 KB hangs (.NET: until
`HttpClient.Timeout`, 100 s by default; Node: until the connection ends in `ECONNRESET`). That
includes every authenticated call, because the bearer token alone is about 2 KB. The sign-in call
and error responses are small and still succeed, so the failure looks like a slow or flaky MISA.
curl behaves the same, so this is not specific to either SDK, and neither can work around it.

Hosts in Vietnam are not affected. Elsewhere:

- Lower the MTU of the network interface that reaches MISA, e.g. `ip link set dev eth0 mtu 1280` on
  Linux. This repository's sandbox CI (`.github/workflows/integration.yml`) does exactly that.
  Before the change, 14 .NET sandbox tests timed out; after it, all of them passed.
- Enabling TCP MTU probing (`sysctl -w net.ipv4.tcp_mtu_probing=1`) may also help, but it has not
  been tested against MISA.

Two further effects hit Node.js only, and the TypeScript SDK's built-in HTTP client handles both:
the larger post-quantum TLS ClientHello that Node ≥ 22 with OpenSSL 3.5 sends is reset (the client
pins classical key-exchange groups), and Node's 250 ms happy-eyeballs connect attempt is shorter
than the ~210–250 ms TCP handshake from abroad (the client allows 2 s). If you pass your own
`fetch`, see the TypeScript [Configuration](./typescript/configuration) page.

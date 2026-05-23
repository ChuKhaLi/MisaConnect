---
title: EInvoice
layout: default
nav_order: 2
has_children: true
permalink: /einvoice/
---

# MisaConnect.EInvoice

Community .NET SDK for the **MISA MeInvoice** HTTP API. Provides a port-and-adapter facade for token acquisition, template lookup, invoice preview / save / PDF / delete, lookup, and amendment operations. Targets .NET 8.

```
dotnet add package MisaConnect.EInvoice
```

## Guides

- [Getting started](./getting-started) — install, configure DI, and call your first MISA API.
- [Configuration](./configuration) — every `MisaEInvoiceOptions` setting explained.
- [Sandbox setup](./sandbox-setup) — provisioning a MISA sandbox tenant and wiring credentials for integration tests.

## Supported operations

| Use case | Purpose |
| --- | --- |
| `ListActiveTemplates` | Enumerate publishable invoice templates for the current tax payer. |
| `PreviewInvoice` | Generate a non-issued PDF preview for an in-memory invoice. |
| `SaveDraftInvoices` | Persist one or more drafts using a client-supplied `RefId`. |
| `GetDraftPdfByRefId` | Fetch a draft's PDF render by RefId. |
| `DeleteDraftInvoice` | Remove a draft (with opt-in raw-error surfacing for diagnostics). |
| `LookupByRefIds` | Resolve drafts / issued invoices back to MISA invoice metadata. |
| `LookupStandard`, `LookupCalculating` | Read issued or calculating-state invoices for downstream reconciliation. |
| `IssueReplacementInvoice` | Issue a replacement for an issued invoice. |
| `IssueAdjustmentInvoice` | Issue an adjustment invoice. |

All operations live behind `IMisaEInvoiceClient` (resolved via DI) and follow MISA's wire format verbatim.

## Sibling package

Need digital-signature operations? See [`MisaConnect.ESign`](../esign/).

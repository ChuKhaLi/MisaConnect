# Architecture

MisaConnect follows a strict layered (ports-and-adapters) architecture. Each layer depends only on layers below it. Both product families — `MisaConnect.EInvoice` and `MisaConnect.ESign` — apply the same pattern; the diagram below shows EInvoice as the reference, with the equivalent `MisaConnect.ESign.{Client,Infrastructure,Application,Domain}` stack mirroring each row one-for-one.

```
┌────────────────────────────────────────────────────────┐
│  Consumer code (your app / sample API / sample console)│
└────────────────────────────────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  MisaConnect.EInvoice.Client                           │
│  - Facade types, DI extension, factory                 │
│  - DTO mappers between consumer-facing Dtos and Domain │
└────────────────────────────────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  MisaConnect.EInvoice.Infrastructure                   │
│  - MISA HTTP client (typed HttpClient)                 │
│  - Auth (bearer-token handler), retry, logging         │
│  - Configuration binding, validators                   │
│  - In-memory token cache (default adapter)             │
└────────────────────────────────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  MisaConnect.EInvoice.Application                      │
│  - Use cases (one type per operation)                  │
│  - Port interfaces (the swappable seams)               │
│  - Errors, mapping, validation                         │
└────────────────────────────────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  MisaConnect.EInvoice.Domain                           │
│  - Entities, value objects, errors, enums              │
│  - Zero external dependencies                          │
└────────────────────────────────────────────────────────┘
```

## Layer rules

| Layer | Allowed dependencies |
| --- | --- |
| Domain | (none) |
| Application | Domain, `Microsoft.Extensions.Logging.Abstractions` |
| Infrastructure | Application, Domain, `Microsoft.Extensions.*` (Configuration/DI/Http/Options/Logging) |
| Client | Application, Domain, Infrastructure (for DI), `Microsoft.Extensions.*` |

Domain has zero dependencies. Application has zero infrastructure concerns. This is enforced by csproj `ProjectReference` graph plus an internal namespace audit in the test suite. The same rules apply identically to each product family — see [.specify/memory/constitution.md](../.specify/memory/constitution.md) Principles II and VII.

## Port-and-adapter seams

### EInvoice ports

Consumers swap any of these by registering their own implementation before `AddMisaConnectEInvoice`:

| Port | Default adapter | Why swap? |
| --- | --- | --- |
| `IMeInvoiceClient` | typed `MeInvoiceClient` + `MeInvoiceCallLogger` | Replace HTTP with a fake (testing) |
| `ITokenCache` | `InMemoryTokenCache` | Use Redis / distributed cache |
| `IRefIdGenerator` | GUID generator | Use a domain-meaningful RefId scheme |
| `ISystemClock` | `TimeProvider.System` | Test-time clocks |
| `ICorrelationIdAccessor` | (you must register) | ASP.NET vs console vs background worker |
| `ITemplateResolver` | `TemplateResolver` | Custom template selection |
| `IInvoiceValidator` | `InvoiceValidator` | Domain-specific pre-flight validation |
| `IDeleteOptionsAccessor` | `DeleteOptionsAccessor` | Override raw-error opt-in at runtime |

### ESign ports

Consumers swap any of these by registering their own implementation before `AddMisaConnectESign`:

| Port | Default adapter | Why swap? |
| --- | --- | --- |
| `IESignClient` | typed `ESignClient` + `ESignCallLogger` | Replace HTTP with a fake (testing) |
| `ITokenCache` | `InMemoryTokenCache` | Use Redis / distributed cache across instances |
| `ICertificateSelector` | first-`ACTIVE` selector | Pick by issuer DN, alias, expiry, etc. |
| `IOtpProvider` | (you must register for transparent 2FA) | Pull OTP from SMS gateway / TOTP / interactive prompt |
| `ISystemClock` | `TimeProvider.System` | Test-time clocks |
| `ICorrelationIdAccessor` | (you must register) | ASP.NET vs console vs background worker |
| `IWebhookDeliveryHook` | (you must register for webhook mode) | Fan out signed bytes to your queue / DB / SignalR |
| `ISigningSessionStore` | `InMemorySigningSessionStore` | Distributed store (Redis, SQL) for multi-instance webhook mode |

## Product families

Both `MisaConnect.EInvoice` (v1.x) and `MisaConnect.ESign` (v2.x) ship from this repo. Each family lives entirely under its own namespace tree and ships as its own NuGet package:

```
src/
  MisaConnect.EInvoice.Domain/         entities, value objects, errors
  MisaConnect.EInvoice.Application/    use cases + port interfaces
  MisaConnect.EInvoice.Infrastructure/ MISA HTTP client + DI
  MisaConnect.EInvoice.Client/         consumer-facing facade (NuGet: MisaConnect.EInvoice)
  MisaConnect.ESign.Domain/            entities, value objects, errors
  MisaConnect.ESign.Application/       use cases + port interfaces
  MisaConnect.ESign.Infrastructure/    MISA HTTP client + DI
  MisaConnect.ESign.Client/            consumer-facing facade (NuGet: MisaConnect.ESign)
```

The families version independently (EInvoice tags are `v1.x.y`; ESign tags are `v2.x.y`) and have no cross-references. A shared `MisaConnect.Common` layer will be extracted only when real duplication appears between products — not speculatively.

## Wire-format fidelity

MISA's published APIs use specific JSON envelope shapes, casing, and date formats. Every wire DTO matches the documented format verbatim, per family:

- EInvoice wire DTOs live in `MisaConnect.EInvoice.Infrastructure/MeInvoice/Wire/`; mapping in `MisaConnect.EInvoice.Infrastructure/MeInvoice/Mapping/`.
- ESign wire DTOs live in `MisaConnect.ESign.Infrastructure/ESign/Wire/`; mapping in `MisaConnect.ESign.Infrastructure/ESign/Mapping/`.

Cross-reference the [MISA reference docs](misa-api-reference/) for ground truth.

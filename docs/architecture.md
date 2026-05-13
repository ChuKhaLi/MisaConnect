# Architecture

MisaConnect follows a strict layered (ports-and-adapters) architecture. Each layer depends only on layers below it.

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

Domain has zero dependencies. Application has zero infrastructure concerns. This is enforced by csproj `ProjectReference` graph plus an internal namespace audit in the test suite.

## Port-and-adapter seams

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

## Future products (v2.0)

The v1.0 release ships only `MisaConnect.EInvoice`. The reserved slot for MISA eSign:

```
src/
  MisaConnect.ESign.Domain/         (planned for v2.0)
  MisaConnect.ESign.Application/    (planned for v2.0)
  MisaConnect.ESign.Infrastructure/ (planned for v2.0)
  MisaConnect.ESign.Client/         (planned for v2.0)
```

A shared `MisaConnect.Common` layer will be extracted only when real duplication appears between products — not speculatively.

## Wire-format fidelity

MISA's published API uses specific JSON envelope shapes, casing, and date formats. Every wire DTO in `Infrastructure/MeInvoice/Wire/` matches the documented format verbatim. Mapping between wire DTOs and Domain types happens in `Infrastructure/MeInvoice/Mapping/`. Cross-reference the [MISA reference docs](misa-api-reference/) for ground truth.

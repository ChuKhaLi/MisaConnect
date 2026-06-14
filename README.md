<p align="center">
  <img src="https://raw.githubusercontent.com/ChuKhaLi/MisaConnect/main/icon.png" alt="MisaConnect" width="128" height="128" />
</p>

<h1 align="center">MisaConnect</h1>

[![Docs](https://img.shields.io/badge/docs-chukhali.github.io%2FMisaConnect-blue)](https://chukhali.github.io/MisaConnect/)
[![MisaConnect.EInvoice](https://img.shields.io/nuget/v/MisaConnect.EInvoice.svg?label=MisaConnect.EInvoice)](https://www.nuget.org/packages/MisaConnect.EInvoice)
[![Downloads](https://img.shields.io/nuget/dt/MisaConnect.EInvoice.svg?label=downloads)](https://www.nuget.org/packages/MisaConnect.EInvoice)
[![MisaConnect.ESign](https://img.shields.io/nuget/v/MisaConnect.ESign.svg?label=MisaConnect.ESign)](https://www.nuget.org/packages/MisaConnect.ESign)
[![Downloads](https://img.shields.io/nuget/dt/MisaConnect.ESign.svg?label=downloads)](https://www.nuget.org/packages/MisaConnect.ESign)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Community .NET SDK for MISA cloud APIs. Two independent NuGet packages ship from this repo, each a port-and-adapter facade over a separate MISA product:

| Package | Covers | Docs |
| --- | --- | --- |
| **[`MisaConnect.EInvoice`](https://www.nuget.org/packages/MisaConnect.EInvoice)** | MISA MeInvoice — token acquisition, template lookup, invoice preview / save / PDF / delete, lookup, replacement & adjustment invoices. | [docs/einvoice/](docs/einvoice/) |
| **[`MisaConnect.ESign`](https://www.nuget.org/packages/MisaConnect.ESign)** | MISA eSign RemoteSigning — PDF / XML / Word / Excel signing, 2FA / OTP (explicit + transparent), webhook-mode (non-blocking) signing, optional per-call credentials via `IMisaCredentialsAccessor`. | [docs/esign/](docs/esign/) |

Both packages target **.NET 8** and share the same layered architecture, options-pattern configuration, swappable ports, and wire-format fidelity with MISA's published APIs.

> 📖 **Full docs:** <https://chukhali.github.io/MisaConnect/>

## Install

```
dotnet add package MisaConnect.EInvoice    # invoice operations
dotnet add package MisaConnect.ESign       # digital-signature operations
```

The two packages are entirely independent — install whichever you need (or both).

## Quickstart

See the per-package READMEs and getting-started guides for runnable examples:

- **EInvoice** — [package README](src/MisaConnect.EInvoice.Client/README.md) · [getting started](docs/einvoice/getting-started.md)
- **ESign** — [package README](src/MisaConnect.ESign.Client/README.md) · [getting started](docs/esign/getting-started.md)

## Project layout

```
src/
  MisaConnect.EInvoice.Domain/         entities, value objects, errors
  MisaConnect.EInvoice.Application/    use cases + port interfaces
  MisaConnect.EInvoice.Infrastructure/ MISA HTTP client + DI
  MisaConnect.EInvoice.Client/         consumer-facing facade (the NuGet package)
  MisaConnect.ESign.Domain/            entities, value objects, errors
  MisaConnect.ESign.Application/       use cases + port interfaces
  MisaConnect.ESign.Infrastructure/    MISA HTTP client + DI
  MisaConnect.ESign.Client/            consumer-facing facade (the NuGet package)
samples/
  MisaConnect.Samples.Console/         minimal DI wiring + ListTemplates demo (EInvoice)
  MisaConnect.Samples.Api/             ASP.NET Core minimal-API reference host (both families)
tests/
  MisaConnect.EInvoice.UnitTests/      EInvoice unit tests, no network
  MisaConnect.EInvoice.IntegrationTests/ EInvoice sandbox + fake-server tests
  MisaConnect.ESign.UnitTests/         ESign unit tests, no network
  MisaConnect.ESign.IntegrationTests/  ESign sandbox + EsignFake tests
docs/
  architecture.md                      layered design shared by both families
  einvoice/                            EInvoice guides (getting-started, configuration, sandbox)
  esign/                               ESign guides (getting-started, configuration, sandbox)
  misa-api-reference/                  copies of MISA public reference docs
```

See [docs/architecture.md](docs/architecture.md) for the layered (Domain → Application → Infrastructure → Client) design and [.specify/memory/constitution.md](.specify/memory/constitution.md) for the binding project principles.

## Documentation

- [Architecture](docs/architecture.md) — shared design across both families
- [MisaConnect.EInvoice guides](docs/einvoice/) — getting started, configuration, sandbox setup
- [MisaConnect.ESign guides](docs/esign/) — getting started, configuration, sandbox setup
- [MISA API reference](docs/misa-api-reference/) — copies of MISA's official CURL/schema docs
- [CHANGELOG](CHANGELOG.md) — per-release notes, both families

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Slice-driven development via [Spec Kit](https://github.com/github/spec-kit) under `specs/`.

## License

MIT — see [LICENSE](LICENSE).

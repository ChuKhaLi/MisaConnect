<p align="center">
  <img src="https://raw.githubusercontent.com/ChuKhaLi/MisaConnect/main/icon.png" alt="MisaConnect" width="128" height="128" />
</p>

<h1 align="center">MisaConnect</h1>

[![Docs](https://img.shields.io/badge/docs-chukhali.github.io%2FMisaConnect-blue)](https://chukhali.github.io/MisaConnect/)
[![MisaConnect.EInvoice](https://img.shields.io/nuget/v/MisaConnect.EInvoice.svg?label=MisaConnect.EInvoice)](https://www.nuget.org/packages/MisaConnect.EInvoice)
[![MisaConnect.ESign](https://img.shields.io/nuget/v/MisaConnect.ESign.svg?label=MisaConnect.ESign)](https://www.nuget.org/packages/MisaConnect.ESign)
[![@misaconnect/einvoice](https://img.shields.io/npm/v/@misaconnect/einvoice.svg?label=%40misaconnect%2Feinvoice)](https://www.npmjs.com/package/@misaconnect/einvoice)
[![@misaconnect/esign](https://img.shields.io/npm/v/@misaconnect/esign.svg?label=%40misaconnect%2Fesign)](https://www.npmjs.com/package/@misaconnect/esign)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Community SDKs for MISA cloud APIs, for **.NET** and **TypeScript**. Four independent packages, one per MISA product and language, with the same operations in both languages. Not affiliated with MISA JSC.

| Product | .NET (NuGet) | TypeScript (npm) | Docs |
| --- | --- | --- | --- |
| **EInvoice** — MISA MeInvoice: invoice and e-ticket templates, preview, drafts, lookups, replacement and adjustment invoices | [`MisaConnect.EInvoice`](https://www.nuget.org/packages/MisaConnect.EInvoice) | [`@misaconnect/einvoice`](https://www.npmjs.com/package/@misaconnect/einvoice) | [docs/einvoice](docs/einvoice/index.md) |
| **ESign** — MISA eSign RemoteSigning: PDF / XML / Word / Excel signing, two-factor sign-in, webhook mode, per-call credentials | [`MisaConnect.ESign`](https://www.nuget.org/packages/MisaConnect.ESign) | [`@misaconnect/esign`](https://www.npmjs.com/package/@misaconnect/esign) | [docs/esign](docs/esign/index.md) |

Full documentation: <https://chukhali.github.io/MisaConnect/> (sources in [`docs/`](docs/index.md)).

## Quickstart

.NET 8 / .NET 10 (`dotnet add package MisaConnect.EInvoice`):

<!-- snippet: partial -->
```csharp
services.AddEInvoiceClient(configuration);           // binds "Misa:EInvoice"
var client = provider.GetRequiredService<IMisaEInvoiceClient>();
var templates = await client.ListTemplatesAsync();
```

TypeScript, Node.js 22+ (`npm install @misaconnect/einvoice`):

<!-- snippet: partial -->
```ts
import { createMisaEInvoiceClient } from '@misaconnect/einvoice';

const client = createMisaEInvoiceClient({ environment: 'sandbox', baseUrl, taxCode, userName, password, appId });
const templates = await client.listTemplates();
```

Complete, runnable versions: [EInvoice for .NET](docs/einvoice/dotnet/getting-started.md) · [EInvoice for TypeScript](docs/einvoice/typescript/getting-started.md) · [ESign for .NET](docs/esign/dotnet/getting-started.md) · [ESign for TypeScript](docs/esign/typescript/getting-started.md).

## Repository layout

```
src/dotnet/        MisaConnect.{EInvoice,ESign}.{Domain,Application,Infrastructure,Client}  (Client = the NuGet package)
src/typescript/    einvoice/, esign/  (the npm packages)
samples/dotnet/    MisaConnect.Samples.Console (EInvoice), MisaConnect.Samples.Api (ASP.NET Core, both products)
samples/typescript express/, nestjs/
tests/dotnet/      unit + integration tests per product (integration = in-repo fake servers + sandbox)
tests/package-smoke/  consumer apps run against the packed NuGet packages
docs/              documentation site (GitHub Pages), architecture, MISA API reference copies
```

The .NET layering (Domain → Application → Infrastructure → Client) is described in [docs/architecture.md](docs/architecture.md); project principles in [docs/constitution.md](docs/constitution.md).

## Build and test

```bash
dotnet build MisaConnect.slnx
dotnet test --project tests/dotnet/MisaConnect.EInvoice.UnitTests     # also: EInvoice.IntegrationTests, ESign.UnitTests, ESign.IntegrationTests
dotnet format MisaConnect.slnx

pnpm install --frozen-lockfile     # TypeScript workspace (pnpm 12 via packageManager; corepack pnpm works)
pnpm test
pnpm run lint && pnpm run format:check && pnpm run typecheck
```

Sandbox tests skip cleanly without MISA credentials; see the sandbox-setup pages in the docs.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Each change starts from a design in `docs/superpowers/specs/` and a plan in `docs/superpowers/plans/`. Release notes: [CHANGELOG.md](CHANGELOG.md) (NuGet packages), [einvoice](src/typescript/einvoice/CHANGELOG.md) and [esign](src/typescript/esign/CHANGELOG.md) CHANGELOGs (npm packages).

## License

MIT — see [LICENSE](LICENSE).

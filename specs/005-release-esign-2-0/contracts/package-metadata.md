# Contract — `MisaConnect.ESign.2.0.0.nupkg` Contents and Metadata

Specifies what a fresh `dotnet pack` of `src/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj` at version `2.0.0` MUST produce. Verifiable per FR-018 by opening the `.nupkg` as a ZIP and inspecting its contents.

---

## Required entries

```text
MisaConnect.ESign.2.0.0.nupkg
├── _rels/                                        # NuGet bookkeeping (auto)
├── package/                                      # NuGet bookkeeping (auto)
├── lib/
│   └── net8.0/
│       ├── MisaConnect.ESign.Client.dll
│       ├── MisaConnect.ESign.Client.pdb
│       ├── MisaConnect.ESign.Application.dll
│       ├── MisaConnect.ESign.Application.pdb
│       ├── MisaConnect.ESign.Infrastructure.dll
│       ├── MisaConnect.ESign.Infrastructure.pdb
│       ├── MisaConnect.ESign.Domain.dll
│       └── MisaConnect.ESign.Domain.pdb
├── README.md
├── icon.png
├── MisaConnect.ESign.nuspec
└── [Content_Types].xml
```

A separate `MisaConnect.ESign.2.0.0.snupkg` symbol package MUST be produced alongside (default behavior under `<IncludeSymbols>true</IncludeSymbols>` if configured globally, or `--include-symbols` on the pack command).

## Required `.nuspec` field values

| Field | Required value | Source |
|---|---|---|
| `<id>` | `MisaConnect.ESign` | csproj `<PackageId>` |
| `<version>` | `2.0.0` | csproj `<Version>` or `-p:Version=` override |
| `<title>` | (inherited from `<id>` if not set; acceptable) | — |
| `<description>` | csproj line 8 verbatim | csproj `<Description>` |
| `<tags>` | `misa esign remotesigning vietnam pdf signature sdk` (space-separated) | csproj `<PackageTags>` |
| `<authors>` | from `Directory.Build.props` | inherited |
| `<requireLicenseAcceptance>` | `false` | default |
| `<license>` | `MIT` or whatever the project uses (verify against `Directory.Build.props`) | inherited |
| `<icon>` | `icon.png` | csproj `<None Include="..\..\icon.png" Pack="true" PackagePath="\">` |
| `<readme>` | `README.md` | csproj `<None Include="..\..\README.md" Pack="true" PackagePath="\">` |
| `<projectUrl>` | from `Directory.Build.props` | inherited |
| `<repository>` | `git` + repo URL | inherited |
| `<dependencies>` (under `<group targetFramework="net8.0">`) | `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options` — version ranges from `Directory.Packages.props` central management | csproj `<PackageReference>` entries (lines 21–23) |

## Properties that MUST NOT appear

- **No transitive references to `MisaConnect.ESign.Application`, `.Infrastructure`, or `.Domain`** as NuGet dependencies. The `PrivateAssets="all"` on each `<ProjectReference>` (csproj lines 15–17) ensures consumers get the bundled DLLs but not transitive package deps. **Verification**: open the `.nuspec` `<dependencies>` group and confirm it lists ONLY the three `Microsoft.Extensions.*` packages.
- **No assemblies for any other TFM** than `net8.0` — the csproj has a single `<TargetFramework>net8.0</TargetFramework>`. If `lib/netstandard2.0/` or similar appears, something is misconfigured.
- **No `MisaConnect.EInvoice.*` assemblies** in the bundle — the EInvoice and ESign families must remain independently shippable.

## Verification commands (FR-018)

```pwsh
# After dotnet pack, inspect the nupkg as a zip
$nupkg = "artifacts/local/MisaConnect.ESign.2.0.0.nupkg"
$tmp = New-TemporaryFile | %{ Remove-Item $_; mkdir $_ }
Expand-Archive -Path $nupkg -DestinationPath $tmp

# Required DLLs present?
@(
  "MisaConnect.ESign.Client.dll",
  "MisaConnect.ESign.Application.dll",
  "MisaConnect.ESign.Infrastructure.dll",
  "MisaConnect.ESign.Domain.dll"
) | ForEach-Object {
  $path = Join-Path $tmp "lib/net8.0/$_"
  if (-not (Test-Path $path)) { throw "Missing: $_" }
}

# README + icon at package root?
foreach ($f in @("README.md", "icon.png")) {
  if (-not (Test-Path (Join-Path $tmp $f))) { throw "Missing root: $f" }
}

# Nuspec has correct id + version?
$nuspec = Get-Content (Join-Path $tmp "MisaConnect.ESign.nuspec") -Raw
if ($nuspec -notmatch '<id>MisaConnect\.ESign</id>') { throw "Wrong id" }
if ($nuspec -notmatch '<version>2\.0\.0</version>') { throw "Wrong version" }

# No EInvoice contamination?
if ($nuspec -match 'MisaConnect\.EInvoice') { throw "EInvoice leak in nuspec" }
Get-ChildItem (Join-Path $tmp "lib") -Recurse -File | Where-Object Name -match 'MisaConnect\.EInvoice' | ForEach-Object { throw "EInvoice DLL leaked: $($_.Name)" }
```

The block above is reference-quality; the actual FR-018 implementation may use a `dotnet nuget verify` invocation or `nuget.exe` instead — equivalent intent.

## Notes on the bundling mechanism

The `BundleReferencedProjects` MSBuild target at csproj lines 36–45 walks `@(ReferenceCopyLocalPaths)` and emits each project-reference DLL (and its PDB, if present) as `<BuildOutputInPackage>`. This is the existing EInvoice precedent — no change for ESign. Do NOT replace this with `<PrivateAssets>` tweaks or `<IncludeAssets>`; both would change consumer transitive behavior and break the bundled-package contract.

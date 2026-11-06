#!/usr/bin/env bash
# Packs MisaConnect.ESign and MisaConnect.EInvoice, then builds and runs two net8.0 consumer apps
# (console, ASP.NET Core) against the packed packages from a local feed. Fails when a package does
# not declare a dependency that its bundled assemblies load at runtime.
#
# To smoke-test packages that are already packed (release.yml), set SMOKE_FEED to the folder
# holding the .nupkg files and SMOKE_VERSION to their version; packing is then skipped.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/misaconnect-smoke.XXXXXX")"
trap 'rm -rf "$work"' EXIT

export NUGET_PACKAGES="$work/packages" # isolated cache: never reuse a stale smoke package

if [ -n "${SMOKE_FEED:-}" ]; then
  : "${SMOKE_VERSION:?SMOKE_VERSION is required with SMOKE_FEED}"
  feed="$(cd "$SMOKE_FEED" && pwd)"
  version="$SMOKE_VERSION"
else
  version="0.0.0-smoke.$(date +%s)"
  feed="$work/feed"
  dotnet pack "$root/src/dotnet/MisaConnect.ESign.Client/MisaConnect.ESign.Client.csproj" -c Release -o "$feed" -p:Version="$version" --nologo -v q
  dotnet pack "$root/src/dotnet/MisaConnect.EInvoice.Client/MisaConnect.EInvoice.Client.csproj" -c Release -o "$feed" -p:Version="$version" --nologo -v q
fi

# nuget.config needs a native path (Git Bash on Windows uses /c/... paths).
feed_native="$feed"
if command -v cygpath >/dev/null 2>&1; then feed_native="$(cygpath -w "$feed")"; fi

cat > "$work/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="smoke" value="$feed_native" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

status=0
for app in Console Web; do
  proj="$root/tests/package-smoke/$app/PackageSmoke.$app.csproj"
  echo "== package smoke: $app (net8.0, MisaConnect $version)"
  dotnet restore "$proj" --configfile "$work/nuget.config" -p:SmokePackageVersion="$version" --nologo -v q
  if ! out="$(dotnet run --project "$proj" --no-restore -p:SmokePackageVersion="$version" 2>&1)"; then
    echo "$out" | head -20
    status=1
    continue
  fi
  echo "$out" | tail -1
  case "$out" in *"SMOKE OK"*) ;; *) status=1 ;; esac
done
exit $status

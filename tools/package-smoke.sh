#!/bin/bash
# Smoke-test packed E# packages from a local folder feed, away from any checkout:
#   install AmigaESharp.Tool into a tool path, `esharp new` a project (PackageReference to AmigaESharp.Sdk),
#   dotnet build and run it, check that the subset analyzer from the package reports ES0001, `esharp test` it on the
#   host, `esharp build --emit-only`, and with an image also an m68k build in Docker.
#
#   tools/package-smoke.sh <feed dir> <version> [m68k docker image]
#
# Used by .github/workflows/ci.yml and release.yml; works the same on a dev machine (nothing is installed globally:
# the tool, the project and the NuGet package cache all live in a temporary directory, removed afterwards unless
# ESHARP_SMOKE_KEEP=1).
set -euo pipefail
feed=$(cd "${1:?feed dir}" && pwd)
version=${2:?package version}
image=${3:-}

work=$(mktemp -d "${TMPDIR:-/tmp}/esharp-smoke.XXXXXX")
[ "${ESHARP_SMOKE_KEEP:-0}" = 1 ] || trap 'rm -rf "$work"' EXIT
echo "smoke test in $work"
# A fresh package cache, so a re-packed version with the same number is not served from an older cache entry.
export NUGET_PACKAGES="$work/packages" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
unset ESHARP_ROOT ESHARP_REPO
cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
cd "$work"
dotnet tool install AmigaESharp.Tool --tool-path "$work/tool" --version "$version" --add-source "$feed"
esharp="$work/tool/esharp"

test "$("$esharp" --version)" = "$version"
"$esharp" new demo
grep -q "<PackageReference Include=\"AmigaESharp.Sdk\" Version=\"$version\" />" demo/demo.csproj
cd demo
dotnet build -nologo
dotnet run | tee run.log
grep -qx 'Hello from E#!' run.log
"$esharp" test
"$esharp" build --emit-only
test -s obj/m68k/demo.c

# The analyzer comes from the package: a try statement must fail the host build with ES0001.
cd "$work"
"$esharp" new bad
sed -i.orig 's|Dos.PutStr("Hello from E#!\\n");|try { Dos.PutStr("x"); } catch { }|' bad/Program.cs
if dotnet build bad -nologo > bad.log 2>&1; then echo "expected ES0001 from the packaged analyzer"; exit 1; fi
grep -q 'error ES0001' bad.log

if [ -n "$image" ]; then
  cd "$work/demo"
  "$esharp" build --target m68k --docker "$image"
  test -s obj/m68k/demo
fi
echo "package smoke test passed ($version)"

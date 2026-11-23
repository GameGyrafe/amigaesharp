#!/bin/bash
# Smoke-test packed E# packages from a local folder feed, away from any checkout:
#   install AmigaESharp.Tool into a tool path, `esharp new` a project (PackageReference to AmigaESharp.Sdk),
#   dotnet build and run it, check that the subset analyzer from the package reports ES0001, `esharp test` it on the
#   host, `esharp build --emit-only`, and with an image also an m68k build in Docker. Then an E# library package (issue #31):
#   a class library with EsLibrary=true, an assets.json (a short sound) and a C file of its own (native/common/, feature NativeC)
#   packed against the packed AmigaESharp.Sdk into a second local feed, a new game that references it (esharp build, esharp test,
#   the target and GPL checks), and with an image its m68k build (the library's C compiled and linked with the game).
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
mkdir -p "$work/libfeed"
cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="libraries" value="$work/libfeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <!-- The packages under test come only from the local feeds, never from nuget.org. -->
  <packageSourceMapping>
    <packageSource key="local"><package pattern="AmigaESharp.*" /></packageSource>
    <packageSource key="libraries"><package pattern="ESharpSmoke.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
EOF
cd "$work"
# the feed comes from nuget.config (its source mapping takes AmigaESharp.* from the local feed only)
dotnet tool install AmigaESharp.Tool --tool-path "$work/tool" --version "$version"
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
# An E# library package (issue #31): sources and manifest embedded by the Sdk's buildTransitive step, found by esharp after
# dotnet restore. The library lists m68k and sdl only; 1.0.1 is the same library under a GPL licence.
cd "$work"
mkdir -p smokelib
cat > smokelib/smokelib.csproj <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <EsLibrary>true</EsLibrary>
    <EsTargets>m68k;sdl</EsTargets>
    <EsFeatures>Native;NativeC</EsFeatures>
    <PackageId>ESharpSmoke.Lib</PackageId>
    <Version>1.0.0</Version>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AmigaESharp.Sdk" Version="$version" />
  </ItemGroup>
</Project>
EOF
cat > smokelib/Twice.cs <<'EOF'
namespace SmokeLib;

public static class Twice
{
    public static T Pick<T>(bool first, T a, T b) where T : unmanaged => first ? a : b;
    public static int Of(int x) => Pick(true, x * 2, 0);
}
EOF
# C in the library (#31): native/common/triple.c, called through a [Native] extern (C# on the dotnet host). No EsNativePrefix,
# so the prefix comes from the package id: esharpsmoke_lib_.
mkdir -p smokelib/native/common
echo 'int32_t esharpsmoke_lib_triple(int32_t x) { return x * 3; }' > smokelib/native/common/triple.c
cat > smokelib/Triple.cs <<'EOF'
using ESharp;

namespace SmokeLib;

public static class Triple
{
#if HOST
    public static int Of(int x) => x * 3;
#else
    [Native("esharpsmoke_lib_triple")] public static extern int Of(int x);
#endif
}
EOF
# Assets in the library (#31): a short sound, converted by esharp assets into Assets.g.cs, which the package carries.
printf 'RIFF\x28\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00\x40\x1f\x00\x00\x40\x1f\x00\x00\x01\x00\x08\x00data\x04\x00\x00\x00\x80\xe4\x1c\x80' > smokelib/tick.wav
cat > smokelib/assets.json <<'EOF2'
{ "namespace": "SmokeLib", "class": "Sounds", "samples": [ { "name": "Tick", "file": "tick.wav" } ] }
EOF2
"$esharp" assets smokelib
grep -q 'TickLength = 4' smokelib/Assets.g.cs
dotnet pack smokelib -o libfeed -nologo
dotnet pack smokelib -o libfeed -nologo -p:Version=1.0.1 -p:PackageLicenseExpression=GPL-3.0-only
"$esharp" new uselib
sed -i.orig "s|<PackageReference Include=\"AmigaESharp.Sdk\" Version=\"$version\" />|&<PackageReference Include=\"ESharpSmoke.Lib\" Version=\"1.0.0\" />|" uselib/uselib.csproj
grep -q 'Include="ESharpSmoke.Lib"' uselib/uselib.csproj
sed -i.orig 's|sum == 55 ?|sum == 55 \&\& SmokeLib.Twice.Of(21) == 42 \&\& SmokeLib.Triple.Of(14) == 42 \&\& SmokeLib.Sounds.TickData[1] == 100 ?|' uselib/Program.cs
grep -q 'SmokeLib.Twice.Of' uselib/Program.cs
if "$esharp" build uselib --emit-only > nolib.log 2>&1; then echo "expected an error before dotnet restore"; exit 1; fi
grep -q 'dotnet restore' nolib.log
dotnet restore uselib
"$esharp" build uselib --emit-only | tee lib.log
grep -q 'Package ESharpSmoke.Lib 1.0.0 (MIT): 3 C# file(s)' lib.log
grep -q 'native C (prefix esharpsmoke_lib_; compiled into the program, runs with its privileges): native/common/triple.c' lib.log
grep -q '^extern int32_t esharpsmoke_lib_triple(int32_t);' uselib/obj/m68k/uselib.c
grep -q 'esharpsmoke_lib_triple' uselib/obj/m68k/esharp-native/ESharpSmoke.Lib/triple.c
grep -q 'assets: SmokeLib.Sounds (Assets.g.cs' lib.log
grep -q '<package ESharpSmoke.Lib 1.0.0>/Twice.cs' uselib/obj/m68k/uselib.c
grep -q '^static __chip uint8_t Es_SmokeLib_Sounds_TickData\[4\] = { 0, 100, 156, 0 };' uselib/obj/m68k/uselib.c
(cd uselib && "$esharp" test) | tee libtest.log
grep -q 'TEST PASSED' libtest.log
dotnet run --project uselib | grep -q 'ok sum of 1 to 10'
if "$esharp" build uselib --target os4 --emit-only > os4.log 2>&1; then echo "expected ES0110 for a target the library does not list"; exit 1; fi
grep -q 'ES0110' os4.log
sed -i.orig 's|Include="ESharpSmoke.Lib" Version="1.0.0"|Include="ESharpSmoke.Lib" Version="1.0.1"|' uselib/uselib.csproj
dotnet restore uselib
if "$esharp" build uselib --emit-only > gpl.log 2>&1; then echo "expected ES0111 for a GPL package"; exit 1; fi
grep -q 'ES0111' gpl.log
sed -i.orig 's|<OutputType>Exe</OutputType>|&<EsAllowCopyleft>true</EsAllowCopyleft>|' uselib/uselib.csproj
"$esharp" build uselib --emit-only 2>&1 | tee gplok.log
grep -q 'allowed by EsAllowCopyleft' gplok.log
if [ -n "$image" ]; then
  "$esharp" build uselib --target m68k --docker "$image" | tee libm68k.log
  grep -q 'compiled with the program' libm68k.log
  test -s uselib/obj/m68k/uselib
fi
echo "package smoke test passed ($version)"

#!/usr/bin/env bash
# Regenerates src/ESharp.Platform/Layout.g.cs: the struct offsets E# reads directly, measured with each
# target's own C compiler and headers (tools/BindGen/structs.txt lists them). Needs docker and the three images
# (esharp-aros-i386 is built locally from docker/aros-i386).
# Usage: tools/BindGen/layouts.sh [m68k image] [aros image] [aros-i386 image]
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"; cd "$root"
m68k="${1:-amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859}"
aros="${2:-amigadev/crosstools@sha256:9c4e978301da6b6584d68d4014caa1fa9e2689529e79e71f7190d40d1cb62e50}"
i386="${3:-esharp-aros-i386}"
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
# MUI structs ("zune" lines of structs.txt) are defined by the AROS Zune header, read from the AROS image for this run only.
docker run --rm "$aros" cat /opt/x86_64-aros/include/libraries/mui.h > "$tmp/zune-mui.h"
run() { dotnet run --project tools/BindGen -- offsets tools/BindGen/structs.txt "$1" --docker "$2" --zune "$tmp/zune-mui.h"; }
run aros "$aros" > "$tmp/aros.txt"; run aros-i386 "$i386" > "$tmp/aros-i386.txt"; run m68k "$m68k" > "$tmp/m68k.txt"
# bindgen layout writes the C# (and refuses if the targets measured different items).
dotnet run --project tools/BindGen -- layout "$tmp/aros.txt" "$tmp/aros-i386.txt" "$tmp/m68k.txt" src/ESharp.Platform/Layout.g.cs
echo "wrote src/ESharp.Platform/Layout.g.cs"

#!/usr/bin/env bash
# Builds the hardware test pack: every sample for m68k (68020) plus RunAll, a checklist and a
# results folder, in one directory you can copy to the Amiga (CF card, USB stick, network).
# Usage: tools/hwpack/build.sh <image> [outdir]     (default outdir: obj/hwtest)
# <image> is the m68k toolchain image, e.g. esharp-amiga-toolchain. Needs docker and dotnet.
set -euo pipefail
image="${1:?usage: tools/hwpack/build.sh <docker image> [outdir]}"
out="${2:-obj/hwtest}"
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
cd "$root"

# Order: quick pure checks first, then the ones that open a screen or the audio device.
samples="hwinfo hello phase2 memory generics properties operators language collections inherit lambdas linq refs screen aga rtg sound controls keys dates cards rummy"

rm -rf "$out"; mkdir -p "$out/results"
for s in $samples; do
  echo "== $s"
  dotnet run --project src/ESharp.Cli -- build "samples/$s" --target m68k --docker "$image" >/dev/null
  exe="$(find "samples/$s/obj/m68k" -maxdepth 1 -type f ! -name '*.c' ! -name run | head -1)"
  [ -n "$exe" ] || { echo "no executable for $s"; exit 1; }
  cp "$exe" "$out/$s"
done

cp "$here/RunAll" "$out/RunAll"
cp "$here/README.txt" "$out/README.txt"
echo "$samples" > "$out/samples.txt"
git rev-parse --short HEAD > "$out/BUILD" 2>/dev/null || echo unknown > "$out/BUILD"
echo "Hardware test pack in $out ($(du -sh "$out" | cut -f1)); $(echo $samples | wc -w) programs."
echo "Note: the RunAll script must have the Amiga 's' protection bit set (Protect RunAll +s) after copying"
echo "from a non-Amiga file system; the README says how."

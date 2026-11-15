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

# Order: quick pure checks first, then the ones that open a screen or the audio device. RunAll runs them in the order of
# its own lines; keep the two in step (tools/hwpack/RunAll lists every program but gui, which needs clicks: README.txt).
samples="hwinfo hello phase2 memory generics properties operators language collections inherit lambdas linq refs patterns objects mainargs names dates interrupts trace timing fonts iff controls keys asl screen aga assets rtg sound dbuffer textspeed cards rummy gui"

# The pack's own counting helper (AmigaDOS cannot count lines), built like a sample.
buildone() {   # <source dir> <name>
  local dir="$1" name="$2" extra=""
  [ "$name" = trace ] && extra="--trace"   # the trace sample needs TRACE to write to the serial port
  dotnet run --project src/ESharp.Cli -- build "$dir" --target m68k --docker "$image" $extra >/dev/null
  local exe
  exe="$(find "$dir/obj/m68k" -maxdepth 1 -type f ! -name '*.c' ! -name '*.o' ! -name run | head -1)"
  [ -n "$exe" ] || { echo "no executable for $name"; exit 1; }
  cp "$exe" "$out/$name"
}

rm -rf "$out"; mkdir -p "$out/results"
for s in $samples; do
  echo "== $s"
  buildone "samples/$s" "$s"
done
echo "== Summary (helper)"
buildone tools/hwpack/summary Summary

# Every program RunAll starts must be in the pack, and every program in the pack except hwinfo (run first by RunAll itself) and gui must be started by RunAll.
for s in $samples; do
  [ "$s" = gui ] || [ "$s" = hwinfo ] || grep -q "^Execute RunOne [^ ]* $s " "$here/RunAll" || { echo "RunAll does not run $s"; exit 1; }
done
grep -o '^Execute RunOne [^ ]* [^ ]*' "$here/RunAll" | awk '{print $4}' | sort -u | while read -r p; do
  [ -f "$out/$p" ] || { echo "RunAll runs $p, which is not in the pack"; exit 1; }
done

cp "$here/RunAll" "$here/RunOne" "$out/"
cp "$here/README.txt" "$out/README.txt"
echo "$samples Summary" > "$out/samples.txt"
git rev-parse --short HEAD > "$out/BUILD" 2>/dev/null || echo unknown > "$out/BUILD"
echo "Hardware test pack in $out ($(du -sh "$out" | cut -f1)); $(echo $samples | wc -w) programs plus Summary."
echo "Note: copying from a non-Amiga file system loses the Amiga protection bits; the README says how to set them"
echo "(Protect RunAll RunOne +s, and +e on the programs). Emulator run: tools/hwpack/run-emulated.sh."

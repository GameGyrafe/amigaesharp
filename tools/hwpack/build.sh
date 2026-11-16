#!/usr/bin/env bash
# Builds the hardware test pack: every sample for m68k (68020) plus RunAll, a checklist and a
# results folder, in one directory you can copy to the Amiga (CF card, USB stick, network).
# Also writes <outdir>.adf (obj/hwtest.adf): the same files as ONE 880 KB AmigaDOS floppy image (FFS, volume HWTEST, only our files,
# not bootable) for a real A1200 with a floppy drive only, a Gotek, the THEA500 Mini or FS-UAE. See README.txt.
# Usage: tools/hwpack/build.sh <image> [outdir] [--boot-wb <Workbench 3.x disk 2 .adf>] [--adf-only]     (default outdir: obj/hwtest)
# <image> is the m68k toolchain image, e.g. esharp-amiga-toolchain. Needs docker, dotnet and python3.
# --boot-wb also writes <outdir>-boot.adf: our files plus the few AmigaOS files taken from YOUR Workbench disk (the commands the
# scripts use and the libraries the programs open) and our own startup-sequence. For your own use only: it holds copyrighted
# AmigaOS files, lives only under obj/ (git-ignored) and must never be committed, attached or shared.
set -euo pipefail
image=""; out=""; bootwb=""; adfonly=0
while [ $# -gt 0 ]; do
  case "$1" in
    --adf-only) adfonly=1; shift;;   # keep the programs already in <outdir>, only (re)write the floppy image(s)
    --boot-wb) bootwb="${2:?--boot-wb needs a Workbench disk .adf}"; shift 2;;
    -*) echo "unknown option $1" >&2; exit 2;;
    *) if [ -z "$image" ]; then image="$1"; elif [ -z "$out" ]; then out="$1"; else echo "too many arguments" >&2; exit 2; fi; shift;;
  esac
done
[ -n "$image" ] || { echo "usage: tools/hwpack/build.sh <docker image> [outdir] [--boot-wb <Workbench disk .adf>]" >&2; exit 2; }
out="${out:-obj/hwtest}"
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
cd "$root"

# Order: quick pure checks first, then the ones that open a screen or the audio device. RunAll runs them in the order of
# its own lines; keep the two in step (tools/hwpack/RunAll lists every program but gui, which needs clicks: README.txt).
samples="hwinfo hello phase2 memory generics properties operators language collections inherit lambdas closures linq refs patterns objects mainargs names dates interrupts trace timing fonts iff controls keys asl boopsi screen aga assets rtg sound dbuffer textspeed cards rummy gui"

# The pack's own counting helper (AmigaDOS cannot count lines), built like a sample.
buildone() {   # <source dir> <name>
  local dir="$1" name="$2" extra=""
  [ "$name" = trace ] && extra="--trace"   # the trace sample needs TRACE to write to the serial port
  dotnet run --project src/ESharp.Cli -- build "$dir" --target m68k --docker "$image" $extra >/dev/null
  local exe
  exe="$(find "$dir/obj/m68k" -maxdepth 1 -type f ! -name '*.c' ! -name '*.o' ! -name '*.info' ! -name run | head -1)"
  [ -n "$exe" ] || { echo "no executable for $name"; exit 1; }
  cp "$exe" "$out/$name"
  # The Workbench icon `esharp build` wrote next to the program (issue #84): the pack's programs show and start by double-click.
  [ -f "$exe.info" ] && cp "$exe.info" "$out/$name.info"
  return 0
}

if [ $adfonly = 0 ]; then
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

fi
cp "$here/RunAll" "$here/RunOne" "$out/"
cp "$here/README.txt" "$out/README.txt"
echo "$samples Summary" > "$out/samples.txt"
git rev-parse --short HEAD > "$out/BUILD" 2>/dev/null || echo unknown > "$out/BUILD"
# The floppy image(s). mkadf.py is our own writer (python3 standard library only); it stops with "disk full" if the pack does not fit.
adf="${out%/}.adf"
python3 -I "$here/mkadf.py" create "$out" "$adf" --name HWTEST --protect 'RunAll=srwed' --protect 'RunOne=srwed' \
  || { echo "The pack does not fit on one 880 KB floppy. Drop the biggest programs (cards, rummy, ...) from 'samples' above and from tools/hwpack/RunAll." >&2; exit 1; }
if [ -n "$bootwb" ]; then
  bootadf="${out%/}-boot.adf"
  # The bootable image holds AmigaOS files: refuse to write it anywhere git does not ignore (obj/ is ignored).
  git check-ignore -q -- "$bootadf" || { echo "refusing to write $bootadf: it holds AmigaOS files and is not git-ignored (use an outdir under obj/)" >&2; exit 1; }
  # Only what the scripts and programs use that Kickstart 3.1 does not hold in ROM: the C: commands RunAll/RunOne and our startup call
  # (Execute, Date, Delete, Type, Assign, MakeDir, SetPatch) and the libraries the programs open from LIBS: (asl, diskfont, iffparse, lowlevel).
  from=(); for f in C/Execute C/Date C/Delete C/Type C/Assign C/MakeDir C/SetPatch Libs/asl.library Libs/diskfont.library Libs/iffparse.library Libs/lowlevel.library; do
    from+=(--from-adf "$bootwb:$f"); done
  # The bootable image has about 108 KB free without icons and the icons (37 files of 2 KB, 5 blocks each) need about 120 KB, so it
  # is written from a copy of the pack without the .info files; the one-disk pack above and the pack drawer keep them.
  noicons="$(mktemp -d)"; trap 'rm -rf "$noicons"' EXIT
  cp -r "$out"/. "$noicons"/; rm -f "$noicons"/*.info
  python3 -I "$here/mkadf.py" create "$noicons" "$bootadf" --name HWTEST --bootblock-from "$bootwb" --protect 'RunAll=srwed' --protect 'RunOne=srwed' \
    --protect 'S/Startup-Sequence=srwed' "${from[@]}" --file S/Startup-Sequence="$here/boot-startup" \
    || { echo "The pack plus the AmigaOS files does not fit on one floppy." >&2; exit 1; }
  echo "$bootadf holds AmigaOS files from your Workbench disk: for your own use only, never commit or share it."
fi
echo "Hardware test pack in $out ($(du -sh "$out" | cut -f1)); $(echo $samples | wc -w) programs plus Summary."
echo "Note: copying from a non-Amiga file system loses the Amiga protection bits; the README says how to set them"
echo "(Protect RunAll RunOne +s, and +e on the programs). Emulator run: tools/hwpack/run-emulated.sh."

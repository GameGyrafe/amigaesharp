#!/usr/bin/env bash
# Runs the whole hardware test pack (RunAll) in FS-UAE on an A1200 and prints SUMMARY.txt, the per-program results and the total time.
# Usage: tools/hwpack/run-emulated.sh <pack dir> --rom <kickstart> --wb <workbench disk.adf> [options]
#   <pack dir>      what tools/hwpack/build.sh built (default obj/hwtest)
#   --rom <file>    Kickstart 3.1 A1200 ROM (or ESHARP_KICKSTART); read in place, never copied
#   --wb <file>     an AmigaOS 3.1 Workbench disk (ADF; or ESHARP_WORKBENCH): RunAll needs the C: commands (Execute, Search, Type, Date,
#                   Delete) and a bare emulated hard disk has none. Mounted read-only as DF0: and read in place, never copied.
#   --profile P     stock (default): A1200 as FS-UAE models it, 2 MB chip RAM, no fast RAM, FS-UAE's most accurate setting (accuracy 1).
#                   fast8: the same plus 8 MB fast RAM (a typical accelerated A1200). These are the strictest settings FS-UAE offers
#                   for an A1200; it does not model the THEA1200's FPGA or a real display.
#   --fast <MB>     override the fast RAM size of the profile
#   --timeout <s>   give up after this many seconds (default 900)
#   --cmd <lines>   AmigaDOS lines to run in the pack drawer instead of "Execute RunAll" (for testing the scripts, e.g. "Execute RunAll RESUME")
#   --out <dir>     keep the run directory here (default: a new temporary directory, printed at the end)
#   Floppy modes (stock A1200, Kickstart 3.1, NO hard disk, chip RAM only; the pack is read back from the .adf afterwards with mkadf.py):
#   --floppy <hwtest.adf>        DF0 = your Workbench disk (--wb; a throwaway copy with one added S:User-Startup that runs the pack from DF1),
#                                DF1 = a copy of the pack disk. No pack dir is needed in the floppy modes (the .adf is the pack).
#   --boot-floppy <hwtest-boot.adf>  DF0 = the bootable pack disk alone (a copy whose startup-sequence has one more line, the done marker).
#   --floppy-speed N             floppy drive speed in percent (default 0 = turbo, to keep the run short; 100 = real speed, about 3x longer)
#   In both modes your own files are never written: copies in the run directory take the results.
# Without DISPLAY it runs under xvfb-run (needs xvfb and fs-uae installed). Sound goes to the dummy driver.
set -euo pipefail
[ -n "${DISPLAY:-}" ] || exec xvfb-run -a -s "-screen 0 800x600x24" "$0" "$@"   # no display: run under Xvfb
pack=""; rom="${ESHARP_KICKSTART:-}"; wb="${ESHARP_WORKBENCH:-}"; profile=stock; fast=""; timeout=900; out=""; cmd="Execute RunAll"; floppy=""; bootfloppy=""; floppy_speed=0
while [ $# -gt 0 ]; do
  case "$1" in
    --rom) rom="$2"; shift 2;;
    --wb) wb="$2"; shift 2;;
    --profile) profile="$2"; shift 2;;
    --fast) fast="$2"; shift 2;;
    --timeout) timeout="$2"; shift 2;;
    --out) out="$2"; shift 2;;
    --cmd) cmd="$2"; shift 2;;
    --floppy-speed) floppy_speed="$2"; shift 2;;
    --floppy) floppy="$2"; shift 2;;
    --boot-floppy) bootfloppy="$2"; shift 2;;
    -*) echo "unknown option $1" >&2; exit 2;;
    *) pack="$1"; shift;;
  esac
done
pack="${pack:-obj/hwtest}"
here="$(cd "$(dirname "$0")" && pwd)"
mkadf() { python3 -I "$here/mkadf.py" "$@"; }
if [ -n "$floppy$bootfloppy" ]; then
  [ -f "$rom" ] || { echo "need --rom <Kickstart 3.1 A1200 ROM> (or ESHARP_KICKSTART)" >&2; exit 2; }
  [ -z "$floppy" ] || [ -z "$bootfloppy" ] || { echo "--floppy and --boot-floppy are two different modes: pick one" >&2; exit 2; }
  [ -z "$floppy" ] || [ -f "$wb" ] || { echo "--floppy needs --wb <Workbench 3.1 disk .adf> (or ESHARP_WORKBENCH)" >&2; exit 2; }
  case "$profile" in stock) : "${fast:=0}";; fast8) : "${fast:=8}";; *) echo "unknown profile $profile (stock, fast8)" >&2; exit 2;; esac
  run="${out:-$(mktemp -d)}"; mkdir -p "$run"; run="$(cd "$run" && pwd)"
  cfg="[fs-uae]
amiga_model = A1200
chip_memory = 2048
fast_memory = $((fast * 1024))
accuracy = 1
kickstart_file = $rom
writable_floppy_images = 1
floppy_drive_speed = $floppy_speed
serial_port = $run/serial.txt
window_width = 640
window_height = 480
fullscreen = 0"
  if [ -n "$floppy" ]; then
    # DF0: a throwaway copy of the Workbench disk plus S:User-Startup (its startup-sequence runs it before LoadWB); DF1: the pack disk.
    printf 'Makedir RAM:Fonts\nAssign FONTS: RAM:Fonts\nCD DF1:\n%s\nEcho done >DF1:done\n' "$cmd" > "$run/User-Startup"
    mkadf create - "$run/df0.adf" --name-from "$wb" --bootblock-from "$wb" --from-adf "$wb:" --file S/User-Startup="$run/User-Startup" >/dev/null
    cp "$floppy" "$run/df1.adf"; data="$run/df1.adf"
    cfg="$cfg
floppy_drive_0 = $run/df0.adf
floppy_drive_1 = $run/df1.adf"
    desc="DF0 = a copy of the Workbench disk with S:User-Startup, DF1 = the pack disk"
  else
    # DF0: the bootable pack disk, its startup-sequence plus a done marker line.
    mkadf cat "$bootfloppy" S/Startup-Sequence > "$run/startup"
    printf '%s\nEcho done >SYS:done\n' "$(CMD="$cmd" awk '$0 == "Execute RunAll" { print ENVIRON["CMD"]; next } { print }' "$run/startup")" > "$run/startup2"
    mkadf create - "$run/df0.adf" --name-from "$bootfloppy" --bootblock-from "$bootfloppy" --from-adf "$bootfloppy:" \
      --file S/Startup-Sequence="$run/startup2" --protect 'S/Startup-Sequence=srwed' >/dev/null
    data="$run/df0.adf"
    cfg="$cfg
floppy_drive_0 = $run/df0.adf"
    desc="DF0 = the bootable pack disk alone"
  fi
  printf '%s\n' "$cfg" > "$run/run.fs-uae"
  : > "$run/serial.txt"
  export SDL_AUDIODRIVER=dummy
  start=$(date +%s)
  fs-uae --base-dir="$run/fsuae" "$run/run.fs-uae" >/dev/null 2>&1 &
  pid=$!
  until mkadf cat "$data" done >/dev/null 2>&1 || ! kill -0 $pid 2>/dev/null || [ $(( $(date +%s) - start )) -ge "$timeout" ]; do sleep 5; done
  finished=0; mkadf cat "$data" done >/dev/null 2>&1 && finished=1
  sleep 8   # let FS-UAE write the last sectors
  kill $pid 2>/dev/null || true; wait $pid 2>/dev/null || true
  elapsed=$(( $(date +%s) - start ))
  echo "floppy mode (drive speed $floppy_speed), profile $profile: A1200, 2 MB chip, $fast MB fast, no hard disk, accuracy 1; $desc; wall time ${elapsed}s; run directory $run"
  # Keep only our results: the Workbench copy (df0.adf) and, for the boot disk, the AmigaOS files are never left behind.
  rm -rf "$run/results-disk"
  if [ -n "$floppy" ]; then mkadf extract "$data" "$run/results-disk" || echo "(the disk image has problems: see above)"
  else mkadf extract "$data" "$run/results-disk" SUMMARY.txt results || echo "(the disk image has problems: see above)"; fi
  mkadf check "$data" | tail -1 || true
  rm -f "$run/df0.adf"   # a Workbench copy, or the boot disk with its AmigaOS files
  if [ $finished = 1 ]; then cat "$run/results-disk/SUMMARY.txt"; else echo "RunAll did not finish within ${timeout}s"; cat "$run/results-disk/results/PROGRESS.txt" 2>/dev/null || true; exit 1; fi
  exit 0
fi
[ -f "$pack/RunAll" ] || { echo "$pack is not a built pack (no RunAll): run tools/hwpack/build.sh first" >&2; exit 2; }
[ -f "$rom" ] || { echo "need --rom <Kickstart 3.1 A1200 ROM> (or ESHARP_KICKSTART)" >&2; exit 2; }
[ -f "$wb" ] || { echo "need --wb <Workbench 3.1 disk .adf> (or ESHARP_WORKBENCH)" >&2; exit 2; }
case "$profile" in
  stock) : "${fast:=0}";;
  fast8) : "${fast:=8}";;
  *) echo "unknown profile $profile (stock, fast8)" >&2; exit 2;;
esac

run="${out:-$(mktemp -d)}"; mkdir -p "$run"; run="$(cd "$run" && pwd)"
rm -rf "$run/dh0"; mkdir -p "$run/dh0/S" "$run/dh0/pack"
cp -r "$pack"/. "$run/dh0/pack/"
rm -rf "$run/dh0/pack/results"; mkdir "$run/dh0/pack/results"
# C: and LIBS: come from the Workbench disk, T:, ENV: and FONTS: from RAM: (diskfont and asl raise "insert volume" requesters otherwise).
# The hard disk then runs the pack and writes a marker.
cat > "$run/dh0/S/Startup-sequence" <<'SEQ'
DF0:C/Assign C: DF0:C
Makedir RAM:T
Assign T: RAM:T
Assign LIBS: DF0:Libs ADD
Makedir RAM:Fonts
Assign FONTS: RAM:Fonts
Makedir RAM:Env
Assign ENV: RAM:Env
CD DH0:pack
SEQ
printf '%s\n' "$cmd" >> "$run/dh0/S/Startup-sequence"
echo "Echo done >DH0:done" >> "$run/dh0/S/Startup-sequence"
cat > "$run/run.fs-uae" <<CFG
[fs-uae]
amiga_model = A1200
chip_memory = 2048
fast_memory = $((fast * 1024))
accuracy = 1
kickstart_file = $rom
floppy_drive_0 = $wb
floppy_drive_0_read_only = 1
hard_drive_0 = $run/dh0
hard_drive_0_label = DH0
hard_drive_0_priority = 20
serial_port = $run/serial.txt
window_width = 640
window_height = 480
fullscreen = 0
CFG
: > "$run/serial.txt"
export SDL_AUDIODRIVER=dummy
start=$(date +%s)
fs-uae --base-dir="$run/fsuae" "$run/run.fs-uae" >/dev/null 2>&1 &
pid=$!
while [ ! -f "$run/dh0/done" ] && kill -0 $pid 2>/dev/null && [ $(( $(date +%s) - start )) -lt "$timeout" ]; do sleep 2; done
elapsed=$(( $(date +%s) - start ))
finished=0; [ -f "$run/dh0/done" ] && finished=1
kill $pid 2>/dev/null || true; wait $pid 2>/dev/null || true
echo "profile $profile: A1200, 2 MB chip, $fast MB fast, accuracy 1; wall time ${elapsed}s; run directory $run"
if [ $finished = 1 ]; then cat "$run/dh0/pack/SUMMARY.txt"; else echo "RunAll did not finish within ${timeout}s"; cat "$run/dh0/pack/results/PROGRESS.txt" 2>/dev/null || true; exit 1; fi

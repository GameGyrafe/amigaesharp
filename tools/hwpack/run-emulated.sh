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
# Without DISPLAY it runs under xvfb-run (needs xvfb and fs-uae installed). Sound goes to the dummy driver.
set -euo pipefail
[ -n "${DISPLAY:-}" ] || exec xvfb-run -a -s "-screen 0 800x600x24" "$0" "$@"   # no display: run under Xvfb
pack=""; rom="${ESHARP_KICKSTART:-}"; wb="${ESHARP_WORKBENCH:-}"; profile=stock; fast=""; timeout=900; out=""; cmd="Execute RunAll"
while [ $# -gt 0 ]; do
  case "$1" in
    --rom) rom="$2"; shift 2;;
    --wb) wb="$2"; shift 2;;
    --profile) profile="$2"; shift 2;;
    --fast) fast="$2"; shift 2;;
    --timeout) timeout="$2"; shift 2;;
    --out) out="$2"; shift 2;;
    --cmd) cmd="$2"; shift 2;;
    -*) echo "unknown option $1" >&2; exit 2;;
    *) pack="$1"; shift;;
  esac
done
pack="${pack:-obj/hwtest}"
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

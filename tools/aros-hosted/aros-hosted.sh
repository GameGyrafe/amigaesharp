#!/bin/bash
# Run an E# sample (built with --target aros) on the hosted AROS x86-64 build, headless under Xvfb.
#
#   AROS_HOSTED=<dir of the extracted AROS-*-linux-x86_64-system archive> \
#   tools/aros-hosted/aros-hosted.sh <sample> [--shot out.png] [--wait N] [--log] [--input "click 400,300;key 9"]
#
# Or, with no local AROS tree, inside the esharp-aros-hosted image (docker/aros-hosted/README.md): add --docker <image>.
# Every run then copies the image's pristine tree, so nothing is patched in place. Same options.
#
# Prints the program's output. With --shot, waits for the program to print SHOT, then saves a
# screenshot of the X root. --input is a script for xinput.py (click x,y / key keycode) run after SHOT.
# Needs: xvfb, x11-apps (xwd), python3, libX11 + libXtst (for --input). Not part of the test suite.
# The archive is NOT in the repo: download it from the AROS nightlies and extract it somewhere.
set -u
here=$(cd "$(dirname "$0")" && pwd); root=$(cd "$here/../.." && pwd)
s=${1:?sample name}; shift
shot=""; wait=2; input=""; log=0; image=""
while [ $# -gt 0 ]; do case $1 in
  --shot) shot=$2; shift 2;; --wait) wait=$2; shift 2;; --input) input=$2; shift 2;; --log) log=1; shift;;
  --docker) image=$2; shift 2;; *) echo "bad arg $1"; exit 2;; esac; done
if [ -n "$image" ]; then
  # Re-run this script in the image: the repo is mounted read-only, screenshots go to a mounted folder.
  [ -f "$root/samples/$s/obj/aros/$s" ] || { echo "build it first: esharp build samples/$s --target aros --docker <image>"; exit 1; }
  args=("$s" --wait "$wait"); mounts=(-v "$root:/repo:ro")
  [ -n "$input" ] && args+=(--input "$input"); [ "$log" = 1 ] && args+=(--log)
  if [ -n "$shot" ]; then
    od=$(cd "$(dirname "$shot")" && pwd); mounts+=(-v "$od:/out"); args+=(--shot "/out/$(basename "$shot")")
  fi
  exec docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp "${mounts[@]}" "$image" /repo/tools/aros-hosted/aros-hosted.sh "${args[@]}"
fi
if [ -n "${AROS_HOSTED_COPY:-}" ]; then
  # inside the image: a fresh copy of the pristine tree for this run
  A=$(mktemp -d); cp -a "$AROS_HOSTED_COPY/." "$A/"; trap 'rm -rf "$A"' EXIT
else
  A=${AROS_HOSTED:?set AROS_HOSTED to the extracted hosted AROS directory (or pass --docker <image>)}
fi
bin="$root/samples/$s/obj/aros/$s"; [ -f "$bin" ] || { echo "build it first: esharp build samples/$s --target aros --docker <image>"; exit 1; }
cd "$A" || exit 1
cp "$bin" "./$s"; chmod +x "$s"
python3 -I - "$s" <<'P'
import sys,re
s=sys.argv[1];p='S/Startup-Sequence';t=open(p).read()
t=re.sub(r'\nSYS:\S+ >SYS:\S+\nEcho >SYS:done.txt done\n',"\n",t)
t=t.replace("FailAt 21\n","FailAt 21\n\nSYS:%s >SYS:%s.out\nEcho >SYS:done.txt done\n"%(s,s),1)
open(p,'w').write(t)
P
rm -f ./*.out done.txt boot.log
# Sound: on a headless host the ALSA mode makes audio.device hang (its driver finds no usable card). AHI's "void" mode takes the
# samples and paces them in real time without a card, so make that the only audio mode (the archive is a scratch copy).
if [ -f Storage/AudioModes/VOID ] && [ ! -f Devs/AudioModes/VOID ]; then
  mkdir -p Devs/AudioModes.orig && mv Devs/AudioModes/* Devs/AudioModes.orig/ 2>/dev/null
  cp Storage/AudioModes/VOID Devs/AudioModes/VOID
fi
tmp=$(mktemp -d)
D=$(( 200 + RANDOM % 500 ))
Xvfb :$D -screen 0 1024x768x24 >/dev/null 2>&1 & XP=$!; sleep 2
DISPLAY=:$D timeout 90 ./boot/linux/AROSBootstrap > boot.log 2>&1 & BP=$!
if [ -n "$shot" ] || [ -n "$input" ]; then
  for i in $(seq 80); do grep -q SHOT "$s.out" 2>/dev/null && break; sleep 1; done
  sleep "$wait"
  [ -n "$input" ] && python3 -I "$here/xinput.py" ":$D" "$input"
  if [ -n "$shot" ]; then DISPLAY=:$D xwd -root -silent > "$tmp/s.xwd"; python3 -I "$here/xwd2png.py" "$tmp/s.xwd" "$shot"; fi
fi
for i in $(seq 80); do [ -f done.txt ] && break; sleep 1; done; sleep 1
kill $BP $XP 2>/dev/null; wait 2>/dev/null; rm -rf "$tmp"
echo "traps: $(grep -c Trap boot.log) done: $([ -f done.txt ] && echo y || echo n)"; cat "$s.out" 2>/dev/null
if [ "$log" = 1 ]; then echo "--- AROS debug output (boot.log: RawPutChar, i.e. Trace) ---"; cat boot.log; fi

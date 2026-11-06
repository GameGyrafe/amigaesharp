#!/bin/bash
# Run an E# sample (built with --target aros) on the hosted AROS x86-64 build, headless under Xvfb.
#
#   AROS_HOSTED=<dir of the extracted AROS-*-linux-x86_64-system archive> \
#   tools/aros-hosted/aros-hosted.sh <sample> [--shot out.png] [--wait N] [--input "click 400,300;key 9"]
#
# Prints the program's output. With --shot, waits for the program to print SHOT, then saves a
# screenshot of the X root. --input is a script for xinput.py (click x,y / key keycode) run after SHOT.
# Needs: xvfb, x11-apps (xwd), python3, libX11 + libXtst (for --input). Not part of the test suite.
# The archive is NOT in the repo: download it from the AROS nightlies and extract it somewhere.
set -u
here=$(cd "$(dirname "$0")" && pwd); root=$(cd "$here/../.." && pwd)
A=${AROS_HOSTED:?set AROS_HOSTED to the extracted hosted AROS directory}
s=${1:?sample name}; shift
shot=""; wait=2; input=""
while [ $# -gt 0 ]; do case $1 in
  --shot) shot=$2; shift 2;; --wait) wait=$2; shift 2;; --input) input=$2; shift 2;; *) echo "bad arg $1"; exit 2;; esac; done
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
# no sound card on a headless host makes audio.device hang in OpenDevice; a null ALSA device avoids it
tmp=$(mktemp -d); echo 'pcm.!default { type null }
ctl.!default { type null }' > "$tmp/asound.conf"; export ALSA_CONFIG_PATH="$tmp/asound.conf"
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

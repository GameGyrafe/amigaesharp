#!/bin/bash
# Run an E# sample (built with --target aros or --target aros-i386) on a hosted AROS build, headless under Xvfb.
#
#   AROS_HOSTED=<dir of the extracted AROS-*-linux-x86_64-system archive> \
#   tools/aros-hosted/aros-hosted.sh <sample> [--arch x86_64|i386] [--shot out.png] [--wait N] [--log] [--input "click 400,300;key 9"]
#                                    [--args "WAIT"] [--files <dir>] [--wanderer]
#
# Or, with no local AROS tree, inside the esharp-aros-hosted image (docker/aros-hosted/README.md): add --docker <image>.
# Every run then copies the image's pristine tree, so nothing is patched in place. Same options.
#
# --arch picks the build and the program: x86_64 (default) runs samples/<sample>/obj/aros/<sample>, i386 runs
# obj/aros-i386/<sample> (hosted linux-i386 AROS, a 32-bit Linux program). With --docker and no --arch, an image whose name
# contains i386 (esharp-aros-hosted-i386) means i386. Inside an image, its AROS_HOSTED_ARCH must match.
#
# Prints the program's output. With --shot, waits for the program to print SHOT, then saves a
# screenshot of the X root. --input is a script for xinput.py (click x,y / dbl x,y / sleep s / key keycode) run after SHOT.
# --files <dir>: the files of <dir> are copied into a drawer SYS:ESTest (with a drawer icon, so Wanderer shows it).
# --wanderer: the program is not started from S:Startup-Sequence; it and its .info (obj/<target>/<sample>.info) go into SYS:ESTest,
# AROS boots on to Wanderer (its Workbench), and --input / --shot run after --wait seconds (default 25 then) instead of after SHOT.
# The run ends after the shot; the program's own output then is whatever it shows on screen.
# Needs: xvfb, x11-apps (xwd), python3, libX11 + libXtst (for --input). Not part of the test suite.
# The archive is NOT in the repo: download it from the AROS nightlies and extract it somewhere.
set -u
here=$(cd "$(dirname "$0")" && pwd); root=$(cd "$here/../.." && pwd)
s=${1:?sample name}; shift
shot=""; wait=""; input=""; log=0; image=""; pargs=""; files=""; wanderer=0; arch=""
while [ $# -gt 0 ]; do case $1 in
  --shot) shot=$2; shift 2;; --wait) wait=$2; shift 2;; --input) input=$2; shift 2;; --log) log=1; shift;;
  --docker) image=$2; shift 2;; --args) pargs=$2; shift 2;; --files) files=$2; shift 2;; --wanderer) wanderer=1; shift;;
  --arch) arch=$2; shift 2;; *) echo "bad arg $1"; exit 2;; esac; done
if [ -z "$arch" ]; then
  if [ -n "$image" ]; then case $image in *i386*) arch=i386;; *) arch=x86_64;; esac
  else arch=${AROS_HOSTED_ARCH:-x86_64}; fi
fi
case $arch in x86_64) target=aros;; i386) target=aros-i386;; *) echo "--arch is x86_64 or i386, not $arch"; exit 2;; esac
if [ -z "$image" ] && [ -n "${AROS_HOSTED_ARCH:-}" ] && [ "$AROS_HOSTED_ARCH" != "$arch" ]; then
  echo "this image runs hosted AROS $AROS_HOSTED_ARCH, not $arch (use the esharp-aros-hosted image built with AROS_ARCH=$arch)"; exit 2
fi
[ -z "$wait" ] && { [ "$wanderer" = 1 ] && wait=25 || wait=2; }
[ -n "$files" ] && { [ -d "$files" ] || { echo "--files: $files is not a directory"; exit 2; }; }
if [ -n "$image" ]; then
  # Re-run this script in the image: the repo is mounted read-only, screenshots go to a mounted folder.
  [ -f "$root/samples/$s/obj/$target/$s" ] || { echo "build it first: esharp build samples/$s --target $target --docker <image>"; exit 1; }
  args=("$s" --arch "$arch" --wait "$wait"); mounts=(-v "$root:/repo:ro")
  [ -n "$input" ] && args+=(--input "$input"); [ "$log" = 1 ] && args+=(--log); [ -n "$pargs" ] && args+=(--args "$pargs")
  [ "$wanderer" = 1 ] && args+=(--wanderer)
  [ -n "$files" ] && { mounts+=(-v "$(cd "$files" && pwd):/files:ro"); args+=(--files /files); }
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
bin="$root/samples/$s/obj/$target/$s"; [ -f "$bin" ] || { echo "build it first: esharp build samples/$s --target $target --docker <image>"; exit 1; }
cd "$A" || exit 1
cp "$bin" "./$s"; chmod +x "$s"
# E# Amiga libraries the program uses (esharp build puts them next to it, issue #154): into LIBS: of the fresh copy.
for l in "$(dirname "$bin")"/*.library; do
  [ -f "$l" ] || continue
  if [ -n "${AROS_HOSTED_COPY:-}" ]; then cp "$l" Libs/; else echo "note: $l is not copied into your AROS tree; put it in its Libs drawer yourself"; fi
done
if [ -n "$files" ] || [ "$wanderer" = 1 ]; then
  rm -rf ESTest; mkdir ESTest
  [ -n "$files" ] && cp -a "$files/." ESTest/
  # A drawer icon from the tree itself (this copy only), so Wanderer shows the drawer in the SYS: window.
  [ -f ESTest.info ] || cp Tools.info ESTest.info
  if [ "$wanderer" = 1 ]; then
    cp "$bin" "ESTest/$s"; chmod +x "ESTest/$s"
    [ -f "$bin.info" ] && cp "$bin.info" "ESTest/$s.info"
  fi
fi
python3 -I - "$s" "$pargs" "$wanderer" <<'P'
import sys,re
s=sys.argv[1];a=sys.argv[2];w=sys.argv[3]=="1";p='S/Startup-Sequence';t=open(p).read()
t=re.sub(r'\nSYS:\S+ >SYS:\S+[^\n]*\nEcho >SYS:done.txt done\n',"\n",t)
if not w: t=t.replace("FailAt 21\n","FailAt 21\n\nSYS:%s >SYS:%s.out %s\nEcho >SYS:done.txt done\n"%(s,s,a),1)
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
DISPLAY=:$D timeout $([ "$wanderer" = 1 ] && echo 300 || echo 90) ./boot/linux/AROSBootstrap > boot.log 2>&1 & BP=$!
if [ "$wanderer" = 1 ]; then
  sleep "$wait"
  [ -n "$input" ] && python3 -I "$here/xinput.py" ":$D" "$input"
  if [ -n "$shot" ]; then DISPLAY=:$D xwd -root -silent > "$tmp/s.xwd"; python3 -I "$here/xwd2png.py" "$tmp/s.xwd" "$shot"; fi
  kill $BP $XP 2>/dev/null; wait 2>/dev/null; rm -rf "$tmp"
  echo "traps: $(grep -c Trap boot.log) (Wanderer run: no program output file)"
  [ "$log" = 1 ] && { echo "--- AROS debug output (boot.log) ---"; cat boot.log; }
  exit 0
fi
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

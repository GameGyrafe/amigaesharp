#!/usr/bin/env bash
# Smoke test for the esharp-aros-i386 toolchain image: builds samples/hello with --target aros-i386 and checks that the
# result is a 32-bit i386 ELF; then, if the hosted i386 image is there, runs it on hosted AROS i386.
# Usage: docker/aros-i386/smoke-test.sh [toolchain image] [hosted image]   (defaults esharp-aros-i386, esharp-aros-hosted-i386)
set -euo pipefail
IMAGE="${1:-esharp-aros-i386}"; HOSTED="${2:-esharp-aros-hosted-i386}"
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
docker run --rm "$IMAGE" i386-aros-gcc --version | head -1
dotnet run --project src/ESharp.Cli -- build samples/hello --target aros-i386 --docker "$IMAGE"
kind=$(docker run --rm -v "$root/samples/hello/obj/aros-i386:/work:ro" -w /work "$IMAGE" file -b hello)
echo "hello: $kind"
echo "$kind" | grep -q 'ELF 32-bit LSB.*Intel 80386' || { echo "FAIL: not an i386 ELF"; exit 1; }
if docker image inspect "$HOSTED" > /dev/null 2>&1; then
  out=$(tools/aros-hosted/aros-hosted.sh hello --arch i386 --docker "$HOSTED")
  echo "$out"
  echo "$out" | grep -q '^traps: 0 done: y' || { echo "FAIL: AROS did not finish cleanly"; exit 1; }
  echo "$out" | grep -q 'Hello from E#!' || { echo "FAIL: hello output missing"; exit 1; }
else
  echo "($HOSTED not built: the hosted run is skipped; docker/aros-hosted/README.md)"
fi
echo "ok smoke test passed"

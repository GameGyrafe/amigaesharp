#!/usr/bin/env bash
# Smoke test for the esharp-aros-hosted image: builds samples/hello for the aros target (toolchain image) and runs it
# on hosted AROS in the image. Usage: docker/aros-hosted/smoke-test.sh [aros-hosted image] [toolchain image]
#   (defaults esharp-aros-hosted, esharp-amiga-toolchain). Run from anywhere; needs dotnet and docker.
set -euo pipefail
IMAGE="${1:-esharp-aros-hosted}"; TOOLCHAIN="${2:-esharp-amiga-toolchain}"
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
dotnet run --project src/ESharp.Cli -- build samples/hello --target aros --docker "$TOOLCHAIN"
out=$(tools/aros-hosted/aros-hosted.sh hello --docker "$IMAGE")
echo "$out"
echo "$out" | grep -q '^traps: 0 done: y' || { echo "FAIL: AROS did not finish cleanly"; exit 1; }
echo "$out" | grep -q 'Hello from E#!' || { echo "FAIL: hello output missing"; exit 1; }
# the pristine tree in the image must be untouched by the run
docker run --rm --entrypoint sh "$IMAGE" -c '! grep -q "SYS:hello" /opt/aros/S/Startup-Sequence' \
  || { echo "FAIL: image Startup-Sequence was patched"; exit 1; }
echo "ok smoke test passed"

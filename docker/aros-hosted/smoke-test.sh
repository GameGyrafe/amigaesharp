#!/usr/bin/env bash
# Smoke test for an esharp-aros-hosted image: builds samples/hello for the matching target (toolchain image) and runs it
# on hosted AROS in the image. Usage: docker/aros-hosted/smoke-test.sh [aros-hosted image] [toolchain image]
#   x86_64 (defaults esharp-aros-hosted, esharp-amiga-toolchain; target aros) or, when the image name contains i386,
#   i386 (e.g. esharp-aros-hosted-i386 with esharp-aros-i386; target aros-i386). Run from anywhere; needs dotnet and docker.
set -euo pipefail
IMAGE="${1:-esharp-aros-hosted}"
case $IMAGE in *i386*) arch=i386 target=aros-i386 tc=esharp-aros-i386;; *) arch=x86_64 target=aros tc=esharp-amiga-toolchain;; esac
TOOLCHAIN="${2:-$tc}"
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
# Images built before AROS_HOSTED_ARCH existed are x86_64.
docker run --rm --entrypoint sh "$IMAGE" -c "test \"\${AROS_HOSTED_ARCH:-x86_64}\" = $arch" \
  || { echo "FAIL: $IMAGE is not a hosted AROS $arch image (AROS_HOSTED_ARCH)"; exit 1; }
dotnet run --project src/ESharp.Cli -- build samples/hello --target "$target" --docker "$TOOLCHAIN"
out=$(tools/aros-hosted/aros-hosted.sh hello --arch "$arch" --docker "$IMAGE")
echo "$out"
echo "$out" | grep -q '^traps: 0 done: y' || { echo "FAIL: AROS did not finish cleanly"; exit 1; }
echo "$out" | grep -q 'Hello from E#!' || { echo "FAIL: hello output missing"; exit 1; }
# the pristine tree in the image must be untouched by the run
docker run --rm --entrypoint sh "$IMAGE" -c '! grep -q "SYS:hello" /opt/aros/S/Startup-Sequence' \
  || { echo "FAIL: image Startup-Sequence was patched"; exit 1; }
echo "ok smoke test passed ($arch)"

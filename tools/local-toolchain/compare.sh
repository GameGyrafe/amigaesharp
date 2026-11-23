#!/usr/bin/env bash
# Local m68k toolchain against Docker (issue #122): unpacks the toolchain of the pinned m68k image into a folder (docker create
# and docker cp, so the versions match by construction), builds samples with --toolchain <folder> instead of --docker, builds
# the same samples with --docker <image>, and compares the outputs byte for byte.
#
#   tools/local-toolchain/compare.sh [work dir] [sample...]      (default: a temporary folder; hello and cards; a sample in a
#   sub-folder such as packages/TweenDemo needs its packages restored first, samples/packages/README.md)
#
# The executable, its .info icon and the generated C must be identical. The <name>.o that every m68k build also writes (with
# -g, for esharp addr) is not compared: its debug info holds the build folder (/work in the container), so it differs.
# ESHARP sets the esharp command (default: dotnet run --project src/ESharp.Cli -c Release --), IMAGE the image.
set -euo pipefail
cd "$(dirname "$0")/../.."
image="${IMAGE:-amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859}"
work="${1:-$(mktemp -d)}"
shift || true
samples=("$@")
[ ${#samples[@]} -gt 0 ] || samples=(hello cards)
read -r -a esharp <<< "${ESHARP:-dotnet run --project src/ESharp.Cli -c Release --}"
mkdir -p "$work"
work="$(cd "$work" && pwd)"
tc="$work/m68k-amigaos"

if [ ! -x "$tc/bin/m68k-amigaos-gcc" ]; then
  docker image inspect "$image" >/dev/null 2>&1 || docker pull -q "$image"
  c=$(docker create "$image")
  trap 'docker rm "$c" >/dev/null' EXIT
  docker cp -q "$c:/opt/m68k-amigaos" "$tc"
fi
echo "toolchain: $tc ($("$tc/bin/m68k-amigaos-gcc" --version | head -1))"

status=0
for s in "${samples[@]}"; do
  "${esharp[@]}" build "samples/$s" --toolchain "$tc" -o "$work/local/$s"
  "${esharp[@]}" build "samples/$s" --docker "$image" -o "$work/docker/$s"
  b=$(basename "$s")   # a sample in a sub-folder (packages/TweenDemo) is named by its last part
  for f in "$b" "$b.info" "$b.c"; do
    if [ ! -e "$work/docker/$s/$f" ] && [ ! -e "$work/local/$s/$f" ]; then continue; fi
    if cmp -s "$work/local/$s/$f" "$work/docker/$s/$f"; then
      echo "same   $s/$f ($(wc -c < "$work/local/$s/$f") bytes, sha256 $(sha256sum "$work/local/$s/$f" | cut -c1-16))"
    else
      echo "DIFFER $s/$f"; status=1
    fi
  done
done
[ $status = 0 ] && echo "local toolchain and Docker builds are identical" || echo "local toolchain and Docker builds differ"
exit $status

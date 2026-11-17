#!/usr/bin/env bash
# Downloads the official MUI archive for esharp run/test --mui, checks its sha256 and unpacks it into a per-user cache.
# MUI is third-party shareware: its licence allows only verbatim copies of the whole archive, so it is never committed or
# shipped. See tools/mui/README.md.
#
#   tools/mui/fetch-mui.sh 3.8|5 [image]      image defaults to esharp-amiga-toolchain (its lha unpacks the archive)
#
# Cache: ${ESHARP_MUI_CACHE:-${XDG_CACHE_HOME:-~/.cache}/esharp/mui}/<version>/ (archive, and files/MUI = the unpacked drawer).
set -euo pipefail

version=${1:-}
image=${2:-esharp-amiga-toolchain}
case "$version" in
  3.8) tag=3.8;                 asset=mui38usr.lha;                 sha=f3acb2fe98f74c025daf10ae7df92d08abe787332124963771e2c21a869cca5b ;;
  5|5.0) version=5; tag=MUI-5.0-20210831; asset=MUI-5.0-20210831-os3.lha; sha=8a869a037db465828dbe42fa7854114121280ed2691afa61fe53a25bf427a847 ;;
  *) echo "usage: $0 3.8|5 [docker image]" >&2; exit 2 ;;
esac
url="https://github.com/amiga-mui/muidev/releases/download/$tag/$asset"

root=${ESHARP_MUI_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/esharp/mui}
dest="$root/$version"
if [ -f "$dest/files/MUI/Libs/muimaster.library" ]; then echo "MUI $version is already in $dest"; exit 0; fi

mkdir -p "$root"
work=$(mktemp -d "$root/.fetch.XXXXXX")
trap 'rm -rf "$work"' EXIT
echo "Downloading $url"
curl -fsSL -o "$work/$asset" "$url"
got=$(sha256sum "$work/$asset" | cut -d' ' -f1)
if [ "$got" != "$sha" ]; then
  echo "sha256 mismatch for $asset: expected $sha, got $got" >&2
  exit 1
fi

# The download is untrusted data: unpack it in its own empty directory, inside a container with no network. Nothing from it runs on the host.
mkdir "$work/files"
docker run --rm --network none -u "$(id -u)" -v "$work:/w" -w /w/files --entrypoint /opt/m68k-amigaos/bin/lha "$image" xq "../$asset"
[ -f "$work/files/MUI/Libs/muimaster.library" ] || { echo "unexpected archive layout: no MUI/Libs/muimaster.library" >&2; exit 1; }

rm -rf "$dest"
mkdir -p "$dest"
mv "$work/$asset" "$work/files" "$dest/"
echo "MUI $version is in $dest (use: esharp run <sample> --mui $version ...)"

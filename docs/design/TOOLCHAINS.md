# Local toolchains: options per platform and target (research note, issue #122)

Status: research for the maintainer to choose from. Docker (`--docker <image>`) stays the default way to build; this note is
about the second way, a toolchain installed on the developer's machine and named with `--toolchain <dir>` (or
`ESHARP_TOOLCHAIN_<TARGET>` / `ESHARP_TOOLCHAIN`, see [GUIDE.md](../GUIDE.md#building)). E# never bundles or redistributes a
toolchain: the user installs it.

Everything marked **verified** was run on this project's Linux x86-64 dev host on 2026-11-21. Everything else is
**not verified**: facts from the project pages cited, not tried. No toolchain was downloaded or installed on a host for this
note; the copies that were tried were unpacked from the pinned images into a scratch folder with `docker create` and
`docker cp`.

## What the pinned images contain (measured)

| Target | Image (docker/Dockerfile) | Folder in the image | `-dumpversion` | `--version` first line |
|---|---|---|---|---|
| `m68k` | `amigadev/crosstools:m68k-amigaos@sha256:32ec76da...` | `/opt/m68k-amigaos` | `6.5.0b` | `m68k-amigaos-gcc (AmigaDev amiga6-g0a5f82c43) 6.5.0b 20260819091705` |
| `os4` | `amigadev/crosstools:ppc-amigaos@sha256:de69c1dd...` | `/opt/ppc-amigaos` | `11.5.0` | `ppc-amigaos-gcc (adtools build 11.5.0) 11.5.0` |
| `morphos` | `amigadev/crosstools:ppc-morphos@sha256:b5f9f677...` | `/opt/ppc-morphos` | `15.2.0` | `ppc-morphos-gcc (GCC) 15.2.0` |
| `aros` | `amigadev/crosstools@sha256:9c4e9783...` | `/opt/x86_64-aros` (also the sysroot) | `9.1.0` | `x86_64-aros-gcc (GCC) 9.1.0` |
| `aros-i386` | `esharp-aros-i386` (built from docker/aros-i386) | `/opt/i386-aros` (compilers at the top, SDK in `sdk/`) | `6.5.0` | `i386-aros-gcc (GCC) 6.5.0` |

`esharp build` checks a local compiler's `-dumpversion` against this column (`TargetInfo.CompilerVersion`) and stops on any
other version unless `--any-cc-version` is given. It does not check the C library or NDK/SDK versions.

Note: the Docker Hub description of `amigadev/crosstools` (https://hub.docker.com/r/amigadev/crosstools) lists other gcc
versions (gcc 16.2 for m68k, gcc 8 adtools for os4, gcc 9 and SDK 3.14 for MorphOS) than the pinned digests report; the
table above is what the pinned images actually contain.

The m68k image's toolchain:

- What an E# m68k program links (from `m68k-amigaos-gcc -v -noixemul`): libnix's `ncrt0.o` startup, `-lnix20 -lnixmain -lnix
  -lstubs -lamiga -lgcc`. The headers are the NDK 3.2 ones (`ndk-include`, `$VER` 47.x).
- Host requirements of the binaries (measured with objdump): glibc 2.38 and libstdc++ `GLIBCXX_3.4.31` at most (gdb needs
  2.42 but E# does not use it). The image itself is Debian forky/sid.
- **Relocatable (verified):** the folder copied anywhere works; gcc finds its parts relative to `bin/`.

The other images' toolchains, copied out of the image into another folder (**verified: they do not work there**):

- `os4` and `morphos`: `fatal error: stdint.h: No such file or directory`. gcc's relocated search path drops the include
  folders configured with absolute paths (for MorphOS `/opt/ppc-morphos/includestd` and `/opt/ppc-morphos/include`).
- `aros` and `aros-i386`: `collect-aros: /opt/x86_64-aros/bin/x86_64-aros-ld: No such file or directory` (and
  `/opt/i386-aros/i386-aros-ld`): the AROS link wrapper has the binutils paths built in.
- So these copies only work at the image's own path (`/opt/...`), which means a root-owned install on the host. Not tried
  (no host installs here); **not verified**.

## Licences of what gets linked into games

From the third-party list of the toolchain the m68k image comes from,
https://github.com/AmigaPorts/m68k-amigaos-gcc/blob/master/COPYING-THIRD-PARTY.md:

| Component | Licence (as listed) | Linked into an E# m68k game? |
|---|---|---|
| libnix (startup, C library) | "Public Domain" | yes (`-noixemul`) |
| libgcc | GPL v3 with the GCC Runtime Library Exception 3.1 | yes; the exception allows this |
| clib2 | BSD 3-Clause | no (not selected) |
| newlib | collection of BSD-style licences | no on m68k (not selected) |
| ixemul | GPL v2 | **no**: E# always passes `-noixemul`; it must stay that way |
| NDK 3.2 headers and link libraries | "unknown license" | headers yes; `libamiga.a` is linked (where it comes from was not checked) |
| binutils, gcc, sfdc, fd2sfd | GPL | build tools only, not linked |
| vbcc, vasm, vlink | "free for non-commercial purposes" | not used by E# |

Other targets (**not verified** beyond the sources named):

- `os4`: E# links `-lauto` and the SDK's C library (newlib on AmigaOS 4). The AmigaOS 4 SDK comes from Hyperion; its
  licence terms for the link libraries were not checked for this note. clib2 for OS4 is BSD-licensed
  (https://github.com/adtools/clib2). adtools: https://github.com/sba1/adtools.
- `morphos`: `-noixemul` gives libnix (public domain, as above). The MorphOS SDK comes from the MorphOS team
  (https://www.morphos-team.net/downloads, "MorphOS 3.20 SDK - May 2026"); the page states no licence for it.
- `aros`, `aros-i386`: the startup code and libraries are under the AROS Public License 1.1 (MPL-based; the `LICENSE` file in
  the `esharp-aros-i386` image, docker/aros-i386/README.md), libgcc under the runtime exception. Not GPL/LGPL.

Open question for the maintainer: the NDK 3.2 entry ("unknown license") and the OS4 SDK link libraries are the only parts
in a game binary whose terms this note could not pin down. The Docker builds link the same parts, so this is not new with
local toolchains.

## Options per platform

### Linux (x86-64)

| Target | Option | Where from | Version match | Status |
|---|---|---|---|---|
| m68k | Copy the folder out of the pinned image (`docker create` + `docker cp /opt/m68k-amigaos`), any folder | the pinned image | identical by construction | **verified**: `samples/hello` and `samples/cards` built with `--toolchain` are byte-identical to the Docker builds; cards runs in FS-UAE on Kickstart 3.1; also in CI (`tools/local-toolchain/compare.sh`) |
| m68k | Build AmigaPorts m68k-amigaos-gcc (`git clone https://github.com/AmigaPorts/m68k-amigaos-gcc; make update; make all PREFIX=<dir>`; Ubuntu/Debian or Fedora packages listed in https://github.com/AmigaPorts/m68k-amigaos-gcc/blob/master/doc/build.md) | AmigaPorts (the continuation of Bebbo's amiga-gcc: https://codeberg.org/bebbo/amiga-gcc, https://franke.ms/amiga/amiga-gcc.wiki) | gcc 6.5.0b if the default branch still builds it; the pinned image says `amiga6-g0a5f82c43`. libnix and NDK versions depend on the checkout | not verified |
| os4 | adtools (https://github.com/sba1/adtools; the page's prebuilt Debian packages were on Bintray, which has shut down) plus the AmigaOS 4 SDK from Hyperion | adtools, Hyperion | the image's is "adtools build 11.5.0"; matching it means the same adtools gcc 11 branch | not verified |
| os4, morphos, aros | The image's folder at the image's path (`/opt/ppc-amigaos`, ...) | the pinned images | identical | not verified (needs a root install; copies elsewhere fail, see above) |
| aros | AROS's own cross toolchain, built by the AROS build system (https://github.com/aros-development-team/AROS) | AROS | the image has gcc 9.1.0 | not verified |
| aros-i386 | Build it as docker/aros-i386 does (AROS sources at a pinned commit, gcc 6.5.0) | AROS | same recipe | not verified outside the image |

### macOS (Intel and Apple Silicon)

- m68k: AmigaPorts m68k-amigaos-gcc lists macOS as a build host (Xcode plus Homebrew: `brew install bash bison coreutils
  curl flex gettext gnu-sed gnu-tar grep make wget xz`, GNU tools first on PATH; build.md above). Apple Silicon is not named
  on that page. Bebbo's wiki lists macOS too. **Not verified** on either Mac.
- The Linux image copies cannot run on macOS (they are Linux ELF programs).
- os4, morphos, aros: no macOS-hosted toolchain was found documented on the pages above; **not verified**. Docker Desktop
  stays the way there (on Apple Silicon it runs the amd64 images under emulation; not verified here).

### Windows

- m68k: AmigaPorts build.md documents MSYS2 only (`pacman -S curl git base-devel gcc flex rsync`, run make from an absolute
  path). Bebbo's wiki says Windows through "MSYS2, WSL, Cygwin". A fork's README links a prebuilt installer on franke.ms
  (https://github.com/mheyer32/amiga-gcc); not checked. **Not verified.**
- WSL2: the Linux route above (image copy or a Linux build) inside WSL, with esharp also run inside WSL. **Not verified.**
- `esharp` looks for `bin/<compiler>.exe` on Windows. **Not verified** with a real Windows toolchain.
- os4, morphos, aros: Docker Desktop. A native Windows toolchain was not found documented; **not verified**.

## Recommendation (for the maintainer to decide)

1. Keep Docker the default everywhere, as now: one toolchain, pinned, byte-for-byte reproducible.
2. Local m68k on Linux: document the image copy (`docker create`/`docker cp` of `/opt/m68k-amigaos` into any folder, then
   `--toolchain <folder>`). It needs Docker once, gives the same bytes, and is what CI checks. Without Docker at all,
   building AmigaPorts m68k-amigaos-gcc is the documented route (not verified; `-dumpversion` must say 6.5.0b).
3. Local m68k on macOS and Windows: AmigaPorts m68k-amigaos-gcc built with Homebrew / MSYS2 (or WSL2 with the Linux route).
   Verify by hand on real machines first (part 2 of #122: the steps and a paste-back checklist are in docs/GUIDE.md, "Local m68k toolchain on macOS and Windows", all marked not verified), comparing the output with the Docker build of the same source.
4. os4, morphos, aros, aros-i386: stay Docker-only for now. Their image copies are not relocatable; supporting them locally
   means a root install at `/opt/<name>` on Linux or the vendors' own SDK setups, each to be tried before it is documented.
5. Keep `-noixemul` on m68k and morphos, so no GPL C library is ever linked into a game; settle the NDK 3.2 and OS4 SDK link
   library terms separately (they apply to Docker builds too).

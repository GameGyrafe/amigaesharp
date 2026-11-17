# esharp-aros-i386

The toolchain for the `aros-i386` target: `i386-aros-gcc` (gcc 6.5.0, binutils 2.32, the versions AROS's own crosstools
rules default to for i386) and the AROS i386 SDK (headers and link libraries: startup code, autoinit, libamiga, the C library
stubs), built from the AROS sources with AROS's configure and `make crosstools sdk`. No prebuilt i386 AROS toolchain image was
found, so this one is built here.

```bash
docker build -t esharp-aros-i386 docker/aros-i386        # long: see "Build" below
docker/aros-i386/smoke-test.sh                           # builds samples/hello for aros-i386 (and runs it if esharp-aros-hosted-i386 exists)

dotnet run --project src/ESharp.Cli -- build samples/cards --target aros-i386 --docker esharp-aros-i386
docker build --build-arg AROS_ARCH=i386 -t esharp-aros-hosted-i386 docker/aros-hosted
tools/aros-hosted/aros-hosted.sh cards --arch i386 --docker esharp-aros-hosted-i386 --shot shot.png --input "click 400,300"
```

In the image: the compiler is `/opt/i386-aros/i386-aros-gcc` (on `PATH`), the SDK is `/opt/i386-aros/sdk` (`include`, `lib`),
passed as `--sysroot=/opt/i386-aros/sdk` (the target's flags in `src/ESharp.Compiler/Targets.cs`). `/opt/i386-aros/AROS_COMMIT`
names the source commit; AROS's `LICENSE` and `ACKNOWLEDGEMENTS` are next to it.

## Pins

- Base image: `ubuntu:24.04` by digest (the AROS nightlies are built on Ubuntu 24.04; gcc 6.5.0 builds with its gcc 13).
- AROS source: commit `656d2825b63dce8e3acca5e139197864169dc209` (2026-11-15), the last commit before the linux-i386 nightly
  20261116 was configured, so the SDK matches the hosted i386 AROS that `esharp-aros-hosted-i386` runs. `git` checks the sha.
  Another commit: `--build-arg AROS_COMMIT=<sha>`.
- The tarballs the AROS crosstools rules would otherwise download (gcc 6.5.0, binutils 2.32, gmp 6.3.0, mpfr 4.2.2, mpc 1.4.1,
  isl 0.25) are fetched first and checked by sha256, then handed to AROS with `--with-portssources`.
- Configure: `--target=linux-i386`, as the nightly. The nightly also passes `--with-aros-toolchain=yes`, which means "use a
  prebuilt toolchain" (its toolchain comes from an earlier stage), so it is left out here and `make crosstools` builds it.

## Build

Multi-stage: the build stage (AROS tree, build directory, host packages) is thrown away; the final image is Ubuntu plus
`/opt/i386-aros`. `--build-arg JOBS=<n>` sets make's parallelism (default 16). The final stage builds a one-line program
with the SDK before the image is kept.

Licences: AROS is under the AROS Public License. gcc and binutils (GPL) are build tools in the image; programs link AROS's
APL startup code and libraries and libgcc (GCC runtime library exception), as with the x86-64 AROS toolchain.

Not in CI: the build takes far longer than a CI job should (see docs/STATUS.md for the measured time and size). Build it
locally.

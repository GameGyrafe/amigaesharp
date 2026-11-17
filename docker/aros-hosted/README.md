# esharp-aros-hosted

A pinned runtime for the `aros` target: the AROS x86-64 hosted nightly (a Linux process that boots AROS) with Xvfb,
xwd and python3, so no AROS tree has to be unpacked on the host. With `--build-arg AROS_ARCH=i386` the same Dockerfile
makes the runtime for the `aros-i386` target (tag it `esharp-aros-hosted-i386`): the linux-i386 hosted nightly, a 32-bit
Linux program, plus the i386 C library and the X11 and ALSA libraries it loads (`libc6:i386`, libx11, libxext, libxxf86vm,
libxcursor, libxrender, libasound2, all `:i386`).

```bash
docker build -t esharp-aros-hosted docker/aros-hosted     # downloads the nightly from SourceForge, checks its sha256
docker/aros-hosted/smoke-test.sh                          # builds samples/hello for aros and runs it in the image

docker build --build-arg AROS_ARCH=i386 -t esharp-aros-hosted-i386 docker/aros-hosted
docker/aros-hosted/smoke-test.sh esharp-aros-hosted-i386  # builds hello for aros-i386 (esharp-aros-i386, docker/aros-i386) and runs it

dotnet run --project src/ESharp.Cli -- build samples/cards --target aros --docker esharp-amiga-toolchain
tools/aros-hosted/aros-hosted.sh cards --docker esharp-aros-hosted --shot shot.png --input "click 400,300"
```

`aros-hosted.sh <sample> --docker esharp-aros-hosted [--arch x86_64|i386] [--shot f.png] [--input "..."] [--wait N] [--log]` mounts the repo
read-only, copies the image's pristine AROS tree for each run (S:Startup-Sequence is never patched in place) and prints
the program's output. `--arch i386` runs `obj/aros-i386/<sample>`; without `--arch` an image name containing `i386` means
i386, and the image's `AROS_HOSTED_ARCH` must agree. Without `--docker`, the script still works on an extracted archive (`AROS_HOSTED=<dir>`).

## Pins

- Base image: `debian:bookworm-slim` by digest.
- Nightly, x86_64: `20261114`, sha256 `e05fecb5...4cb7`; i386: `20261116` (`AROS-20261116-linux-i386-system.tar.bz2`),
  sha256 `ffeb6e7b...dcca`, the nightly built from AROS commit `656d2825` that `docker/aros-i386` builds its toolchain from
  (the SDK headers match that nightly's `Developer/include`). Build args `AROS_DATE` and `AROS_SHA256` override the pin of
  the chosen arch; the build fails if the sha256 differs.
  Another nightly: `--build-arg AROS_DATE=<yyyymmdd> --build-arg AROS_SHA256=<sha256 of its archive>`.
- SourceForge rotates nightlies. To build from a local copy of `AROS-<date>-linux-<arch>-system.tar.bz2`, put it in a
  folder and pass `--build-context archive=<folder>`; the sha256 is checked the same way. Archives are git-ignored
  and never committed.
- AHI's "void" audio mode is the only audio mode (ALSA's mode hangs audio.device without a sound card).

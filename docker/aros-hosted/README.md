# esharp-aros-hosted

A pinned runtime for the `aros` target: the AROS x86-64 hosted nightly (a Linux process that boots AROS) with Xvfb,
xwd and python3, so no AROS tree has to be unpacked on the host.

```bash
docker build -t esharp-aros-hosted docker/aros-hosted     # downloads the nightly from SourceForge, checks its sha256
docker/aros-hosted/smoke-test.sh                          # builds samples/hello for aros and runs it in the image

dotnet run --project src/ESharp.Cli -- build samples/cards --target aros --docker esharp-amiga-toolchain
tools/aros-hosted/aros-hosted.sh cards --docker esharp-aros-hosted --shot shot.png --input "click 400,300"
```

`aros-hosted.sh <sample> --docker esharp-aros-hosted [--shot f.png] [--input "..."] [--wait N] [--log]` mounts the repo
read-only, copies the image's pristine AROS tree for each run (S:Startup-Sequence is never patched in place) and prints
the program's output. Without `--docker`, the script still works on an extracted archive (`AROS_HOSTED=<dir>`).
`--files <dir>` copies a folder into a drawer `SYS:ESTest`; `--wanderer` starts no program at boot but puts it and its `.info` into
`SYS:ESTest` and boots on to Wanderer, then runs `--input` (`click`, `dbl`, `sleep`, `key`) and `--shot` after `--wait` seconds (issue #101).

## Pins

- Base image: `debian:bookworm-slim` by digest.
- Nightly: `AROS_DATE=20261114`, `AROS_SHA256=e05fecb5...4cb7` (build args; the build fails if the sha256 differs).
  Another nightly: `--build-arg AROS_DATE=<yyyymmdd> --build-arg AROS_SHA256=<sha256 of its archive>`.
- SourceForge rotates nightlies. To build from a local copy of `AROS-<date>-linux-x86_64-system.tar.bz2`, put it in a
  folder and pass `--build-context archive=<folder>`; the sha256 is checked the same way. Archives are git-ignored
  and never committed.
- AHI's "void" audio mode is the only audio mode (ALSA's mode hangs audio.device without a sound card).

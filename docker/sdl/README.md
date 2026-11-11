# esharp-sdl: build image for the E# `sdl` target

Debian bookworm-slim (pinned by digest) with gcc, make, cmake, ninja, pkg-config and **SDL 3.4.20** built from the
pinned release (tag `release-3.4.20`, tarball sha256 checked in the Dockerfile) as a static library (`libSDL3.a`) with the
X11, Wayland, ALSA, PulseAudio, KMS/DRM and OpenGL/EGL backends. SDL dlopens those at run time, so a binary linked with
`pkg-config --static` runs on a Linux desktop with no SDL installed, and headless with
`SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy`. SDL 3 is zlib-licensed. Image size: about 877 MB.

```bash
docker build -t esharp-sdl docker/sdl
docker/sdl/smoke-test.sh                      # static link, offscreen window/renderer/texture, dummy audio
docker run --rm -v "$PWD":/work esharp-sdl sh -c 'gcc t.c $(pkg-config --cflags --libs --static sdl3) -o t'
```

Only the static library is installed (no `libSDL3.so`), so even the plain `pkg-config --libs sdl3` line links statically.
To update SDL: change `SDL_VERSION` and `SDL_SHA256` in the Dockerfile together, rebuild, run the smoke test.

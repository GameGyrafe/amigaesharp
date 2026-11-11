# Build environment for the E# "sdl" target (Linux x86-64): gcc, make, cmake, pkg-config and SDL 3 built from a
# pinned release as a static library (libSDL3.a). SDL dlopens X11, Wayland, ALSA and PulseAudio at run time, so a
# binary linked with `pkg-config --static` runs on a desktop without SDL installed, and headless with
# SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy.
#
#   docker build -t esharp-sdl docker/sdl && docker/sdl/smoke-test.sh
#   docker run --rm -v "$PWD":/work -w /work esharp-sdl sh -c 'gcc t.c $(pkg-config --cflags --libs --static sdl3) -o t'
#
# Everything is pinned: the base image by digest (debian:bookworm-slim) and SDL 3 by tag and tarball sha256.
# Update them deliberately and re-run the smoke test. SDL 3 is zlib-licensed.
FROM debian@sha256:734a72b3d6142a71993b4bc580ca01d8ca687f77a2c422d96a961d3376b179bc

ARG SDL_VERSION=3.4.20
ARG SDL_SHA256=f3bb06f55bed9557227a700af2ea62e49497ebdf58037732a731a5fb3ef8581f

ENV DEBIAN_FRONTEND=noninteractive
# Dev packages SDL's desktop build needs (headers at build time; the libraries are dlopened at run time).
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl gcc libc6-dev make cmake ninja-build pkg-config \
        libasound2-dev libpulse-dev libdbus-1-dev libudev-dev libdrm-dev libgbm-dev libgl-dev libegl-dev \
        libx11-dev libxext-dev libxcursor-dev libxi-dev libxfixes-dev libxrandr-dev libxss-dev libxtst-dev \
        libxkbcommon-dev libwayland-dev wayland-protocols libdecor-0-dev \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL -o /tmp/sdl.tar.gz "https://github.com/libsdl-org/SDL/releases/download/release-${SDL_VERSION}/SDL3-${SDL_VERSION}.tar.gz" \
    && echo "${SDL_SHA256}  /tmp/sdl.tar.gz" | sha256sum -c - \
    && tar -xzf /tmp/sdl.tar.gz -C /tmp \
    && cmake -S /tmp/SDL3-${SDL_VERSION} -B /tmp/sdl-build -G Ninja -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_INSTALL_PREFIX=/usr/local -DSDL_STATIC=ON -DSDL_SHARED=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF \
        -DSDL_INSTALL_TESTS=OFF \
    && cmake --build /tmp/sdl-build && cmake --install /tmp/sdl-build \
    && ldconfig \
    && rm -rf /tmp/sdl.tar.gz /tmp/SDL3-${SDL_VERSION} /tmp/sdl-build

ENV PKG_CONFIG_PATH=/usr/local/lib/pkgconfig \
    SDL_VERSION=${SDL_VERSION}
WORKDIR /work

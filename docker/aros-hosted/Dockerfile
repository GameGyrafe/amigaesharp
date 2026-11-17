# Runtime for the E# "aros" and "aros-i386" targets: an AROS hosted nightly (a Linux process that boots AROS) plus Xvfb,
# xwd and python3 for screenshots and scripted input. Build and run E# samples with
# tools/aros-hosted/aros-hosted.sh <sample> --docker esharp-aros-hosted   (docs: docker/aros-hosted/README.md)
#
#   docker build -t esharp-aros-hosted docker/aros-hosted && docker/aros-hosted/smoke-test.sh
#   docker build --build-arg AROS_ARCH=i386 -t esharp-aros-hosted-i386 docker/aros-hosted && docker/aros-hosted/smoke-test.sh i386
#
# AROS_ARCH picks the hosted build: x86_64 (default, the "aros" target) or i386 (the "aros-i386" target: a 32-bit Linux
# program, so the image also gets the i386 C library and the X11/ALSA libraries it loads).
#
# Pinned: the base image by digest (debian:bookworm-slim) and the AROS nightly per arch by date and tarball sha256 (the
# build fails on a mismatch). SourceForge rotates nightlies, so to build from a local copy of the archive, put
# AROS-<date>-linux-<arch>-system.tar.bz2 in a folder and pass it as a named context:
#   docker build --build-context archive=<folder> [--build-arg AROS_ARCH=i386] -t <tag> docker/aros-hosted
# To use another nightly: --build-arg AROS_DATE=<yyyymmdd> --build-arg AROS_SHA256=<sha256 of that archive>.
# The archive is never committed. AROS is under the AROS Public License (see LICENSE inside the image, /opt/aros).

# Default for the "archive" context: empty, so the download is used. --build-context archive=<folder> replaces it.
FROM scratch AS archive

FROM debian@sha256:734a72b3d6142a71993b4bc580ca01d8ca687f77a2c422d96a961d3376b179bc

ARG AROS_ARCH=x86_64
# Empty: the pinned nightly of AROS_ARCH below.
ARG AROS_DATE=
ARG AROS_SHA256=

ENV DEBIAN_FRONTEND=noninteractive
RUN case "$AROS_ARCH" in x86_64) extra="";; \
        i386) dpkg --add-architecture i386 \
              && extra="libc6:i386 libx11-6:i386 libxext6:i386 libxxf86vm1:i386 libxcursor1:i386 libxrender1:i386 libasound2:i386";; \
        *) echo "AROS_ARCH must be x86_64 or i386"; exit 1;; esac \
    && apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl bzip2 xvfb x11-apps python3 libx11-6 libxtst6 $extra \
    && rm -rf /var/lib/apt/lists/*

# The pinned nightlies: x86_64 20261114, i386 20261116 (the AROS commit docker/aros-i386 builds its toolchain from).
RUN --mount=type=bind,from=archive,target=/archive \
    case "$AROS_ARCH" in \
      x86_64) date=${AROS_DATE:-20261114} sha=${AROS_SHA256:-e05fecb59ab5907a5b12be690abc787a498df9d3632ac267f27cdad304de4cb7};; \
      i386) date=${AROS_DATE:-20261116} sha=${AROS_SHA256:-ffeb6e7bf6407911631e793399c5f0c6e62ed2c81fcbc20c943116b86a87dcca};; \
    esac \
    && f=AROS-${date}-linux-${AROS_ARCH}-system.tar.bz2 \
    && if [ -f "/archive/$f" ]; then cp "/archive/$f" /tmp/aros.tar.bz2; \
       else curl -fsSL -o /tmp/aros.tar.bz2 "https://downloads.sourceforge.net/project/aros/nightly2/${date}/Binaries/$f"; fi \
    && echo "${sha}  /tmp/aros.tar.bz2" | sha256sum -c - \
    && mkdir /opt/aros-x && tar -xjf /tmp/aros.tar.bz2 -C /opt/aros-x && rm /tmp/aros.tar.bz2 \
    && mv /opt/aros-x/* /opt/aros && rmdir /opt/aros-x \
    && test -x /opt/aros/boot/linux/AROSBootstrap

# AHI "void" mode as the only audio mode: ALSA's mode hangs audio.device when there is no card (see tools/aros-hosted).
RUN cd /opt/aros && mkdir -p Devs/AudioModes.orig && mv Devs/AudioModes/* Devs/AudioModes.orig/ 2>/dev/null; \
    cp Storage/AudioModes/VOID Devs/AudioModes/VOID && test -f Devs/AudioModes/VOID
# Runs use an unprivileged user (aros-hosted.sh passes -u): everything must be readable.
RUN chmod -R a+rX /opt/aros

# /opt/aros is the pristine tree: every run copies it (aros-hosted.sh), so S:Startup-Sequence is never patched here.
# AROS_HOSTED_ARCH tells aros-hosted.sh which build is inside (and so which obj/<target> folder to run from).
ENV AROS_HOSTED_COPY=/opt/aros AROS_HOSTED_ARCH=${AROS_ARCH}
WORKDIR /work

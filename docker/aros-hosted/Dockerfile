# Runtime for the E# "aros" target: the AROS x86-64 hosted nightly (a Linux process that boots AROS) plus Xvfb,
# xwd and python3 for screenshots and scripted input. Build and run E# samples with
# tools/aros-hosted/aros-hosted.sh <sample> --docker esharp-aros-hosted   (docs: docker/aros-hosted/README.md)
#
#   docker build -t esharp-aros-hosted docker/aros-hosted && docker/aros-hosted/smoke-test.sh
#
# Pinned: the base image by digest (debian:bookworm-slim) and the AROS nightly by date and tarball sha256 (the build
# fails on a mismatch). SourceForge rotates nightlies, so to build from a local copy of the archive, put
# AROS-<date>-linux-x86_64-system.tar.bz2 in a folder and pass it as a named context:
#   docker build --build-context archive=<folder> -t esharp-aros-hosted docker/aros-hosted
# To use another nightly: --build-arg AROS_DATE=<yyyymmdd> --build-arg AROS_SHA256=<sha256 of that archive>.
# The archive is never committed. AROS is under the AROS Public License (see LICENSE inside the image, /opt/aros).

# Default for the "archive" context: empty, so the download is used. --build-context archive=<folder> replaces it.
FROM scratch AS archive

FROM debian@sha256:734a72b3d6142a71993b4bc580ca01d8ca687f77a2c422d96a961d3376b179bc

ARG AROS_DATE=20261114
ARG AROS_SHA256=e05fecb59ab5907a5b12be690abc787a498df9d3632ac267f27cdad304de4cb7

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl bzip2 xvfb x11-apps python3 libx11-6 libxtst6 \
    && rm -rf /var/lib/apt/lists/*

RUN --mount=type=bind,from=archive,target=/archive \
    f=AROS-${AROS_DATE}-linux-x86_64-system.tar.bz2 \
    && if [ -f "/archive/$f" ]; then cp "/archive/$f" /tmp/aros.tar.bz2; \
       else curl -fsSL -o /tmp/aros.tar.bz2 "https://downloads.sourceforge.net/project/aros/nightly2/${AROS_DATE}/Binaries/$f"; fi \
    && echo "${AROS_SHA256}  /tmp/aros.tar.bz2" | sha256sum -c - \
    && mkdir /opt/aros-x && tar -xjf /tmp/aros.tar.bz2 -C /opt/aros-x && rm /tmp/aros.tar.bz2 \
    && mv /opt/aros-x/* /opt/aros && rmdir /opt/aros-x \
    && test -x /opt/aros/boot/linux/AROSBootstrap

# AHI "void" mode as the only audio mode: ALSA's mode hangs audio.device when there is no card (see tools/aros-hosted).
RUN cd /opt/aros && mkdir -p Devs/AudioModes.orig && mv Devs/AudioModes/* Devs/AudioModes.orig/ 2>/dev/null; \
    cp Storage/AudioModes/VOID Devs/AudioModes/VOID && test -f Devs/AudioModes/VOID
# Runs use an unprivileged user (aros-hosted.sh passes -u): everything must be readable.
RUN chmod -R a+rX /opt/aros

# /opt/aros is the pristine tree: every run copies it (aros-hosted.sh), so S:Startup-Sequence is never patched here.
ENV AROS_HOSTED_COPY=/opt/aros
WORKDIR /work

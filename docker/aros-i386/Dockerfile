# Toolchain for the E# "aros-i386" target: an i386-aros-gcc cross compiler and the AROS i386 SDK (headers and link
# libraries: startup, autoinit, libamiga, the C library stubs), built from the AROS source tree with AROS's own
# configure and crosstools rules. docs: docker/aros-i386/README.md
#
#   docker build -t esharp-aros-i386 docker/aros-i386 && docker/aros-i386/smoke-test.sh
#
# Pinned: the base image by digest (ubuntu:24.04, the host the AROS nightlies are built on), the AROS source by commit
# (the one the linux-i386 nightly of AROS_NIGHTLY was built from; git checks the sha), and every tarball the AROS
# crosstools rules would download (gcc, binutils, gmp, mpfr, mpc, isl) by sha256 (the build fails on a mismatch).
# Configured as --target=linux-i386, the target of that nightly, so the headers and ABI match the hosted i386 AROS that
# docker/aros-hosted runs (AROS_ARCH=i386). About 10 minutes with 16 jobs on a 32-core host (crosstools ~9 minutes, the SDK
# ~1 minute); the final stage (1.3 GB) keeps only the toolchain and the SDK.
#
# Licences: AROS is under the AROS Public License (LICENSE in /opt/i386-aros). gcc and binutils are GPL build tools
# here; programs link only AROS's APL startup and libraries plus libgcc (GCC runtime library exception).

ARG UBUNTU=ubuntu@sha256:73b999393d07f2bf65a40373778a44dc9a6d54ad7718d29247d8ff9413c4d6a8

FROM ${UBUNTU} AS build
ARG AROS_COMMIT=656d2825b63dce8e3acca5e139197864169dc209
ARG JOBS=16
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential gcc-multilib g++-multilib git ca-certificates curl wget xz-utils bzip2 patch file \
        gawk bison flex netpbm autoconf automake cmake meson ninja-build nasm gperf texinfo pkg-config \
        python3 python-is-python3 python3-mako python3-yaml libswitch-perl \
        libpng-dev zlib1g-dev liblzma-dev libx11-dev libxcursor-dev libasound2-dev \
    && rm -rf /var/lib/apt/lists/*

# The crosstools sources the AROS rules fetch, placed in --with-portssources so nothing is downloaded unchecked.
RUN mkdir /ports && cd /ports && set -e; \
    get() { curl -fsSL -o "$2" "$1/$2" && echo "$3  $2" | sha256sum -c -; }; \
    get https://ftpmirror.gnu.org/gnu/gcc/gcc-6.5.0 gcc-6.5.0.tar.xz 7ef1796ce497e89479183702635b14bb7a46b53249209a5e0f999bebf4740945; \
    get https://ftpmirror.gnu.org/gnu/binutils binutils-2.32.tar.bz2 de38b15c902eb2725eac6af21183a5f34ea4634cb0bcef19612b50e5ed31072d; \
    get https://ftpmirror.gnu.org/gnu/gmp gmp-6.3.0.tar.bz2 ac28211a7cfb609bae2e2c8d6058d66c8fe96434f740cf6fe2e47b000d1c20cb; \
    get https://ftpmirror.gnu.org/gnu/mpfr mpfr-4.2.2.tar.bz2 9ad62c7dc910303cd384ff8f1f4767a655124980bb6d8650fe62c815a231bb7b; \
    get https://ftpmirror.gnu.org/gnu/mpc mpc-1.4.1.tar.xz 91204cd32f164bd3b7c992d4a6a8ce6519511aadab30f78b6982d0bf8d73e931; \
    get https://libisl.sourceforge.io isl-0.25.tar.bz2 4305c54d4eebc4bf3ce365af85f04984ef5aa97a52e01128445e26da5b1f467a

RUN git init -q /src && cd /src \
    && git fetch -q --depth 1 https://github.com/aros-development-team/AROS "${AROS_COMMIT}" \
    && git checkout -q FETCH_HEAD && test "$(git rev-parse HEAD)" = "${AROS_COMMIT}"

# The linux-i386 nightly's target, with the crosstools built here (the nightly passes --with-aros-toolchain=yes because
# its toolchain comes prebuilt from another stage). The toolchain installs into /opt/i386-aros.
WORKDIR /build
RUN /src/configure --target=linux-i386 --with-portssources=/ports \
        --with-aros-toolchain-install=/opt/i386-aros > configure.log 2>&1 \
    || { tail -60 configure.log; exit 1; }
RUN make -j"${JOBS}" crosstools > crosstools.log 2>&1 || { tail -150 crosstools.log; exit 1; }
RUN make -j"${JOBS}" sdk > sdk.log 2>&1 || { tail -150 sdk.log; exit 1; }
# The SDK: headers and link libraries as the compiler's sysroot (/include, /lib), as in the x86-64 AROS image.
RUN set -e; d=bin/linux-i386/AROS/Developer; test -d "$d/include" && test -d "$d/lib"; \
    mkdir -p /opt/i386-aros/sdk && cp -a "$d/include" "$d/lib" /opt/i386-aros/sdk/ \
    && cp /src/LICENSE /src/ACKNOWLEDGEMENTS /opt/i386-aros/ \
    && echo "${AROS_COMMIT}" > /opt/i386-aros/AROS_COMMIT \
    && rm -rf /opt/i386-aros/share/info /opt/i386-aros/share/man
# The toolchain must build and link a program on its own before it is kept.
RUN printf '#include <proto/dos.h>\nint main(void){ PutStr("ok\\n"); return 0; }\n' > /tmp/t.c \
    && /opt/i386-aros/i386-aros-gcc --sysroot=/opt/i386-aros/sdk -O2 -o /tmp/t /tmp/t.c && file /tmp/t | grep -q 'ELF 32-bit.*80386'

FROM ${UBUNTU}
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends make file && rm -rf /var/lib/apt/lists/*
COPY --from=build /opt/i386-aros /opt/i386-aros
ENV PATH=/opt/i386-aros:$PATH
RUN printf '#include <proto/dos.h>\nint main(void){ PutStr("ok\\n"); return 0; }\n' > /tmp/t.c \
    && i386-aros-gcc --sysroot=/opt/i386-aros/sdk -O2 -o /tmp/t /tmp/t.c && rm /tmp/t /tmp/t.c
WORKDIR /work

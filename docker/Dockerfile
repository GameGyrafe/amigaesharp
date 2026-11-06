# syntax=docker/dockerfile:1
#
# E# Amiga toolchain image: every Amiga-family C compiler E# can target, pinned by digest so a
# fresh checkout builds the same way everywhere.
#
#   m68k-amigaos-gcc   classic AmigaOS 1.x-3.x on 68000+   (Bebbo's amiga-gcc, GCC 6.5.0b)
#   ppc-amigaos-gcc    AmigaOS 4 on PowerPC                (adtools, GCC 11.5.0)
#   ppc-morphos-gcc    MorphOS on PowerPC                  (GCC 15.2.0)
#
# To update a toolchain, pull the new tag, copy its digest here, and run docker/smoke-test.sh.
FROM amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859 AS m68k
FROM amigadev/crosstools:ppc-amigaos@sha256:de69c1ddd5ea77c7202f50304919b45b94d45060a11f70eaf3767175cd73f8b3 AS os4
FROM amigadev/crosstools:ppc-morphos@sha256:b5f9f677adb0adfa25a1ecdecb1265029b1b60e34d9148e72848f8c84abae54c AS morphos

FROM m68k
COPY --from=os4 /opt/ppc-amigaos /opt/ppc-amigaos
COPY --from=morphos /opt/ppc-morphos /opt/ppc-morphos
ENV PATH=/opt/m68k-amigaos/bin:/opt/ppc-amigaos/bin:/opt/ppc-morphos/bin:$PATH
WORKDIR /work

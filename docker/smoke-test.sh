#!/usr/bin/env bash
# Builds a trivial C program for every E# Amiga target inside the toolchain image and checks the
# output format. Usage: docker/smoke-test.sh [image]   (default: esharp-amiga-toolchain)
set -euo pipefail
image="${1:-esharp-amiga-toolchain}"

docker run --rm "$image" bash -euo pipefail -c '
cat > t.c <<C
#include <proto/dos.h>
int main(void) { PutStr("ok\n"); return 0; }
C
magic() { od -A n -t x1 -N 4 "$1" | tr -d " "; }

m68k-amigaos-gcc -noixemul -m68020 -O2 -Wall -o t.m68k t.c
[ "$(magic t.m68k)" = "000003f3" ] || { echo "m68k: not a hunk executable"; exit 1; }
echo "m68k    ok (AmigaOS hunk)"

ppc-amigaos-gcc -O2 -Wall -D__USE_INLINE__ -o t.os4 t.c -lauto
[ "$(magic t.os4)" = "7f454c46" ] || { echo "os4: not ELF"; exit 1; }
echo "os4     ok (PPC ELF)"

ppc-morphos-gcc -noixemul -O2 -Wall -o t.mos t.c
[ "$(magic t.mos)" = "7f454c46" ] || { echo "morphos: not ELF"; exit 1; }
echo "morphos ok (PPC ELF)"

cat > a.c <<C
int main(void) { return 0; }
C
x86_64-aros-gcc --sysroot=/opt/x86_64-aros -O2 -fwrapv -Wall -o t.aros a.c
[ "$(magic t.aros)" = "7f454c46" ] || { echo "aros: not ELF"; exit 1; }
# ELF e_machine (offset 18, little endian) 0x3e = x86-64
[ "$(od -A n -t x1 -j 18 -N 2 t.aros | tr -d " ")" = "3e00" ] || { echo "aros: not x86-64"; exit 1; }
echo "aros    ok (x86-64 ELF)"
'

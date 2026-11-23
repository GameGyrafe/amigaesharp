/* m68k: eight bytes per pass. On a 68020 the loop's own compare and branch cost about as much as summing a byte, and an
   unrolled body of eight still fits the 256-byte instruction cache. */
#ifndef CKSUM_TUNE_H
#define CKSUM_TUNE_H
#define CKSUM_UNROLL 8
#endif

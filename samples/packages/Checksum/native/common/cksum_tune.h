/* How many bytes cksum_adler32 sums per pass of its inner loop. Targets other than m68k: one, and the C compiler
   unrolls as it sees fit. native/m68k/cksum_tune.h replaces this file on m68k. */
#ifndef CKSUM_TUNE_H
#define CKSUM_TUNE_H
#define CKSUM_UNROLL 1
#endif

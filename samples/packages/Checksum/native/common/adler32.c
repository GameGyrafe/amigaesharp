/* Adler-32 for the ESharpSample.Checksum E# library. esharp build includes esharp_natives.h first: the prototype of
   cksum_adler32 as Adler32.cs declares it, so a definition that does not match is a compile error. */
#include "cksum_tune.h"

/* The largest number of bytes whose sums cannot pass 2^32 before the modulo, starting from sums below 65521. */
#define CKSUM_BLOCK 5552
#define CKSUM_MOD 65521u

/* Static: private to this file, so it needs no prefix. */
static uint32_t cksum_sum_block(const uint8_t *p, int32_t n, uint32_t *b)
{
    uint32_t a = 0, s = *b;
    while (n >= CKSUM_UNROLL)
    {
        int k;
        for (k = 0; k < CKSUM_UNROLL; k++) { a += p[k]; s += a; }
        p += CKSUM_UNROLL;
        n -= CKSUM_UNROLL;
    }
    while (n-- > 0) { a += *p++; s += a; }
    *b = s;
    return a;
}

uint32_t cksum_adler32(uint32_t sum, uint8_t *data, int32_t length)
{
    uint32_t a = sum & 0xFFFFu, b = sum >> 16;
    while (length > 0)
    {
        int32_t n = length < CKSUM_BLOCK ? length : CKSUM_BLOCK;
        uint32_t before = b + a * (uint32_t)n;   /* each byte's run adds the a it started from, n times */
        uint32_t extra = 0;
        a += cksum_sum_block(data, n, &extra);
        b = before + extra;
        a %= CKSUM_MOD;
        b %= CKSUM_MOD;
        data += n;
        length -= n;
    }
    return (b << 16) | a;
}

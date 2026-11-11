/* ---- E# runtime for non-Amiga targets: standard C only (stdio, stdlib, time). ---- */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

/* Text written through Dos.PutStr goes here too, so the SDL layer can see "SHOT" (it starts scripted input then). */
static void (*es_std_out_hook)(const char *s);

static __attribute__((unused)) int32_t es_std_putstr(const char *s)
{
    if (s == NULL) return 0;
    fputs(s, stdout);
    if (es_std_out_hook != NULL) es_std_out_hook(s);
    return 0;
}

static __attribute__((unused)) void es_std_flush(void) { fflush(stdout); }

/* Memory: calloc with a small header that remembers the size and kind, so Mem.AvailChip/AvailFast can show what is in use
   (there is no chip RAM: both kinds are ordinary memory, and each kind has a nominal 256 MB, so an allocation larger than what
   is left fails as it would on an Amiga instead of "succeeding" with an overcommitting desktop allocator). */
#define ES_STD_POOL 0x10000000u
static uint32_t es_std_used[2];
typedef struct { uint32_t size; uint32_t chip; uint32_t pad[2]; } EsStdHdr;

static __attribute__((unused)) uint8_t *es_std_alloc(uint32_t size, bool chip)
{
    if (size == 0 || size > ES_STD_POOL - es_std_used[chip ? 1 : 0]) return NULL;
    EsStdHdr *h = (EsStdHdr *)calloc(1, sizeof(EsStdHdr) + (size_t)size);
    if (h == NULL) return NULL;
    h->size = size;
    h->chip = chip ? 1u : 0u;
    es_std_used[h->chip] += size;
    return (uint8_t *)(h + 1);
}

static __attribute__((unused)) void es_std_free(uint8_t *p)
{
    if (p == NULL) return;
    EsStdHdr *h = (EsStdHdr *)p - 1;
    es_std_used[h->chip] -= h->size;
    free(h);
}

static __attribute__((unused)) uint32_t es_std_avail(bool chip)
{
    uint32_t used = es_std_used[chip ? 1 : 0];
    return used >= ES_STD_POOL ? 0u : ES_STD_POOL - used;
}

/* Files. */
static __attribute__((unused)) int32_t es_std_file_size(const char *path)
{
    FILE *f = fopen(path, "rb");
    if (f == NULL) return -1;
    long n = -1;
    if (fseek(f, 0, SEEK_END) == 0) n = ftell(f);
    fclose(f);
    return n < 0 || n > 0x7FFFFFFFL ? -1 : (int32_t)n;
}

static __attribute__((unused)) int32_t es_std_file_read(const char *path, uint8_t *buffer, int32_t length)
{
    FILE *f = fopen(path, "rb");
    if (f == NULL) return -1;
    size_t got = fread(buffer, 1, (size_t)length, f);
    fclose(f);
    return (int32_t)got;
}

static __attribute__((unused)) bool es_std_file_write(const char *path, const uint8_t *data, int32_t length)
{
    FILE *f = fopen(path, "wb");
    if (f == NULL) return false;
    size_t put = length > 0 ? fwrite(data, 1, (size_t)length, f) : 0;
    return fclose(f) == 0 && put == (size_t)length;
}

static __attribute__((unused)) bool es_std_file_delete(const char *path) { return remove(path) == 0; }

/* The local time now, in seconds since 1 January 1978 (the Amiga's epoch), from the C library's calendar. */
static __attribute__((unused)) int64_t es_std_days(int64_t y, int64_t m, int64_t d)   /* days since 1970-01-01 of a civil date */
{
    y -= m <= 2;
    int64_t era = (y >= 0 ? y : y - 399) / 400;
    int64_t yoe = y - era * 400;
    int64_t doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
    int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    return era * 146097 + doe - 719468;
}

static __attribute__((unused)) uint32_t es_std_now(void)
{
    time_t t = time(NULL);
    struct tm *lt = localtime(&t);
    if (lt == NULL) return 0;
    int64_t days = es_std_days(lt->tm_year + 1900, lt->tm_mon + 1, lt->tm_mday) - es_std_days(1978, 1, 1);
    int64_t s = days * 86400 + lt->tm_hour * 3600 + lt->tm_min * 60 + lt->tm_sec;
    return s < 0 ? 0u : (uint32_t)s;
}

/* Debug trace: standard error. */
static __attribute__((unused)) void es_std_trace(const char *s) { if (s != NULL) fputs(s, stderr); }
static __attribute__((unused)) void es_std_trace_char(int32_t c) { fputc(c, stderr); }
static __attribute__((unused)) void es_std_trace_int(int32_t v) { fprintf(stderr, "%ld", (long)v); }

/* A US keyboard for Amiga raw key codes (the same table as the dotnet host). Returns an ISO-8859-1 character or 0. */
static __attribute__((unused)) int32_t es_std_keychar(int32_t raw, int32_t qualifier)
{
    static const char plain[] = "`1234567890-=\\\0" "0qwertyuiop[]\0" "123asdfghjkl;'\0\0" "456\0zxcvbnm,./\0" ".789 ";
    static const char shifted[] = "~!@#$%^&*()_+|\0" "0QWERTYUIOP{}\0" "123ASDFGHJKL:\"\0\0" "456\0ZXCVBNM<>?\0" ".789 ";
    if (raw < 0 || (raw & 0x80) != 0) return 0;
    if (raw == 0x41) return 8;
    if (raw == 0x42) return 9;
    if (raw == 0x43 || raw == 0x44) return 13;
    if (raw == 0x45) return 27;
    if (raw == 0x46) return 127;
    if (raw >= (int32_t)sizeof(plain) - 1) return 0;
    int shift = (qualifier & 3) != 0;
    int c = (unsigned char)(shift ? shifted[raw] : plain[raw]);
    if ((qualifier & 4) != 0 && !shift && c >= 'a' && c <= 'z') c -= 32;
    if ((qualifier & 8) != 0 && c >= 'a' - 32 && c <= 'z') return c & 31;
    return c;
}

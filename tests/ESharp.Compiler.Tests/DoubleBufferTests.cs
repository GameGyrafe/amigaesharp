using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// Video double buffering (issue #66): the library calls of the Amiga builds and the SDL runtime, and the Amiga code run natively against a shim of intuition/graphics/exec screen buffers that
/// checks the message protocol and which bitmap is on display.
/// </summary>
public class DoubleBufferTests
{
    private static readonly string Samples = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples");

    [Theory]
    [InlineData("m68k")]
    [InlineData("os4")]
    [InlineData("aros")]
    public void Dbuffer_sample_compiles_with_screen_buffers_and_the_offscreen_fallback(string target)
    {
        var options = Targets.Find(target)!.ToOptions();
        var r = EsCompiler.Compile(new[] { new SourceFile("DBuffer.cs", File.ReadAllText(Path.Combine(Samples, "dbuffer", "DBuffer.cs"))) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        foreach (var call in new[] { "AllocScreenBuffer(", "ChangeScreenBuffer(", "FreeScreenBuffer(", "InitRastPort(", "AllocBitMap(", "FreeBitMap(", "BltBitMap(", "WaitPort(" })
            Assert.Contains(call, r.C!);
    }

    [Fact]
    public void Dbuffer_sample_compiles_for_sdl_with_two_framebuffers()
    {
        var r = EsCompiler.Compile(new[] { new SourceFile("DBuffer.cs", File.ReadAllText(Path.Combine(Samples, "dbuffer", "DBuffer.cs"))) }, Targets.Sdl.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("es_sdl_dbuf_enable(", r.C!);
        Assert.Contains("es_sdl_flip(", r.C!);
        Assert.Contains("es_sdl_front", r.C!);
    }

    /// <summary>
    /// The Amiga Video code (64-bit layout, as AROS x86-64) built with the host gcc against a shim: screens, windows and bitmaps are
    /// byte arrays, ChangeScreenBuffer puts the shown bitmap in place and queues one message at each DBufInfo reply port it finds at the
    /// measured offsets, and WaitPort on an empty port stops the program (a real one would hang). The shim prints what it was asked.
/// (The host fake and the sample on the host are tested in <see cref="HostTargetTests"/>, which owns the shared Video state.)
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Amiga_double_buffer_runs_natively_against_a_screen_buffer_shim(bool messages, bool stuck)
    {
        if (!HasGcc) return;
        // stuck: the system never switches back to the screen's own bitmap, so the second one must not be freed while on display.
        const string stuckBody = @"
Video.Open(32, 16, 2);
Video.EnableDoubleBuffer();
Video.FillRect(0, 0, 32, 16, 2);
Video.Flip();
Video.DisableDoubleBuffer();
Dos.PutStr(""off "" + (int)Video.Buffering() + ""\n"");
Video.Close();
Dos.PutStr(""closed\n"");";
        const string normalBody = @"
Video.Open(32, 16, 2);
Video.FillRect(0, 0, 32, 16, 1);
bool on = Video.EnableDoubleBuffer();
Dos.PutStr(""on "" + (on ? 1 : 0) + "" "" + (int)Video.Buffering() + ""\n"");
Video.FillRect(-4, -4, 100, 100, 2);
Dos.PutStr(""back "" + Video.GetPixel(0, 0) + ""\n"");
Video.Flip();
Dos.PutStr(""after flip "" + Video.GetPixel(31, 15) + ""\n"");
Video.FillRect(0, 0, 32, 16, 3);
Video.Flip();
Video.FillRect(0, 0, 32, 16, 1);
Video.Flip();
Video.DisableDoubleBuffer();
Dos.PutStr(""off "" + Video.GetPixel(5, 5) + "" "" + Frame.Count + ""\n"");
Video.EnableBackBuffer();
Dos.PutStr(""copy "" + (int)Video.Buffering() + "" "" + Video.GetPixel(5, 5) + ""\n"");
Video.FillRect(0, 0, 32, 16, 2);
Video.Flip();
Dos.PutStr(""copied "" + Video.GetPixel(5, 5) + ""\n"");
Video.Close();
Dos.PutStr(""closed\n"");";
        string body = stuck ? stuckBody : normalBody;
        var c = CompileOk(Program("", body), new CompileOptions { Defines = new[] { "AMIGA", "X86_64", "ESHARP_HOSTC" } });
        string shim = @"#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
struct Library; struct Device;
#define SHIM_MESSAGES " + (messages ? 1 : 0) + @"
#define SHIM_STUCK " + (stuck ? 1 : 0) + @"
typedef struct { int id; int w, h; uint8_t px[64 * 64]; } ShimBm;
typedef struct { int pending; } ShimPort;
static int shim_ids;
static ShimBm *shim_bm0, *shim_shown;
static unsigned char shim_screen[512], shim_window[512], shim_winrp[512];
static ShimBm *shim_newbm(int w, int h) { ShimBm *b = calloc(1, sizeof(ShimBm)); b->id = shim_ids++; b->w = w; b->h = h; return b; }
static ShimBm *shim_rpbm(void *rp) { return *(ShimBm **)((unsigned char *)rp + 8); }
static inline void *OpenLibrary(const char *n, uint32_t v) { (void)n; (void)v; return NULL; }
static inline void CloseLibrary(void *l) { (void)l; }
static inline int OpenDevice(const char *n, uint32_t u, void *io, uint32_t f) { (void)n; (void)u; (void)io; (void)f; return -1; }
static inline void CloseDevice(void *io) { (void)io; }
static inline int DoIO(void *io) { (void)io; return 0; }
static inline uint32_t ReadEClock(void *d) { (void)d; return 0; }
static inline void Delay(int32_t t) { (void)t; }
static inline uint32_t SetChipRev(uint32_t w) { (void)w; return 0; }
static inline uint32_t BestCModeIDTagList(void *t) { (void)t; return 0xFFFFFFFFu; }
static inline uint32_t IsCyberModeID(uint32_t id) { (void)id; return 0; }
static inline void *CreateMsgPort(void) { return calloc(1, sizeof(ShimPort)); }
static inline void DeleteMsgPort(void *p) { if (((ShimPort *)p)->pending) printf(""shim: port deleted with %d messages\n"", ((ShimPort *)p)->pending); free(p); }
static inline void *GetMsg(void *p) { static int msg; ShimPort *s = p; if (s->pending == 0) return NULL; s->pending--; return &msg; }
static inline void *WaitPort(void *p) { if (((ShimPort *)p)->pending == 0) { printf(""shim: WaitPort would hang\n""); exit(3); } return p; }
static inline void *OpenScreenTagList(void *ns, void *tags) { (void)ns; (void)tags; shim_bm0 = shim_newbm(32, 16); shim_shown = shim_bm0; return shim_screen; }
static inline void *OpenWindowTagList(void *nw, void *tags) { (void)nw; (void)tags; *(void **)(shim_winrp + 8) = shim_bm0; *(void **)(shim_window + 80) = shim_winrp; return shim_window; }
static inline void CloseWindow(void *w) { (void)w; }
static inline int CloseScreen(void *s) { (void)s; printf(""shim: close, bitmap %d on display\n"", shim_shown->id); return 1; }
static inline void SetRGB4(void *vp, int i, int r, int g, int b) { (void)vp; (void)i; (void)r; (void)g; (void)b; }
static inline void SetAPen(void *rp, int pen) { *(int *)((unsigned char *)rp + 160) = pen; }
static inline void RectFill(void *rp, int x0, int y0, int x1, int y1)
{
    ShimBm *b = shim_rpbm(rp);
    if (x0 < 0 || y0 < 0 || x1 >= b->w || y1 >= b->h) { printf(""shim: RectFill outside the bitmap\n""); exit(4); }
    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) b->px[y * 64 + x] = (uint8_t)*(int *)((unsigned char *)rp + 160);
}
static inline int ReadPixel(void *rp, int x, int y) { ShimBm *b = shim_rpbm(rp); return b->px[y * 64 + x]; }
static inline void WaitTOF(void) { }
static inline void WaitBlit(void) { }
static inline void InitRastPort(void *rp) { memset(rp, 0, 176); }
static inline void *AllocBitMap(uint32_t w, uint32_t h, uint32_t d, uint32_t f, void *fr) { (void)d; (void)f; (void)fr; return shim_newbm((int)w, (int)h); }
static inline void FreeBitMap(void *b) { printf(""shim: free bitmap %d\n"", ((ShimBm *)b)->id); free(b); }
static inline int BltBitMap(void *s, int xs, int ys, void *d, int xd, int yd, int w, int h, uint32_t m, uint32_t mask, void *t)
{
    (void)m; (void)mask; (void)t;
    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) ((ShimBm *)d)->px[(yd + y) * 64 + xd + x] = ((ShimBm *)s)->px[(ys + y) * 64 + xs + x];
    return 2;
}
static inline void BltBitMapRastPort(void *s, int xs, int ys, void *rp, int xd, int yd, int w, int h, int m) { BltBitMap(s, xs, ys, shim_rpbm(rp), xd, yd, w, h, (uint32_t)m, 0xFF, NULL); }
static inline void *AllocScreenBuffer(void *sc, void *bm, uint32_t flags)
{
    (void)sc; (void)bm;
    unsigned char *sb = calloc(1, 16);
    ShimBm *b = shim_bm0;
    if (flags == 2) { b = shim_newbm(32, 16); memcpy(b->px, shim_bm0->px, sizeof b->px); }
    *(void **)sb = b;
    *(void **)(sb + 8) = calloc(1, 256);
    return sb;
}
static inline void FreeScreenBuffer(void *sc, void *sb)
{
    (void)sc;
    ShimBm *b = *(ShimBm **)sb;
    if (b == shim_shown && b != shim_bm0) printf(""shim: freed the bitmap on display\n"");
    printf(""shim: free buffer of bitmap %d\n"", b->id);
    if (b != shim_bm0) free(b);
    free(*(void **)((unsigned char *)sb + 8));
    free(sb);
}
static inline uint32_t ChangeScreenBuffer(void *sc, void *sb)
{
    (void)sc;
    unsigned char *dbi = *(unsigned char **)((unsigned char *)sb + 8);
    if (SHIM_STUCK && *(ShimBm **)sb == shim_bm0 && shim_shown != shim_bm0) return 0;
    shim_shown = *(ShimBm **)sb;
    printf(""shim: show bitmap %d (%d)\n"", shim_shown->id, shim_shown->px[0]);
    if (SHIM_MESSAGES)
    {
        ShimPort *safe = *(ShimPort **)(dbi + 48), *disp = *(ShimPort **)(dbi + 120);
        if (safe == NULL || disp == NULL) { printf(""shim: no reply port\n""); exit(5); }
        safe->pending++;
        disp->pending++;
    }
    return 1;
}
";
        var headers = new System.Collections.Generic.Dictionary<string, string>
        {
            ["proto/intuition.h"] = shim,
            ["proto/graphics.h"] = "",
            ["proto/cybergraphics.h"] = "",
            ["proto/keymap.h"] = "",
            ["proto/timer.h"] = "",
        };
        var (code, output) = RunWithHostGcc(c, headers);
        Assert.True(code == 0, output);
        if (stuck)
        {
            Assert.Equal(
                "shim: show bitmap 1 (2)\n" +
                "off 0\n" +                                              // no \"freed the bitmap on display\", no free buffer lines: leaked
                "shim: close, bitmap 1 on display\n" +
                "closed\n", output);
            return;
        }
        var expected =
            "on 1 1\n" +
            "back 2\n" +                                              // drawing went to the second bitmap, clipped to it
            "shim: show bitmap 1 (2)\n" +
            "after flip 1\n" +                                        // the screen's own bitmap is the back buffer now
            "shim: show bitmap 0 (3)\n" +
            "shim: show bitmap 1 (1)\n" +
            "shim: show bitmap 0 (1)\n" +                             // turning it off copies the shown frame back and shows the screen's own bitmap
            "shim: free buffer of bitmap 1\n" +
            "shim: free buffer of bitmap 0\n" +
            $"off 1 {(messages ? 3 : 4)}\n" +                       // three flips (and without messages a frame waited when turning it off)
            "copy 2 1\n" +
            "copied 2\n" +
            "shim: free bitmap 2\n" +
            "shim: close, bitmap 0 on display\n" +
            "closed\n";
        Assert.Equal(expected, output);
    }
}

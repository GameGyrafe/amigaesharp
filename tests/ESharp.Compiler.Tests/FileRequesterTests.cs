using ESharp.Compiler;
using ESharp.Target.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// FileRequester (issue #155 step 3): the Amiga build of the platform code run with host gcc against a fake asl.library (the tags it
/// gets, the strings it hands back, drawer and file joined with AddPart, copied out before the requester is freed), the sdl build, and the
/// host fake. The emulator runs (Kickstart 3.1, hosted AROS, the OS4 VM) and the SDL run in the image are recorded in docs/STATUS.md.
/// </summary>
public class FileRequesterTests
{
    private static CompileOptions Defines(params string[] d) => new() { Defines = d };

    private static readonly CompileOptions HostC = Defines("AMIGA", "X86_64", "AROS", "ESHARP_HOSTC");

    /// <summary>
    /// A fake asl.library: AslRequest prints the tags it gets (title, drawer, file, pattern, patterns on, save mode, window) and answers with the
    /// next scripted result; the strings it leaves in the requester are freed and overwritten by FreeAslRequest, so a path that was not copied is garbage.
    /// </summary>
    private const string AslShim = @"#pragma once
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
static int shim_calls, shim_frees;
static inline __attribute__((unused)) void *AllocAslRequest(uint32_t type, void *tags)
{
    (void)tags;
    if (type != 0) return NULL;
    unsigned char *r = calloc(1, 64);
    return r;
}
static inline __attribute__((unused)) void FreeAslRequest(void *req)
{
    unsigned char *r = req;
    char *file = *(char **)(r + 8), *drawer = *(char **)(r + 16);
    if (file != NULL) memset(file, '#', strlen(file));
    if (drawer != NULL) memset(drawer, '#', strlen(drawer));
    free(file);
    free(drawer);
    free(r);
    shim_frees++;
}
static inline __attribute__((unused)) int32_t AslRequest(void *req, void *tags)
{
    unsigned char *r = req;
    uintptr_t *t = tags;
    const char *title = """", *drawer = """", *file = """", *pattern = """";
    int patterns = 0, save = 0, window = 0;
    for (; t[0] != 0; t += 2)
    {
        switch ((uint32_t)t[0])
        {
            case 0x80080001u: title = (const char *)t[1]; break;
            case 0x80080009u: drawer = (const char *)t[1]; break;
            case 0x80080008u: file = (const char *)t[1]; break;
            case 0x8008000Au: pattern = (const char *)t[1]; break;
            case 0x8008002Eu: patterns = (int)t[1]; break;
            case 0x8008002Cu: save = (int)t[1]; break;
            case 0x80080002u: window = 1; break;
            default: printf(""[unknown tag %x]\n"", (unsigned)t[0]);
        }
    }
    printf(""[title '%s' drawer '%s' file '%s' pattern '%s' patterns %d save %d window %d]\n"", title, drawer, file, pattern, patterns, save, window);
    static const char *answers[][2] = { { ""RAM:"", ""a.txt"" }, { ""Work:Stuff/"", """" }, { ""Work:Stuff"", ""b.png"" }, { ""RAM:x/"", ""c"" }, { ""RAM:"", ""x"" }, { ""RAM:"", ""x"" } };
    static const int ok[] = { 1, 1, 1, 0, 1, 1 };
    int n = shim_calls++;
    if (!ok[n]) return 0;
    if (n == 4) { char *l = malloc(301); memset(l, 'd', 300); l[300] = 0; *(char **)(r + 16) = l; }        /* drawer too long */
    else if (n == 5) { char *l = malloc(252); memset(l, 'd', 251); l[251] = 0; *(char **)(r + 16) = l; }   /* drawer fits, the file does not */
    else *(char **)(r + 16) = strdup(answers[n][0]);
    *(char **)(r + 8) = n == 5 ? strdup(""abcdefgh"") : strdup(answers[n][1]);
    return 1;
}
";

    /// <summary>
    /// What the rest of the platform code in the same program (Video's double buffering, Frame's timer) needs to link: the Amiga's exec and
    /// intuition calls as stubs that do nothing. OpenLibrary hands out a dummy base for asl.library and fails for everything else.
    /// </summary>
    private const string Stubs = @"#pragma once
#include <stdint.h>
#include <stdlib.h>
struct Library; struct Device;
static char shim_aslbase;
static inline __attribute__((unused)) void *OpenLibrary(const char *n, uint32_t v) { (void)v; return n[0] == 'a' ? (void *)&shim_aslbase : NULL; }
static inline __attribute__((unused)) void CloseLibrary(void *l) { (void)l; }
static inline __attribute__((unused)) int OpenDevice(const char *n, uint32_t u, void *io, uint32_t f) { (void)n; (void)u; (void)io; (void)f; return -1; }
static inline __attribute__((unused)) void CloseDevice(void *io) { (void)io; }
static inline __attribute__((unused)) int DoIO(void *io) { (void)io; return 0; }
static inline __attribute__((unused)) uint32_t ReadEClock(void *d) { (void)d; return 0; }
static inline __attribute__((unused)) void *CreateMsgPort(void) { return calloc(1, 16); }
static inline __attribute__((unused)) void DeleteMsgPort(void *p) { free(p); }
static inline __attribute__((unused)) void *GetMsg(void *p) { (void)p; return NULL; }
static inline __attribute__((unused)) void *WaitPort(void *p) { return p; }
static inline __attribute__((unused)) void WaitTOF(void) { }
static inline __attribute__((unused)) void FreeScreenBuffer(void *sc, void *sb) { (void)sc; (void)sb; }
static inline __attribute__((unused)) void Delay(int32_t t) { (void)t; }
static inline __attribute__((unused)) void WaitBlit(void) { }
static inline __attribute__((unused)) void FreeBitMap(void *b) { (void)b; }
static inline __attribute__((unused)) int BltBitMap(void *s, int xs, int ys, void *d, int xd, int yd, int w, int h, uint32_t m, uint32_t mask, void *t)
{ (void)s; (void)xs; (void)ys; (void)d; (void)xd; (void)yd; (void)w; (void)h; (void)m; (void)mask; (void)t; return 0; }
static inline __attribute__((unused)) uint32_t ChangeScreenBuffer(void *sc, void *sb) { (void)sc; (void)sb; return 1; }
";

    private static readonly System.Collections.Generic.Dictionary<string, string> Shims = new()
    {
        ["proto/asl.h"] = AslShim, ["proto/intuition.h"] = Stubs, ["proto/graphics.h"] = "", ["proto/cybergraphics.h"] = "", ["proto/keymap.h"] = "", ["proto/timer.h"] = "",
    };

    [Fact]
    public void Amiga_build_passes_the_tags_joins_the_path_with_AddPart_and_copies_it_out_before_freeing()
    {
        var c = CompileOk(Program("", @"
string? a = FileRequester.Open(""Pick"", ""RAM:"", ""x.txt"", ""#?.txt"");
Dos.PutStr(a == null ? ""null\n"" : $""{a}\n"");
string? b = FileRequester.Save(""Save"", ""Work:"", """", """");
Dos.PutStr(b == null ? ""null\n"" : $""{b}\n"");
string? d = FileRequester.Open(""Again"", """", """", ""#?.(png|jpg)"");
Dos.PutStr(d == null ? ""null\n"" : $""{d}\n"");
string? e = FileRequester.Open(""Cancel"", ""RAM:x"", ""c"", """");
Dos.PutStr(e == null ? ""null\n"" : $""{e}\n"");
string? g = FileRequester.Open(""Long"", """", """", """");
Dos.PutStr(g == null ? ""null\n"" : $""{g.Length}\n"");
string? h = FileRequester.Open(""Long2"", """", """", """");
Dos.PutStr(h == null ? ""null\n"" : $""{h.Length}\n"");"), HostC);
        Assert.Contains("AllocAslRequest(", c);
        Assert.Contains("AddPart(", c);
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c, Shims);
        Assert.True(code == 0, output);
        Assert.Equal(
            "[title 'Pick' drawer 'RAM:' file 'x.txt' pattern '#?.txt' patterns 1 save 0 window 0]\nRAM:a.txt\n" +       // no '/' after a volume
            "[title 'Save' drawer 'Work:' file '' pattern '' patterns 0 save 1 window 0]\nnull\n" +                          // no file name: null, not the drawer
            "[title 'Again' drawer '' file '' pattern '#?.(png|jpg)' patterns 1 save 0 window 0]\nWork:Stuff/b.png\n" +    // '/' added in a drawer
            "[title 'Cancel' drawer 'RAM:x' file 'c' pattern '' patterns 0 save 0 window 0]\nnull\n" +                    // cancel
            "[title 'Long' drawer '' file '' pattern '' patterns 0 save 0 window 0]\nnull\n" +                            // drawer longer than the buffer
            "[title 'Long2' drawer '' file '' pattern '' patterns 0 save 0 window 0]\nnull\n",                            // AddPart does not fit
            output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Asl_PickFile_still_takes_title_and_drawer_only()
    {
        var c = CompileOk(Program("", "string? a = Asl.PickFile(\"T\", \"RAM:\");"), HostC);
        Assert.Contains("AslRequest(", c);
    }

    [Fact]
    public void Sdl_build_uses_SDL_ShowFileDialogWithProperties_through_the_C_runtime()
    {
        var c = CompileOk(Program("", "string? a = FileRequester.Open(\"T\", \"/tmp\", \"\", \"#?.png\"); string? b = FileRequester.Save(\"T\", \"\", \"x\", \"\");"), Targets.Sdl.ToOptions());
        Assert.Contains("es_sdl_file_dialog(", c);
        Assert.Contains("SDL_ShowFileDialogWithProperties", c);
        Assert.Contains("SDL_FILEDIALOG_SAVEFILE", c);
        Assert.DoesNotContain("AslRequest(", c);
    }

    [Fact]
    public void FileRequester_is_on_every_Amiga_target_and_Asl_stays_Amiga_only()
    {
        foreach (var t in Targets.All.Where(t => t.IsAmiga))
            Assert.Contains("AslRequest(", CompileOk(Program("", "string? a = FileRequester.Save(\"T\", \"RAM:\", \"f\", \"#?\");"), t.ToOptions()));
        var r = Compile(Program("", "string? a = Asl.PickFile(\"T\", \"/tmp\");"), Targets.Sdl.ToOptions());
        Assert.Contains("ES0102", ErrorIds(r));
    }

    [Fact]
    public void Host_fake_answers_the_preset_path_or_a_cancel_and_records_the_call()
    {
        ESharp.Platform.FileRequester.HostPick = null;
        Assert.Null(ESharp.Platform.FileRequester.Open("T", "RAM:", "f", "#?.txt"));
        Assert.Equal("T", ESharp.Platform.FileRequester.HostLastTitle);
        Assert.Equal("#?.txt", ESharp.Platform.FileRequester.HostLastPattern);
        Assert.False(ESharp.Platform.FileRequester.HostLastSave);
        ESharp.Platform.FileRequester.HostPick = "RAM:picked.txt";
        Assert.Equal("RAM:picked.txt", ESharp.Platform.FileRequester.Save("S", "RAM:", "", ""));
        Assert.True(ESharp.Platform.FileRequester.HostLastSave);
        Assert.Equal("RAM:", ESharp.Platform.FileRequester.HostLastDrawer);
        ESharp.Platform.FileRequester.HostPick = null;
    }

    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "filereq", "FileReq.cs");

    [Fact]
    public void FileReq_sample_builds_for_every_target()
    {
        var src = File.ReadAllText(SamplePath);
        foreach (var t in Targets.All)
        {
            var r = EsCompiler.Compile(new[] { new SourceFile(SamplePath, src) }, t.ToOptions());
            Assert.True(r.Success, t.Name + ": " + string.Join("\n", r.Diagnostics));
        }
    }

    [Fact]
    public void FileReq_sample_runs_on_dotnet_with_the_preset_answer()
    {
        var compilation = CSharpCompilation.Create("filereq", new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(SamplePath)) }, PlatformSubclassTests.HostReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var asm = System.Reflection.Assembly.Load(ms.ToArray());
        var main = asm.GetType("FileReqSample.Program")!.GetMethod("Main")!;
        try
        {
            ESharp.Platform.Args.HostSet("EXPECT=RAM:filereq-hello.txt", "SAVE");
            using (var console = HostConsole.Capture())
            {
                main.Invoke(null, null);
                Assert.Equal("SHOT\nresult: picked RAM:filereq-hello.txt\nok the expected path was picked\n", console.Output);
            }
            Assert.True(ESharp.Platform.FileRequester.HostLastSave);
            Assert.Equal("RAM:", ESharp.Platform.FileRequester.HostLastDrawer);
            ESharp.Platform.Args.HostSet();
            using (var console = HostConsole.Capture())
            {
                main.Invoke(null, null);
                Assert.Equal("SHOT\nresult: cancelled\nok cancelled, as expected\n", console.Output);
            }
            Assert.False(ESharp.Platform.FileRequester.HostLastSave);
        }
        finally { ESharp.Platform.Args.HostReset(); ESharp.Platform.FileRequester.HostPick = null; }
    }
}

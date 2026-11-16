using System.Collections.Generic;
using ESharp.Target.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// BOOPSI core (issue #40, part 1): [HookEntry] methods and their per-target entries, the Hook class (a C# delegate as a struct
/// Hook), BoopsiObject / BoopsiWindow ownership against a fake intuition.library, and samples/boopsi.
/// </summary>
public class BoopsiTests
{
    private static CompileOptions Defines(params string[] d) => new() { Defines = d };

    private static readonly CompileOptions M68k = Defines("AMIGA", "M68K", "AMIGAOS3");

    // AROS x86-64 calls hooks as plain C functions (hook, object, message), and so does the host gcc of these tests.
    private static readonly CompileOptions HostC = Defines("AMIGA", "X86_64", "AROS", "ESHARP_HOSTC");

    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "boopsi", "Boopsi.cs");

    private const string HookMembers = @"
static int Calls;
[HookEntry] static nuint Count(void* hook, void* obj, void* msg) { Calls++; return (nuint)(*(int*)msg + (obj == null ? 0 : 100)); }
[HookEntry] static void Quiet(void* hook, nint obj, nuint msg) { Calls += 10; }
[HookEntry] static int Minus(void* hook, void* obj, void* msg) { return -1; }";

    private const string HookMain = "delegate*<void*, void*, void*, nuint> c = &Count; delegate*<void*, nint, nuint, void> q = &Quiet; delegate*<void*, void*, void*, int> m = &Minus; Dos.PutStr(c != null && q != null && m != null ? \"entries\\n\" : \"none\\n\");";

    private static string HookProgram() => Prelude + $@"
public static unsafe class P
{{
    [EntryPoint]
    public static void Main() {{ {HookMain} }}
    {HookMembers}
}}";

    [Fact]
    public void HookEntry_on_m68k_gets_a_register_entry_that_passes_a0_a2_a1()
    {
        var c = CompileOk(HookProgram(), M68k);
        Assert.Contains("uintptr_t Es_P_Count_hook(void *es_hook, void *es_obj, void *es_msg)", c);
        Assert.Contains("#define Es_P_Count_HOOK ((void *)Es_P_Count_hentry)", c);
        Assert.Contains("void *c = Es_P_Count_HOOK;", c);
        // a1 (message) pushed first, then a2 (object), then a0 (hook): the C function sees (hook, object, message).
        Assert.Contains("_Es_P_Count_hentry:\\n\\tmove.l a1,-(sp)\\n\\tmove.l a2,-(sp)\\n\\tmove.l a0,-(sp)\\n\\tjsr _Es_P_Count_hook\\n\\tlea 12(sp),sp\\n\\trts", c);
        // MorphOS: a 68k-callable gate reading the same registers from the emulation handle.
        Assert.Contains("#define Es_P_Count_HOOK ((void *)&Es_P_Count_gate)", c);
        Assert.Contains("return Es_P_Count_hook((void *)REG_A0, (void *)REG_A2, (void *)REG_A1);", c);
        Assert.Contains("const struct EmulLibEntry Es_P_Count_gate = { TRAP_LIB, 0, (void (*)(void))Es_P_Count_gate_fn };", c);
        Assert.Contains("#include <emul/emulregs.h>", c);
    }

    [Fact]
    public void HookEntry_functions_are_called_as_C_with_hook_object_message()
    {
        if (!HasGcc) return;
        var c = CompileOk(HookProgram(), HostC);
        Assert.Contains("#define Es_P_Count_HOOK ((void *)Es_P_Count_hook)", c);
        // Call the entries through their h_Entry addresses as AROS (and AmigaOS 4) does: plain C, hook first.
        c = c.Replace("int main(void)", "static int es_real_main(void)") + @"
int main(void)
{
    int msg = 5, obj = 0;
    uintptr_t (*entry)(void *, void *, void *) = (uintptr_t (*)(void *, void *, void *))Es_P_Count_HOOK;
    uintptr_t r1 = entry(NULL, &obj, &msg);
    uintptr_t r2 = entry(NULL, NULL, &msg);
    uintptr_t r3 = ((uintptr_t (*)(void *, void *, void *))Es_P_Quiet_HOOK)(NULL, NULL, NULL);
    intptr_t r4 = (intptr_t)((uintptr_t (*)(void *, void *, void *))Es_P_Minus_HOOK)(NULL, NULL, NULL);
    printf(""%u %u %u %d %d\n"", (unsigned)r1, (unsigned)r2, (unsigned)r3, (int)r4, (int)Es_P_Calls);
    return es_real_main();
}
";
        var (code, output) = RunWithHostGcc(c);
        Assert.True(code == 0, output);
        Assert.Equal("105 5 0 -1 12\nentries\n", output);   // a void hook returns 0; int results are sign-extended
    }

    [Theory]
    [InlineData("[HookEntry] static nuint F(void* h, void* o, void* m) { return 0; }", "F(null, null, null);", "SDL", "sdl")]
    [InlineData("[HookEntry] static nuint F(void* h, void* o) { return 0; }", "F(null, null);", "AMIGA,M68K", "exactly three")]
    [InlineData("[HookEntry] static nuint F(void* h, void* o, int m) { return 0; }", "F(null, null, 1);", "AMIGA,M68K", "exactly three")]
    [InlineData("[HookEntry] static nuint F(void* h, void* o, ref nuint m) { return 0; }", "nuint x = 0; F(null, null, ref x);", "AMIGA,M68K", "exactly three")]
    [InlineData("[HookEntry] static long F(void* h, void* o, void* m) { return 0; }", "F(null, null, null);", "AMIGA,M68K", "result")]
    [InlineData("} public unsafe class Q { [HookEntry] public nuint F(void* h, void* o, void* m) { return 0; }", "new Q().F(null, null, null);", "AMIGA,M68K", "static")]
    [InlineData("[HookEntry, Interrupt] static void F(void* h, void* o, void* m) { }", "F(null, null, null);", "AMIGA,M68K", "[Interrupt]")]
    [InlineData("[HookEntry, InlineAsm(\"moveq #0,%[result]\")] static uint F(void* h, void* o, void* m) { return 0; }", "F(null, null, null);", "AMIGA,M68K", "[InlineAsm] together with [HookEntry]")]
    [InlineData("[HookEntry] static extern nuint F(void* h, void* o, void* m);", "F(null, null, null);", "AMIGA,M68K", "without a body")]
    [InlineData("static void G() { }", "delegate*<void> f = &G;", "AMIGA,M68K", "only [Interrupt] and [HookEntry]")]
    public void Bad_HookEntry_methods_are_ES0099(string member, string use, string defines, string message)
    {
        var src = Prelude + "public static unsafe class P { [EntryPoint] public static void Main() { " + use + " } " + member + " }";
        var r = Compile(src, Defines(defines.Split(',')));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.GetMessage().Contains(message));
    }

    [Fact]
    public void Boopsi_classes_on_sdl_are_Amiga_only()
    {
        var r = Compile(Program("", "var w = new BoopsiWindow(); var o = new BoopsiObject(\"propgclass\");"), Targets.Sdl.ToOptions());
        Assert.False(r.Success);
        var messages = r.Diagnostics.Where(d => d.Id == "ES0102").Select(d => d.GetMessage()).ToList();
        Assert.Contains(messages, m => m.Contains("'BoopsiWindow'") && m.Contains("'sdl'"));
        Assert.Contains(messages, m => m.Contains("'BoopsiObject'"));
    }

    [Fact]
    public void Overloads_with_nullable_string_parameters_get_C_names()
    {
        // string? is part of no C name ('?' is not an identifier character): it mangles like string.
        var c = CompileOk(Program(@"
static int F(string? s) { return s == null ? 0 : 1; }
static int F(int i) { return i; }", "Dos.PutStr(F((string?)null) == 0 && F(\"x\") == 1 && F(3) == 3 ? \"ok\\n\" : \"bad\\n\");"));
        Assert.Contains("Es_P_F_string(", c);
        Assert.DoesNotContain("string?", c.Replace("/*", "").Split('\n').Where(l => !l.TrimStart().StartsWith("#line")).Aggregate("", (a, l) => a + l));
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.True(code == 0, output);
        Assert.Equal("ok\n", output);
    }

    // ------------------------------------------------------------------ a fake intuition.library for the platform classes

    /// <summary>Objects with an attribute store, lifecycle lines on stdout, CallHookPkt through h_Entry as AROS calls it, DoMethodA for OM_GET.</summary>
    private const string IntuitionShim = @"#pragma once
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
typedef void Object;
typedef void *Msg;
struct Library;
typedef struct { unsigned char head[128]; char cls[24]; int n; uintptr_t tag[32], val[32]; } ShimObj;
static int shim_live;
static void shim_set(ShimObj *o, const uintptr_t *t)
{
    for (; t != NULL && t[0] != 0; t += 2)
    {
        int i;
        for (i = 0; i < o->n; i++) if (o->tag[i] == t[0]) break;
        if (i == o->n) { if (o->n == 32) return; o->n++; }
        o->tag[i] = t[0];
        o->val[i] = t[1];
    }
}
static inline void *NewObjectA(void *cl, const void *id, void *tags)
{
    static const char *known[] = { ""propgclass"", ""strgclass"", ""frbuttonclass"", ""buttongclass"", ""frameiclass"", NULL };
    (void)cl;
    int k = 0;
    while (known[k] != NULL && strcmp(known[k], (const char *)id) != 0) k++;
    if (known[k] == NULL) return NULL;
    ShimObj *o = calloc(1, sizeof(ShimObj));
    strcpy(o->cls, known[k]);
    shim_set(o, tags);
    shim_live++;
    return o;
}
static inline void DisposeObject(void *o) { printf(""[dispose %s]\n"", ((ShimObj *)o)->cls); shim_live--; free(o); }
static inline uintptr_t SetAttrsA(void *o, void *t) { shim_set(o, t); return 0; }
static inline uintptr_t SetGadgetAttrsA(void *g, void *w, void *r, void *t) { (void)w; (void)r; shim_set(g, t); return 1; }
static inline uintptr_t GetAttr(uint32_t id, void *o, void *store)
{
    ShimObj *s = o;
    for (int i = 0; i < s->n; i++) if (s->tag[i] == id) { *(uintptr_t *)store = s->val[i]; return 1; }
    return 0;
}
static inline uint16_t AddGList(void *w, void *g, uint32_t p, int32_t n, void *r) { (void)w; (void)p; (void)n; (void)r; printf(""[add %s]\n"", ((ShimObj *)g)->cls); return 0; }
static inline uint16_t RemoveGList(void *w, void *g, int32_t n) { (void)w; (void)n; printf(""[remove %s]\n"", ((ShimObj *)g)->cls); return 0; }
static inline void RefreshGList(void *g, void *w, void *r, int32_t n) { (void)g; (void)w; (void)r; (void)n; }
static inline void DrawImage(void *rp, void *i, int32_t l, int32_t t) { (void)rp; (void)l; (void)t; printf(""[draw %s]\n"", ((ShimObj *)i)->cls); }
static unsigned char shim_screen[512], shim_drawinfo[64];
static inline void *LockPubScreen(const void *n) { (void)n; return shim_screen; }
static inline void UnlockPubScreen(const void *n, void *s) { (void)n; (void)s; printf(""[unlock screen]\n""); }
static inline void *GetScreenDrawInfo(void *s) { (void)s; return shim_drawinfo; }
static inline void FreeScreenDrawInfo(void *s, void *d) { (void)s; (void)d; printf(""[free drawinfo]\n""); }
static inline void *OpenWindowTagList(void *nw, void *t) { (void)nw; (void)t; printf(""[open window]\n""); return calloc(1, 512); }
static inline void CloseWindow(void *w) { printf(""[close window]\n""); free(w); }
/* Only reached by BoopsiWindow.Wait, which these programs do not call: no events here. */
static inline void *GetMsg(void *p) { (void)p; return NULL; }
static inline void *WaitPort(void *p) { (void)p; printf(""[WaitPort would hang]\n""); exit(3); }
static inline void ReplyMsg(void *m) { (void)m; }
static inline void *Output(void) { return NULL; }
static inline int32_t Flush(void *f) { (void)f; fflush(stdout); return 1; }
";

    private const string UtilityShim = @"#pragma once
#include <stdint.h>
static unsigned char shim_lib[64];
static inline void *OpenLibrary(const void *n, uint32_t v) { (void)n; (void)v; return shim_lib; }
static inline void CloseLibrary(void *l) { (void)l; }
/* As AROS's CallHookPkt: h_Entry (after the MinNode) called as C with the hook, the object and the message. */
static inline uintptr_t CallHookPkt(void *hook, void *obj, void *msg) { return ((uintptr_t (*)(void *, void *, void *))((void **)hook)[2])(hook, obj, msg); }
";

    private const string AlibShim = @"#pragma once
/* DoMethodA for the fake objects: OM_GET only (the method numbers are the same on every target). */
static inline uintptr_t DoMethodA(void *obj, void *msg)
{
    uintptr_t *m = msg;
    return m[0] == 260 ? GetAttr((uint32_t)m[1], obj, (void *)m[2]) : 0;
}
";

    private static Dictionary<string, string> Shims() => new()
    {
        ["proto/intuition.h"] = IntuitionShim,
        ["proto/utility.h"] = UtilityShim,
        ["clib/alib_protos.h"] = AlibShim,
    };

    private static (int, string) RunShimmed(string c, bool sanitize)
    {
        // RunSanitized has no header option: inline the shims in front of the generated C instead (they are #pragma once).
        if (sanitize)
        {
            var inlined = "#include <stdint.h>\n#include <stdio.h>\n" + IntuitionShim.Replace("#pragma once", "") + UtilityShim.Replace("#pragma once", "") + AlibShim.Replace("#pragma once", "") +
                          c.Replace("#include <proto/intuition.h>", "").Replace("#include <proto/utility.h>", "").Replace("#include <clib/alib_protos.h>", "");
            return RunSanitized(inlined);
        }
        return RunWithHostGcc(c, Shims());
    }

    [Fact]
    public void Hook_calls_a_capturing_lambda_through_its_entry_and_frees_the_closure()
    {
        if (!HasGcc) return;
        const string body = @"
int calls = 0;
int last = 0;
Hook h = new Hook((obj, msg) => { calls++; last = *(int*)msg; return (nuint)(last * 2); });
int m = 21;
nuint r = h.Call(null, &m);
m = 4;
nuint r2 = h.Call(null, &m);
Dos.PutStr($""{(int)r} {(int)r2} {calls} {last} {Heap.LiveClosures()}\n"");
h.Dispose();
Heap.Free(ref h);
Dos.PutStr($""{Heap.LiveClosures()}\n"");";
        var c = CompileOk(Program("", body).Replace("public static class P", "public static unsafe class P"), HostC);
        Assert.Contains("Es_ESharp_Platform_Hook_Dispatch_HOOK", c);
        var (code, output) = RunShimmed(c, HasSanitizers);
        Assert.True(code == 0, output);
        Assert.Equal("42 8 2 4 1\n0\n", output);
    }

    [Fact]
    public void Boopsi_sample_runs_against_a_fake_intuition_with_no_leak_and_the_right_order()
    {
        if (!HasGcc) return;
        var c = CompileOk(File.ReadAllText(SamplePath), HostC);
        var (code, output) = RunShimmed(c, HasSanitizers);
        Assert.True(code == 0, output);
        Assert.DoesNotContain("FAIL", output);
        Assert.Contains("boopsi: all ok\n", output);
        // Objects outside a window, and a kept hook: disposed by their owners.
        Assert.Contains("[dispose propgclass]\n[dispose strgclass]\nok the objects are disposed and freed", output);
        // The window shows images before the gadgets they surround, takes the gadgets out before it closes, and disposes after that:
        // the button before the frame it keeps.
        var close = output[output.IndexOf("[remove frbuttonclass]", StringComparison.Ordinal)..];
        Assert.StartsWith("[remove frbuttonclass]\n[remove propgclass]\n[remove strgclass]\n[close window]\n[free drawinfo]\n[unlock screen]\n" +
                          "[dispose frbuttonclass]\n[dispose frameiclass]\n[dispose propgclass]\n[dispose frameiclass]\n[dispose strgclass]\n", close);
        Assert.True(output.IndexOf("[draw frameiclass]", StringComparison.Ordinal) < output.IndexOf("[add strgclass]", StringComparison.Ordinal), output);
    }

    [Fact]
    public void Boopsi_sample_runs_the_same_on_dotnet()
    {
        var src = File.ReadAllText(SamplePath);
        var compilation = CSharpCompilation.Create("boopsi", new[] { CSharpSyntaxTree.ParseText(src) }, PlatformSubclassTests.HostReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var asm = System.Reflection.Assembly.Load(ms.ToArray());
        ESharp.Platform.Args.HostSet("WAIT");
        try
        {
            using var console = HostConsole.Capture();
            asm.GetType("BoopsiSample.Program")!.GetMethod("Main")!.Invoke(null, null);
            Assert.DoesNotContain("FAIL", console.Output);
            Assert.Contains("event: button clicked\nevent: slider at 70\nevent: text accepted: Amiga\n", console.Output);
            Assert.Contains("events: 1 click(s), 1 slider update(s), last position 70, 1 Return(s), 0 key(s) in the edit hook\n", console.Output);
            Assert.EndsWith("boopsi: all ok\n", console.Output);
        }
        finally { ESharp.Platform.Args.HostReset(); }
    }

    [Theory]
    [InlineData("m68k")]
    [InlineData("aros")]
    [InlineData("os4")]
    [InlineData("morphos")]
    public void Boopsi_sample_compiles_for_every_amiga_target(string target)
    {
        var r = EsCompiler.Compile(new[] { new SourceFile("Boopsi.cs", File.ReadAllText(SamplePath)) }, Targets.Find(target)!.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("NewObjectA(NULL, ", r.C!);
        Assert.Contains("Es_ESharp_Platform_Hook_Dispatch_HOOK", r.C!);
        Assert.Contains("static inline uintptr_t es_domethod(void *obj, void *msg) { return (uintptr_t)IDoMethodA((Object *)obj, (Msg)msg); }", r.C!);
        Assert.Contains("CALLHOOKPKT((struct Hook *)c, obj, msg)", r.C!);
    }
}

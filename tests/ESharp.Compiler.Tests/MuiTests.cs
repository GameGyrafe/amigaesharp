using System.Collections.Generic;
using ESharp.Target.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// MUI wrappers on the BOOPSI core (issue #40, part 2): the muimaster.library calls the emitter writes per target (no MUI header),
/// samples/mui on dotnet and, as generated C, against a fake muimaster.library under the sanitizers (each object disposed once, by
/// its parent; no leak), the overrides of library classes in their vtables, and setter properties in object initializers.
/// </summary>
public class MuiTests
{
    private static CompileOptions Defines(params string[] d) => new() { Defines = d };

    // The host gcc calls hooks as plain C functions (hook, object, message), as AROS does; its pointers are 64 bits.
    private static readonly CompileOptions HostC = Defines("AMIGA", "X86_64", "AROS", "ESHARP_HOSTC");

    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "mui", "Mui.cs");

    /// <summary>
    /// A fake muimaster.library (and the bits of intuition, utility and exec the wrappers call): objects with an attribute store, children
    /// (OM_ADDMEMBER, a window's root, a listview's list), MUIM_Notify with MUIM_CallHook run from SetAttrsA as MUI does, a string list,
    /// MUI_DisposeObject that disposes the children first. Text and string contents are copied, as MUI does. Lifecycle lines on stdout.
    /// </summary>
    private const string MuiShim = """
#pragma once
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
typedef struct MObj MObj;
struct MObj { char cls[24]; int n; uintptr_t tag[40], val[40]; MObj *kids[32]; int nkids; uintptr_t note[16][3]; int nnotes; char *items[16]; int nitems; };
static __attribute__((unused)) int shim_made, shim_disposed, shim_changes, shim_quit;
static __attribute__((unused)) int shim_is_text(uintptr_t t) { return t == 0x8042f8dcu || t == 0x80428ffdu; }
static __attribute__((unused)) MObj *shim_new(const char *cls, const uintptr_t *tags);
static __attribute__((unused)) void shim_store(MObj *o, uintptr_t t, uintptr_t v)
{
    int i;
    for (i = 0; i < o->n; i++) if (o->tag[i] == t) break;
    if (i == o->n) { if (o->n == 40) return; o->n++; o->tag[i] = t; o->val[i] = 0; }
    if (shim_is_text(t)) { free((char *)o->val[i]); v = v ? (uintptr_t)strdup((const char *)v) : 0; }
    o->val[i] = v;
    if (t == 0x8042cba5u || t == 0x8042bcceu) o->kids[o->nkids++] = (MObj *)v;   /* a window's root, a listview's list */
}
static __attribute__((unused)) void shim_fire(MObj *o, uintptr_t t, uintptr_t v)
{
    for (int k = 0; k < o->nnotes; k++)
        if (o->note[k][0] == t && (o->note[k][1] == 0x49893131u || o->note[k][1] == v))
        {
            uintptr_t parms[1] = { v };
            void **hook = (void **)o->note[k][2];
            ((uintptr_t (*)(void *, void *, void *))hook[2])(hook, o, parms);   /* h_Entry after the MinNode, as CallHookPkt */
        }
}
static __attribute__((unused)) void *MUI_NewObjectA(const void *cls, void *tags)
{
    static const char *known[] = { "Application.mui", "Window.mui", "Group.mui", "Text.mui", "String.mui", "Cycle.mui", "Slider.mui",
        "Gauge.mui", "Rectangle.mui", "List.mui", "Listview.mui", NULL };
    int k = 0;
    while (known[k] != NULL && strcmp(known[k], (const char *)cls) != 0) k++;
    return known[k] == NULL ? NULL : shim_new(known[k], tags);
}
static __attribute__((unused)) MObj *shim_new(const char *cls, const uintptr_t *tags)
{
    MObj *o = calloc(1, sizeof(MObj));
    snprintf(o->cls, sizeof o->cls, "%s", cls);
    for (; tags != NULL && tags[0] != 0; tags += 2) shim_store(o, tags[0], tags[1]);
    shim_made++;
    return o;
}
static __attribute__((unused)) void *MUI_MakeObjectA(int32_t type, uintptr_t *parms)
{
    char cls[24];
    snprintf(cls, sizeof cls, "Make%d", (int)type);
    MObj *o = shim_new(cls, NULL);
    if (type == 2 && parms[0] != 0) shim_store(o, 0x8042f8dcu, parms[0]);   /* a button shows its label */
    return o;
}
static __attribute__((unused)) void MUI_DisposeObject(void *obj)
{
    MObj *o = obj;
    for (int k = 0; k < o->nkids; k++) MUI_DisposeObject(o->kids[k]);
    for (int i = 0; i < o->n; i++) if (shim_is_text(o->tag[i])) free((char *)o->val[i]);
    for (int i = 0; i < o->nitems; i++) free(o->items[i]);
    printf("[dispose %s]\n", o->cls);
    shim_disposed++;
    free(o);
}
static __attribute__((unused)) int32_t MUI_RequestA(void *app, void *win, uint32_t flags, const void *title, const void *gadgets, const void *format, void *parms)
{ (void)app; (void)win; (void)flags; (void)title; (void)gadgets; (void)format; (void)parms; return 1; }
static __attribute__((unused)) int32_t MUI_Error(void) { return 0; }
static __attribute__((destructor)) void shim_report(void) { printf("[shim: %d made, %d disposed, %d group changes]\n", shim_made, shim_disposed, shim_changes); }
""";

    private const string IntuitionShim = """
#pragma once
#include <stdio.h>
#include <stdint.h>
typedef void Object;
typedef void *Msg;
struct Library;
static __attribute__((unused)) unsigned char shim_lib[64];
static inline __attribute__((unused)) void *OpenLibrary(const void *n, uint32_t v) { (void)v; return strcmp((const char *)n, "muimaster.library") == 0 && getenv("SHIM_NO_MUI") ? NULL : shim_lib; }
static inline __attribute__((unused)) void CloseLibrary(void *l) { (void)l; printf("[close library]\n"); }
static inline __attribute__((unused)) uint32_t Wait(uint32_t s) { return s; }
static inline __attribute__((unused)) void Delay(int32_t t) { (void)t; }
static inline __attribute__((unused)) void *Output(void) { return NULL; }
static inline __attribute__((unused)) int32_t Flush(void *f) { (void)f; fflush(stdout); return 1; }
static inline __attribute__((unused)) void *NewObjectA(void *cl, const void *id, void *tags) { (void)cl; (void)id; (void)tags; return NULL; }
static inline __attribute__((unused)) void DisposeObject(void *o) { (void)o; printf("[intuition DisposeObject of a MUI object]\n"); }
static inline __attribute__((unused)) uintptr_t SetAttrsA(void *obj, void *tags)
{
    MObj *o = obj;
    const uintptr_t *t = tags;
    for (; t != NULL && t[0] != 0; t += 2) shim_store(o, t[0], t[1]);
    for (t = tags; t != NULL && t[0] != 0; t += 2) shim_fire(o, t[0], t[1]);
    return 0;
}
static inline __attribute__((unused)) uintptr_t SetGadgetAttrsA(void *g, void *w, void *r, void *t) { (void)w; (void)r; return SetAttrsA(g, t); }
static inline __attribute__((unused)) void RefreshGList(void *g, void *w, void *r, int32_t n) { (void)g; (void)w; (void)r; (void)n; }
static inline __attribute__((unused)) uint16_t RemoveGList(void *w, void *g, int32_t n) { (void)w; (void)g; (void)n; return 0; }
static inline __attribute__((unused)) uintptr_t GetAttr(uint32_t id, void *obj, void *store)
{
    MObj *o = obj;
    if (id == 0x80421654u) { *(uintptr_t *)store = (uintptr_t)o->nitems; return 1; }
    for (int i = 0; i < o->n; i++) if (o->tag[i] == id) { *(uintptr_t *)store = o->val[i]; return 1; }
    return 0;
}
""";

    private const string AlibShim = """
#pragma once
/* DoMethodA for the fake MUI objects. */
static __attribute__((unused)) uintptr_t DoMethodA(void *obj, void *msg)
{
    MObj *o = obj;
    uintptr_t *m = msg;
    switch (m[0])
    {
        case 260: return GetAttr((uint32_t)m[1], obj, (void *)m[2]);
        case 0x109: o->kids[o->nkids++] = (MObj *)m[1]; return 0;
        case 0x10a:
            for (int k = 0; k < o->nkids; k++)
                if (o->kids[k] == (MObj *)m[1]) { o->kids[k] = o->kids[--o->nkids]; break; }
            return 0;
        case 0x8042c9cbu:
            if (m[5] != 0x8042b96bu || o->nnotes == 16) return 0;
            o->note[o->nnotes][0] = m[1]; o->note[o->nnotes][1] = m[2]; o->note[o->nnotes][2] = m[6]; o->nnotes++;
            return 0;
        case 0x80423ba6u: *(uint32_t *)m[1] = 0; return shim_quit ? 0xffffffffu : 0;
        case 0x804276efu: shim_quit = 1; return 0;
        case 0x804254d5u: if (o->nitems < 16) o->items[o->nitems++] = strdup((const char *)m[1]); return 0;
        case 0x8042ad89u: for (int i = 0; i < o->nitems; i++) free(o->items[i]); o->nitems = 0; return 0;
        case 0x804280ecu: { intptr_t i = (intptr_t)m[1]; *(void **)m[2] = i >= 0 && i < o->nitems ? o->items[i] : NULL; return 0; }
        case 0x80420887u: case 0x8042d1ccu: shim_changes++; return 0;
    }
    return 0;
}
""";

    private static Dictionary<string, string> Shims() => new()
    {
        ["es_muimaster_shim.h"] = MuiShim,
        ["proto/intuition.h"] = "#pragma once\n#include <es_muimaster_shim.h>\n" + IntuitionShim,
        ["proto/utility.h"] = "#pragma once\n",
        ["clib/alib_protos.h"] = "#pragma once\n#include <proto/intuition.h>\n" + AlibShim,
    };

    private static (int, string) RunShimmed(string c) => HasSanitizers ? RunSanitized(c, Shims()) : RunWithHostGcc(c, Shims());

    [Fact]
    public void Mui_sample_runs_against_a_fake_muimaster_with_each_object_disposed_once_and_no_leak()
    {
        if (!HasGcc) return;
        var c = CompileOk(File.ReadAllText(SamplePath), HostC);
        Assert.Contains("#include <es_muimaster_shim.h>", c);
        Assert.DoesNotContain("proto/muimaster.h", c);
        Assert.DoesNotContain("libraries/mui.h", c);
        var (code, output) = RunShimmed(c);
        Assert.True(code == 0, output);
        Assert.DoesNotContain("FAIL", output);
        Assert.Contains("mui: all ok\n", output);
        // MUI disposes the tree: never intuition's DisposeObject. The lone Text and Slider, the object taken out of the open window (a
        // group told before and after, twice) and the application with the 19 objects in it: 23 made (the unknown class is not), 23 disposed.
        Assert.DoesNotContain("[intuition DisposeObject", output);
        Assert.Contains("[shim: 23 made, 23 disposed, 4 group changes]", output);
        // The application goes last, after its window, which goes after its root group (children first).
        var tree = output[output.IndexOf("ok the window closes\n", StringComparison.Ordinal)..];
        Assert.True(tree.IndexOf("[dispose Group.mui]", StringComparison.Ordinal) < tree.IndexOf("[dispose Window.mui]", StringComparison.Ordinal)
                    && tree.IndexOf("[dispose Window.mui]", StringComparison.Ordinal) < tree.IndexOf("[dispose Application.mui]", StringComparison.Ordinal), tree);
        Assert.Contains("[close library]", output);
    }

    [Fact]
    public void Mui_sample_says_skip_without_muimaster()
    {
        if (!HasGcc) return;
        var c = CompileOk(File.ReadAllText(SamplePath), HostC);
        Environment.SetEnvironmentVariable("SHIM_NO_MUI", "1");
        try
        {
            var (code, output) = RunWithHostGcc(c, Shims());
            Assert.True(code == 0, output);
            Assert.StartsWith("skip: MUI not installed\n", output);
            Assert.DoesNotContain("ok ", output);
        }
        finally { Environment.SetEnvironmentVariable("SHIM_NO_MUI", null); }
    }

    [Theory]
    [InlineData(false, "mui: all ok\n")]
    [InlineData(true, "events: 1 Hello press(es), 2 slider change(s), last 70, 1 Return(s), list entry 1\n")]
    public void Mui_sample_runs_the_same_on_dotnet(bool wait, string expected)
    {
        var src = File.ReadAllText(SamplePath);
        var compilation = CSharpCompilation.Create("mui", new[] { CSharpSyntaxTree.ParseText(src) }, PlatformSubclassTests.HostReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var asm = System.Reflection.Assembly.Load(ms.ToArray());
        if (wait) ESharp.Platform.Args.HostSet("WAIT");
        try
        {
            using var console = HostConsole.Capture();
            asm.GetType("MuiSample.Program")!.GetMethod("Main")!.Invoke(null, null);
            Assert.DoesNotContain("FAIL", console.Output);
            Assert.Contains(expected, console.Output);
            Assert.EndsWith("mui: all ok\n", console.Output);
        }
        finally { ESharp.Platform.Args.HostReset(); }
    }

    [Theory]
    [InlineData("m68k", "jsr -30(%%a6)")]
    [InlineData("aros", "AROS_LC2(APTR, MUI_NewObjectA")]
    [InlineData("os4", "(IExec->EmulateTags)((APTR)((uint8_t *)es_MUIMasterBase - (lvo))")]
    [InlineData("morphos", "(*MyEmulHandle->EmulCallDirectOS)(-(lvo))")]
    public void Mui_sample_compiles_for_every_amiga_target_without_a_MUI_header(string target, string call)
    {
        var r = EsCompiler.Compile(new[] { new SourceFile("Mui.cs", File.ReadAllText(SamplePath)) }, Targets.Find(target)!.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains(call, r.C!);
        Assert.Contains("#define MUIMasterBase es_MUIMasterBase", r.C!);
        Assert.DoesNotContain("muimaster.h", r.C!);
        Assert.DoesNotContain("libraries/mui.h", r.C!);
        Assert.Contains("Es_ESharp_Platform_MuiNotification_Entry_HOOK", r.C!);
    }

    [Fact]
    public void Mui_vectors_match_the_fd_bias()
    {
        // Facts from the AROS fd of muimaster.library (bias 30, one vector each 6 bytes): NewObjectA 1st, DisposeObject 2nd,
        // RequestA 3rd, Error 7th, MakeObjectA 16th. The C# declarations, the m68k jsr offsets and the AROS call numbers must agree.
        var lvos = new Dictionary<string, int> { ["MUI_NewObjectA"] = 30, ["MUI_DisposeObject"] = 36, ["MUI_RequestA"] = 42, ["MUI_Error"] = 66, ["MUI_MakeObjectA"] = 120 };
        var platform = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ESharp.Platform", "Mui.cs"));
        string C(string target) => EsCompiler.Compile(new[] { new SourceFile("Mui.cs", File.ReadAllText(SamplePath)) }, Targets.Find(target)!.ToOptions()).C!;
        string m68k = C("m68k"), aros = C("aros"), morphos = C("morphos");
        foreach (var (name, lvo) in lvos)
        {
            Assert.Matches($@"\[AmigaLib\(""muimaster"", -{lvo}\), Since\(19\)\] public static extern [\w*]+ {name}\(", platform);
            Assert.Contains($"jsr -{lvo}(%%a6)", m68k);
            Assert.Contains($"AROS_LC", aros);
            Assert.Contains($"struct Library *, es_MUIMasterBase, {lvo / 6}, MUIMaster)", aros);
            Assert.Contains($"ES_MUI_CALL({lvo})", morphos);
        }
    }

    [Fact]
    public void Mui_classes_on_sdl_are_Amiga_only()
    {
        var r = Compile(Program("", "var a = new MuiApplication(\"x\", \"X\"); var t = new MuiText(\"y\");"), Targets.Sdl.ToOptions());
        Assert.False(r.Success);
        var messages = r.Diagnostics.Where(d => d.Id == "ES0102").Select(d => d.GetMessage()).ToList();
        Assert.Contains(messages, m => m.Contains("'MuiApplication'") && m.Contains("'sdl'"));
        Assert.Contains(messages, m => m.Contains("'MuiText'"));
    }

    [Fact]
    public void An_override_in_a_library_class_is_in_its_vtable_when_only_the_base_member_is_called()
    {
        // BoopsiObject.Create calls the virtual CreateAll, which MuiObject overrides: nothing names the override, yet it must be in the
        // table of every constructed MUI class (it was missing: a null slot, called on AmigaOS 4 as address 0).
        var c = CompileOk(Program("", "var t = new MuiText(\"x\"); if (Mui.Open() && t.Create()) Dos.PutStr(\"made\\n\"); t.Dispose(); Heap.Free(ref t);"), HostC);
        Assert.Contains("v_ESharp_Platform_BoopsiObject_CreateAll = Es_ESharp_Platform_MuiText__v_ESharp_Platform_BoopsiObject_CreateAll", c);
        Assert.Contains("v_ESharp_Platform_BoopsiObject_DisposeSystem = Es_ESharp_Platform_MuiText__v_ESharp_Platform_BoopsiObject_DisposeSystem", c);
        // A program without MUI classes does not pull them in through BoopsiObject's virtual members.
        var plain = CompileOk(Program("", "var o = new BoopsiObject(\"propgclass\"); o.Create(); o.Dispose(); Heap.Free(ref o);"), HostC);
        Assert.DoesNotContain("es_MUIMasterBase", plain);
        Assert.DoesNotContain("MuiObject_CreateAll(", plain);
    }

    [Fact]
    public void An_object_initializer_runs_property_setters_in_order()
    {
        if (!HasGcc) return;
        var c = CompileOk(Program(@"
class Box
{
    public int Log;
    public int Width { get; set; }
    public int Frame { set { Log = Log * 10 + value; } }
    public string? Title { set { Log = Log * 10 + (value == null ? 0 : value.Length); } }
    public Box(int start) { Log = start; }
}", "var b = new Box(1) { Frame = 2, Width = 7, Title = \"abc\" }; Dos.PutStr($\"{b.Log} {b.Width}\\n\"); Heap.Free(ref b);"));
        var (code, output) = RunWithHostGcc(c);
        Assert.True(code == 0, output);
        Assert.Equal("123 7\n", output);
    }
}

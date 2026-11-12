using System.Collections.Generic;
using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

// tools/BindGen as a generator: fd facts plus prototype types to C# declarations, the --check comparison with
// hand-written bindings, and Layout.g.cs written from measured numbers. The fixtures describe an invented library
// written for these tests (no SDK text); the SDK-backed test at the end skips unless ESHARP_SDK is set.
public class BindGenTests
{
    private const string WidgetFd = """
        ##base _WidgetBase
        ##bias 30
        ##public
        *--- functions in V36 or higher ---
        OpenWidget(name,flags)(a0,d0)
        CloseWidget(widget)(a0)
        ##private
        widgetPrivate1()()
        ##public
        WidgetReserved1()()
        *--- functions in V39 or higher ---
        DrawWidget(widget,x,y,label)(a1,d0/d1,a2)
        WidgetName(widget,buffer,size)(a0/a1,d0)
        LinkWidgets(list,event,tags)(a0,a1,a2)
        WidgetFile(widget)(a0)
        *--- functions in V47 or higher ---
        SpinWidget(widget,turns)(a0,d0)
        ##end
        """;

    // Classic NDK form, written for the invented library above.
    private const string WidgetProtos = """
        #ifndef CLIB_WIDGET_PROTOS_H
        #define CLIB_WIDGET_PROTOS_H
        /* invented for the BindGen tests */
        struct Widget *OpenWidget( CONST_STRPTR name, ULONG flags );
        VOID CloseWidget( struct Widget *widget );
        LONG DrawWidget( struct Widget *widget, WORD x, WORD y,
            CONST UBYTE *label );
        BOOL WidgetName( CONST struct Widget *widget, STRPTR buffer, LONG size );
        VOID LinkWidgets( struct Widget **list, struct InputEvent *event, CONST struct TagItem *tags );
        BPTR WidgetFile( struct Widget *widget );
        ULONG *SpinWidget( struct Widget *widget, LONG turns );
        struct Widget *OpenWidgetTags( CONST_STRPTR name, Tag tag1, ... );
        #endif
        """;

    private static List<Func> Widget() => Protos.Merge(Fd.ParseText(WidgetFd), Protos.ParseClassic(WidgetProtos));

    [Fact]
    public void Classic_prototypes_give_types_and_names_and_skip_varargs_stubs()
    {
        var p = Protos.ParseClassic(WidgetProtos);
        Assert.Equal(new[] { "OpenWidget", "CloseWidget", "DrawWidget", "WidgetName", "LinkWidgets", "WidgetFile", "SpinWidget" }, p.Select(f => f.Name));
        var draw = p.Single(f => f.Name == "DrawWidget");
        Assert.Equal("LONG", draw.Ret);
        Assert.Equal(new[] { "widget", "x", "y", "label" }, draw.Args.Select(a => a.Name));
        Assert.Equal("CONST UBYTE *", draw.Args[3].Type);
        Assert.Equal("ULONG *", p.Single(f => f.Name == "SpinWidget").Ret);
    }

    [Fact]
    public void Aros_prototypes_give_vectors_and_versions()
    {
        const string aros = """
            #if !defined(__WIDGET_LIBAPI__) || (39 <= __WIDGET_LIBAPI__)
            AROS_LP2(IPTR, WidgetSize,
                     AROS_LPA(struct Widget *, widget, A0),
                     AROS_LPA(ULONG, which, D0),
                     LIBBASETYPEPTR, WidgetBase, 9, Widget
            );
            #endif
            """;
        var f = Assert.Single(Protos.ParseAros(aros));
        Assert.Equal(("WidgetSize", "IPTR", 9, 39), (f.Name, f.Ret, f.Lvo, f.Since));
        Assert.Equal(new[] { "A0", "D0" }, f.Args.Select(a => a.Register));
        Assert.Equal("nuint", BindTypes.Map(f.Ret).Cs);
    }

    [Theory]
    [InlineData("struct Screen *", "void*")]
    [InlineData("CONST struct TagItem *", "void*")]
    [InlineData("struct Gadget **", "void**")]
    [InlineData("STRPTR", "byte*")]
    [InlineData("CONST_STRPTR", "string")]
    [InlineData("CONST UBYTE *", "string")]
    [InlineData("UBYTE *", "byte*")]
    [InlineData("BOOL", "int")]
    [InlineData("ULONG", "uint")]
    [InlineData("LONG", "int")]
    [InlineData("UWORD", "ushort")]
    [InlineData("WORD", "short")]
    [InlineData("APTR", "void*")]
    [InlineData("BPTR", "nuint")]
    [InlineData("IPTR", "nuint")]
    [InlineData("Tag", "uint")]
    [InlineData("ULONG *", "uint*")]
    [InlineData("APTR *", "void**")]
    [InlineData("VOID", "void")]
    [InlineData("void (*)()", "void*")]
    public void Type_map_follows_the_platform_style(string c, string cs)
    {
        var (mapped, known) = BindTypes.Map(c);
        Assert.True(known);
        Assert.Equal(cs, mapped);
    }

    [Fact]
    public void Unknown_typedefs_are_marked_for_review()
    {
        var (cs, known) = BindTypes.Map("WidgetMode");
        Assert.False(known);
        Assert.Equal("nuint /* WidgetMode */", cs);
        Assert.Equal("nuint /* no prototype */", BindTypes.Map("").Cs);
    }

    [Fact]
    public void Generator_writes_the_platform_style_and_skips_private_and_reserved_slots()
    {
        var text = BindingWriter.Write("widget", Widget(), new BindingWriter.Options(BaseName: "WidgetBase"));
        Assert.Contains("[LibraryBase(\"widget\", \"WidgetBase\", \"struct Library\")]", text);
        Assert.Contains("public static unsafe class WidgetLibrary", text);
        Assert.Contains("public static extern void SetBase(void* library);", text);
        Assert.Contains("[AmigaLib(\"widget\", -30), Since(36)] public static extern void* OpenWidget(string name, uint flags);", text);
        Assert.Contains("[AmigaLib(\"widget\", -36), Since(36)] public static extern void CloseWidget(void* widget);", text);
        // -42 is private and -48 a reserved slot: both still take a vector.
        Assert.DoesNotContain("Private", text);
        Assert.DoesNotContain("Reserved", text);
        Assert.Contains("[AmigaLib(\"widget\", -54), Since(39)] public static extern int DrawWidget(void* widget, short x, short y, string label);", text);
        Assert.Contains("[AmigaLib(\"widget\", -60), Since(39)] public static extern int WidgetName(void* widget, byte* buffer, int size);", text);
        Assert.Contains("public static extern void LinkWidgets(void** list, void* eventArg, void* tags);", text);   // C# keyword renamed
        Assert.Contains("[AmigaLib(\"widget\", -72), Since(39)] public static extern nuint WidgetFile(void* widget);", text);
        Assert.Contains("[AmigaLib(\"widget\", -78), Since(47)] public static extern uint* SpinWidget(void* widget, int turns);", text);
        Assert.DoesNotContain("OpenWidgetTags", text);
        Assert.DoesNotContain("check the marked type", text);
    }

    [Fact]
    public void The_libraries_the_startup_code_opens_get_no_library_base()
    {
        var exec = new List<Func> { new("Forbid", "VOID", new List<Arg>(), 22, 0) };
        var text = BindingWriter.Write("exec", exec, new BindingWriter.Options(Docs: true));
        Assert.DoesNotContain("LibraryBase", text);
        Assert.DoesNotContain("SetBase", text);
        Assert.Contains("public static unsafe class ExecLibrary", text);
        Assert.Contains("    /// <summary>exec vector -132.</summary>\n    [AmigaLib(\"exec\", -132)] public static extern void Forbid();", text);
    }

    [Fact]
    public void A_function_without_a_prototype_is_generated_with_marked_types()
    {
        var funcs = Protos.Merge(Fd.ParseText(WidgetFd), new List<Func>());
        var text = BindingWriter.Write("widget", funcs, new BindingWriter.Options());
        Assert.Contains("    // check the marked type\n    [AmigaLib(\"widget\", -30), Since(36)] public static extern nuint /* no prototype */ OpenWidget(nuint /* no prototype */ name, nuint /* no prototype */ flags);", text);
    }

    // The generated declarations compile through the emitter and call the C prototypes in their argument order:
    // the shim checks every value where the proto header would put it, with gcc -Wall -Werror catching type mismatches.
    [Fact]
    public void Generated_bindings_compile_to_calls_that_match_the_C_prototypes()
    {
        if (!HasGcc) return;
        var binding = BindingWriter.Write("widget", Widget(), new BindingWriter.Options(BaseName: "WidgetBase"));
        const string program = Prelude + """
            public static unsafe class P
            {
                [EntryPoint]
                public static void Main()
                {
                    int[] fake = new int[1];
                    byte[] buf = new byte[8];
                    fixed (int* lib = fake)
                    fixed (byte* b = buf)
                    {
                        WidgetLibrary.SetBase(lib);
                        void* w = WidgetLibrary.OpenWidget("wid", 7u);
                        int drawn = WidgetLibrary.DrawWidget(w, (short)-3, (short)4, "ok");
                        int named = WidgetLibrary.WidgetName(w, b, 8);
                        WidgetLibrary.LinkWidgets(&w, null, null);
                        nuint file = WidgetLibrary.WidgetFile(w);
                        uint* spin = WidgetLibrary.SpinWidget(w, 9);
                        Dos.PutStr($"{drawn} {named} {buf[0]} {(int)file} {*spin}\n");
                        WidgetLibrary.CloseWidget(w);
                        WidgetLibrary.SetBase(null);
                    }
                }
            }
            """;
        var r = EsCompiler.Compile(new[] { new SourceFile("Widget.g.cs", binding), new SourceFile("P.cs", program) },
            new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } });
        Assert.True(r.Success, string.Join("\n", r.Diagnostics) + "\n" + binding);
        var shim = new Dictionary<string, string>
        {
            ["proto/widget.h"] = """
                #include <string.h>
                #include <stdint.h>
                struct Library; struct Widget { int id; }; struct InputEvent; struct TagItem;
                /* Host stand-ins: E# strings are const char * in C, which the target compilers pass to the NDK's
                   const UBYTE * with only a pointer-sign warning; this shim is built with -Werror, so it says char. */
                typedef unsigned char UBYTE; typedef UBYTE *STRPTR; typedef const char *CONST_STRPTR;
                typedef uint32_t ULONG; typedef int32_t LONG; typedef int16_t WORD; typedef int16_t BOOL; typedef uintptr_t BPTR;
                extern struct Library *WidgetBase;
                static struct Widget es_w;
                static ULONG es_spin;
                static inline struct Widget *OpenWidget(CONST_STRPTR name, ULONG flags) { es_w.id = (int)strlen((const char *)name) * 10 + (int)flags; return &es_w; }
                static inline void CloseWidget(struct Widget *w) { (void)w; }
                static inline LONG DrawWidget(struct Widget *w, WORD x, WORD y, CONST_STRPTR label) { return w->id * 100 + x * 10 + y + (label[0] == 'o'); }
                static inline BOOL WidgetName(const struct Widget *w, STRPTR buffer, LONG size) { buffer[0] = (UBYTE)(w->id + size); return 1; }
                /* A pointer to a struct pointer is void** in C#; C sees void ** (see docs/STATUS.md, BindGen). */
                static inline void LinkWidgets(void **list, struct InputEvent *e, const struct TagItem *t) { ((struct Widget *)*list)->id += (e == 0) + (t == 0); }
                static inline BPTR WidgetFile(struct Widget *w) { return (BPTR)w->id; }
                static inline ULONG *SpinWidget(struct Widget *w, LONG turns) { es_spin = (ULONG)(w->id * turns); return &es_spin; }
                """,
        };
        var (code, output) = RunWithHostGcc(r.C!, shim);
        Assert.Equal(0, code);
        // OpenWidget: 3*10+7 = 37; DrawWidget: 3700-30+4+1; WidgetName: 37+8; LinkWidgets: +2; SpinWidget: 39*9.
        Assert.Equal("3675 1 45 39 351\n", output);
    }

    private const string HandWritten = """
        [LibraryBase("widget", "WidgetBase", "struct Library")]
        public static unsafe class Widget
        {
            [AmigaLib("widget", -30), Since(36)] public static extern void* OpenWidget(string name, uint flags);
            [AmigaLib("widget", -42)] public static extern void CloseWidget(void* widget);
            [AmigaLib("widget", -54), Since(39)] public static extern int DrawWidget(void* widget, int x, int y);
            [AmigaLib("widget", -60), Since(39)] public static extern short WidgetName(void* widget, string buffer, int size);
            /// <summary>Doc comments may sit above the attribute.</summary>
            [AmigaLib("widget", -66), Since(39)]
            public static extern int LinkWidgets(void** list, void* inputEvent,
                void* tags);
            [AmigaLib("widget", -72), Since(39)] public static extern int WidgetFile(void* widget);
            [AmigaLib("widget", -78), Since(40)] public static extern void* SpinWidget(void* widget, int turns);
            [AmigaLib("widget", -84)] public static extern void Unknown();
        }
        """;

    [Fact]
    public void Check_reports_each_kind_of_difference_and_tells_problems_from_notes()
    {
        var mine = PlatformCheck.Parse(HandWritten, "Widget.cs");
        Assert.Equal(8, mine.Count);
        Assert.Equal(new[] { ("void**", "list"), ("void*", "inputEvent"), ("void*", "tags") }, mine.Single(d => d.Name == "LinkWidgets").Params);
        var diffs = PlatformCheck.Diff("widget", mine, Widget()).Select(d => (d.Name, d.Problem, d.What)).ToList();
        void Has(string name, bool problem, string what) =>
            Assert.Contains(diffs, d => d.Name == name && d.Problem == problem && d.What.Contains(what, StringComparison.Ordinal));
        Has("CloseWidget", true, "vector -42, fd file -36");
        Has("CloseWidget", false, "no Since, fd file says v36");
        Has("DrawWidget", true, "3 arguments, fd file 4");
        Has("WidgetName", true, "argument 2 (buffer): C# string, prototype STRPTR (generator: byte*): a string where the library may write");
        Has("WidgetName", false, "return: C# short, prototype BOOL");
        Has("LinkWidgets", true, "return: C# int, prototype VOID (generator: void): the function returns nothing");
        Has("WidgetFile", true, "pointer-sized against fixed-size");
        Has("SpinWidget", true, "Since(40), fd file v47");
        Has("Unknown", true, "not in the fd file (vector -84)");
        Has("CloseWidget", true, "vector -42");
        Assert.DoesNotContain(diffs, d => d.Name == "OpenWidget");
        Assert.DoesNotContain(diffs, d => d.Name == "SpinWidget" && d.What.StartsWith("return", StringComparison.Ordinal));   // void* for uint* is the platform's style
    }

    [Fact]
    public void Check_compares_types_only_when_there_is_a_prototype()
    {
        var mine = PlatformCheck.Parse(HandWritten, "Widget.cs");
        var diffs = PlatformCheck.Diff("widget", mine, Protos.Merge(Fd.ParseText(WidgetFd), new List<Func>()));
        Assert.DoesNotContain(diffs, d => d.What.Contains("prototype", StringComparison.Ordinal));
        Assert.Contains(diffs, d => d.Name == "CloseWidget" && d.Problem);
    }

    [Fact]
    public void Platform_bindings_return_nothing_where_the_prototypes_return_nothing()
    {
        // RectFill, BltBitMapRastPort and AbortIO are VOID in the NDK prototypes (found by bindgen gen --check): using a
        // result is now a C# error instead of C that does not compile.
        foreach (var call in new[] { "Graphics.RectFill(null, 0, 0, 1, 1)", "Graphics.BltBitMapRastPort(null, 0, 0, null, 0, 0, 1, 1, 0xC0)", "Exec.AbortIO(null)" })
        {
            var r = Compile(Program("", $"unsafe {{ int x = {call}; }}"));
            Assert.False(r.Success, call);
            Assert.Contains("CS0029", ErrorIds(r));
        }
        var ok = Compile(Program("", "unsafe { Graphics.RectFill(null, 0, 0, 1, 1); nuint m = Exec.AvailMem(0); }"));
        Assert.True(ok.Success, string.Join("\n", ok.Diagnostics));
    }

    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    [Fact]
    public void Layout_g_cs_is_what_the_layout_writer_makes_of_its_own_numbers()
    {
        string path = Path.Combine(Repo(), "src", "ESharp.Platform", "Layout.g.cs");
        string text = File.ReadAllText(path).Replace("\r\n", "\n");
        var (aros, m68k) = LayoutWriter.Read(text);
        Assert.NotEmpty(aros);
        Assert.Equal(text, LayoutWriter.Write(aros, m68k));
        // Every item of structs.txt is measured for both targets, in the spec's order.
        var spec = LayoutWriter.SpecNames(File.ReadAllText(Path.Combine(Repo(), "tools", "BindGen", "structs.txt")));
        Assert.Equal(spec, aros.Select(e => e.Name));
        Assert.Equal(spec, m68k.Select(e => e.Name));
    }

    [Fact]
    public void Layout_writer_takes_measured_lines_and_refuses_mismatched_targets()
    {
        var a = LayoutWriter.ParseMeasured("WidgetSize = 48\nWidgetFlag = 4294967295u\n");
        var m = LayoutWriter.ParseMeasured("WidgetSize = 22\nWidgetFlag = 4294967295u\n");
        string text = LayoutWriter.Write(a, m);
        Assert.Contains("#if X86_64\n    public const int WidgetSize = 48;\n    public const uint WidgetFlag = 4294967295u;\n#else\n    public const int WidgetSize = 22;\n", text);
        Assert.Throws<InvalidOperationException>(() => LayoutWriter.Write(a, LayoutWriter.ParseMeasured("WidgetSize = 22\n")));
        Assert.Throws<FormatException>(() => LayoutWriter.ParseMeasured("WidgetSize = ?\n"));
    }

    /// <summary>
    /// With ESHARP_SDK naming an fd directory (the m68k toolchain's ndk/lib/fd) and optionally ESHARP_SDK_INCLUDE an
    /// include directory with clib/*_protos.h (NDK or AROS): the hand-written bindings have no problems, and the generated
    /// bindings for every library we bind compile through the emitter. Skipped otherwise.
    /// </summary>
    public sealed class SdkFactAttribute : FactAttribute
    {
        public SdkFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ESHARP_SDK")))
                Skip = "set ESHARP_SDK to an fd directory (and ESHARP_SDK_INCLUDE to a prototype include dir) to run it";
        }
    }

    private static readonly string[] Bound = { "exec", "dos", "graphics", "intuition", "gadtools", "lowlevel", "keymap", "utility", "diskfont", "iffparse", "asl", "timer" };

    [SdkFact]
    public void Sdk_hand_written_bindings_match_and_generated_ones_compile()
    {
        string fd = Environment.GetEnvironmentVariable("ESHARP_SDK")!;
        string? include = Environment.GetEnvironmentVariable("ESHARP_SDK_INCLUDE");
        var mine = PlatformCheck.ParseDir(Path.Combine(Repo(), "src", "ESharp.Platform"));
        var sources = new List<SourceFile>();
        foreach (var lib in Bound)
        {
            var funcs = Fd.Parse(fd, lib);
            if (!string.IsNullOrEmpty(include)) funcs = Protos.Merge(funcs, Protos.Has(include, lib) ? Protos.Load(include, lib) : new List<Func>());
            var problems = PlatformCheck.Diff(lib, mine, funcs).Where(d => d.Problem).ToList();
            Assert.True(problems.Count == 0, string.Join("\n", problems));
            sources.Add(new SourceFile(lib + ".g.cs", BindingWriter.Write(lib, funcs, new BindingWriter.Options(BaseName: Fd.BaseNameOf(fd, lib)))));
        }
        sources.Add(new SourceFile("P.cs", Program("", "")));
        var r = EsCompiler.Compile(sources, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics.Take(20)));
    }
}

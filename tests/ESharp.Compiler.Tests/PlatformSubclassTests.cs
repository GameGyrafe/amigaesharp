using System.Collections.Immutable;
using ESharp.Analyzers;
using ESharp.Target.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>Classes of the program that derive from [Extensible] platform classes (issue #42): class MyWindow : GtWindow.</summary>
public class PlatformSubclassTests
{
    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "guiclass", "GuiClass.cs");

    // GtWindow's constructor, Add, Dispatch and the hooks need no gadtools.library, so this runs as native C on the host
    // (Amiga build of the platform, host gcc): constructor chaining into the library, overrides called from library code,
    // base calls, is/as across the library boundary, a derived class without a constructor, and freeing through the base type.
    private const string Members = @"
public class Counter : GtWindow
{
    public int Clicks;
    public int Changes;
    public int Asks;
    public int BaseCalls;
    public GtButton Go;
    public GtString Field;
    public Counter() : base()
    {
        Title = ""Counter"";
        Go = new GtButton { Label = ""Go"" };
        Field = new GtString { Label = ""Name"" };
        Add(Go);
        Add(Field);
    }
    protected override void OnClicked(GtGadget gadget) { if (gadget == Go) Clicks++; base.OnClicked(gadget); BaseCalls++; }
    protected override void OnChanged(GtGadget gadget) { if (gadget == Field) Changes++; }
    protected override bool OnCloseRequest() { Asks++; return Asks >= 2; }
}
public sealed class Wide : Counter
{
    public Wide() { Width = 400; }
    protected override bool OnCloseRequest() { return base.OnCloseRequest() || true; }
}
public class Bare : GtWindow { }
static int Ok;
static void Check(bool c, string name) { if (c) { Ok++; } else { Dos.PutStr(""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); } }";

    private const string Body = @"
Counter c = new Counter();
Check(c.Width == 260 && c.Title == ""Counter"", ""the platform constructor ran, then the derived one"");
Check(c.Dispatch(new GuiEvent(GuiEventKind.Clicked, c.Go)) && c.Clicks == 1 && c.BaseCalls == 1, ""a click reaches the override"");
Check(c.Dispatch(new GuiEvent(GuiEventKind.Changed, c.Field)) && c.Changes == 1, ""a change reaches the override"");
Check(c.Dispatch(new GuiEvent(GuiEventKind.Close, null)) && c.Asks == 1, ""a refused close keeps going"");
Check(!c.Dispatch(new GuiEvent(GuiEventKind.Close, null)) && c.Asks == 2, ""an accepted close stops"");
GtWindow asBase = c;
Check(asBase is Counter && !(asBase is Wide) && (asBase as Bare) == null, ""is and as from the platform type"");
Check(!asBase.Add(c.Go), ""inherited platform method on the derived object"");
Wide w = new Wide();
Check(w.Width == 400 && w.Title == ""Counter"", ""implicit base() through two constructors"");
Check(!w.Dispatch(new GuiEvent(GuiEventKind.Close, null)) && w.Asks == 1, ""two levels of overrides and base calls"");
Bare b = new Bare();
GtWindow plain = new GtWindow();
Check(b.Width == 260 && plain.Dispatch(new GuiEvent(GuiEventKind.Clicked, c.Go)) && !plain.Dispatch(new GuiEvent(GuiEventKind.Close, null)), ""no constructor of its own; the platform's own hooks"");
Heap.Free(ref c.Go);       // Close frees the added gadgets, but it calls intuition, which this host run does not have
Heap.Free(ref c.Field);
Heap.Free(ref w.Go);
Heap.Free(ref w.Field);
Heap.Free(ref asBase);
Heap.Free(ref w);
Heap.Free(ref b);
Heap.Free(ref plain);
Check(asBase == null && w == null, ""freed through the base type"");
Dos.PutStr(Ok == 11 ? ""all ok\n"" : ""some failed\n"");";

    private static string Source => Program(Members, Body);

    [Fact]
    public void Derived_platform_window_compiles_to_C_and_runs()
    {
        var c = CompileOk(Source, new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } });
        // The library constructor is emitted because the derived one chains to it (nothing else names it).
        Assert.Contains("void Es_ESharp_Platform_GtWindow_new_init(Es_ESharp_Platform_GtWindow *_this)\n{", c);
        Assert.DoesNotContain("OpenWindowTagList", c);   // only what is used: no Open
        if (!HasGcc) return;
        var (code, output) = HasSanitizers ? RunSanitized(c) : RunWithHostGcc(c);
        Assert.True(code == 0, output);
        Assert.Equal("all ok\n", output);
    }

    [Fact]
    public void Derived_platform_window_runs_the_same_on_dotnet()
    {
        // The same members as the C test, compiled by dotnet against the host platform assembly (as an IDE or 'esharp test' does).
        var src = Program(Members, Body).Replace("public static class P", "public static class PlatformSubclassProgram");
        var refs = HostReferences();
        var compilation = CSharpCompilation.Create("subclass", new[] { CSharpSyntaxTree.ParseText(src) }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var asm = System.Reflection.Assembly.Load(ms.ToArray());
        using var console = HostConsole.Capture();
        asm.GetType("PlatformSubclassProgram")!.GetMethod("Main")!.Invoke(null, null);
        Assert.Equal("all ok\n", console.Output);
    }

    [Theory]
    [InlineData("m68k")]
    [InlineData("aros")]
    [InlineData("os4")]
    public void Guiclass_sample_compiles_for_amiga_targets_with_the_overrides_in_the_class_table(string target)
    {
        var options = target switch { "aros" => Targets.Aros.ToOptions(), "os4" => Targets.Os4.ToOptions(), _ => Targets.M68k.ToOptions() };
        var r = EsCompiler.Compile(new[] { new SourceFile("GuiClass.cs", File.ReadAllText(SamplePath)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var c = r.C!;
        Assert.Contains("void Es_ESharp_Platform_GtWindow_new_init(Es_ESharp_Platform_GtWindow *_this)\n{", c);
        Assert.Contains(".es_base.v_ESharp_Platform_GtWindow_OnClicked = Es_GuiClassSample_Greeter__v_ESharp_Platform_GtWindow_OnClicked", c);
        Assert.Contains(".es_base.v_ESharp_Platform_GtWindow_OnCloseRequest = Es_GuiClassSample_Greeter__v_ESharp_Platform_GtWindow_OnCloseRequest", c);
        Assert.Contains("GT_GetIMsg(", c);
    }

    [Fact]
    public void Guiclass_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        GuiClassSample.Program.Main();
        Assert.DoesNotContain("FAIL", console.Output);
        Assert.Equal(9, console.Output.Split('\n').Count(l => l.StartsWith("ok ")));
        Assert.Contains("reply: Hello, Ada!\n", console.Output);
    }

    [Theory]
    [InlineData("public class B : GtButton { }", "GtButton")]
    [InlineData("public class B : GtGadget { }", "GtGadget")]
    public void Library_classes_without_Extensible_cannot_be_base_classes(string members, string name)
    {
        var r = Compile(Program(members));
        Assert.Contains("ES0007", ErrorIds(r));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0007" && d.GetMessage().Contains("'" + name + "'") && d.GetMessage().Contains("[Extensible]"));
    }

    [Theory]
    [InlineData("ESharp")]
    [InlineData("ESharp.Platform")]
    public void A_program_in_a_library_namespace_may_derive_from_its_own_classes_but_not_from_closed_library_ones(string ns)
    {
        // Library classes are recognised by where they are declared (the compiler's lib/ sources or the assemblies), not by namespace.
        var own = $@"namespace {ns};
public class Base {{ public virtual int F() {{ return 1; }} }}
public class Mid : Base {{ public override int F() {{ return 2; }} }}
public sealed class Leaf : Mid {{ public override int F() {{ return base.F() + 1; }} }}
public static class P {{ [EntryPoint] public static void Main() {{ Base b = new Leaf(); Dos.PutStr(b.F() == 3 ? ""ok\n"" : ""FAIL\n""); Heap.Free(ref b); }} }}";
        var c = CompileOk("using ESharp;\nusing ESharp.Platform;\n" + own);
        if (HasGcc)
        {
            var (code, output) = RunWithHostGcc(c);
            Assert.Equal(0, code);
            Assert.Equal("ok\n", output);
        }
        var bad = Compile($"using ESharp;\nusing ESharp.Platform;\nnamespace {ns};\npublic class B : GtButton {{ }}\npublic static class P {{ [EntryPoint] public static void Main() {{ }} }}");
        Assert.Contains(bad.Diagnostics, d => d.Id == "ES0007" && d.GetMessage().Contains("'GtButton'"));
    }

    [Fact]
    public void An_implicit_base_constructor_with_optional_parameters_is_ES0099_not_skipped()
    {
        // C# binds the implicit base() to Base(int n = 5); the emitter would not run it, so it is reported instead.
        var r = Compile(Program("public class Base { public int N; public Base(int n = 5) { N = n; } }\npublic class D : Base { public D() { } }", "D d = new D(); Heap.Free(ref d);"));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.GetMessage().Contains("optional parameters"));
    }

    [Fact]
    public void Deriving_from_GtWindow_on_sdl_says_that_the_class_is_Amiga_only()
    {
        var r = Compile(Program("public class W : GtWindow { }"), Targets.Sdl.ToOptions());
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0102" && d.GetMessage().Contains("'GtWindow'"));
    }

    [Fact]
    public void Analyzer_accepts_an_Extensible_base_from_a_referenced_platform_assembly()
    {
        // In the IDE (a project made by 'esharp new') ESharp.Platform is a referenced assembly, not source: GtWindow is metadata.
        const string ok = "using ESharp.Platform;\npublic class W : GtWindow { protected override void OnClicked(GtGadget g) { } }\npublic class W2 : W { }";
        Assert.DoesNotContain(AnalyzeAgainstPlatformAssembly(ok), d => d.Id == "ES0007");

        const string bad = "using ESharp.Platform;\npublic class B : GtButton { }";
        var found = AnalyzeAgainstPlatformAssembly(bad);
        Assert.Contains(found, d => d.Id == "ES0007" && d.GetMessage().Contains("'GtButton'") && d.GetMessage().Contains("[Extensible]"));

        const string dotnet = "public class R : System.Random { }";
        Assert.Contains(AnalyzeAgainstPlatformAssembly(dotnet), d => d.Id == "ES0007");
    }

    internal static System.Collections.Generic.IEnumerable<MetadataReference> HostReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileNameWithoutExtension(p) is "System.Private.CoreLib" or "System.Runtime" or "System.Console")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(ESharp.Platform.GtWindow).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ESharp.EntryPointAttribute).Assembly.Location));

    private static ImmutableArray<Diagnostic> AnalyzeAgainstPlatformAssembly(string src)
    {
        var compilation = CSharpCompilation.Create("ide", new[] { CSharpSyntaxTree.ParseText(src) }, HostReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SubsetAnalyzer()))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
}

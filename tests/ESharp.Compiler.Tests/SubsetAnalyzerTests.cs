using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

public class SubsetAnalyzerTests
{
    [Fact]
    public void Linq_subset_over_arrays_has_no_diagnostics()
    {
        var r = Compile("using System.Linq;\n" + Program("", "int[] a = new int[4]; int n = a.Where(x => x > 1).Select(x => x * 2).Sum() + a.Count() + (from v in a where v > 0 select v).Count(); n += (a.Contains(3) ? 1 : 0) + System.Linq.Enumerable.Range(0, 3).Max();"));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
    }

    [Fact]
    public void Valid_program_has_no_diagnostics()
    {
        var r = Compile(Program("public static int Add(int a, int b) { return a + b; }"));
        Assert.True(r.Success);
        Assert.Empty(r.Diagnostics);
    }

    [Theory]
    [InlineData("static void F() { throw new System.Exception(); }", "ES0001")]
    [InlineData("static void F() { try { } catch { } }", "ES0001")]
    [InlineData("static int F(int[] a) { return System.Linq.Enumerable.Sum(System.Linq.Enumerable.Distinct(a)); }", "ES0002")]
    [InlineData("static int F(int[] a) { var q = from x in a from y in a select x + y; return 0; }", "ES0002")]
    [InlineData("static void F() { float x = 1; }", "ES0003")]
    [InlineData("static double F() { return 1; }", "ES0003")]
    [InlineData("static void F() { var x = 1.5; }", "ES0003")]
    [InlineData("static async System.Threading.Tasks.Task F() { await System.Threading.Tasks.Task.Delay(1); }", "ES0004")]
    [InlineData("static void F() { dynamic d = 1; }", "ES0005")]
    [InlineData("static string F() { return typeof(P).FullName; }", "ES0006")]
    [InlineData("class Derived : System.Random { }", "ES0007")]
    [InlineData("class Shape { public virtual int Area { get; set; } }", "ES0007")]
    [InlineData("class G<T> { }", "ES0008")]
    [InlineData("interface IThing { int Size { get; } }", "ES0009")]
    [InlineData("struct S { public string Name; public object O; }", "ES0010")]
    public void Out_of_subset_code_is_rejected(string members, string expectedId)
    {
        var r = Compile(Program(members));
        Assert.False(r.Success);
        Assert.Contains(expectedId, ErrorIds(r));
    }

    [Theory]
    [InlineData("static int[] F() { return null; }")]
    [InlineData("static void F(int[] a) { }")]
    [InlineData("static void F(int n) { int[] a = new int[n]; }")]
    [InlineData("static void F() { var a = new int[4]; var b = new int[4]; a = b; }")]
    [InlineData("static void F() { int[] a = new int[4]; int[] b = a; }")]
    [InlineData("static void F() { int[,] a = new int[2, 2]; }")]
    [InlineData("static void F() { int[][] a = new int[2][]; }")]
    [InlineData("static int Count(int[] a) { return a.Length; } static void G() { int x = Count(new int[3]); }")]
    public void Heap_and_unsized_arrays_are_rejected(string members)
    {
        var r = Compile(Program(members));
        Assert.Contains("ES0011", ErrorIds(r));
    }

    [Fact]
    public void Fixed_size_arrays_and_spans_are_accepted()
    {
        var r = Compile(Program(
            "const int N = 4; static int[] T = new int[N]; static int F(System.Span<int> s) { int[] a = { 1, 2 }; return s[0] + a[1]; }"));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
    }

    [Fact]
    public void Float_is_allowed_when_opted_in()
    {
        var r = Compile(Program("static float F() { return 1.5f; }"), new CompileOptions { AllowFloat = true });
        Assert.DoesNotContain("ES0003", ErrorIds(r));
    }

    [Fact]
    public void Long_arithmetic_warns_but_builds()
    {
        var r = Compile(Program("static long F(long a) { return a * 2; }"));
        Assert.True(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0100");
    }

    private const string NewApi = "[AmigaLib(\"dos\", -900), Since(50)] static extern int NewCall(int x); [AmigaLib(\"dos\", -30), Since(36)] static extern int OldCall(int x);";

    [Fact]
    public void Call_newer_than_the_target_libraries_is_an_error_on_m68k()
    {
        var m68k = Targets.M68k.ToOptions();
        var r = Compile(Program(NewApi, "NewCall(1);"), m68k);
        Assert.Contains("ES0101", ErrorIds(r));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0101" && d.GetMessage().Contains("version 50") && d.GetMessage().Contains("version 40"));
    }

    [Fact]
    public void Call_within_the_target_libraries_and_unused_declarations_are_fine()
    {
        var m68k = Targets.M68k.ToOptions();
        var r = Compile(Program(NewApi, "OldCall(1);"), m68k);   // NewCall is declared but never called
        Assert.DoesNotContain("ES0101", ErrorIds(r));
    }

    [Theory]
    [InlineData("[RequiresLib(\"dos\", 50)] static void Caller() { NewCall(1); }", "")]                          // on the method
    [InlineData("[RequiresLib(\"dos.library\", 50)] static void Caller() { NewCall(1); }", "")]                  // long name
    [InlineData("[RequiresLib(\"DOS\", 51)] static void Caller() { NewCall(1); }", "")]                          // higher is fine
    [InlineData("[RequiresLib(\"dos\", 50)] class Holder { public static void Caller() { NewCall(1); } }", "")]  // on the type
    [InlineData("static void Caller() { [RequiresLib(\"dos\", 50)] void Local() { } Local(); NewCall(1); }", "ES0101")]   // a local function does not cover its caller
    [InlineData("[RequiresLib(\"dos\", 45)] static void Caller() { NewCall(1); }", "ES0101")]                    // version too low
    [InlineData("[RequiresLib(\"graphics\", 50)] static void Caller() { NewCall(1); }", "ES0101")]              // another library
    [InlineData("static void Caller() { NewCall(1); }", "ES0101")]
    public void RequiresLib_opts_a_method_or_type_in(string members, string expected)
    {
        var r = Compile(Program(NewApi + members, "Caller();"), Targets.M68k.ToOptions());
        if (members.Contains("class Holder")) r = Compile(Program(NewApi + members, "Holder.Caller();"), Targets.M68k.ToOptions());
        if (expected == "") Assert.DoesNotContain("ES0101", ErrorIds(r)); else Assert.Contains(expected, ErrorIds(r));
    }

    [Fact]
    public void RequiresLib_on_the_assembly_covers_the_program()
    {
        var src = Prelude + "[assembly: RequiresLib(\"dos\", 50)]\n" + Program(NewApi, "NewCall(1);").Substring(Prelude.Length);
        Assert.DoesNotContain("ES0101", ErrorIds(Compile(src, Targets.M68k.ToOptions())));
    }

    [Fact]
    public void Library_versions_come_from_one_table()
    {
        Assert.Equal(40, ESharp.Analyzers.LibraryVersions.For(Targets.M68k.Defines));
        Assert.Equal(51, ESharp.Analyzers.LibraryVersions.For(Targets.Aros.Defines));
        Assert.Null(ESharp.Analyzers.LibraryVersions.For(Targets.Os4.Defines));
        Assert.Null(ESharp.Analyzers.LibraryVersions.For(Targets.MorphOs.Defines));
        Assert.Null(ESharp.Analyzers.LibraryVersions.For(Targets.Sdl.Defines));
    }

    [Fact]
    public void Aros_target_accepts_the_newer_call()
    {
        var r = Compile(Program(NewApi, "NewCall(1);"), Targets.Aros.ToOptions());
        Assert.DoesNotContain("ES0101", ErrorIds(r));
    }

    [Fact]
    public void Missing_entry_point_is_an_error()
    {
        var r = Compile(Prelude + "public static class P { public static void Main() { } }");
        Assert.Contains("ES0020", ErrorIds(r));
    }

    [Fact]
    public void Duplicate_entry_points_are_an_error()
    {
        var r = Compile(Prelude + "public static class P { [EntryPoint] public static void A() { } [EntryPoint] public static void B() { } }");
        Assert.Contains("ES0021", ErrorIds(r));
    }

    [Fact]
    public void Ordinary_csharp_errors_are_reported()
    {
        var r = Compile(Program("static int F() { return \"x\"; }"));
        Assert.Contains("CS0029", ErrorIds(r));
    }
}

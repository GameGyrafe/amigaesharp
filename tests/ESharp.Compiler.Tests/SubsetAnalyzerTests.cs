using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

public class SubsetAnalyzerTests
{
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
    [InlineData("static int F(int[] a) { return System.Linq.Enumerable.Sum(a); }", "ES0002")]
    [InlineData("static int F(int[] a) { var q = from x in a select x; return 0; }", "ES0002")]
    [InlineData("static void F() { float x = 1; }", "ES0003")]
    [InlineData("static double F() { return 1; }", "ES0003")]
    [InlineData("static void F() { var x = 1.5; }", "ES0003")]
    [InlineData("static async System.Threading.Tasks.Task F() { await System.Threading.Tasks.Task.Delay(1); }", "ES0004")]
    [InlineData("static void F() { dynamic d = 1; }", "ES0005")]
    [InlineData("static string F() { return typeof(P).FullName; }", "ES0006")]
    [InlineData("class Heap { }", "ES0007")]
    [InlineData("static void F<T>() { }", "ES0008")]
    [InlineData("interface IThing { }", "ES0009")]
    [InlineData("struct S { public string Name; public object O; }", "ES0010")]
    public void Out_of_subset_code_is_rejected(string members, string expectedId)
    {
        var r = Compile(Program(members));
        Assert.False(r.Success);
        Assert.Contains(expectedId, ErrorIds(r));
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

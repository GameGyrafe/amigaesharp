using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>ES0103: the program and E#'s library are one compilation, but the library's internal members are not the program's.</summary>
public class LibraryInternalTests
{
    [Theory]
    [InlineData("lib/Platform/Evil.cs")]
    [InlineData("lib/Runtime/Evil.cs")]
    [InlineData("<esharp-lib>/Platform/Evil.cs")]
    public void A_program_file_cannot_pass_as_a_library_file_by_its_path(string path)
    {
        var src = Program("static unsafe void F() { var o = new object(); void* p = Cast.ObjectToPtr(o); }");
        var r = EsCompiler.Compile(new[] { new SourceFile(path, src) });
        Assert.Contains("ES0103", ErrorIds(r));
    }

    [Fact]
    public void Direct_call_of_an_internal_library_method_is_rejected()
    {
        var r = Compile(Program("static unsafe void F() { var o = new object(); void* p = Cast.ObjectToPtr(o); }"));
        Assert.Contains("ES0103", ErrorIds(r));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0103" && d.GetMessage().Contains("ObjectToPtr"));
    }

    [Fact]
    public void Use_from_a_class_in_the_library_namespace_is_rejected()
    {
        var src = Prelude + @"
namespace ESharp.Platform
{
    public class Sneaky { public static unsafe void F(object o) { void* p = Cast.ObjectToPtr(o); } }
}
public static class P { [EntryPoint] public static void Main() { } }";
        Assert.Contains("ES0103", ErrorIds(Compile(src)));
    }

    [Fact]
    public void Use_of_an_internal_type_is_rejected()
    {
        var r = Compile(Program("static void F() { DosLib.Close(0); }"), Targets.M68k.ToOptions());
        Assert.Contains("ES0103", ErrorIds(r));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0103" && d.GetMessage().Contains("DosLib"));
    }

    [Fact]
    public void Public_library_members_and_the_librarys_own_internal_use_are_accepted()
    {
        // Heap, Video and others call internal members inside the library; the program only calls the public surface.
        var r = Compile(Program("static void F() { Trace.Line(\"x\"); }", "F();"));
        Assert.DoesNotContain("ES0103", ErrorIds(r));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
    }

    [Fact]
    public void A_program_member_named_like_a_library_internal_is_accepted()
    {
        var r = Compile(Program("internal static int ObjectToPtr(int x) { return x; } static int G() { return ObjectToPtr(1); }"));
        Assert.DoesNotContain("ES0103", ErrorIds(r));
    }
}

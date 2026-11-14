using ESharp.Target.Host;
using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>The entry points Main(), int Main(), Main(string[] args) and int Main(string[] args) (issue #66): the generated C is built with host gcc and run with arguments.</summary>
public class MainArgsTests
{
    private const string Show = @"
public static class P
{
    [EntryPoint]
    public static int Main(string[] args)
    {
        Dos.PutStr(args.Length == 0 ? ""none\n"" : ""n="" + args.Length + ""\n"");
        for (int i = 0; i < args.Length; i++) { Dos.PutStr(""["" + args[i] + ""]\n""); }
        foreach (string a in args) { if (a == ""RC"") return 7; }
        return 0;
    }
}";

    [Fact]
    public void Main_with_args_gets_the_command_line_and_returns_the_exit_code()
    {
        var c = CompileOk(Prelude + Show);
        if (!HasGcc) return;
        var (rc0, none) = RunWithHostGcc(c);
        Assert.Equal((0, "none\n"), (rc0, none));
        var (rc1, some) = RunWithHostGcc(c, null, "one", "two words", "");
        Assert.Equal((0, "n=3\n[one]\n[two words]\n[]\n"), (rc1, some));
        var (rc2, _) = RunWithHostGcc(c, null, "RC");
        Assert.Equal(7, rc2);
    }

    [Fact]
    public void Void_main_with_args_and_plain_main_still_work()
    {
        var voidArgs = CompileOk(Prelude + "public static class P { [EntryPoint] public static void Main(string[] a) { Dos.PutStr(a.Length == 2 ? \"two\\n\" : \"other\\n\"); } }");
        var plainInt = CompileOk(Prelude + "public static class P { [EntryPoint] public static int Main() { return 3; } }");
        var plain = CompileOk(Program("", "Dos.PutStr(\"hi\\n\");"));
        if (!HasGcc) return;
        Assert.Equal((0, "two\n"), RunWithHostGcc(voidArgs, null, "a", "b"));
        Assert.Equal(3, RunWithHostGcc(plainInt).exitCode);
        Assert.Equal((0, "hi\n"), RunWithHostGcc(plain));
    }

    [Fact]
    public void Args_class_and_args_parameter_agree()
    {
        var c = CompileOk(Prelude + "public static class P { [EntryPoint] public static void Main(string[] args) { Dos.PutStr(Args.Count == args.Length && Args.Get(0) == args[0] && Args.Int(\"SEED\", 1) == 42 ? \"same\\n\" : \"differ\\n\"); } }");
        if (!HasGcc) return;
        Assert.Equal((0, "same\n"), RunWithHostGcc(c, null, "SEED=42", "x"));
    }

    [Theory]
    [InlineData("public static string Main() { return null; }")]
    [InlineData("public static void Main(int x) { }")]
    [InlineData("public static void Main(string[] a, string b) { }")]
    [InlineData("public static void Main(string a) { }")]
    public void Other_entry_point_signatures_are_ES0022(string method)
    {
        var r = Compile(Prelude + "public static class P { [EntryPoint] " + method + " }");
        Assert.Contains("ES0022", ErrorIds(r));
    }

    [Fact]
    public void Calling_the_args_entry_point_is_ES0099()
    {
        var r = Compile(Prelude + "public static class P { [EntryPoint] public static void Main(string[] args) { if (args.Length > 5) Main(args); } }");
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.GetMessage().Contains("entry point"));
    }

    [Fact]
    public void Main_with_args_compiles_for_m68k_and_sdl_and_the_sample_runs_on_the_host()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "mainargs", "MainArgs.cs");
        foreach (var t in new[] { Targets.M68k, Targets.Sdl })
        {
            var r = EsCompiler.Compile(new[] { new SourceFile("MainArgs.cs", File.ReadAllText(path)) }, t.ToOptions());
            Assert.True(r.Success, string.Join("\n", r.Diagnostics.Select(d => d.ToString())));
        }
        using var console = HostConsole.Capture();
        Assert.Equal(5, MainArgsSample.Program.Main(new[] { "ONE", "two words", "RC=5" }));
        Assert.Equal("3 arguments\narg 0: [ONE]\narg 1: [two words]\narg 2: [RC=5]\nrc 5\n", console.Output);
    }

    [Fact]
    public void Args_line_for_sdl_is_split_with_quotes_kept_together()
    {
        Assert.Equal(new[] { "ONE", "two words", "RC=5" }, ESharp.Cli.SdlRun.SplitCommandLine("ONE \"two words\"   RC=5 "));
        Assert.Equal(new[] { "a", "", "b" }, ESharp.Cli.SdlRun.SplitCommandLine("a \"\" b"));
        Assert.Empty(ESharp.Cli.SdlRun.SplitCommandLine("   "));
    }
}

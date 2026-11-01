using System.Diagnostics;
using ESharp.Compiler;
using Microsoft.CodeAnalysis;

namespace ESharp.Compiler.Tests;

internal static class TestHelpers
{
    public const string Prelude = "using ESharp;\nusing ESharp.Platform;\n";

    /// <summary>Wraps a method body in a static class with an entry point.</summary>
    public static string Program(string members, string entryBody = "") => Prelude + $@"
public static class P
{{
    [EntryPoint]
    public static void Main() {{ {entryBody} }}
    {members}
}}";

    public static CompileResult Compile(string source, CompileOptions? options = null) =>
        EsCompiler.Compile(new[] { new SourceFile("test.cs", source) }, options);

    public static string[] ErrorIds(CompileResult r) =>
        r.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).Distinct().ToArray();

    public static string CompileOk(string source, CompileOptions? options = null)
    {
        var r = Compile(source, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics.Select(d => d.ToString())));
        return r.C!;
    }

    public static bool HasGcc { get; } = Which("gcc");

    private static bool Which(string exe)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, "--version")
            { RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Compiles generated C with the host gcc against a shim of proto/dos.h and runs it.</summary>
    public static (int exitCode, string output) RunWithHostGcc(string c)
    {
        var dir = Path.Combine(Path.GetTempPath(), "esharp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "proto"));
        File.WriteAllText(Path.Combine(dir, "proto", "dos.h"),
            "#include <stdio.h>\nstatic inline long PutStr(const char *s) { return fputs(s, stdout) < 0; }\n");
        var cFile = Path.Combine(dir, "prog.c");
        File.WriteAllText(cFile, c);
        var exe = Path.Combine(dir, "prog");

        var cc = Process.Start(new ProcessStartInfo("gcc", $"-std=gnu11 -Wall -Werror -I\"{dir}\" -o \"{exe}\" \"{cFile}\"")
        { RedirectStandardError = true })!;
        var err = cc.StandardError.ReadToEnd();
        cc.WaitForExit();
        Assert.True(cc.ExitCode == 0, "gcc failed:\n" + err + "\n" + c);

        var run = Process.Start(new ProcessStartInfo(exe) { RedirectStandardOutput = true })!;
        var output = run.StandardOutput.ReadToEnd();
        run.WaitForExit();
        return (run.ExitCode, output);
    }
}

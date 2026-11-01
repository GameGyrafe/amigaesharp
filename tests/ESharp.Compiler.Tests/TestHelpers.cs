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
        // A tiny exec.library: AllocVec/FreeVec on calloc, with a 512 KB "chip" pool tracked by flags.
        File.WriteAllText(Path.Combine(dir, "proto", "exec.h"),
            "#include <stdlib.h>\n#include <stdint.h>\n#include <stddef.h>\n" +
            "#define MEMF_CHIP 2UL\n" +
            "typedef struct { uint32_t size; uint32_t chip; } EsHdr;\n" +
            "static uint32_t es_chip_used;\n" +
            "static inline void *AllocVec(uint32_t size, uint32_t req) {\n" +
            "  int chip = (req & 2UL) != 0;\n" +
            "  if (size == 0 || (chip && size > 524288u - es_chip_used)) return NULL;\n" +
            "  EsHdr *h = (EsHdr *)calloc(1, 16 + size);\n  if (!h) { return NULL; }\n" +
            "  h->size = size;\n  h->chip = chip;\n  if (chip) { es_chip_used += size; }\n  return (char *)h + 16;\n}\n" +
            "static inline void FreeVec(void *p) {\n  if (!p) { return; }\n  EsHdr *h = (EsHdr *)((char *)p - 16);\n" +
            "  if (h->chip) { es_chip_used -= h->size; }\n  free(h);\n}\n" +
            "static inline uint32_t AvailMem(uint32_t req) { return (req & 2UL) ? 524288u - es_chip_used : 0u; }\n");
        var cFile = Path.Combine(dir, "prog.c");
        File.WriteAllText(cFile, c);
        var exe = Path.Combine(dir, "prog");

        var cc = Process.Start(new ProcessStartInfo("gcc", $"-std=gnu11 -fwrapv -Wall -Werror -I\"{dir}\" -o \"{exe}\" \"{cFile}\"")
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

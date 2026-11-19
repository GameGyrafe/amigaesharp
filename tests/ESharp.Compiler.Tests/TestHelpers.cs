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

    /// <summary>
    /// The C of an AROS build without E#'s own Workbench start entry (issue #107), for tests that build AROS C with the host gcc against
    /// a small shim of their own for something else (the entry needs aros/startup.h and the dos and Workbench headers).
    /// </summary>
    public static string WithoutArosWorkbenchStart(string c)
    {
        const string start = "#if defined(__AROS__)\n#include <proto/exec.h>\n#include <proto/dos.h>\n#include <aros/startup.h>\n";
        const string end = "ADD2SET(es_aros_wbstart, PROGRAM_ENTRIES, -50)\n#endif\n";
        var from = c.IndexOf(start, StringComparison.Ordinal);
        var to = c.IndexOf(end, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "no AROS Workbench start entry in the C");
        return c.Remove(from, to + end.Length - from);
    }

    public static string CompileOk(string source, CompileOptions? options = null)
    {
        var r = Compile(source, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics.Select(d => d.ToString())));
        return r.C!;
    }

    /// <summary>The C compiler for the native tests: ESHARP_TEST_CC, default gcc.</summary>
    public static string Cc { get; } = Environment.GetEnvironmentVariable("ESHARP_TEST_CC") is { Length: > 0 } v ? v : "gcc";
    public static bool HasGcc { get; } = Which(Cc);

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

    /// <summary>Compiles generated C with the host gcc against a shim of proto/dos.h (plus any <paramref name="extraHeaders"/>, relative path to text) and runs it.</summary>
    public static (int exitCode, string output) RunWithHostGcc(string c, System.Collections.Generic.IReadOnlyDictionary<string, string>? extraHeaders = null, params string[] programArgs) =>
        RunWithHostGccFlags(c, "", extraHeaders, programArgs);

    /// <summary>True when the host compiler builds and runs a program with AddressSanitizer and LeakSanitizer (gcc or clang on Linux).</summary>
    public static bool HasSanitizers { get; } = HasGcc && Probe();

    private static bool Probe()
    {
        try
        {
            var (code, output) = RunWithHostGccFlags("#include <stdio.h>\nint main(void) { puts(\"x\"); return 0; }\n", "-fsanitize=address,undefined", null);
            return code == 0 && output == "x\n";
        }
        catch { return false; }
    }

    /// <summary>
    /// Runs generated C with AddressSanitizer, LeakSanitizer and UBSan: a use after free, a double free or a block still
    /// allocated at exit fails the run (the exit code is not 0 and the report is in the output).
    /// </summary>
    public static (int exitCode, string output) RunSanitized(string c) => RunWithHostGccFlags(c, "-fsanitize=address,undefined -fno-sanitize-recover=all -g", null);

    /// <summary><see cref="RunSanitized(string)"/> with extra headers (relative path to text), as for <see cref="RunWithHostGcc"/>.</summary>
    public static (int exitCode, string output) RunSanitized(string c, System.Collections.Generic.IReadOnlyDictionary<string, string> extraHeaders) =>
        RunWithHostGccFlags(c, "-fsanitize=address,undefined -fno-sanitize-recover=all -g", extraHeaders);

    private static (int exitCode, string output) RunWithHostGccFlags(string c, string flags, System.Collections.Generic.IReadOnlyDictionary<string, string>? extraHeaders, params string[] programArgs)
    {
        var dir = Path.Combine(Path.GetTempPath(), "esharp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "proto"));
        if (extraHeaders != null) foreach (var (name, text) in extraHeaders) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, name))!); File.WriteAllText(Path.Combine(dir, name), text); }
        File.WriteAllText(Path.Combine(dir, "proto", "dos.h"),
            "#pragma once\n#include <stdio.h>\nstatic inline long PutStr(const char *s) { return fputs(s, stdout) < 0; }\n" +
            // Output and Flush (Dos.FlushOutput), and the error output the AROS branch of the generated C writes its messages to.
            "static inline void *ErrorOutput(void) { return stderr; }\nstatic inline void *Output(void) { return stdout; }\n" +
            "static inline long FPuts(void *f, const void *s) { return fputs((const char *)s, (FILE *)f) < 0; }\n" +
            "static inline long Flush(void *f) { return fflush((FILE *)f) == 0; }\n");
        // A tiny exec.library: AllocVec/FreeVec on calloc, with a 2 MB "chip" pool tracked by flags.
        File.WriteAllText(Path.Combine(dir, "proto", "exec.h"),
            "#pragma once\n#include <stdlib.h>\n#include <stdint.h>\n#include <stddef.h>\n" +
            "#define MEMF_CHIP 2UL\n" +
            "typedef struct { uint32_t size; uint32_t chip; } EsHdr;\n" +
            "static uint32_t es_chip_used;\n" +
            "static inline void *AllocVec(uint32_t size, uint32_t req) {\n" +
            "  int chip = (req & 2UL) != 0;\n" +
            "  if (size == 0 || (chip && size > 2097152u - es_chip_used)) return NULL;\n" +
            "  EsHdr *h = (EsHdr *)calloc(1, 16 + size);\n  if (!h) { return NULL; }\n" +
            "  h->size = size;\n  h->chip = chip;\n  if (chip) { es_chip_used += size; }\n  return (char *)h + 16;\n}\n" +
            "static inline void FreeVec(void *p) {\n  if (!p) { return; }\n  EsHdr *h = (EsHdr *)((char *)p - 16);\n" +
            "  if (h->chip) { es_chip_used -= h->size; }\n  free(h);\n}\n" +
            "static inline uint32_t AvailMem(uint32_t req) { return (req & 2UL) ? 2097152u - es_chip_used : 0u; }\n");
        var cFile = Path.Combine(dir, "prog.c");
        File.WriteAllText(cFile, c);
        var exe = Path.Combine(dir, "prog");

        var cc = Process.Start(new ProcessStartInfo(Cc, $"-std=gnu11 -fwrapv -Wall -Werror {flags} -I\"{dir}\" -o \"{exe}\" \"{cFile}\"")
        { RedirectStandardError = true })!;
        var err = cc.StandardError.ReadToEnd();
        cc.WaitForExit();
        Assert.True(cc.ExitCode == 0, Cc + " failed:\n" + err + "\n" + c);

        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = flags.Length > 0 };
        foreach (var a in programArgs) psi.ArgumentList.Add(a);
        var run = Process.Start(psi)!;
        var stderrTask = flags.Length > 0 ? run.StandardError.ReadToEndAsync() : null;
        var output = run.StandardOutput.ReadToEnd();
        run.WaitForExit();
        if (stderrTask != null) output += stderrTask.Result;
        // A native Windows C runtime writes text-mode CRLF; the expected texts use LF.
        return (run.ExitCode, output.Replace("\r\n", "\n"));
    }
}

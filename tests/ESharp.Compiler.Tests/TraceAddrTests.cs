using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ESharp.Cli;
using ESharp.Platform;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>Serial trace (Trace.Write/Line/Value) and <c>esharp addr</c> (crash address to C# line).</summary>
public class TraceAddrTests
{
    private static string SampleDir => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "trace");

    private static CompileResult CompileSample(bool trace)
    {
        var path = Path.Combine(SampleDir, "TraceDemo.cs");
        return EsCompiler.Compile(new[] { new SourceFile("samples/trace/TraceDemo.cs", File.ReadAllText(path)) }, Targets.M68k.ToOptions(trace: trace));
    }

    [Fact]
    public void Trace_sample_writes_to_the_trace_output_on_the_host()
    {
        var writer = new StringWriter();
        var previous = Trace.Out;
        Trace.Out = writer;
        try
        {
            using var console = ESharp.Target.Host.HostConsole.Capture();
            TraceSample.Program.Main();
            Assert.Equal("trace sample done, sum 14\n", console.Output);
        }
        finally { Trace.Out = previous; }
        Assert.Equal("trace start\nsquare arg=1\nsum=1\nsquare arg=2\nsum=5\nsquare arg=3\nsum=14\nnegative: n=-42\ntrace end\n", writer.ToString());
    }

    [Fact]
    public void Without_TRACE_the_trace_calls_and_their_code_are_gone()
    {
        var r = CompileSample(trace: false);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        foreach (var f in new[] { "Trace_Write", "Trace_Line", "Trace_Value", "Trace_Put" }) Assert.DoesNotContain(f, r.C!);   // only an unused flag (one byte of bss) is left
        Assert.DoesNotContain("es_rawputchar", r.C!);
    }

    [Fact]
    public void With_TRACE_the_calls_go_to_exec_RawPutChar()
    {
        var r = CompileSample(trace: true);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("Es_ESharp_Platform_Trace_Value(", r.C!);
        Assert.Contains("jsr -516(%%a6)", r.C!);   // RawPutChar
        Assert.Contains("jsr -504(%%a6)", r.C!);   // RawIOInit
    }

    [Fact]
    public void Debug_implies_TRACE()
    {
        Assert.Contains("TRACE", Targets.M68k.ToOptions(debug: true).Defines);
        Assert.DoesNotContain("TRACE", Targets.M68k.ToOptions().Defines);
    }

    [Fact]
    public void Trace_sample_builds_and_runs_with_the_host_gcc_with_and_without_TRACE()
    {
        if (!HasGcc) return;
        foreach (var trace in new[] { false, true })
        {
            var (code, output) = RunWithHostGcc(CompileSample(trace).C!);
            Assert.Equal(0, code);
            Assert.Equal("trace sample done, sum 14\n", output);
        }
    }

    [Fact]
    public void Conditional_calls_in_expression_bodies_lambdas_and_incrementors_are_compiled_out()
    {
        const string members = @"
static int hits;
static int F() { hits++; return 1; }
[System.Diagnostics.Conditional(""NEVER_DEFINED"")] static void T(int v) { hits += 100; }
static void L() => T(F());
static int Count() { System.Action a = () => T(F()); a(); int n = 0; for (int i = 0; i < 3; i++, T(F())) n++; return n; }";
        var c = CompileOk(Program(members, "L(); int n = Count(); Dos.PutStr(hits == 0 && n == 3 ? \"ok\\n\" : \"bad\\n\");"));
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void Addr_parses_hunk_and_offset_in_the_forms_the_tools_print()
    {
        foreach (var words in new[] { new[] { "0", "e8" }, new[] { "0:e8" }, new[] { "0:0xE8" }, new[] { "0000", "$000000E8" }, new[] { "0,", "000000e8" } })
        {
            Assert.True(Addr.TryParseAddress(words, out var hunk, out var offset), string.Join(" ", words));
            Assert.Equal(0, hunk);
            Assert.Equal(0xe8, offset);
        }
        Assert.False(Addr.TryParseAddress(new[] { "zz", "1" }, out _, out _));
        Assert.False(Addr.TryParseAddress(new[] { "1" }, out _, out _));
    }

    [Fact]
    public void Addr_finds_the_line_in_a_decoded_line_table()
    {
        var symbols = Addr.ParseSymbols(
            "SYMBOL TABLE:\n00000000         *UND*      0002 81 _DOSBase\n00000010 g       .text      0000 01 _Es_P_Main\n00000026 g       .text      0000 01 _main\n");
        Assert.Equal(2, symbols.Count);
        var lines = Addr.ParseLines(
            "Contents of the .debug_line section:\n\nHello.cs:\nFile name   Line number    Starting address    View    Stmt\n" +
            "Hello.cs                                  10                0x10               x\n" +
            "Hello.cs                                  11                0x12               x\n" +
            "Hello.cs                                   -                0x26\n");
        var loc = Addr.Find(0x14, lines, symbols, new System.Collections.Generic.List<string> { "samples/hello/Hello.cs" });
        Assert.Equal(new Addr.Location("samples/hello/Hello.cs", 11, "Es_P_Main"), loc);
        Assert.Equal(10, Addr.Find(0x10, lines, symbols, new System.Collections.Generic.List<string>())!.Line);
        Assert.Null(Addr.Find(0x0c, lines, symbols, new System.Collections.Generic.List<string>()));
        Assert.Null(Addr.Find(0x30, lines, symbols, new System.Collections.Generic.List<string>()));   // after the sequence ends
    }

    private const string M68kImage = "amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859";

    private static bool HasImage()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("docker") { ArgumentList = { "image", "inspect", M68kImage }, RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string Docker(string dir, params string[] tool)
    {
        var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "run", "--rm", "-v", $"{dir}:/work", "-w", "/work", M68kImage }.Concat(tool)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, string.Join(" ", tool) + ": " + err.Result);
        return output;
    }

    /// <summary>
    /// Builds the trace sample for m68k in the toolchain image (skipped when the pinned m68k image is not present locally),
    /// takes the address of a function from the exe's symbols, and checks that esharp addr gives its C# lines.
    /// </summary>
    [Fact]
    public void Addr_maps_a_symbol_address_of_a_built_exe_to_the_right_cs_lines()
    {
        if (!HasImage()) return;
        var dir = Path.Combine(Path.GetTempPath(), "esharp-addr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var r = CompileSample(trace: true);
            Assert.True(r.Success, string.Join("\n", r.Diagnostics));
            File.WriteAllText(Path.Combine(dir, "trace.c"), r.C!);
            const string flags = "-noixemul -m68020 -O2 -fwrapv";
            Docker(dir, "sh", "-c", $"m68k-amigaos-gcc {flags} -o trace trace.c && m68k-amigaos-gcc {flags} -g -c -o trace.o trace.c");

            // Ground truth from the symbol table: where Square and Main start in hunk 0.
            var symbols = Addr.ParseSymbols(Docker(dir, "m68k-amigaos-objdump", "-t", "trace"));
            var square = symbols.Single(s => s.Name == "_Es_TraceSample_Program_Square").Address;
            var main = symbols.Single(s => s.Name == "_Es_TraceSample_Program_Main").Address;

            string Lookup(long offset)
            {
                var w = new StringWriter();
                Assert.Equal(0, Addr.Run(new[] { Path.Combine(dir, "trace"), "0", offset.ToString("x"), "--docker", M68kImage }, w));
                return w.ToString();
            }

            var s1 = Lookup(square + 0x0c);   // inside Square: lines 14 to 17 of TraceDemo.cs
            Assert.Contains("samples/trace/TraceDemo.cs:1", s1);
            Assert.Contains("C function Es_TraceSample_Program_Square", s1);
            Assert.Matches(@"TraceDemo\.cs:1[4-7]\b", s1);

            var s2 = Lookup(main + 0x14);     // inside Main: lines 21 to 35
            Assert.Contains("C function Es_TraceSample_Program_Main", s2);
            Assert.Matches(@"TraceDemo\.cs:(2[1-9]|3[0-5])\b", s2);

            // The first statement of Main is Trace.Line("trace start") on line 22; its code is the first line entry after the prologue.
            var lines = Addr.ParseLines(Docker(dir, "m68k-amigaos-objdump", "--dwarf=decodedline", "trace.o"));
            var objSymbols = Addr.ParseSymbols(Docker(dir, "m68k-amigaos-objdump", "-t", "trace.o"));
            var baseAddress = Addr.ObjectBase(symbols, objSymbols)!.Value;
            var line22 = lines.First(l => l.Line == 22 && l.File == "TraceDemo.cs").Address + baseAddress;
            Assert.Contains("TraceDemo.cs:22", Lookup(line22));

            Assert.Contains("not in the program's own code", Lookup(0x20));   // the startup code
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

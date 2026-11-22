using System.Collections.Generic;
using System.Text.RegularExpressions;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ESharp.Cli;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// <c>esharp addr</c> beyond the trace sample (TraceAddrTests): the hunk table and the linker map of an m68k build with chip
/// data, ELF builds (AROS x86-64 and i386, AmigaOS 4), the address forms of the crash reports, and Trace.Where.
/// </summary>
public class AddrTests
{
    // ---- parsing, no toolchain needed ----

    private static byte[] Hunks(params (uint Type, uint Memory, byte[] Body, (string Name, uint Value)[] Symbols, bool Relocs)[] hunks)
    {
        var o = new List<byte>();
        void L(uint v) { o.Add((byte)(v >> 24)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 8)); o.Add((byte)v); }
        L(0x3F3); L(0); L((uint)hunks.Length); L(0); L((uint)hunks.Length - 1);
        foreach (var h in hunks) L((uint)((h.Body.Length + 3) / 4) | h.Memory);
        foreach (var h in hunks)
        {
            L(h.Type);
            L((uint)((h.Body.Length + 3) / 4));
            if (h.Type != 0x3EB) { o.AddRange(h.Body); while (o.Count % 4 != 0) o.Add(0); }
            if (h.Relocs) { L(0x3EC); L(2); L(0); L(4); L(8); L(0); L(0x3FC); o.AddRange(new byte[] { 0, 1, 0, 1, 0, 12, 0, 0 }); }
            if (h.Symbols.Length > 0)
            {
                L(0x3F0);
                foreach (var (name, value) in h.Symbols)
                {
                    var b = System.Text.Encoding.ASCII.GetBytes(name).ToList();
                    while (b.Count % 4 != 0) b.Add(0);
                    L((uint)(b.Count / 4));
                    o.AddRange(b);
                    L(value);
                }
                L(0);
            }
            L(0x3F1); L(1); L(0x12345678);   // a debug block is skipped
            L(0x3F2);
        }
        return o.ToArray();
    }

    [Fact]
    public void The_hunk_table_gives_every_hunk_with_its_kind_size_memory_and_symbols()
    {
        var file = Hunks(
            (0x3E9, 0, new byte[40], new[] { ("_start", 0u), ("_main", 0x20u) }, true),
            (0x3EA, 0, new byte[12], new[] { ("_SysBase", 0u) }, true),
            (0x3EB, 0, new byte[16], Array.Empty<(string, uint)>(), false),
            (0x3EA, 0x40000000, new byte[8], Array.Empty<(string, uint)>(), false),   // chip data
            (0x3EB, 0x40000000, new byte[256], new[] { ("_buffer", 4u) }, false),     // chip bss
            (0x3EA, 0x80000000, new byte[4], Array.Empty<(string, uint)>(), false));  // fast data
        var hunks = Addr.ReadHunks(file);
        Assert.Equal(6, hunks.Count);
        Assert.Equal(new[] { "code, 40 bytes", "data, 12 bytes", "bss, 16 bytes", "data, chip RAM, 8 bytes", "bss, chip RAM, 256 bytes", "data, fast RAM, 4 bytes" },
            hunks.Select(h => h.Describe()));
        Assert.Equal(new[] { "_start", "_main" }, hunks[0].Symbols.Select(s => s.Name));
        Assert.Equal(0x20, hunks[0].Symbols[1].Address);
        Assert.Equal(".bss", hunks[4].Symbols.Single().Section);
        Assert.Throws<InvalidDataException>(() => Addr.ReadHunks(file.Take(file.Length - 18).ToArray()));   // inside the last hunk's body
        Assert.Throws<InvalidDataException>(() => Addr.ReadHunks(file.Take(file.Length - 28).ToArray()));   // the last hunk missing
        Assert.Throws<InvalidDataException>(() => Addr.ReadHunks(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }));
    }

    private const string MapText = @"Archive member included to satisfy reference by file (symbol)

Linker script and memory map

.text           0x00000000     0x3a40
 *(.text)
 .text          0x00000000       0xd8 /opt/m68k-amigaos/m68k-amigaos/libnix/lib/ncrt0.o
                0x00000000                _start
 .text          0x000000d8     0x3180 /tmp/ccDvK9SI.o
                0x000000d8                Es_P_Main
 .text          0x00003258      0x28c /opt/m68k-amigaos/libnix/lib/libm020/libnix20.a(__nocommandline.o)
 .text.a_rather_long_input_section_name
                0x000034e4       0x1c /tmp/ccDvK9SI.o
.data           0x00003a40      0x2e0
 .data          0x00003a40        0xc /opt/m68k-amigaos/m68k-amigaos/libnix/lib/ncrt0.o
 .data          0x00003a4c      0x2ac /tmp/ccDvK9SI.o
.bss            0x00003d20      0x4d8
 .bss           0x00003d24      0x4a0 /tmp/ccDvK9SI.o
 COMMON         0x000041e8        0xc /tmp/ccDvK9SI.o
.datachip       0x000041f8      0x840
 .datachip      0x000041f8      0x840 /tmp/ccDvK9SI.o
.bsschip        0x00004a38      0x8c0
 .bsschip       0x00004a38      0x8c0 /tmp/ccDvK9SI.o
.debug_frame    0x00000000        0x0
.a_very_long_output_section_name_here
                0x00004a38        0x0
";

    [Fact]
    public void The_linker_map_gives_the_hunk_sections_and_where_the_program_object_went()
    {
        var (sections, pieces) = Addr.ParseMap(MapText.Replace("\n", "\r\n"));
        Assert.Equal(new[] { ".text", ".data", ".bss", ".datachip", ".bsschip" }, Addr.HunkSections(sections).Select(s => s.Name));
        Assert.Equal(0x41f8, Addr.HunkSections(sections)[3].Address);
        Assert.Equal("/tmp/ccDvK9SI.o", Addr.ProgramObject(pieces));
        var wrapped = pieces.Single(p => p.Section == ".text.a_rather_long_input_section_name");
        Assert.Equal((0x34e4L, 0x1cL, "/tmp/ccDvK9SI.o"), (wrapped.Address, wrapped.Size, wrapped.File));
        Assert.Contains(pieces, p => p.Section == "COMMON" && p.Address == 0x41e8);
        Assert.Equal(2, pieces.Count(p => p.File.Contains("ncrt0.o")));
        Assert.Contains(sections, s => s.Name == ".a_very_long_output_section_name_here" && s.Size == 0);
    }

    [Fact]
    public void Crash_report_lines_are_read_as_they_are_printed()
    {
        Addr.Where W(string s) { Assert.True(Addr.TryParseWhere(s.Split(' '), out var w), s); return w; }
        Assert.Equal(new Addr.Where(0, null, null, 0x916, false), W("hunk 0 offset 0x916"));                       // Trace.Where on m68k, SegTracker
        Assert.Equal(new Addr.Where(3, null, null, 0x10, false), W("3:$00000010"));
        Assert.Equal(new Addr.Where(1, ".text", null, 0xcf0, false), W("Module crash Segment 1 .text (0x000000004217B0C0) Offset 0x0000000000000CF0"));   // AROS alert
        Assert.Equal(new Addr.Where(1, ".text", null, 0xc5f, false), W("segment 1 .text offset 0xc5f"));            // Trace.Where on AROS
        Assert.Equal(new Addr.Where(null, ".text", null, 0x698, false), W(".text 698"));
        Assert.Equal(new Addr.Where(null, null, "Es_CrashSample_Program_Main", 0xa0, false), W("Function Es_CrashSample_Program_Main (0x000000004217BD10) Offset 0x00000000000000A0"));
        // The Grim Reaper: the symbol form wins over its "section N @" (which was 4 below the section offset in the crashes seen).
        Assert.Equal(new Addr.Where(null, null, "Es_CrashSample_Program_Main", 0xe0, false), W("ESTEST:crash:Es_CrashSample_Program_Main()+0xe0 (section 1 @ 0x1098)"));
        Assert.Equal(new Addr.Where(null, null, "_start", 0x1094, false), W("_start+0x1094"));                    // Trace.Where on AmigaOS 4
        Assert.Equal(new Addr.Where(null, null, null, 0x1001110, true), W("0x1001110"));
        Assert.False(Addr.TryParseWhere(new[] { "nonsense", "here" }, out _));
        Assert.False(Addr.TryParseAddress(new[] { ".text", "10" }, out _, out _));
    }

    [Fact]
    public void Addr2line_output_with_inlined_frames_is_read_innermost_first()
    {
        var frames = Addr.ParseAddr2Line("Es_P_Divide\n/work/samples/crash/Crash.cs:39 (discriminator 1)\nEs_P_Main\n/work/samples/crash/Crash.cs:59\n");
        Assert.Equal(new[] { new Addr.Location("/work/samples/crash/Crash.cs", 39, "Es_P_Divide"), new Addr.Location("/work/samples/crash/Crash.cs", 59, "Es_P_Main") }, frames);
        Assert.Equal("samples/crash/Crash.cs", Addr.SourcePath("/work/samples/crash/Crash.cs", new List<string> { "samples/crash/Crash.cs", "samples/other/Crash.cs" }));
        Assert.Null(Addr.ParseAddr2Line("??\n??:0\n").Single().Function);
    }

    [Fact]
    public void An_elf_file_gives_its_sections_and_symbols()
    {
        if (!HasGcc || OperatingSystem.IsWindows()) return;   // a host-compiled ELF object
        var dir = Path.Combine(Path.GetTempPath(), "esharp-elf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "t.c"), "static int counter;\nint table[4] = { 1, 2, 3, 4 };\nint bump(void) { return ++counter + table[1]; }\n");
            using (var p = Process.Start(new ProcessStartInfo(Cc) { ArgumentList = { "-c", "-O2", "-o", Path.Combine(dir, "t.o"), Path.Combine(dir, "t.c") } })!)
            {
                p.WaitForExit();
                Assert.Equal(0, p.ExitCode);
            }
            var elf = Addr.ReadElf(File.ReadAllBytes(Path.Combine(dir, "t.o")));
            Assert.True(elf.Relocatable);
            var text = elf.Sections.Single(s => s.Name == ".text");
            Assert.True(text.Code && text.Alloc && text.Size > 0);
            var bump = elf.Symbols.Single(s => s.Name == "bump");
            Assert.Equal(text.Index, bump.Section);
            Assert.Equal(2, bump.Kind);
            Assert.Equal(".data", elf.Sections[elf.Symbols.Single(s => s.Name == "table").Section].Name);
            Assert.Contains(elf.Symbols, s => s.Name == "counter");   // static: in the symbol table, which is what data offsets map to
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ---- Trace.Where in the emitter ----

    private const string WhereProgram = "Trace.Where(\"here\"); Dos.PutStr(\"done\\n\");";

    [Fact]
    public void Trace_Where_is_a_call_site_macro_with_TRACE_and_gone_without_it()
    {
        var with = CompileOk(Program("", WhereProgram), Targets.M68k.ToOptions(trace: true));
        Assert.Contains("es_trace_where(\"here\");", with);
        Assert.Contains("#define es_trace_where(l)", with);
        Assert.Contains("__builtin_return_address(0) - 2", with);    // m68k: inside the jsr/bsr
        Assert.Contains("cli_Module", with);                          // the m68k segment walk
        Assert.Contains("DecodeLocationA", with);                     // AROS
        var without = CompileOk(Program("", WhereProgram), Targets.M68k.ToOptions());
        Assert.DoesNotContain("es_trace_where", without);
        if (!HasGcc) return;
        foreach (var c in new[] { with, without })
        {
            var (code, output) = RunWithHostGcc(c);   // the host C has no serial port: the label part is a no-op there
            Assert.Equal(0, code);
            Assert.Equal("done\n", output);
        }
    }

    [Fact]
    public void A_native_method_can_give_its_own_C_name()
    {
        const string members = @"
[ESharp.Native(""es_answer"")] static extern int Answer();";
        var c = CompileOk(Program(members, "Dos.PutInt(Answer());"));
        Assert.Contains("es_answer()", c);
        Assert.DoesNotContain("Answer()", c.Replace("es_answer()", ""));
    }

    [Fact]
    public void After_a_user_function_the_C_file_gets_its_own_line_numbers_back()
    {
        var c = CompileOk(Program("static int Twice(int x) => x * 2;", "Dos.PutInt(Twice(21)); Dos.PutStr(\"\\n\");"),
            Targets.M68k.ToOptions(cFileName: "demo.c"));
        var lines = c.Replace("\r\n", "\n").Split('\n');
        var resets = lines.Select((l, i) => (l, i)).Where(x => x.l.StartsWith("#line ", StringComparison.Ordinal) && x.l.EndsWith("\"demo.c\"", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(resets);
        foreach (var (l, i) in resets) Assert.Equal($"#line {i + 2} \"demo.c\"", l);   // the next line (1-based: i + 2) has its own number
        Assert.DoesNotContain("ES_LINE_RESET", c);
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Trace_Where_on_the_host_writes_the_label()
    {
        var writer = new StringWriter();
        var previous = ESharp.Platform.Trace.Out;
        ESharp.Platform.Trace.Out = writer;
        try { ESharp.Platform.Trace.Where("here"); }
        finally { ESharp.Platform.Trace.Out = previous; }
        Assert.Equal("here: (no code offset on the host)\n", writer.ToString());
    }

    [Fact]
    public void FindInlined_reads_the_innermost_inlined_call_from_the_dwarf_dump()
    {
        const string info = @"
 <0><b>: Abbrev Number: 1 (DW_TAG_compile_unit)
    <19>   DW_AT_low_pc      : 0
 <1><20>: Abbrev Number: 5 (DW_TAG_subprogram)
    <21>   DW_AT_name        : (indirect string, offset: 0x1): Divide
 <1><40>: Abbrev Number: 6 (DW_TAG_subprogram)
    <41>   DW_AT_name        : (indirect string, offset: 0x2): Main
    <45>   DW_AT_low_pc      : 0x100
    <49>   DW_AT_high_pc     : 0x80
 <2><50>: Abbrev Number: 7 (DW_TAG_lexical_block)
 <3><51>: Abbrev Number: 8 (DW_TAG_inlined_subroutine)
    <52>   DW_AT_abstract_origin: <0x20>
    <56>   DW_AT_ranges      : 0x18
 <2><70>: Abbrev Number: 8 (DW_TAG_inlined_subroutine)
    <71>   DW_AT_abstract_origin: <0x20>
    <75>   DW_AT_low_pc      : 0x1f0
    <79>   DW_AT_high_pc     : 0x4
";
        const string ranges = @"
    Offset   Begin    End
    00000018 00000110 00000118
    00000018 00000120 00000124
    00000018 <End of list>
";
        var hit = Addr.FindInlined(0x122, info, ranges);
        Assert.Equal("Divide", hit?.Callee);
        Assert.Equal("Main", hit?.Caller);
        Assert.Equal("Divide", Addr.FindInlined(0x1f2, info, ranges)?.Callee);
        Assert.Null(Addr.FindInlined(0x11a, info, ranges));
        Assert.Null(Addr.FindInlined(0x1f4, info, ranges));
    }

    [Fact]
    public void FindInlined_reads_high_pc_as_an_address_in_dwarf_2_and_3()
    {
        const string ranges = "\n    Offset   Begin    End\n";
        string Info(int version, string highPc) => $@"
  Compilation Unit @ offset 0x0:
   Version:       {version}
 <0><b>: Abbrev Number: 1 (DW_TAG_compile_unit)
    <19>   DW_AT_low_pc      : 0
 <1><20>: Abbrev Number: 5 (DW_TAG_subprogram)
    <21>   DW_AT_name        : Divide
 <1><40>: Abbrev Number: 6 (DW_TAG_subprogram)
    <41>   DW_AT_name        : Main
 <2><70>: Abbrev Number: 8 (DW_TAG_inlined_subroutine)
    <71>   DW_AT_abstract_origin: <0x20>
    <75>   DW_AT_low_pc      : 0x1f0
    <79>   DW_AT_high_pc     : {highPc}
";
        // DWARF 3: high_pc is the end address; DWARF 4: a length from low_pc.
        var v3 = Info(3, "0x1f8");
        Assert.Equal("Divide", Addr.FindInlined(0x1f2, v3, ranges)?.Callee);
        Assert.Equal("Divide", Addr.FindInlined(0x1f7, v3, ranges)?.Callee);
        Assert.Null(Addr.FindInlined(0x1f8, v3, ranges));
        var v4 = Info(4, "0x8");
        Assert.Equal("Divide", Addr.FindInlined(0x1f7, v4, ranges)?.Callee);
        Assert.Null(Addr.FindInlined(0x1f8, v4, ranges));
        Assert.Null(Addr.FindInlined(0x1ef, v4, ranges));
    }

    [Fact]
    public void A_missing_link_map_deletes_a_stale_map_of_an_earlier_build()
    {
        var dir = TempDir("stalemap");
        try
        {
            var exe = Path.Combine(dir, "game");
            File.WriteAllText(exe + ".map", "old");
            Assert.Equal(0, ESharp.Cli.Program.MoveLinkMap(dir, exe));
            Assert.False(File.Exists(exe + ".map"));
            File.WriteAllText(Path.Combine(dir, "esharp-link.map"), "new");
            File.WriteAllText(exe + ".map", "old");
            Assert.Equal(0, ESharp.Cli.Program.MoveLinkMap(dir, exe));
            Assert.Equal("new", File.ReadAllText(exe + ".map"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>A read-only output folder makes File.Move throw UnauthorizedAccessException: reported, not crashed. Needs POSIX permissions and a non-root user, so it does nothing on Windows or as root.</summary>
    [Fact]
    public void A_link_map_that_cannot_be_moved_is_reported_as_an_error()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        var dir = TempDir("rodir");
        try
        {
            File.WriteAllText(Path.Combine(dir, "esharp-link.map"), "new");
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try { File.Create(Path.Combine(dir, "probe")).Dispose(); return; }   // the folder is still writable (e.g. CAP_DAC_OVERRIDE): cannot test
            catch (UnauthorizedAccessException) { }
            Assert.Equal(1, ESharp.Cli.Program.MoveLinkMap(dir, Path.Combine(dir, "game")));
        }
        finally
        {
            try { File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Missing_binutils_are_not_told_to_use_emit_only()
    {
        var error = ESharp.Cli.Toolchain.Resolve(Targets.M68k, null, null, _ => "", out _, "esharp-no-such-tool", forBinutils: true);
        Assert.NotNull(error);
        Assert.DoesNotContain("--emit-only", error);
        Assert.Contains("--docker", error);
        Assert.Contains("--emit-only", ESharp.Cli.Toolchain.Resolve(Targets.M68k, null, null, _ => "", out _, "esharp-no-such-tool")!);   // a build still can
    }

    // ---- real builds in the toolchain images (each skipped when its image is not in the local docker) ----

    private const string M68kImage = "amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859";

    /// <summary>A fact that shows as skipped (not passed) when the docker image it builds in is not in the local docker.</summary>
    public sealed class DockerImageFactAttribute : FactAttribute
    {
        public DockerImageFactAttribute(string image)
        {
            if (!HasImage(image)) Skip = $"docker image {image} is not available (build it from docker/ or pull it)";
        }
    }

    private static string SampleDir => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "crash");

    private static bool HasImage(string image)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("docker") { ArgumentList = { "image", "inspect", image }, RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string Docker(string image, string dir, params string[] tool)
    {
        var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "run", "--rm", "-v", $"{dir}:/work", "-w", "/work", image }.Concat(tool)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, string.Join(" ", tool) + ": " + err.Result);
        return output;
    }

    /// <summary>Builds samples/crash with --trace for a target into a temp folder.</summary>
    private static string BuildCrash(string target, string image, string dir)
    {
        Assert.Equal(0, ESharp.Cli.Program.Build(new[] { Path.GetFullPath(SampleDir), "--trace", "--target", target, "--docker", image, "-o", dir }, out var exe));
        return exe!;
    }

    /// <summary>esharp addr's output, with / in paths (the #line paths are relative to the test's folder).</summary>
    private static string Lookup(string exe, string target, string image, params string[] where)
    {
        var w = new StringWriter();
        Assert.Equal(0, Addr.Run(new[] { exe }.Concat(where).Concat(new[] { "--target", target, "--docker", image }).ToArray(), w));
        return w.ToString().Replace('\\', '/');
    }

    private static string TempDir(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"esharp-addr-{tag}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// m68k: the crash sample has chip data and chip bss, so five hunks. Code offsets (from the exe's own symbols) map to lines in
    /// the right functions, and offsets of the program's data, bss, chip data and chip bss map to its static fields (their places
    /// worked out from the map and the object's symbols, independently of esharp addr).
    /// </summary>
    [DockerImageFact(M68kImage)]
    public void M68k_build_with_chip_hunks_maps_code_and_every_kind_of_data()
    {
        var dir = TempDir("m68k");
        try
        {
            var exe = BuildCrash("m68k", M68kImage, dir);
            var hunks = Addr.ReadHunks(File.ReadAllBytes(exe));
            Assert.Equal(new[] { "code", "data", "bss", "data, chip RAM", "bss, chip RAM" }, hunks.Select(h => h.Describe().Substring(0, h.Describe().LastIndexOf(','))));

            // Code: Checksum and Divide are global symbols of the exe.
            var checksum = hunks[0].Symbols.Single(s => s.Name == "_Es_CrashSample_Program_Checksum").Address;
            var s1 = Lookup(exe, "m68k", M68kImage, "0", (checksum + 8).ToString("x"));
            Assert.Contains("C function Es_CrashSample_Program_Checksum", s1);
            Assert.Matches(@"samples/crash/Crash\.cs:(2[5-9]|3[0-4])\b", s1);
            Assert.Contains("C function Es_CrashSample_Program_Checksum", Lookup(exe, "m68k", M68kImage, "Es_CrashSample_Program_Checksum+0x8"));

            // Data: where the linker put each of the program's sections, and the object's symbols in them.
            var (sections, pieces) = Addr.ParseMap(File.ReadAllText(exe + ".map"));
            var hs = Addr.HunkSections(sections);
            var program = Addr.ProgramObject(pieces);
            var objSymbols = Addr.ParseSymbols(Docker(M68kImage, dir, "m68k-amigaos-objdump", "-t", Path.GetFileName(exe) + ".o"));
            foreach (var (field, section, hunk, what) in new[]
                     {
                         ("_Es_CrashSample_Program_Weights", ".data", 1, "data"), ("_Es_CrashSample_Program__calls", ".bss", 2, "bss"),
                         ("_Es_CrashSample_Program_Pattern", ".datachip", 3, "chip data"), ("_Es_CrashSample_Program_Buffer", ".bsschip", 4, "chip bss"),
                     })
            {
                var piece = pieces.Single(p => p.File == program && p.Section == section);
                var offset = piece.Address + objSymbols.Single(s => s.Name == field).Address + 2 - hs[hunk].Address;
                var text = Lookup(exe, "m68k", M68kImage, hunk.ToString(), offset.ToString("x"));
                Assert.Contains($"in the program's {what}: {field.Substring(1)} + 0x2", text);
            }
            // Divide is inlined into Main: the line of its Trace.Where gets an "inlined into" note (found from the object's line table).
            var lines = Addr.ParseLines(Docker(M68kImage, dir, "m68k-amigaos-objdump", "--dwarf=decodedline", Path.GetFileName(exe) + ".o"));
            var textPiece = pieces.Single(p => p.File == program && p.Section == ".text");
            var divideLine = lines.Last(l => l.Line == 38 && l.File.EndsWith("Crash.cs", StringComparison.Ordinal)).Address;
            var divide = Lookup(exe, "m68k", M68kImage, "0", (textPiece.Address + divideLine + 2 - hs[0].Address).ToString("x"));
            Assert.Contains("Crash.cs:38", divide);
            Assert.Contains("C function Es_CrashSample_Program_Divide (inlined into Es_CrashSample_Program_Main)", divide);
            Assert.Contains("Divide (inlined into Es_CrashSample_Program_Main", divide);   // Divide inlined into Main
            Assert.DoesNotContain("inlined into", s1);   // Checksum is a function of its own
            Assert.Contains("chip RAM", Lookup(exe, "m68k", M68kImage, "3", "0"));
            Assert.Contains("not in the program's own data", Lookup(exe, "m68k", M68kImage, "1", "0"));   // the start-up code's data comes first
            Assert.Contains("no such hunk: the exe has 5", Lookup(exe, "m68k", M68kImage, "5", "0"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>A project folder with a comma in its name (the linker option -Wl,-Map,... is split at commas): the map still lands next to the exe.</summary>
    [DockerImageFact(M68kImage)]
    public void M68k_build_of_a_project_with_a_comma_in_its_name_writes_its_map()
    {
        var root = TempDir("comma");
        try
        {
            var project = Path.Combine(root, "my,game");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "Game.cs"), "using ESharp;\nusing ESharp.Platform;\nnamespace Game;\npublic static class Program\n{\n    [EntryPoint]\n    public static void Main() { Dos.PutStr(\"hi\\n\"); }\n}\n");
            Assert.Equal(0, ESharp.Cli.Program.Build(new[] { project, "--target", "m68k", "--docker", M68kImage, "-o", Path.Combine(root, "out") }, out var exe));
            Assert.True(File.Exists(exe + ".map"), exe + ".map");
            Assert.Contains("Linker script and memory map", File.ReadAllText(exe + ".map"));
            Assert.False(File.Exists(Path.Combine(root, "out", "esharp-link.map")));
            var w = new StringWriter();
            Assert.Equal(0, Addr.Run(new[] { exe!, "--target", "m68k", "--docker", M68kImage }, w));
            Assert.Contains("hunks", w.ToString());
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>The calls of es_trace_where_at in a disassembly, with the function they are in (objdump -d).</summary>
    private static List<(string Function, long Address)> TraceCalls(string disassembly)
    {
        var list = new List<(string, long)>();
        string? function = null;
        foreach (var line in disassembly.Split('\n'))
        {
            var f = Regex.Match(line, @"^[0-9a-f]+ <([^>+]+)>:");
            if (f.Success) { function = f.Groups[1].Value; continue; }
            var c = Regex.Match(line, @"^\s*([0-9a-f]+):.*\b(call|callq|bl|calll)\s+[0-9a-f]+ <es_trace_where_at>");
            if (c.Success && function != null) list.Add((function, Convert.ToInt64(c.Groups[1].Value, 16)));
        }
        return list;
    }

    /// <summary>
    /// ELF targets: the code section of the .debug file is byte for byte the exe's; each call of Trace.Where (found in the
    /// disassembly) maps to its own line, in an inlined function too, given as the crash reports give it.
    /// </summary>
    [DockerImageFact("esharp-amiga-toolchain")]
    public void Elf_build_maps_trace_calls_to_their_lines_aros() => ElfBuildMapsTraceCalls("aros", "esharp-amiga-toolchain");

    [DockerImageFact("esharp-aros-i386")]
    public void Elf_build_maps_trace_calls_to_their_lines_aros_i386() => ElfBuildMapsTraceCalls("aros-i386", "esharp-aros-i386");

    [DockerImageFact("esharp-amiga-toolchain")]
    public void Elf_build_maps_trace_calls_to_their_lines_os4() => ElfBuildMapsTraceCalls("os4", "esharp-amiga-toolchain");

    private static void ElfBuildMapsTraceCalls(string target, string image)
    {
        var dir = TempDir(target);
        try
        {
            var exe = BuildCrash(target, image, dir);
            var bytes = File.ReadAllBytes(exe);
            var debugBytes = File.ReadAllBytes(exe + ".debug");
            var elf = Addr.ReadElf(bytes);
            var dbg = Addr.ReadElf(debugBytes);
            var text = elf.Sections.Single(s => s.Name == ".text");
            var twin = dbg.Sections.Single(s => s.Name == ".text");
            Assert.Equal(bytes.Skip((int)text.Offset).Take((int)text.Size), debugBytes.Skip((int)twin.Offset).Take((int)twin.Size));

            var prefix = Addr.ToolPrefix(ESharp.Compiler.Targets.Find(target)!);
            var calls = TraceCalls(Docker(image, dir, prefix + "objdump", "-d", Path.GetFileName(exe)));
            var inMain = calls.Where(c => c.Function == "Es_CrashSample_Program_Main").Select(c => c.Address).ToList();
            var inChecksum = calls.Single(c => c.Function == "Es_CrashSample_Program_Checksum").Address;
            Assert.Equal(3, inMain.Count);   // main, and Divide and Poke inlined
            long Offset(long a) => elf.Relocatable ? a : a - text.Address;

            var checksum = Lookup(exe, target, image, text.Index.ToString(), (Offset(inChecksum) + 1).ToString("x"));
            Assert.Contains("samples/crash/Crash.cs:27", checksum);
            Assert.Contains("C function Es_CrashSample_Program_Checksum", checksum);
            Assert.Contains("Crash.cs:51", Lookup(exe, target, image, ".text", (Offset(inMain[0]) + 1).ToString("x")));
            var divide = Lookup(exe, target, image, $"segment {text.Index} .text offset 0x{Offset(inMain[1]) + 1:x}");
            Assert.Contains("Crash.cs:38", divide);
            Assert.Matches(@"C function Es_CrashSample_Program_Divide \(inlined into Es_CrashSample_Program_Main at \S*samples/crash/Crash\.cs:60\)", divide);
            // Platform code is emitted without #line, so an address in it has no .cs line: it is reported as runtime/platform code
            // with its C function (the program links Args.Has as its own function).
            var platform = elf.Symbols.Single(s => s.Name == "Es_ESharp_Platform_Args_Has" && s.Kind == 2);
            var platformText = Lookup(exe, target, image, $"Es_ESharp_Platform_Args_Has+0x{platform.Size / 2:x}");
            Assert.Contains("in E# runtime/platform code, C function Es_ESharp_Platform_Args_Has", platformText);
            Assert.DoesNotContain(".cs:", platformText);
            Assert.Contains("no E# source line is kept for it", platformText);
            // A segment number that disagrees with the section name (AROS counts segments its own way): the name is used.
            var disagree = Lookup(exe, target, image, $"segment 77 .text offset 0x{Offset(inMain[0]) + 1:x}");
            Assert.Contains("using the section name .text", disagree);
            Assert.Contains("Crash.cs:51", disagree);
            var main = elf.Symbols.Single(s => s.Name == "Es_CrashSample_Program_Main").Value;
            Assert.Contains("Crash.cs:44", Lookup(exe, target, image, $"Es_CrashSample_Program_Main()+0x{inMain[2] + 1 - main:x}"));
            if (!elf.Relocatable) Assert.Contains("Crash.cs:44", Lookup(exe, target, image, $"0x{inMain[2] + 1:x}"));

            var data = elf.Symbols.Single(s => s.Name == "Es_CrashSample_Program_Weights");
            var dataSection = elf.Sections[data.Section];
            Assert.Contains("nearest symbol Es_CrashSample_Program_Weights + 0x4",
                Lookup(exe, target, image, dataSection.Index.ToString(), (data.Value + 4 - (elf.Relocatable ? 0 : dataSection.Address)).ToString("x")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

using System.Diagnostics;
using ESharp.Cli;
using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// The os4 run harness (src/ESharp.Cli/Os4Vm.cs, tools/os4vm): the VM setting, the autorun script, the serial output parsing
/// (markers, prefixed program lines among kernel messages, SHOT, a line still arriving), the QEMU command line, the os4 parts
/// of the generated C, and, when ESHARP_OS4_VM names a VM with the test hook installed, a program run in it.
/// </summary>
public class Os4VmTests
{
    /// <summary>A real xunit skip unless ESHARP_OS4_VM_TESTS=1 and ESHARP_OS4_VM names a VM: a plain dotnet test never boots the VM.</summary>
    public sealed class Os4VmFactAttribute : FactAttribute
    {
        public Os4VmFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("ESHARP_OS4_VM_TESTS") != "1")
                Skip = "boots the AmigaOS 4.1 VM: set ESHARP_OS4_VM_TESTS=1 and ESHARP_OS4_VM to run it";
            else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ESHARP_OS4_VM")))
                Skip = "ESHARP_OS4_VM_TESTS=1 but ESHARP_OS4_VM is not set";
        }
    }

    [Fact]
    public void Unset_vm_setting_gives_the_clear_error()
    {
        Assert.Null(Os4Vm.FindVm(null, out var error));
        Assert.Equal("set ESHARP_OS4_VM to your AmigaOS 4.1 VM dir (see os4vm/README.md next to esharp.dll, tools/os4vm/README.md in the repository)", error);
        Assert.Null(Os4Vm.FindVm("  ", out error));
        Assert.StartsWith("set ESHARP_OS4_VM", error);
    }

    [Fact]
    public void Stale_pid_file_reaps_only_a_qemu_that_belongs_to_its_work_dir()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/bin/bash")) return;
        var root = Path.Combine(Path.GetTempPath(), "esharp-os4-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(root, "esharp-os4-work");
        var other = Path.Combine(root, "other");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(other);
        var pidFile = Path.Combine(root, "pid");
        // A sleep whose argv[0] is <dir>/qemu-system-ppc, so the command line looks like the real thing.
        Process Fake(string dir)
        {
            var psi = new ProcessStartInfo("/bin/bash") { UseShellExecute = false };
            foreach (var a in new[] { "-c", "exec -a \"$0\" sleep 300", Path.Combine(dir, "qemu-system-ppc") }) psi.ArgumentList.Add(a);
            var p = Process.Start(psi)!;
            Thread.Sleep(300);   // let the exec happen
            return p;
        }
        using var stranger = Fake(other);   // same name, but not in the work dir: a reused pid must be left alone
        using var ours = Fake(work);
        try
        {
            File.WriteAllText(pidFile, $"{stranger.Id}\n{work}\n");
            Assert.False(Os4Vm.ReapStale(pidFile));
            Assert.False(stranger.HasExited);
            Assert.False(File.Exists(pidFile));
            Assert.False(Directory.Exists(work));   // the work dir is removed either way

            Directory.CreateDirectory(work);
            File.WriteAllText(pidFile, $"{ours.Id}\n{work}\n");
            Assert.True(Os4Vm.ReapStale(pidFile));
            Assert.True(ours.HasExited);
            Assert.False(stranger.HasExited);
        }
        finally
        {
            if (!stranger.HasExited) stranger.Kill();
            if (!ours.HasExited) ours.Kill();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Vm_dir_must_hold_bboot_kickstart_and_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "esharp-os4vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "bboot"), "");
            Assert.Null(Os4Vm.FindVm(dir, out var error));
            Assert.Contains("lacks Kickstart.zip, amiga.img", error);
            File.WriteAllText(Path.Combine(dir, "Kickstart.zip"), "");
            File.WriteAllText(Path.Combine(dir, "amiga.img"), "");
            Assert.Equal(Path.GetFullPath(dir), Os4Vm.FindVm(dir, out error));
            Assert.Null(error);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Autorun_pipes_the_program_through_estype_between_markers()
    {
        var s = Os4Vm.Autorun("cards", "n0nce");
        var lines = s.Split('\n');
        var begin = Array.IndexOf(lines, "ESTEST:estype -t \"ESHARP-BEGIN n0nce\"");
        var run = Array.IndexOf(lines, "ESTEST:cards | ESTEST:estype");
        var end = Array.IndexOf(lines, "ESTEST:estype -t \"ESHARP-END n0nce\"");
        Assert.True(begin >= 0 && run > begin && end > run, s);
        Assert.Contains("FailAt 1000000", lines);   // a FAIL return code must not stop the script before the end marker
        Assert.Contains("CD RAM:", lines);          // a writable current directory (the CD is read-only)
    }

    [Fact]
    public void Autorun_puts_the_argument_line_after_the_program_name()
    {
        var lines = Os4Vm.Autorun("cards", "n0nce", "SEED=42 \"two words\"").Split('\n');
        Assert.Contains("ESTEST:cards SEED=42 \"two words\" | ESTEST:estype", lines);
        Assert.Contains("ESTEST:cards | ESTEST:estype", Os4Vm.Autorun("cards", "n0nce", "  ").Split('\n'));
    }

    private const string Serial =
        "Booting config 1: AmigaOS_4.1_Final_Edition\n" +
        "ES: old line from an earlier run\n" +
        "ESHARP-BEGIN abc\n" +
        "\n" +
        "No CMI8738 found! :-(\n" +
        "ES: ok first\n" +
        "trace start\n" +
        "ES: FAIL second\n" +
        "ES: \n" +
        "ES: SHOT\n" +
        "ES: ok third\n" +
        "ESHARP-END abc\n" +
        "ES: after the end\n";

    [Fact]
    public void Serial_parse_keeps_program_lines_between_the_markers()
    {
        var st = Os4Vm.Parse(Serial, "abc");
        Assert.True(st.Begun);
        Assert.True(st.Ended);
        Assert.True(st.Shot);
        Assert.Equal("ok first\nFAIL second\n\nSHOT\nok third\n", st.Output);
        Assert.Equal("No CMI8738 found! :-(\ntrace start\n", st.Other);
        var report = TestReport.Parse(st.Output);
        Assert.Equal(2, report.Ok);
        Assert.Equal(1, report.Failed);
    }

    [Fact]
    public void Serial_parse_takes_the_prefix_only_at_the_start_of_a_line()
    {
        var serial =
            "ESHARP-BEGIN abc\r\n" +
            "driver says ES: not ours\r\n" +               // a prefix in the middle of a kernel line
            "[x] ES: nor this\r\n" +
            "\0\x1b\rES: ok after control characters\r\n" +  // stray control characters before the prefix are noise
            "ES: ok value ES: inside\r\n" +                // the program's own text may contain the prefix
            "ES: \r\n" +
            "ESHARP-END abc\r\n";
        var st = Os4Vm.Parse(serial, "abc");
        Assert.Equal("ok after control characters\nok value ES: inside\n\n", st.Output);
        Assert.Equal("driver says ES: not ours\n[x] ES: nor this\n", st.Other);
    }

    [Fact]
    public void Input_script_becomes_monitor_commands()
    {
        var steps = Os4Input.Translate("click 208,101;rel -5,3;press;key 69;key 96+32").Select(x => x.Command).ToList();
        // click: into the top-left corner, then right/down by X,Y; the chunks add up
        var moves = steps.TakeWhile(c => c.StartsWith("mouse_move")).Select(c => c.Split(' ')).ToList();
        Assert.Equal(-4000 + 208, moves.Sum(m => int.Parse(m[1])));
        Assert.Equal(-4000 + 101, moves.Sum(m => int.Parse(m[2])));
        Assert.All(moves, m => Assert.InRange(int.Parse(m[1]), -Os4Input.Chunk, Os4Input.Chunk));
        var rest = steps.Skip(moves.Count).ToList();
        Assert.Equal(new[] { "mouse_button 1", "mouse_button 0", "mouse_move -5 3", "mouse_button 1", "mouse_button 0", "sendkey esc", "sendkey shift-a" }, rest);
        Assert.Throws<FormatException>(() => Os4Input.Translate("wiggle 1,2"));
        Assert.Throws<FormatException>(() => Os4Input.Translate("key 255"));   // no such Amiga raw code
        Assert.Throws<FormatException>(() => Os4Input.Translate("click 5"));
    }

    [Fact]
    public void Amiga_raw_codes_map_to_qemu_key_names()
    {
        Assert.Equal("esc", Os4Input.KeyName(0x45));
        Assert.Equal("a", Os4Input.KeyName(0x20));
        Assert.Equal("q", Os4Input.KeyName(0x10));
        Assert.Equal("m", Os4Input.KeyName(0x37));
        Assert.Equal("1", Os4Input.KeyName(0x01));
        Assert.Equal("0", Os4Input.KeyName(0x0A));
        Assert.Equal("f10", Os4Input.KeyName(0x59));
        Assert.Equal("shift", Os4Input.KeyName(0x60));
        Assert.Equal("ret", Os4Input.KeyName(0x44));
        Assert.Null(Os4Input.KeyName(0x7F));
    }

    [Fact]
    public void Serial_parse_waits_for_markers_and_whole_lines()
    {
        Assert.False(Os4Vm.Parse(Serial, "other").Begun);              // another run's nonce
        var cut = Serial[..Serial.IndexOf("ES: SHOT", StringComparison.Ordinal)] + "ES: SH";
        var st = Os4Vm.Parse(cut, "abc");
        Assert.True(st.Begun);
        Assert.False(st.Ended);
        Assert.False(st.Shot);                                          // "SH" is still arriving
        Assert.Equal("ok first\nFAIL second\n\n", st.Output);
        var noEndNewline = Serial[..(Serial.IndexOf("ESHARP-END", StringComparison.Ordinal) + "ESHARP-END abc".Length)];
        Assert.False(Os4Vm.Parse(noEndNewline, "abc").Ended);           // the marker counts once its line is complete
        Assert.True(Os4Vm.Parse(noEndNewline + "\r\n", "abc").Ended);
    }

    [Fact]
    public void Qemu_runs_headless_on_the_overlay_with_kernel_debug_on_serial()
    {
        var a = Os4Vm.QemuArgs("/vm", "/tmp/x/overlay.qcow2", "/o/estest.iso", "/o/serial.log", "/tmp/x/mon.sock");
        string After(string opt) => a[a.IndexOf(opt) + 1];
        Assert.Equal("pegasos2", After("-M"));
        Assert.Equal("/vm/bboot", After("-kernel").Replace('\\', '/'));
        Assert.Equal("/vm/Kickstart.zip", After("-initrd").Replace('\\', '/'));
        Assert.Equal("serial", After("-append"));
        Assert.Equal("none", After("-display"));
        Assert.Equal("file:/o/serial.log", After("-serial"));
        Assert.Equal("unix:/tmp/x/mon.sock,server,nowait", After("-monitor"));
        Assert.Contains("format=raw,file=/o/estest.iso,index=2,media=cdrom,readonly=on", a);
        Assert.Contains("format=qcow2,file=/tmp/x/overlay.qcow2,index=0,media=disk", a);
        Assert.DoesNotContain(a, x => x.Contains("amiga.img"));         // test runs never open the installed disk itself
    }

    [Fact]
    public void Estype_source_is_embedded_in_esharp()
    {
        using var s = typeof(Os4Vm).Assembly.GetManifestResourceStream("estype.c");
        Assert.NotNull(s);
        var text = new StreamReader(s!).ReadToEnd();
        Assert.Contains("DebugPrintF", text);
        Assert.Contains("\"ES: %s", text);
    }

    [Fact]
    public void Os4_trace_uses_DebugPrintF_and_dos_gets_the_classic_names()
    {
        var c = CompileOk(Program("", "Trace.Line(\"x\"); Dos.PutStr(\"hi\\n\"); Dos.FlushOutput();"), Targets.Os4.ToOptions(trace: true));
        Assert.Contains("#elif defined(__amigaos4__)", c);
        Assert.Contains("(IExec->DebugPrintF)(\"%s\", s);", c);
        var dos = c.IndexOf("#include <proto/dos.h>", StringComparison.Ordinal);
        var obsolete = c.IndexOf("#include <dos/obsolete.h>", StringComparison.Ordinal);
        Assert.True(dos >= 0 && obsolete > dos, "dos/obsolete.h after proto/dos.h");
    }

    [Fact]
    public void Os4_gets_the_cybergraphics_interface_through_SetBase_and_drops_it_again()
    {
        var c = CompileOk(Program("", "Video.HasRtg(320, 200);"), Targets.Os4.ToOptions());
        Assert.Contains("struct CyberGfxIFace *ICyberGfx;", c);
        Assert.Contains("GetInterface(lib, \"main\", 1, NULL)", c);
        Assert.Contains("DropInterface((struct Interface *)ICyberGfx)", c);
        Assert.Contains("ES_SETBASE_CyberGfxBase(", c);
        // a failed GetInterface leaves the base null, and Video sees that through Ready()
        Assert.Contains("== NULL) lib = NULL;", c);
        Assert.Contains("(es_CyberGfxBase != NULL)", c);
        // every other target takes the #else branch: the plain base variable, as before
        Assert.Contains("#else\n#define ES_SETBASE_CyberGfxBase(l) (es_CyberGfxBase = (__typeof__(es_CyberGfxBase))(l))\n#endif", c.Replace("\r", ""));
    }

    [Fact]
    public void Trace_helpers_compile_with_host_gcc_when_not_amigaos4()
    {
        // The os4 branch must not disturb the other branches of the same block: on the host C compiler (neither m68k,
        // AROS nor AmigaOS 4) es_rawputchar is the no-op.
        if (!HasGcc) return;
        var c = CompileOk(Program("", "Trace.Line(\"x\");"), Targets.Os4.ToOptions(trace: true));
        var start = c.IndexOf("#if defined(__AROS__)", StringComparison.Ordinal);
        var end = c.IndexOf("#endif", c.IndexOf("no serial trace on this target yet", StringComparison.Ordinal), StringComparison.Ordinal);
        var block = c[start..(end + "#endif".Length)];
        var (exit, output) = RunWithHostGcc("#include <stdint.h>\n" + block + "\nint main(void) { es_rawioinit(); es_rawputchar('a'); return 7; }\n");
        Assert.True(exit == 7, output);
    }

    // ---- in the VM (only when ESHARP_OS4_VM_TESTS=1 and ESHARP_OS4_VM are set; boots AmigaOS 4.1 for about 10 s) ----

    [Os4VmFact]
    public void Program_runs_in_the_os4_vm_when_one_is_configured()
    {
        const string src = @"
public static class P
{
    [EntryPoint]
    public static void Main()
    {
        Dos.PutStr(""ok os4 output\n"");
        Trace.Line(""os4 trace"");
        Dos.PutStr(""SHOT\n"");
        Dos.FlushOutput();
    }
}";
        var dir = Path.Combine(Path.GetTempPath(), "esharp-os4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "prog.c"), CompileOk(Prelude + src, Targets.Os4.ToOptions(trace: true)));
            var build = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "run", "--rm", "--user", $"{getuid()}:{getgid()}", "-v", $"{dir}:/work", "-w", "/work",
                         "esharp-amiga-toolchain", "ppc-amigaos-gcc", "-O2", "-fwrapv", "-D__USE_INLINE__", "-o", "prog", "prog.c", "-lauto" })
                build.ArgumentList.Add(a);
            using (var b = Process.Start(build)!)
            {
                var err = b.StandardError.ReadToEnd();
                b.WaitForExit();
                Assert.True(b.ExitCode == 0, err);
            }
            var png = Path.Combine(dir, "shot.png");
            var serial = Path.Combine(dir, "trace.txt");
            var (exit, output) = Os4Vm.Run(Path.Combine(dir, "prog"), TimeSpan.FromSeconds(120), null, png, serial, echo: false);
            Assert.Equal(0, exit);
            Assert.Equal("ok os4 output\nSHOT\n", output);
            Assert.Contains("os4 trace", File.ReadAllText(serial));
            Assert.True(new FileInfo(png).Length > 100);
        }
        finally { Directory.Delete(dir, true); }
    }

    [System.Runtime.InteropServices.DllImport("libc")] private static extern uint getuid();
    [System.Runtime.InteropServices.DllImport("libc")] private static extern uint getgid();
}

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
    [Fact]
    public void Unset_vm_setting_gives_the_clear_error()
    {
        Assert.Null(Os4Vm.FindVm(null, out var error));
        Assert.Equal("set ESHARP_OS4_VM to your AmigaOS 4.1 VM dir (see tools/os4vm/README.md)", error);
        Assert.Null(Os4Vm.FindVm("  ", out error));
        Assert.StartsWith("set ESHARP_OS4_VM", error);
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
        Assert.Equal("/vm/bboot", After("-kernel"));
        Assert.Equal("/vm/Kickstart.zip", After("-initrd"));
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

    // ---- in the VM (only when ESHARP_OS4_VM is set; boots AmigaOS 4.1 for about 10 s) ----

    [Fact]
    public void Program_runs_in_the_os4_vm_when_one_is_configured()
    {
        var vm = Environment.GetEnvironmentVariable("ESHARP_OS4_VM");
        if (string.IsNullOrEmpty(vm)) return;
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

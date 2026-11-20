using ESharp.Cli;
using ESharp.Compiler;

namespace ESharp.Compiler.Tests;

/// <summary>
/// esharp test --target aros|aros-i386 on hosted AROS (src/ESharp.Cli/ArosHosted.cs) and the return code of the target
/// program on every runner: the startup lines, the RC and trap parsing, the image choice, the morphos message and the
/// verdict that compares a target run with the host run.
/// </summary>
public class ArosHostedTests
{
    [Fact]
    public void Startup_lines_run_the_program_then_record_its_RC_and_the_done_marker()
    {
        var lines = ArosHosted.StartupLines("language").Split('\n');
        var failAt = Array.IndexOf(lines, "FailAt 1000000");
        var run = Array.IndexOf(lines, "SYS:language >SYS:language.out");
        var rc = Array.IndexOf(lines, "Echo >SYS:esharp-rc.txt \"RC $RC\"");
        var done = Array.IndexOf(lines, "Echo >SYS:done.txt done");
        Assert.True(failAt >= 0 && run > failAt && rc == run + 1 && done > rc, string.Join("\n", lines));
        Assert.Contains("FailAt 21", lines);   // the rest of AROS's own startup sequence keeps its limit
        Assert.Contains("SYS:mainargs >SYS:mainargs.out ONE \"two words\"", ArosHosted.StartupLines("mainargs", " ONE \"two words\" ").Split('\n'));
    }

    [Fact]
    public void The_run_script_is_plain_bash_with_the_heredoc_end_at_the_start_of_a_line()
    {
        Assert.StartsWith("#!/bin/bash\n", ArosHosted.Script.Replace("\r\n", "\n"));
        var lines = ArosHosted.Script.Replace("\r\n", "\n").Split('\n');
        Assert.Contains("P", lines);                                   // the python heredoc terminator, not indented
        Assert.Contains(lines, l => l.Contains("AROSBootstrap", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("AROS_HOSTED_ARCH", StringComparison.Ordinal));
    }

    [Fact]
    public void Return_code_lines_parse_and_anything_else_is_unknown()
    {
        Assert.Equal(0, ArosHosted.ParseReturnCode("RC 0\n"));
        Assert.Equal(25, ArosHosted.ParseReturnCode("RC 25\r\n"));
        Assert.Equal(-3, ArosHosted.ParseReturnCode("RC -3"));
        Assert.Null(ArosHosted.ParseReturnCode(null));
        Assert.Null(ArosHosted.ParseReturnCode(""));
        Assert.Null(ArosHosted.ParseReturnCode("RC $RC\n"));   // a shell without the RC variable leaves it unexpanded
    }

    [Fact]
    public void Traps_are_counted_from_the_debug_log()
    {
        Assert.Equal(0, ArosHosted.CountTraps(null));
        Assert.Equal(0, ArosHosted.CountTraps("[KRN] booting\nall fine\n"));
        Assert.Equal(2, ArosHosted.CountTraps("x\n*** Trap 14 at 0x1234\nmore\n[Alert] Trap in task language\n"));
    }

    [Fact]
    public void Each_aros_target_has_its_own_hosted_image_and_arch()
    {
        Assert.Equal("esharp-aros-hosted", ArosHosted.DefaultImage(Targets.Aros));
        Assert.Equal("esharp-aros-hosted-i386", ArosHosted.DefaultImage(Targets.ArosI386));
        Assert.Equal("x86_64", ArosHosted.Arch(Targets.Aros));
        Assert.Equal("i386", ArosHosted.Arch(Targets.ArosI386));
    }

    [Fact]
    public void Fs_uae_headless_startup_raises_FailAt_and_records_RC_before_the_done_marker()
    {
        var lines = FsUae.HeadlessLines("cards", " SEED=42").Split('\n');
        Assert.Equal(new[] { "FailAt 1000000", "dh0:cards >dh0:out.txt SEED=42", "echo >dh0:rc.txt \"RC $RC\"", "echo done >dh0:done", "" }, lines);
    }

    [Fact]
    public void Test_targets_include_aros_and_morphos_says_it_has_no_runner()
    {
        foreach (var t in new[] { "m68k", "os4", "sdl", "aros", "aros-i386" }) Assert.Null(Program.TestTargetError(t));
        Assert.StartsWith("no runner for morphos", Program.TestTargetError("morphos"));
        Assert.Contains("not 'amiga'", Program.TestTargetError("amiga"));
    }

    [Fact]
    public void Morphos_test_fails_with_the_no_runner_message()
    {
        var err = new StringWriter();
        var saved = Console.Error;
        Console.SetError(err);
        int rc;
        try { rc = Program.Test(new[] { "nowhere", "--target", "morphos" }); }
        finally { Console.SetError(saved); }
        Assert.Equal(1, rc);
        Assert.Contains("no runner for morphos", err.ToString());
    }

    private static readonly TestReport Host = TestReport.Parse("ok a\nok b\n");

    [Fact]
    public void Judge_passes_a_matching_run_with_return_code_0()
    {
        var v = TestReport.Judge(Host, 0, TestReport.Parse("ok a\nok b\n"), "AROS", 0, 0);
        Assert.True(v.Ok);
        Assert.Equal("AROS: 2 ok, 0 FAIL, return code 0, 0 trap(s)", v.Lines[0]);
        Assert.Equal("Host and AROS agree on every check.", v.Lines[^1]);
        Assert.Equal("Amiga: 2 ok, 0 FAIL, return code unknown", TestReport.Judge(Host, 0, TestReport.Parse("ok a\nok b\n"), "Amiga", null).Lines[0]);
    }

    [Fact]
    public void Judge_fails_a_non_zero_target_return_code_when_the_host_returned_0()
    {
        var v = TestReport.Judge(Host, 0, TestReport.Parse("ok a\nok b\n"), "Amiga", 5);
        Assert.False(v.Ok);
        Assert.Contains("Amiga returned 5, the host 0", v.Lines);
        Assert.Contains("Host and Amiga agree on every check.", v.Lines);   // the checks agree; only the return code fails it
    }

    [Fact]
    public void Judge_only_notes_a_different_return_code_when_the_host_failed_too()
    {
        // The host's own non-zero exit fails the test already; the target side is judged on its own lines.
        var v = TestReport.Judge(Host, 3, TestReport.Parse("ok a\nok b\n"), "OS4", 0);
        Assert.True(v.Ok);
        Assert.Contains("Return codes differ: host 3, OS4 0", v.Lines);
        Assert.True(TestReport.Judge(Host, 3, TestReport.Parse("ok a\nok b\n"), "OS4", 3).Ok);
    }

    [Fact]
    public void Judge_counts_traps_FAIL_lines_and_differences_as_failures()
    {
        var trap = TestReport.Judge(Host, 0, TestReport.Parse("ok a\nok b\n"), "AROS", 0, 1);
        Assert.False(trap.Ok);
        Assert.Contains(trap.Lines, l => l.Contains("1 trap line(s)", StringComparison.Ordinal));
        Assert.False(TestReport.Judge(Host, 0, TestReport.Parse("ok a\nFAIL b\n"), "AROS", 0, 0).Ok);
        var missing = TestReport.Judge(Host, 0, TestReport.Parse("ok a\n"), "AROS", null, 0);   // a crash after the first check
        Assert.False(missing.Ok);
        Assert.Contains("  b: host ok, AROS missing", missing.Lines);
    }
}

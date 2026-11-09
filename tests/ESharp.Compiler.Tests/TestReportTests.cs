using ESharp.Cli;

namespace ESharp.Compiler.Tests;

public class TestReportTests
{
    [Fact]
    public void Parse_counts_ok_and_FAIL_lines_and_ignores_the_rest()
    {
        var r = TestReport.Parse("clock 100\r\nok a b\nFAIL c\nokay\nok d\n");
        Assert.Equal(2, r.Ok);
        Assert.Equal(1, r.Failed);
        Assert.Equal("a b", r.Checks[0].Name);
    }

    [Fact]
    public void Compare_reports_fail_and_missing_on_either_side()
    {
        var host = TestReport.Parse("ok a\nok b\nok c\n");
        var amiga = TestReport.Parse("ok a\nFAIL b\nok d\n");
        var diffs = TestReport.Compare(host, amiga);
        Assert.Equal(new[] { "b: host ok, Amiga FAIL", "c: host ok, Amiga missing", "d: host missing, Amiga ok" }, diffs);
    }

    [Fact]
    public void Compare_matches_repeated_names_by_occurrence_and_agrees_when_equal()
    {
        Assert.Empty(TestReport.Compare(TestReport.Parse("ok x\nok x\n"), TestReport.Parse("ok x\nok x\n")));
        Assert.Single(TestReport.Compare(TestReport.Parse("ok x\nok x\n"), TestReport.Parse("ok x\n")));
    }

    [Fact]
    public void Host_project_lists_the_sources_and_defines_HOST()
    {
        var text = HostRun.ProjectText("/repo", new[] { "/p/A.cs" });
        Assert.Contains("HOST", text);
        Assert.Contains("/p/A.cs", text);
        Assert.Contains("ESharp.Target.Host.csproj", text);
    }
}

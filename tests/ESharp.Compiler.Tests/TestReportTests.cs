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
    public void A_skipped_check_is_neither_ok_nor_FAIL_and_never_a_difference()
    {
        var host = TestReport.Parse("ok a\nok b\nok c\n");
        var os4 = TestReport.Parse("ok a\nskip b\nskip c\n");
        Assert.Equal(1, os4.Ok);
        Assert.Equal(0, os4.Failed);
        Assert.Equal(2, os4.Skipped);
        Assert.Equal("1 ok, 0 FAIL, 2 skipped", os4.Summary);
        Assert.Equal("3 ok, 0 FAIL", host.Summary);
        Assert.Empty(TestReport.Compare(host, os4, "OS4"));
        Assert.Empty(TestReport.Compare(os4, host, "OS4"));
        // A check that is missing without a skip line is still a difference.
        Assert.Equal(new[] { "c: host ok, OS4 missing" }, TestReport.Compare(host, TestReport.Parse("ok a\nskip b\n"), "OS4"));
    }

    [Fact]
    public void Host_project_lists_the_sources_and_defines_HOST()
    {
        var text = HostRun.ProjectText("/repo", new[] { "/p/A.cs" });
        Assert.Contains("HOST", text);
        Assert.Contains("/p/A.cs", text);
        Assert.Contains("ESharp.Target.Host.csproj", text);
    }

    [Fact]
    public void Host_project_references_the_packed_host_assemblies_without_a_checkout()
    {
        var libs = Path.Combine(Path.GetTempPath(), "esharp-hostlibs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(libs);
        try
        {
            File.WriteAllText(Path.Combine(libs, "ESharp.Target.Host.dll"), "");
            var text = HostRun.ProjectText(libs, new[] { "/p/A.cs" });
            var doc = System.Xml.Linq.XDocument.Parse(text);
            Assert.Empty(doc.Descendants("ProjectReference"));
            var hints = doc.Descendants("HintPath").Select(h => Path.GetFileName(h.Value)).ToList();
            Assert.Equal(new[] { "ESharp.Runtime.dll", "ESharp.Platform.dll", "ESharp.Target.Host.dll" }, hints);
            Assert.Contains("HOST", text);
        }
        finally { Directory.Delete(libs, true); }
    }
}

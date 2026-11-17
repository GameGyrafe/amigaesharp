using System;
using System.IO;
using System.Linq;
using ESharp.Cli;
using Xunit;

namespace ESharp.Compiler.Tests;

/// <summary>esharp run/test --mui: option parsing, cache lookup and startup generation (no network, no MUI files).</summary>
public class MuiEmulatorTests
{
    [Theory]
    [InlineData("3.8", "3.8")]
    [InlineData("5", "5")]
    [InlineData("5.0", "5")]
    [InlineData(" 3.8 ", "3.8")]
    public void VersionsAreAccepted(string text, string expected) => Assert.Equal(expected, Mui.ParseVersion(text));

    [Theory]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("3.9")]
    [InlineData("mui")]
    public void OtherVersionsAreRejected(string text) => Assert.Null(Mui.ParseVersion(text));

    [Fact]
    public void NullVersionIsRejected() => Assert.Null(Mui.ParseVersion(null));

    [Fact]
    public void CacheRootPrefersTheOverrideThenXdgThenHome()
    {
        Assert.Equal("/c", Mui.CacheRoot(k => k == "ESHARP_MUI_CACHE" ? "/c" : "/x"));
        Assert.Equal(Path.Combine("/x", "esharp", "mui"), Mui.CacheRoot(k => k == "XDG_CACHE_HOME" ? "/x" : null));
        Assert.Equal(Path.Combine("/h", ".cache", "esharp", "mui"), Mui.CacheRoot(k => k == "HOME" ? "/h" : null));
    }

    [Fact]
    public void ResolveFindsTheUnpackedDrawerOrNamesTheFetchCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), "esharp-mui-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var missing = Mui.Resolve("3.8", root, out var error);
            Assert.Null(missing);
            Assert.Contains("fetch-mui.sh", error);
            Assert.Contains(" 3.8   (MUI is shareware", error);

            var libs = Path.Combine(Mui.DrawerPath("3.8", root), "Libs");
            Directory.CreateDirectory(libs);
            File.WriteAllText(Path.Combine(libs, "muimaster.library"), "x");
            var drawer = Mui.Resolve("3.8", root, out error);
            Assert.Null(error);
            Assert.Equal(Mui.DrawerPath("3.8", root), drawer);
            Assert.Null(Mui.Resolve("5", root, out error));   // another version is separate
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void StartupAssignsWhatMuiNeedsAfterTheWorkbenchDisk()
    {
        var lines = Mui.StartupLines();
        var text = string.Join("\n", lines);
        Assert.StartsWith("DF0:C/Assign C: DF0:C", text);   // C: first: it provides Assign
        Assert.Contains("Assign LIBS: MUI:Libs ADD", lines);
        Assert.Contains("Assign LOCALE: MUI:Locale ADD", lines);
        Assert.Contains("Assign IMAGES: MUI:Images", lines);
        Assert.Contains("Assign ENV: RAM:Env", lines);
        Assert.True(Array.IndexOf(lines.ToArray(), "Assign LIBS: DF0:Libs ADD") < Array.IndexOf(lines.ToArray(), "Assign LIBS: MUI:Libs ADD"));
    }

    [Fact]
    public void ConfigMountsMuiAndTheWorkbenchDiskReadOnly()
    {
        var cfg = Mui.ConfigLines("/m/MUI", "/w/wb.adf");
        Assert.Contains("hard_drive_1 = /m/MUI\n", cfg);
        Assert.Contains("hard_drive_1_label = MUI\n", cfg);
        Assert.Contains("hard_drive_1_read_only = 1\n", cfg);
        Assert.Contains("floppy_drive_0 = /w/wb.adf\n", cfg);
        Assert.Contains("floppy_drive_0_read_only = 1\n", cfg);
        Assert.Contains("hard_drive_0_priority = 20\n", cfg);
    }

    [Fact]
    public void WorkbenchDiskIsRequiredAndMustExist()
    {
        var prev = Environment.GetEnvironmentVariable("ESHARP_WORKBENCH");
        Environment.SetEnvironmentVariable("ESHARP_WORKBENCH", null);
        try
        {
            Assert.Null(Mui.ResolveWorkbench(null, out var error));
            Assert.Contains("--workbench", error);
            Assert.Null(Mui.ResolveWorkbench(Path.Combine(Path.GetTempPath(), "no-such-disk.adf"), out error));
            Assert.Contains("not found", error);
            var adf = Path.GetTempFileName();
            try { Assert.Equal(adf, Mui.ResolveWorkbench(adf, out error)); Assert.Null(error); }
            finally { File.Delete(adf); }
        }
        finally { Environment.SetEnvironmentVariable("ESHARP_WORKBENCH", prev); }
    }

    [Fact]
    public void FetchCommandPrefersTheShippedScript()
    {
        var dir = Path.Combine(Path.GetTempPath(), "es-mui-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(Mui.FetchScript, Mui.FetchCommand(dir));   // not shipped here: the repository path
            Directory.CreateDirectory(Path.Combine(dir, "mui"));
            var script = Path.Combine(dir, "mui", "fetch-mui.sh");
            File.WriteAllText(script, "#!/bin/sh\n");
            Assert.Equal("bash \"" + script + "\"", Mui.FetchCommand(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void ToolPackageShipsTheFetchScript()
    {
        var csproj = File.ReadAllText(FindRepoFile(Path.Combine("src", "ESharp.Cli", "ESharp.Cli.csproj")));
        Assert.Contains("fetch-mui.sh", csproj);
    }

    [Fact]
    public void FetchScriptPinsBothArchives()
    {
        var script = File.ReadAllText(FindRepoFile(Path.Combine("tools", "mui", "fetch-mui.sh")));
        Assert.Contains("f3acb2fe98f74c025daf10ae7df92d08abe787332124963771e2c21a869cca5b", script);
        Assert.Contains("8a869a037db465828dbe42fa7854114121280ed2691afa61fe53a25bf427a847", script);
        Assert.Contains("--network none", script);
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, relative);
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(relative);
    }
}

using System.Diagnostics;
using System.IO.Compression;
using ESharp.Compiler;

namespace ESharp.Compiler.Tests;

/// <summary>
/// The E# library package round trip (issue #31) with the samples/packages projects as they are: dotnet pack the library
/// (samples/packages/Tween) into a local folder feed, dotnet restore the game (samples/packages/TweenDemo) from it, then
/// esharp build and the host run of esharp test, and with the m68k image in the local docker also the m68k build.
/// Nothing comes from nuget.org: the feed holds the library and an AmigaESharp.Sdk made here from this build's host
/// assemblies, analyzer and buildTransitive targets, and the package cache is a temporary folder.
/// </summary>
[Collection("ProcessConsole")]
public class PackageRoundTripTests
{
    private const string SdkVersion = "0.0.1-roundtrip";
    private const string M68kImage = "amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859";

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Library_packs_restores_from_a_local_feed_and_builds_with_the_game()
    {
        var work = Path.Combine(Path.GetTempPath(), "esharp-pkg-" + Guid.NewGuid().ToString("N"));
        var feed = Path.Combine(work, "feed");
        Directory.CreateDirectory(feed);
        try
        {
            // The SDK this checkout pins (global.json): its bundled targeting packs, so restore needs no download.
            File.Copy(Path.Combine(RepoRoot, "global.json"), Path.Combine(work, "global.json"));
            File.WriteAllText(Path.Combine(work, "nuget.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources>\n    <clear />\n" +
                $"    <add key=\"local\" value=\"{System.Security.SecurityElement.Escape(feed)}\" />\n  </packageSources>\n</configuration>\n");
            FakeSdkPackage(feed);
            foreach (var project in new[] { "Tween", "TweenDemo" })
                CopyProject(Path.Combine(RepoRoot, "samples", "packages", project), Path.Combine(work, project));

            var packages = Path.Combine(work, "packages");
            Dotnet(work, packages, "pack", "Tween", "-o", feed, "-p:ESharpSdkVersion=" + SdkVersion);
            var nupkg = Path.Combine(feed, "ESharpSample.Tween.1.0.0.nupkg");
            Assert.True(File.Exists(nupkg), "no package from dotnet pack");

            // The DLL in the package carries the manifest and the sources.
            var dllCopy = Path.Combine(work, "Tween.dll");
            using (var zip = ZipFile.OpenRead(nupkg))
                zip.GetEntry("lib/net10.0/Tween.dll")!.ExtractToFile(dllCopy);
            var lib = PackageReader.ReadDll(dllCopy, "ESharpSample.Tween", "1.0.0");
            Assert.NotNull(lib);
            Assert.Empty(lib!.Problems);
            Assert.Equal(new[] { "m68k", "os4", "morphos", "aros", "aros-i386", "sdl" }, lib.Manifest!.Targets);
            Assert.Equal(SdkVersion, lib.Manifest.MinSdk);   // the default: the referenced AmigaESharp.Sdk's version
            Assert.Equal("MIT", lib.License);
            Assert.Empty(lib.Manifest.Features);
            Assert.Equal(new[] { "Ease.cs", "Tween.cs" }, lib.Sources.Select(s => s.Path).ToArray());

            var game = Path.Combine(work, "TweenDemo");
            Assert.NotNull(ESharp.Cli.PackageStep.Load(game, out _));   // not restored yet: says to run dotnet restore
            Dotnet(work, packages, "restore", "TweenDemo", "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.Null(ESharp.Cli.PackageStep.Load(game, out var found));
            var package = Assert.Single(found.Packages);
            Assert.Equal("ESharpSample.Tween", package.Id);
            Assert.False(found.AllowCopyleft);

            var m68k = HasImage(M68kImage);
            var output = new StringWriter();
            var saved = Console.Out;
            Console.SetOut(output);
            try
            {
                Assert.Equal(0, ESharp.Cli.Program.Build(new[] { game, "--emit-only" }, out _));
                Assert.Equal(0, ESharp.Cli.Program.Test(new[] { game }));
                if (m68k)
                {
                    Assert.Equal(0, ESharp.Cli.Program.Build(new[] { game, "--target", "m68k", "--docker", M68kImage }, out var exe));
                    Assert.True(File.Exists(exe) && new FileInfo(exe!).Length > 0, "no m68k program");
                }
            }
            finally { Console.SetOut(saved); }
            var text = output.ToString().Replace("\r\n", "\n");
            Assert.Contains("Package ESharpSample.Tween 1.0.0 (MIT): 2 C# file(s)", text);
            Assert.Contains("Host: 12 ok, 0 FAIL", text);
            Assert.Contains("TEST PASSED", text);
            if (m68k) Assert.Contains("Built " + Path.Combine(game, "obj", "m68k", "TweenDemo"), text);
            Assert.Contains("\"<package ESharpSample.Tween 1.0.0>/Tween.cs\"", File.ReadAllText(Path.Combine(game, "obj", "m68k", "TweenDemo.c")));
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// An AmigaESharp.Sdk package as src/ESharp.Sdk packs it (lib/net10.0 host assemblies, the analyzer, buildTransitive targets),
    /// from what this test build already has, so the round trip does not rebuild src/ or depend on a published version.
    /// </summary>
    private static void FakeSdkPackage(string feed)
    {
        var path = Path.Combine(feed, $"AmigaESharp.Sdk.{SdkVersion}.nupkg");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var nuspec = zip.CreateEntry("AmigaESharp.Sdk.nuspec");
        using (var w = new StreamWriter(nuspec.Open()))
            w.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">\n" +
                    $"  <metadata>\n    <id>AmigaESharp.Sdk</id>\n    <version>{SdkVersion}</version>\n    <authors>test</authors>\n" +
                    "    <description>E# host libraries for the package round-trip test</description>\n  </metadata>\n</package>\n");
        foreach (var dll in new[] { "ESharp.Runtime.dll", "ESharp.Platform.dll", "ESharp.Target.Host.dll" })
            zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, dll), "lib/net10.0/" + dll);
        zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "ESharp.Analyzers.dll"), "analyzers/dotnet/cs/ESharp.Analyzers.dll");
        zip.CreateEntryFromFile(Path.Combine(RepoRoot, "src", "ESharp.Sdk", "buildTransitive", "AmigaESharp.Sdk.targets"), "buildTransitive/AmigaESharp.Sdk.targets");
    }

    private static void CopyProject(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))   // the project's own files (no bin/ or obj/ of a local build)
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }

    private static void Dotnet(string dir, string packages, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("-nologo");
        psi.Environment["NUGET_PACKAGES"] = packages;
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"dotnet {string.Join(" ", args)} failed:\n{output}\n{err.Result}");
    }

    private static bool HasImage(string image)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("docker") { ArgumentList = { "image", "inspect", image }, RedirectStandardOutput = true, RedirectStandardError = true })!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}

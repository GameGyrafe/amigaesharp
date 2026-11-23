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
            foreach (var project in new[] { "Tween", "Checksum", "TweenDemo" })
                CopyProject(Path.Combine(RepoRoot, "samples", "packages", project), Path.Combine(work, project));

            var packages = Path.Combine(work, "packages");
            // A symbol of the library's own (DefineConstants): carried in the manifest for the Amiga parse of its files.
            var csproj = Path.Combine(work, "Tween", "Tween.csproj");
            var projectText = File.ReadAllText(csproj);
            const string ownSymbol = "<DefineConstants>$(DefineConstants);TWEEN_OWN</DefineConstants>\n    <EsLibrary>true</EsLibrary>";
            File.WriteAllText(csproj, projectText.Replace("<EsLibrary>true</EsLibrary>", ownSymbol));

            // <Using> items and E# target symbols stop the library build (their effect would be missing from the Amiga build).
            foreach (var (code, change) in new[]
                     {
                         ("ESL008", "<ItemGroup><Using Include=\"System\" /></ItemGroup>\n</Project>"),
                         ("ESL009", "<PropertyGroup><DefineConstants>$(DefineConstants);AMIGA</DefineConstants></PropertyGroup>\n</Project>"),
                     })
            {
                File.WriteAllText(csproj, projectText.Replace("</Project>", change));
                var (exit, log) = DotnetResult(work, packages, "build", "Tween", "-p:ESharpSdkVersion=" + SdkVersion);
                Assert.True(exit != 0 && log.Contains(code), $"dotnet build with {code} expected to fail:\n{log}");
            }
            File.WriteAllText(csproj, projectText.Replace("<EsLibrary>true</EsLibrary>", ownSymbol));

            // The library's assets: Assets.g.cs (written by esharp assets) is the package's asset data. Missing, it stops the build;
            // older than an input, it is a warning; an assets.json without a namespace of its own stops the build.
            var lib0 = Path.Combine(work, "Tween");
            var generated = Path.Combine(lib0, "Assets.g.cs");
            var assetsJson = Path.Combine(lib0, "assets.json");
            var old = DateTime.UtcNow.AddHours(-2);
            foreach (var input in new[] { assetsJson, Path.Combine(lib0, "ball.png"), Path.Combine(lib0, "blip.wav") }) File.SetLastWriteTimeUtc(input, old);
            File.SetLastWriteTimeUtc(generated, old.AddMinutes(1));
            File.Move(generated, generated + ".away");
            var (exit5, log5) = DotnetResult(work, packages, "build", "Tween", "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.True(exit5 != 0 && log5.Contains("ESL005") && log5.Contains("run esharp assets"), $"dotnet build without Assets.g.cs expected to fail:\n{log5}");
            File.Move(generated + ".away", generated);
            var assetsText = File.ReadAllText(assetsJson);
            File.WriteAllText(assetsJson, assetsText.Replace("\"namespace\": \"Tween\",", "// no namespace\n"));
            var (exit12, log12) = DotnetResult(work, packages, "build", "Tween", "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.True(exit12 != 0 && log12.Contains("ESL012"), $"dotnet build with no assets namespace expected to fail:\n{log12}");
            File.WriteAllText(assetsJson, assetsText);
            // A fresh clone: git writes Assets.g.cs (capital A) before its inputs, so every input is newer. Same contents: no warning.
            var ball = Path.Combine(lib0, "ball.png");
            foreach (var input in new[] { assetsJson, ball, Path.Combine(lib0, "blip.wav") }) File.SetLastWriteTimeUtc(input, old.AddMinutes(5));
            var (exitClone, logClone) = DotnetResult(work, packages, "build", "Tween", "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.True(exitClone == 0 && !logClone.Contains("ESL011"), $"dotnet build of a fresh clone expected not to warn:\n{logClone}");
            // ball.png edited after the last esharp assets (an older time than Assets.g.cs even, so only the content tells): a warning.
            var ballBytes = File.ReadAllBytes(ball);
            File.WriteAllBytes(ball, ballBytes.Concat(new byte[] { 0 }).ToArray());
            File.SetLastWriteTimeUtc(ball, old);
            var (exit11, log11) = DotnetResult(work, packages, "build", "Tween", "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.True(exit11 == 0 && log11.Contains("ESL011") && log11.Contains("Assets.g.cs was not made from the current ball.png;"), $"dotnet build with stale assets expected to warn:\n{log11}");
            File.WriteAllBytes(ball, ballBytes);
            // An Assets.g.cs without the input lines (an older esharp assets): checked by file time, so a newer input warns.
            var generatedText = File.ReadAllText(generated);
            File.WriteAllText(generated, string.Join("\n", generatedText.Split('\n').Where(l => !l.StartsWith("// input sha256:"))));
            foreach (var input in new[] { assetsJson, ball, Path.Combine(lib0, "blip.wav") }) File.SetLastWriteTimeUtc(input, old);
            File.SetLastWriteTimeUtc(generated, old.AddMinutes(1));
            File.SetLastWriteTimeUtc(ball, old.AddMinutes(5));
            var (exitOld, logOld) = DotnetResult(work, packages, "build", "Tween", "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.True(exitOld == 0 && logOld.Contains("ESL011") && logOld.Contains("the current ball.png;"), $"dotnet build with an older Assets.g.cs expected to warn by time:\n{logOld}");
            File.WriteAllText(generated, generatedText);
            foreach (var input in new[] { assetsJson, ball, Path.Combine(lib0, "blip.wav") }) File.SetLastWriteTimeUtc(input, old.AddMinutes(5));
            foreach (var dir in new[] { "bin", "obj" })
                if (Directory.Exists(Path.Combine(work, "Tween", dir))) Directory.Delete(Path.Combine(work, "Tween", dir), true);

            // The C library (ESharpSample.Checksum): C files outside native/common or native/<target>, other files in native/ and prebuilt
            // code anywhere stop the build (ESL006); C files without NativeC in EsFeatures, or NativeC without C files, too (ESL015);
            // so does a native prefix that is E#'s own (ESL014).
            var cks = Path.Combine(work, "Checksum");
            var cksProj = Path.Combine(cks, "Checksum.csproj");
            var cksText = File.ReadAllText(cksProj);
            foreach (var (code, file, says) in new[]
                     {
                         ("ESL006", "stray.c", "stray.c (C files and headers go in native/common/ or native/<target>/)"),
                         ("ESL006", Path.Combine("native", "m68k", "fast.o"), "native/m68k/fast.o (prebuilt code: a package carries C source only)"),
                         ("ESL006", Path.Combine("native", "amigaos", "x.c"), "native/amigaos/x.c (C files go in native/common/ or native/<target>/"),
                         ("ESL006", "libfast.a", "libfast.a (prebuilt code: a package carries C source only)"),
                     })
            {
                var path = Path.Combine(cks, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "int x;\n");
                var (exitC, logC) = DotnetResult(work, packages, "build", "Checksum", "-p:ESharpSdkVersion=" + SdkVersion);
                Assert.True(exitC != 0 && logC.Contains(code) && logC.Contains(says), $"dotnet build with {file} expected to fail with {code}:\n{logC}");
                File.Delete(path);
            }
            foreach (var (code, from, to) in new[]
                     {
                         ("ESL015", "<EsFeatures>Native;NativeC</EsFeatures>", "<EsFeatures>Native</EsFeatures>"),
                         ("ESL014", "<EsNativePrefix>cksum_</EsNativePrefix>", "<EsNativePrefix>es_cksum_</EsNativePrefix>"),
                     })
            {
                File.WriteAllText(cksProj, cksText.Replace(from, to));
                var (exitC, logC) = DotnetResult(work, packages, "build", "Checksum", "-p:ESharpSdkVersion=" + SdkVersion);
                Assert.True(exitC != 0 && logC.Contains(code), $"dotnet build with {to} expected to fail with {code}:\n{logC}");
            }
            File.WriteAllText(cksProj, cksText);
            foreach (var dir in new[] { "bin", "obj" })
                if (Directory.Exists(Path.Combine(cks, dir))) Directory.Delete(Path.Combine(cks, dir), true);
            var (cksExit, cksLog) = DotnetResult(work, packages, "pack", "Checksum", "-o", feed, "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.True(cksExit == 0 && !cksLog.Contains("ESL0"), $"dotnet pack of the C library failed or warned:\n{cksLog}");
            var cksDll = Path.Combine(work, "Checksum.dll");
            using (var zip = ZipFile.OpenRead(Path.Combine(feed, "ESharpSample.Checksum.1.0.0.nupkg")))
            {
                zip.GetEntry("lib/net10.0/Checksum.dll")!.ExtractToFile(cksDll);
                // The C is in the DLL only, as source: no C file, object or archive beside it in the package.
                Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".c") || e.FullName.EndsWith(".h") || e.FullName.EndsWith(".o") || e.FullName.EndsWith(".a"));
            }
            var cksLib = PackageReader.ReadDll(cksDll, "ESharpSample.Checksum", "1.0.0");
            Assert.NotNull(cksLib);
            Assert.Empty(cksLib!.Problems);
            Assert.Equal(new[] { "Native", "NativeC" }, cksLib.Manifest!.Features);
            Assert.Equal("cksum_", cksLib.Manifest.Native!.Prefix);
            Assert.Equal(new[] { "common/adler32.c", "common/cksum_tune.h", "m68k/cksum_tune.h" }, cksLib.Manifest.Native.Files.ToArray());
            Assert.Equal(new[] { "common/adler32.c", "common/cksum_tune.h", "m68k/cksum_tune.h" }, cksLib.Native.Select(n => n.Path).ToArray());

            var (packExit, packLog) = DotnetResult(work, packages, "pack", "Tween", "-o", feed, "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.True(packExit == 0 && !packLog.Contains("ESL0"), $"dotnet pack failed or warned:\n{packLog}");
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
            Assert.Equal(new[] { "TWEEN_OWN" }, lib.Manifest.Symbols);   // not DEBUG/RELEASE/TRACE or HOST
            Assert.Equal(new[] { "Assets.g.cs", "Ball.cs", "Ease.cs", "Tween.cs" }, lib.Sources.Select(s => s.Path).ToArray());
            Assert.Equal(new PackageAssets("Assets.g.cs", "Tween", "Art"), lib.Manifest.Assets);
            // The package carries the converted assets (in the DLL), not the inputs.
            using (var zip = ZipFile.OpenRead(nupkg))
                Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".png") || e.FullName.EndsWith(".wav") || e.FullName.EndsWith("assets.json"));

            var game = Path.Combine(work, "TweenDemo");
            Assert.NotNull(ESharp.Cli.PackageStep.Load(game, out _));   // not restored yet: says to run dotnet restore
            Dotnet(work, packages, "restore", "TweenDemo", "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.Null(ESharp.Cli.PackageStep.Load(game, out var found));
            Assert.Equal(new[] { "ESharpSample.Checksum", "ESharpSample.Tween" }, found.Packages.Select(p => p.Id).ToArray());
            Assert.False(found.AllowCopyleft);

            var m68k = HasImage(M68kImage);
            var output = new StringWriter();
            var saved = Console.Out;
            var savedError = Console.Error;
            Console.SetOut(output);
            Console.SetError(output);   // build errors, so a failure shows them
            try
            {
                Assert.True(ESharp.Cli.Program.Build(new[] { game, "--emit-only" }, out _) == 0, output.ToString());
                Assert.True(ESharp.Cli.Program.Test(new[] { game }) == 0, output.ToString());
                if (m68k)
                {
                    Assert.True(ESharp.Cli.Program.Build(new[] { game, "--target", "m68k", "--docker", M68kImage }, out var exe) == 0, output.ToString());
                    Assert.True(File.Exists(exe) && new FileInfo(exe!).Length > 0, "no m68k program");
                }
            }
            finally { Console.SetOut(saved); Console.SetError(savedError); }
            var text = output.ToString().Replace("\r\n", "\n");
            Assert.Contains("Package ESharpSample.Tween 1.0.0 (MIT): 4 C# file(s)", text);
            Assert.Contains("  assets: Tween.Art (Assets.g.cs; images and samples converted when the library was built)", text);
            // The package's chip data is counted as the program's would be: 64 + 32 bytes of ball, 480 of blip.
            Assert.Matches(@"static +Art\.BallPlanes +64 bytes +<package ESharpSample\.Tween 1\.0\.0>/Assets\.g\.cs\(", text);
            Assert.Matches(@"static +Art\.BallMask +32 bytes", text);
            Assert.Matches(@"static +Art\.BlipData +480 bytes", text);
            Assert.Contains("  its own symbols: TWEEN_OWN", text);
            Assert.Contains("Host: 20 ok, 0 FAIL", text);
            Assert.Contains("Package ESharpSample.Checksum 1.0.0 (MIT): 1 C# file(s)", text);
            Assert.Contains("  native C (prefix cksum_; compiled into the program, runs with its privileges): native/common/adler32.c, native/m68k/cksum_tune.h", text);
            var nativeDir = Path.Combine(game, "obj", "m68k", "esharp-native", "ESharpSample.Checksum");
            Assert.Equal("#define CKSUM_UNROLL 8", File.ReadAllLines(Path.Combine(nativeDir, "cksum_tune.h")).Single(l => l.StartsWith("#define CKSUM_UNROLL")));
            Assert.Contains("extern uint32_t cksum_adler32(uint32_t, uint8_t *, int32_t);", File.ReadAllText(Path.Combine(nativeDir, "esharp_natives.h")));
            Assert.Contains("TEST PASSED", text);
            if (m68k) Assert.Contains("Built " + Path.Combine(game, "obj", "m68k", "TweenDemo"), text);
            var gameC = File.ReadAllText(Path.Combine(game, "obj", "m68k", "TweenDemo.c")).Replace("\r\n", "\n");
            Assert.Contains("\nextern uint32_t cksum_adler32(uint32_t, uint8_t *, int32_t);\n", gameC);
            Assert.DoesNotContain("cksum_tune.h", gameC);
            Assert.Contains("\"<package ESharpSample.Tween 1.0.0>/Tween.cs\"", gameC);
            Assert.Contains("\"<package ESharpSample.Tween 1.0.0>/Ball.cs\"", gameC);
            // The ball's planes as the library's Assets.g.cs has them, in chip RAM.
            var planes = gameC.Split('\n').Single(l => l.Contains("Tween_Art_BallPlanes[") && l.StartsWith("static "));
            Assert.StartsWith("static __chip ", planes);
            var hex = File.ReadAllLines(Path.Combine(RepoRoot, "samples", "packages", "Tween", "Assets.g.cs"))
                .SkipWhile(l => !l.Contains("BallPlanes =")).Skip(2).TakeWhile(l => !l.Contains("};"))
                .SelectMany(l => l.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
            Assert.Equal(64, hex.Count);
            Assert.EndsWith("[64] = { " + string.Join(", ", hex.Select(h => Convert.ToInt32(h, 16))) + " };", planes);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Library_manifest_is_valid_json_and_outside_files_are_skipped()
    {
        var work = Path.Combine(Path.GetTempPath(), "esharp-pkg-" + Guid.NewGuid().ToString("N"));
        var feed = Path.Combine(work, "feed");
        Directory.CreateDirectory(feed);
        try
        {
            File.Copy(Path.Combine(RepoRoot, "global.json"), Path.Combine(work, "global.json"));
            File.WriteAllText(Path.Combine(work, "nuget.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources>\n    <clear />\n" +
                $"    <add key=\"local\" value=\"{System.Security.SecurityElement.Escape(feed)}\" />\n  </packageSources>\n</configuration>\n");
            FakeSdkPackage(feed);
            CopyProject(Path.Combine(RepoRoot, "samples", "packages", "Tween"), Path.Combine(work, "Tween"));
            // A source file next to the project, not in it, with no Link: skipped (it is not the library's own source).
            File.WriteAllText(Path.Combine(work, "Outside.cs"), "namespace Outside; public static class O { }\n");
            File.WriteAllText(Path.Combine(work, "Extra.cs"), "namespace Extra; public static class E { }\n");
            var csproj = Path.Combine(work, "Tween", "Tween.csproj");
            // Quotes and a backslash in the id and the licence expression (written into the csproj: the command line eats quotes).
            const string id = "My\"Lib\\X";
            const string license = "MIT\" OR \"x\\y";
            File.WriteAllText(csproj, File.ReadAllText(csproj)
                .Replace("<PackageId>ESharpSample.Tween</PackageId>", "<PackageId>My&quot;Lib\\X</PackageId>")
                .Replace("<PackageLicenseExpression>MIT</PackageLicenseExpression>", "<PackageLicenseExpression>MIT&quot; OR &quot;x\\y</PackageLicenseExpression>")
                .Replace("</Project>", "<ItemGroup><Compile Include=\"..\\Outside.cs\" /><Compile Include=\"..\\Extra.cs\" Link=\"Linked\\Extra.cs\" /></ItemGroup>\n</Project>"));
            var packages = Path.Combine(work, "packages");
            var (exit, log) = DotnetResult(work, packages, "build", "Tween", "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.True(exit == 0, log);
            // The skipped file is named in a warning (it is in the HOST DLL, but esharp build will not see it).
            Assert.Contains(log.Split('\n'), l => l.Contains("warning ESL010") && l.Contains("Outside.cs is outside the project folder"));
            Assert.DoesNotContain(log.Split('\n'), l => l.Contains("warning ESL010") && l.Contains("Extra.cs"));
            var dll = Directory.GetFiles(Path.Combine(work, "Tween", "bin"), "Tween.dll", SearchOption.AllDirectories).First();
            var lib = PackageReader.ReadDll(dll, id, "1.0.0");
            Assert.NotNull(lib);
            Assert.Empty(lib!.Problems);
            Assert.Equal(id, lib.Manifest!.Id);
            Assert.Equal(license, lib.License);
            // Outside.cs (no Link) is skipped, Extra.cs (a Link) is embedded under its Link path.
            Assert.Equal(new[] { "Assets.g.cs", "Ball.cs", "Ease.cs", "Linked/Extra.cs", "Tween.cs" }, lib.Sources.Select(s => s.Path).ToArray());

            // An author who sets SetLinkMetadataAutomatically keeps it: then the SDK links Outside.cs by its file name, and it is embedded.
            File.WriteAllText(csproj, File.ReadAllText(csproj).Replace("<EsLibrary>true</EsLibrary>",
                "<EsLibrary>true</EsLibrary>\n    <SetLinkMetadataAutomatically>true</SetLinkMetadataAutomatically>"));
            (exit, log) = DotnetResult(work, packages, "build", "Tween", "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.True(exit == 0 && !log.Contains("ESL010"), log);
            lib = PackageReader.ReadDll(dll, id, "1.0.0");
            Assert.Equal(new[] { "Assets.g.cs", "Ball.cs", "Ease.cs", "Linked/Extra.cs", "Outside.cs", "Tween.cs" }, lib!.Sources.Select(s => s.Path).ToArray());

            // assets.json renames the class but esharp assets has not run again: a warning (ESL011), and the manifest names the class
            // the package really has, the one in Assets.g.cs (Tween.Art), not the one assets.json asks for.
            var assetsJson = Path.Combine(work, "Tween", "assets.json");
            File.WriteAllText(assetsJson, File.ReadAllText(assetsJson).Replace("\"class\": \"Art\"", "\"class\": \"Pictures\""));
            (exit, log) = DotnetResult(work, packages, "build", "Tween", "-p:ESharpSdkVersion=" + SdkVersion);
            Assert.True(exit == 0 && log.Contains("ESL011"), log);
            lib = PackageReader.ReadDll(dll, id, "1.0.0");
            Assert.Equal(new PackageAssets("Assets.g.cs", "Tween", "Art"), lib!.Manifest!.Assets);
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
        foreach (var dir in Directory.GetDirectories(from).Where(d => Path.GetFileName(d) is not ("bin" or "obj")))
            CopyProject(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    private static void Dotnet(string dir, string packages, params string[] args)
    {
        var (exit, log) = DotnetResult(dir, packages, args);
        Assert.True(exit == 0, $"dotnet {string.Join(" ", args)} failed:\n{log}");
    }

    private static (int Exit, string Log) DotnetResult(string dir, string packages, params string[] args)
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
        return (p.ExitCode, output + "\n" + err.Result);
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

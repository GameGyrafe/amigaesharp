using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using ESharp.Compiler;
using static ESharp.Compiler.Tests.PackageRoundTripTests;

namespace ESharp.Compiler.Tests;

/// <summary>
/// Precompiled package binaries (issue #154) with the samples/packages projects: dotnet pack of Tween and Checksum with EsPrecompile on
/// (the Sdk's pack step runs this build's esharp precompile), the binaries in the package, TweenDemo restored from the feed and built
/// with them (the package sources not compiled), a stamp that does not match falls back to the sources with ES0124, hand-made objects
/// are refused, esharp verify-package passes and fails on a tampered binary, and two builds in different folders give the same bytes.
/// The sdl binaries are built with the host gcc (ESHARP_TOOLCHAIN_SDL=/usr, Linux only); with the pinned m68k image in the local
/// Docker the m68k binaries too, and the game is linked for m68k.
/// </summary>
[Collection("ProcessConsole")]
public class PrecompiledRoundTripTests
{
    private static string Cli => Path.Combine(AppContext.BaseDirectory, "esharp.dll");

    private static (int Exit, string Output) Esharp(Dictionary<string, string> env, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Cli);
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in env) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output + err.Result);
    }

    private static string InProcess(Func<int> run, out int exit)
    {
        var output = new StringWriter();
        var saved = Console.Out;
        var savedError = Console.Error;
        Console.SetOut(output);
        Console.SetError(output);
        try { exit = run(); }
        finally { Console.SetOut(saved); Console.SetError(savedError); }
        return output.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void Packages_with_binaries_pack_link_fall_back_and_verify()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/gcc") || !File.Exists("/usr/bin/ar")) return;
        var work = Path.Combine(Path.GetTempPath(), "esharp-bin-rt-" + Guid.NewGuid().ToString("N"));
        var feed = Path.Combine(work, "feed");
        Directory.CreateDirectory(feed);
        IsolateFromRepoBuildFiles(work);
        try
        {
            File.Copy(Path.Combine(RepoRoot, "global.json"), Path.Combine(work, "global.json"));
            File.WriteAllText(Path.Combine(work, "nuget.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources>\n    <clear />\n" +
                $"    <add key=\"local\" value=\"{System.Security.SecurityElement.Escape(feed)}\" />\n  </packageSources>\n</configuration>\n");
            FakeSdkPackage(feed);
            foreach (var project in new[] { "Tween", "Checksum", "TweenDemo" })
                CopyProject(Path.Combine(RepoRoot, "samples", "packages", project), Path.Combine(work, project));
            var packages = Path.Combine(work, "packages");
            var m68k = HasImage(M68kImage);
            var env = new Dictionary<string, string> { ["ESHARP_TOOLCHAIN_SDL"] = "/usr" };
            // The targets with a toolchain here (the pack builds a binary for every target the library lists).
            foreach (var lib in new[] { "Tween", "Checksum" })
            {
                var csproj = Path.Combine(work, lib, lib + ".csproj");
                File.WriteAllText(csproj, File.ReadAllText(csproj).Replace("<EsTargets>m68k;os4;morphos;aros;aros-i386;sdl</EsTargets>", m68k ? "<EsTargets>m68k;sdl</EsTargets>" : "<EsTargets>sdl</EsTargets>"));
            }
            var packArgs = new List<string> { "-p:ESharpSdkVersion=" + SdkVersion, $"-p:EsCommand=dotnet \"{Cli}\"" };
            if (m68k) packArgs.Add($"-p:EsPrecompileArgs=--image m68k={M68kImage}");

            foreach (var lib in new[] { "Tween", "Checksum" })
            {
                var (exit, log) = DotnetResult(work, packages, env, new[] { "pack", lib, "-o", feed }.Concat(packArgs).ToArray());
                Assert.True(exit == 0, $"dotnet pack {lib} with binaries failed:\n{log}");
                Assert.Contains($"for sdl: ", log);
            }
            var nupkg = Path.Combine(feed, "ESharpSample.Tween.1.0.0.nupkg");
            using (var zip = ZipFile.OpenRead(nupkg))
            {
                var names = zip.Entries.Select(e => e.FullName).ToList();
                Assert.Contains("lib/net10.0/esharp-bin/sdl-linux-x64/library.a", names);
                Assert.Contains("lib/net10.0/esharp-bin/sdl-linux-x64/binary.json", names);
                Assert.Equal(m68k, names.Contains("lib/net10.0/esharp-bin/m68k/library.a"));
                Assert.Contains("lib/net10.0/Tween.dll", names);   // the sources still ship, in the DLL
            }
            using (var zip = ZipFile.OpenRead(Path.Combine(feed, "ESharpSample.Checksum.1.0.0.nupkg")))
            {
                // The package's C is in its binary (an object of its own) and, as source, in the DLL.
                var json = new StreamReader(zip.GetEntry("lib/net10.0/esharp-bin/sdl-linux-x64/binary.json")!.Open()).ReadToEnd();
                var binary = PackageBinaries.Read(json, out var problem);
                Assert.True(binary != null, problem);
                Assert.Equal(new[] { "library.o", "n_adler32.o" }, binary!.Objects);
                Assert.Equal(PackageBinaries.Stamp(ESharp.Cli.SourceTree.ToolVersion), binary.Stamp);
            }

            // The game links the binaries: its C has the packages' functions declared, not defined.
            var game = Path.Combine(work, "TweenDemo");
            Dotnet(work, packages, "restore", "TweenDemo", "-p:ESharpSdkVersion=" + SdkVersion);
            var text = InProcess(() => ESharp.Cli.Program.Build(new[] { game, "--target", "sdl", "--emit-only" }, out _), out var buildExit);
            Assert.True(buildExit == 0, text);
            Assert.Contains("Package ESharpSample.Tween 1.0.0: linked from its precompiled sdl-linux-x64 binary", text);
            Assert.Contains("Package ESharpSample.Checksum 1.0.0: linked from its precompiled sdl-linux-x64 binary", text);
            Assert.DoesNotContain("ES0124", text);
            var c = File.ReadAllText(Path.Combine(game, "obj", "sdl", "TweenDemo.c"));
            Assert.Contains("int32_t Es_Tween_Ease_InQuad(int32_t t);", c);
            Assert.DoesNotContain("int32_t Es_Tween_Ease_InQuad(int32_t t)\n{", c);
            Assert.Contains("es_pkginit_esharpsample_tween();", c);
            Assert.True(File.Exists(Path.Combine(game, "obj", "sdl", PackageBinaries.FolderName, "ESharpSample.Tween", PackageBinaries.ArchiveName)));
            Assert.False(Directory.Exists(Path.Combine(game, "obj", "sdl", PackageNatives.OutputFolder)));   // Checksum's C is in its binary
            var fromSource = InProcess(() => ESharp.Cli.Program.Build(new[] { game, "--target", "sdl", "--emit-only", "--from-source" }, out _), out buildExit);
            Assert.True(buildExit == 0 && !fromSource.Contains("linked from"), fromSource);
            Assert.Contains("int32_t Es_Tween_Ease_InQuad(int32_t t)\n{", File.ReadAllText(Path.Combine(game, "obj", "sdl", "TweenDemo.c")));
            if (m68k)
            {
                text = InProcess(() => ESharp.Cli.Program.Build(new[] { game, "--target", "m68k", "--docker", M68kImage }, out _), out buildExit);
                Assert.True(buildExit == 0, text);
                Assert.Contains("Package ESharpSample.Tween 1.0.0: linked from its precompiled m68k binary", text);
                Assert.True(new FileInfo(Path.Combine(game, "obj", "m68k", "TweenDemo")).Length > 0);
            }
            var host = InProcess(() => ESharp.Cli.Program.Test(new[] { game }), out var testExit);
            Assert.True(testExit == 0 && host.Contains("Host: 22 ok, 0 FAIL"), host);

            // A binary made by another E# (its stamp): the sources are compiled, with ES0124 saying why.
            var restored = Path.Combine(packages, "esharpsample.tween", "1.0.0", "lib", "net10.0", PackageBinaries.FolderName, "sdl-linux-x64");
            var manifest = Path.Combine(restored, PackageBinaries.ManifestName);
            var manifestText = File.ReadAllText(manifest);
            var stamp = PackageBinaries.Read(manifestText, out _)!.Stamp;
            File.WriteAllText(manifest, manifestText.Replace(stamp, "0.0.1-other+0123"));
            text = InProcess(() => ESharp.Cli.Program.Build(new[] { game, "--target", "sdl", "--emit-only" }, out _), out buildExit);
            Assert.True(buildExit == 0, text);
            Assert.Contains("warning ES0124: Package 'ESharpSample.Tween' 1.0.0 has precompiled binaries, but its sdl-linux-x64 binary was built by E# 0.0.1-other+0123", text);
            Assert.Contains("int32_t Es_Tween_Ease_InQuad(int32_t t)\n{", File.ReadAllText(Path.Combine(game, "obj", "sdl", "TweenDemo.c")));
            File.WriteAllText(manifest, manifestText);

            // Hand-made objects are refused: a file in the binary folder that binary.json does not list, and an object anywhere else.
            var stray = Path.Combine(restored, "extra.o");
            File.WriteAllText(stray, "x");
            text = InProcess(() => ESharp.Cli.Program.Build(new[] { game, "--target", "sdl", "--emit-only" }, out _), out buildExit);
            Assert.True(buildExit != 0 && text.Contains("error ES0118") && text.Contains("it carries 'extra.o', which binary.json does not list"), text);
            File.Delete(stray);
            var loose = Path.Combine(packages, "esharpsample.tween", "1.0.0", "lib", "net10.0", "fast.a");
            File.WriteAllText(loose, "x");
            text = InProcess(() => ESharp.Cli.Program.Build(new[] { game, "--target", "sdl", "--emit-only" }, out _), out buildExit);
            Assert.True(buildExit != 0 && text.Contains("it carries the prebuilt file 'lib/net10.0/fast.a'"), text);
            File.Delete(loose);

            // verify-package rebuilds the binaries from the package's sources: the same bytes.
            var (verifyExit, verify) = m68k ? Esharp(env, "verify-package", nupkg, "--image", "m68k=" + M68kImage) : Esharp(env, "verify-package", nupkg);
            Assert.True(verifyExit == 0 && verify.Contains("ok sdl-linux-x64: library.a rebuilt from the package's sources, byte for byte") && verify.Contains("VERIFIED"), verify);
            // A tampered binary fails: with binary.json's hash left as it was, and with the hash made to match.
            var folder = Path.Combine(work, "extracted");
            ZipFile.ExtractToDirectory(nupkg, folder);
            var archive = Path.Combine(folder, "lib", "net10.0", PackageBinaries.FolderName, "sdl-linux-x64", PackageBinaries.ArchiveName);
            var bytes = File.ReadAllBytes(archive);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(archive, bytes);
            (verifyExit, verify) = Esharp(env, "verify-package", folder);
            Assert.True(verifyExit != 0 && verify.Contains("'library.a' is not the file binary.json lists (SHA-256 differs)") && verify.Contains("NOT VERIFIED"), verify);
            var folderManifest = Path.Combine(Path.GetDirectoryName(archive)!, PackageBinaries.ManifestName);
            var original = PackageBinaries.Read(File.ReadAllText(folderManifest), out _)!.Files.Single().Sha256;
            File.WriteAllText(folderManifest, File.ReadAllText(folderManifest).Replace(original, PackageBinaries.Sha256(bytes)));
            (verifyExit, verify) = Esharp(env, "verify-package", folder);
            Assert.True(verifyExit != 0 && verify.Contains("FAIL sdl-linux-x64: the rebuilt library.a differs from the package's") && verify.Contains("NOT VERIFIED"), verify);

            // Reproducible: two builds of the same library in different folders give the same archive.
            var dll = Path.Combine(work, "dll", "Checksum.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
            using (var zip = ZipFile.OpenRead(Path.Combine(feed, "ESharpSample.Checksum.1.0.0.nupkg")))
                zip.GetEntry("lib/net10.0/Checksum.dll")!.ExtractToFile(dll);
            var one = Path.Combine(work, "one");
            var two = Path.Combine(work, "a", "much", "longer", "folder", "two");
            foreach (var dir in new[] { one, two })
            {
                var (exit, log) = Esharp(env, "precompile", "--dll", dll, "--out", dir, "--targets", "sdl");
                Assert.True(exit == 0, log);
            }
            Assert.Equal(File.ReadAllBytes(Path.Combine(one, "sdl-linux-x64", PackageBinaries.ArchiveName)), File.ReadAllBytes(Path.Combine(two, "sdl-linux-x64", PackageBinaries.ArchiveName)));
            Assert.DoesNotContain(work, System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(one, "sdl-linux-x64", PackageBinaries.ArchiveName))));
            // A missing toolchain stops the pack step before anything is built.
            var (noExit, noLog) = Esharp(new Dictionary<string, string> { ["ESHARP_TOOLCHAIN_SDL"] = Path.Combine(work, "nowhere") },
                "precompile", "--dll", dll, "--out", Path.Combine(work, "three"), "--targets", "sdl");
            Assert.True(noExit != 0 && noLog.Contains("error ESL016") && noLog.Contains("does not exist"), noLog);
            Assert.False(Directory.Exists(Path.Combine(work, "three", "sdl-linux-x64")));
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

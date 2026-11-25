using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ESharp.Compiler;
using Microsoft.CodeAnalysis;

namespace ESharp.Cli;

/// <summary>
/// Precompiled per-target binaries of E# library packages (issue #154).
/// <list type="bullet">
/// <item><c>esharp precompile</c>: the step the AmigaESharp.Sdk targets run at <c>dotnet pack</c> of an <c>EsLibrary</c> project
/// (EsPrecompile, on by default). For every target of EsTargets it compiles the library alone (library mode of the compiler,
/// <see cref="CompileOptions.LibraryBuild"/>) and builds a static library of that C and the package's C files with the target's
/// toolchain, then writes <c>esharp-bin/&lt;target&gt;/library.a</c> and its <c>binary.json</c>, which the targets pack beside the DLL.</item>
/// <item><c>esharp verify-package</c>: rebuilds every binary of a package from the sources the package carries and compares the bytes.</item>
/// </list>
/// The builds are reproducible: the compiler runs in a work folder with fixed relative file names (in Docker always /work), without
/// debug information, and the archive is written by <c>ar</c> in deterministic mode (no times, owners or modes), so two builds
/// in different folders give the same bytes.
/// </summary>
internal static class Precompile
{
    /// <summary>Where a target's compiler and ar come from.</summary>
    private sealed record Tool(string? Image, string? Compiler, string? Sysroot, string Describe);

    private sealed record ToolOptions(string? DockerAll, Dictionary<string, string> Images, bool AnyVersion);

    private static int Fail(string message)
    {
        // The canonical "origin: error code: text" form, so MSBuild's Exec in the pack step shows it as an error.
        Console.Error.WriteLine($"esharp: error ESL016: {message}");
        return 1;
    }

    private static bool ParseToolOption(string[] args, ref int i, ref string? dockerAll, Dictionary<string, string> images, ref bool anyVersion, out string? error)
    {
        error = null;
        switch (args[i])
        {
            case "--docker":
                dockerAll = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[++i] : "";
                return true;
            case "--image" when i + 1 < args.Length:
                var spec = args[++i];
                var eq = spec.IndexOf('=');
                if (eq <= 0 || Targets.Find(spec.Substring(0, eq)) == null) { error = $"--image takes <target>=<image>, not '{spec}'"; return true; }
                images[Targets.Find(spec.Substring(0, eq))!.Name] = spec.Substring(eq + 1);
                return true;
            case "--any-cc-version": anyVersion = true; return true;
        }
        return false;
    }

    /// <summary><c>esharp precompile &lt;library project dir&gt; --dll &lt;dll&gt; --out &lt;dir&gt; [--targets a;b] [--docker [image]] [--image t=img] [--any-cc-version]</c></summary>
    public static int Run(string[] args)
    {
        string? project = null, dll = null, outDir = null, targetsText = null, dockerAll = null;
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        var anyVersion = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (ParseToolOption(args, ref i, ref dockerAll, images, ref anyVersion, out var optionError)) { if (optionError != null) return Fail(optionError); continue; }
            switch (args[i])
            {
                case "--dll" when i + 1 < args.Length: dll = args[++i]; break;
                case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--targets" when i + 1 < args.Length: targetsText = args[++i]; break;
                default:
                    if (args[i].StartsWith("-")) return Fail($"unknown option '{args[i]}'");
                    project = args[i];
                    break;
            }
        }
        if (dll == null || outDir == null) return Fail("precompile needs --dll <the library's DLL> and --out <folder>");
        if (!File.Exists(dll)) return Fail($"{dll} not found (build the library first)");
        if (ReadLibrary(dll, out var library) is { } readError) return Fail(readError);
        if (project != null)
        {
            if (PackageStep.Load(project, out var restored) is { } loadError) return Fail(loadError);
            // A package that uses other E# packages ships its sources only: verify-package rebuilds a binary from the package alone,
            // which such a binary cannot be (it is compiled against its dependencies' code), and main runs the binaries' init functions
            // before any package compiled from source, which would be the wrong order for a binary's dependency.
            var dependencies = restored.Packages.Where(p => !string.Equals(p.Id, library!.Id, StringComparison.OrdinalIgnoreCase)).Select(p => p.Id).ToList();
            if (dependencies.Count > 0)
            {
                Console.WriteLine($"esharp precompile: {library!.Id} uses the E# package(s) {string.Join(", ", dependencies)}, so it gets no precompiled binaries: " +
                                  "the package carries its sources, and programs compile it (a package with E# dependencies is not precompiled yet).");
                return 0;
            }
        }
        var names = (targetsText ?? string.Join(";", library!.Manifest!.Targets)).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var targets = new List<TargetInfo>();
        foreach (var n in names)
        {
            var t = Targets.Find(n);
            if (t == null) return Fail($"unknown target '{n}'");
            if (!library!.Manifest!.Targets.Contains(t.Name, StringComparer.OrdinalIgnoreCase)) return Fail($"the library does not list target '{t.Name}' (EsTargets)");
            targets.Add(t);
        }
        var tools = new ToolOptions(dockerAll, images, anyVersion);
        // Every listed target's toolchain first: the pack fails before anything is built when one is missing.
        var chosen = new List<(TargetInfo Target, Tool Tool)>();
        foreach (var t in targets)
        {
            if (FindTool(t, tools, out var tool) is { } toolError) return Fail(toolError);
            chosen.Add((t, tool!));
        }
        Directory.CreateDirectory(outDir);
        foreach (var (t, tool) in chosen)
        {
            var watch = Stopwatch.StartNew();
            if (BuildBinary(library!, t, tool, outDir, out var binary) is { } buildError) return Fail(buildError);
            Console.WriteLine($"Precompiled {library!.Id} {library.Version} for {t.Name}: {Path.Combine(outDir, binary!.BinaryTarget, PackageBinaries.ArchiveName)} " +
                              $"({binary.Objects.Count} object(s), {binary.Generics.Count} generic copies; {tool.Describe}; {watch.Elapsed.TotalSeconds:0.0} s)");
        }
        return 0;
    }

    /// <summary>The E# library in a DLL (its manifest and sources), or an error.</summary>
    private static string? ReadLibrary(string dll, out EsPackage? library)
    {
        library = null;
        Dictionary<string, byte[]> resources;
        try { resources = PackageReader.Resources(dll); }
        catch (Exception e) when (e is IOException or BadImageFormatException or InvalidOperationException) { return $"cannot read {dll}: {e.Message}"; }
        if (!resources.TryGetValue(PackageReader.ManifestResource, out var manifestBytes)) return $"{dll} carries no E# library manifest (is EsLibrary set?)";
        var problems = new List<string>();
        LibraryManifest? manifest;
        try { manifest = PackageReader.ParseManifest(System.Text.Encoding.UTF8.GetString(manifestBytes), problems); }
        catch (System.Text.Json.JsonException e) { return $"{dll}: its manifest is not valid JSON: {e.Message}"; }
        if (manifest == null) return $"{dll}: {string.Join("; ", problems)}";
        library = PackageReader.FromResources(resources, manifestBytes, manifest.Id, manifest.Version, dll);
        if (library.Problems.Count > 0) return $"{dll}: {string.Join("; ", library.Problems)}";
        return null;
    }

    private static bool ImagePresent(string image)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("docker") { ArgumentList = { "image", "inspect", image }, RedirectStandardOutput = true, RedirectStandardError = true })!;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>
    /// The toolchain for a target: an image given (--image t=img, --docker [img]); else a local toolchain named by ESHARP_TOOLCHAIN_&lt;T&gt;
    /// or ESHARP_TOOLCHAIN; else the default image when it is in the local Docker; else a compiler on PATH of the pinned version.
    /// </summary>
    private static string? FindTool(TargetInfo t, ToolOptions o, out Tool? tool)
    {
        tool = null;
        var image = o.Images.TryGetValue(t.Name, out var given) ? given : o.DockerAll is { } all ? (all.Length > 0 ? all : Program.DefaultImage(t)) : null;
        if (image != null)
        {
            if (!ImagePresent(image)) return $"the Docker image '{image}' for target {t.Name} is not in the local Docker (docker pull it, or build it from the E# repository's docker/ folder)";
            tool = new Tool(image, null, t.Sysroot.Length > 0 ? t.Sysroot : null, "Docker image " + image);
            return null;
        }
        string? Env(string name) => Environment.GetEnvironmentVariable(name);
        var envGiven = !string.IsNullOrWhiteSpace(Env(Toolchain.EnvFor(t))) || t.IsAmiga && !string.IsNullOrWhiteSpace(Env(Toolchain.EnvAll));
        if (envGiven)
        {
            if (Toolchain.Resolve(t, null, null, Env, out var choice) is { } error) return error;
            if (!o.AnyVersion && Toolchain.VersionProblem(t, choice!.Compiler, Toolchain.ReadVersion(choice.Compiler)) is { } versionError) return versionError;
            tool = new Tool(null, choice!.Compiler, choice.Sysroot, $"local {t.Name} toolchain (from {choice.Source})");
            return null;
        }
        var fallback = Program.DefaultImage(t);
        if (ImagePresent(fallback))
        {
            tool = new Tool(fallback, null, t.Sysroot.Length > 0 ? t.Sysroot : null, "Docker image " + fallback);
            return null;
        }
        // A compiler on PATH counts only when it is the pinned version (sdl has none pinned: name its toolchain or image).
        if (t.CompilerVersion.Length > 0 && Toolchain.Resolve(t, null, null, Env, out var onPath) == null &&
            (o.AnyVersion || Toolchain.VersionProblem(t, onPath!.Compiler, Toolchain.ReadVersion(onPath.Compiler)) == null))
        {
            tool = new Tool(null, onPath!.Compiler, onPath.Sysroot, $"{t.Name} compiler on PATH");
            return null;
        }
        return $"no toolchain for target {t.Name}, which the library lists in EsTargets: the pack builds a binary for every listed target. " +
               $"Build or pull the Docker image {fallback}, pass --image {t.Name}=<image> in EsPrecompileArgs, set {Toolchain.EnvFor(t)} to a local toolchain, " +
               "remove the target from EsTargets, or set EsPrecompile to false (the package then ships its sources only)";
    }

    /// <summary>Builds one target's binary of <paramref name="library"/> into <c>&lt;outDir&gt;/&lt;binary target&gt;/</c>.</summary>
    private static string? BuildBinary(EsPackage library, TargetInfo target, Tool tool, string outDir, out PackageBinary? binary)
    {
        binary = null;
        var options = new CompileOptions
        {
            Defines = target.Defines.ToArray(),
            TargetName = target.Name,
            Packages = new[] { library },
            LibraryBuild = library,
            CFileName = "library.c",
            AllowCopyleft = true,   // the library's own licence is the author's choice; programs check it when they use the package
            SdkVersion = SourceTree.ToolVersion,
            Stamp = PackageBinaries.Stamp(SourceTree.ToolVersion),
        };
        var result = EsCompiler.Compile(Array.Empty<SourceFile>(), options);
        foreach (var d in result.Diagnostics) Console.Error.WriteLine(d.ToString());
        if (!result.Success) return $"{library.Id} cannot be built for {target.Name} ({result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} error(s) above)";

        var bt = PackageBinaries.BinaryTarget(target.Name);
        var work = Path.Combine(outDir, ".work-" + bt);
        try
        {
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "library.c"), result.C!);
            foreach (var n in result.Native)
            {
                var file = Path.Combine(work, n.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, n.Text);
            }
            // One object for the library's C, one per C file of the package (named after it), in that order.
            var compiles = new List<(string Object, string Source)> { ("library.o", "library.c") };
            foreach (var n in result.Native.Where(n => n.Compile).OrderBy(n => n.Path, StringComparer.Ordinal))
                compiles.Add(("n_" + Path.GetFileNameWithoutExtension(n.Path) + ".o", n.Path));
            var cflags = target.CFlags.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var commands = new List<List<string>>();
            foreach (var (obj, src) in compiles)
            {
                var cmd = new List<string> { tool.Compiler ?? target.Compiler };
                if (tool.Sysroot != null) cmd.Add("--sysroot=" + tool.Sysroot);
                cmd.AddRange(cflags);
                cmd.Add(PackageNatives.CFlags);
                cmd.AddRange(new[] { "-c", "-o", obj, src });
                commands.Add(cmd);
            }
            var ar = ArFor(tool.Compiler ?? target.Compiler);
            // D: deterministic (zero times, owners and modes); s: the symbol index the linker needs.
            commands.Add(new List<string> { ar, "rcsD", PackageBinaries.ArchiveName }.Concat(compiles.Select(c => c.Object)).ToList());
            if (RunCommands(work, tool.Image, commands) is { } runError) return runError;

            var dest = Path.Combine(outDir, bt);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);
            File.Copy(Path.Combine(work, PackageBinaries.ArchiveName), Path.Combine(dest, PackageBinaries.ArchiveName));
            var files = new[] { new BinaryFile(PackageBinaries.ArchiveName, PackageBinaries.Sha256File(Path.Combine(dest, PackageBinaries.ArchiveName))) };
            binary = PackageBinaries.FromBuild(library, target.Name, options.Stamp!, PackageBinaries.Config(options.Defines), result.Library!,
                compiles.Select(c => c.Object).ToList(), files);
            File.WriteAllText(Path.Combine(dest, PackageBinaries.ManifestName), PackageBinaries.Write(binary));
            binary = binary with { Folder = dest };
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"{target.Name}: {e.Message}"; }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The ar of a toolchain: the compiler's name with gcc replaced (m68k-amigaos-gcc gives m68k-amigaos-ar), in the same folder.</summary>
    private static string ArFor(string compiler)
    {
        var name = Path.GetFileName(compiler);
        var dir = Path.GetDirectoryName(compiler);
        var ext = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ".exe" : "";
        var stem = ext.Length > 0 ? name[..^4] : name;
        var arName = (stem.EndsWith("gcc", StringComparison.Ordinal) ? stem[..^3] + "ar" : "ar") + ext;
        return string.IsNullOrEmpty(dir) ? arName : Path.Combine(dir, arName);
    }

    /// <summary>Runs the commands in <paramref name="work"/>: in one container of <paramref name="image"/> (the folder at /work), or here.</summary>
    private static string? RunCommands(string work, string? image, List<List<string>> commands)
    {
        if (image != null)
        {
            var line = string.Join(" && ", commands.Select(c => string.Join(" ", c.Select(ShellQuote))));
            var psi = new ProcessStartInfo("docker") { UseShellExecute = false };
            foreach (var a in new[] { "run", "--rm", "-v", $"{Path.GetFullPath(work)}:/work", "-w", "/work", image, "sh", "-c", line }) psi.ArgumentList.Add(a);
            return RunOne(psi, "docker run " + image);
        }
        foreach (var c in commands)
        {
            var psi = new ProcessStartInfo(c[0]) { UseShellExecute = false, WorkingDirectory = Path.GetFullPath(work) };
            foreach (var a in c.Skip(1)) psi.ArgumentList.Add(a);
            if (RunOne(psi, c[0]) is { } error) return error;
        }
        return null;
    }

    private static string? RunOne(ProcessStartInfo psi, string what)
    {
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode == 0 ? null : $"{what} failed with exit code {p.ExitCode}";
        }
        catch (System.ComponentModel.Win32Exception) { return $"'{psi.FileName}' not found"; }
    }

    private static string ShellQuote(string s) =>
        s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || "-_./=+:,".Contains(c)) ? s : "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>
    /// <c>esharp verify-package &lt;nupkg or folder&gt; [--docker [image]] [--image t=img] [--any-cc-version]</c>: rebuilds each binary in the
    /// package's esharp-bin folder from the sources the package carries, with the toolchain of its target, and compares the bytes.
    /// Exit code 0 when every binary is rebuilt byte for byte (and its manifest's hashes hold), 1 otherwise.
    /// </summary>
    public static int Verify(string[] args)
    {
        string? path = null, dockerAll = null;
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        var anyVersion = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (ParseToolOption(args, ref i, ref dockerAll, images, ref anyVersion, out var optionError)) { if (optionError != null) return Fail(optionError); continue; }
            if (args[i].StartsWith("-")) return Fail($"unknown option '{args[i]}'");
            path = args[i];
        }
        if (path == null) return Fail("verify-package needs a .nupkg or an extracted package folder");
        string root;
        string? temp = null;
        if (File.Exists(path))
        {
            temp = Path.Combine(Path.GetTempPath(), "esharp-verify-" + Guid.NewGuid().ToString("N"));
            try { ZipFile.ExtractToDirectory(path, temp); }
            catch (InvalidDataException e) { return Fail($"{path} is not a package (zip): {e.Message}"); }
            root = temp;
        }
        else if (Directory.Exists(path)) root = path;
        else return Fail($"{path} not found");
        try
        {
            var dlls = Directory.Exists(Path.Combine(root, "lib")) ? Directory.GetFiles(Path.Combine(root, "lib"), "*.dll", SearchOption.AllDirectories).OrderBy(d => d, StringComparer.Ordinal).ToList() : new List<string>();
            EsPackage? library = null;
            string? dll = null;
            foreach (var d in dlls)
                if (ReadLibrary(d, out var lib) == null) { library = lib; dll = d; break; }
            if (library == null || dll == null) return Fail($"{path} holds no E# library (no DLL under lib/ with an E# manifest)");
            var problems = new List<string>();
            var binaries = PackageBinaries.ReadFolder(Path.GetDirectoryName(dll)!, library, problems);
            var stray = PackageBinaries.StrayPrebuilt(root, Path.GetDirectoryName(dll)!, binaries).ToList();
            Console.WriteLine($"Package {library.Id} {library.Version}: {binaries.Count} binary folder(s){(binaries.Count > 0 ? " (" + string.Join(", ", binaries.Select(b => b.BinaryTarget)) + ")" : "")}");
            var failed = 0;
            foreach (var p in problems) { Console.WriteLine($"FAIL {p}"); failed++; }
            foreach (var s in stray) { Console.WriteLine($"FAIL prebuilt file '{s}' is not a listed binary (hand-made objects are refused)"); failed++; }
            if (binaries.Count == 0 && problems.Count == 0)
            {
                Console.WriteLine("The package has no precompiled binaries: nothing to verify (EsPrecompile=false, or a package made before #154).");
                return stray.Count == 0 ? 0 : 1;
            }
            var tools = new ToolOptions(dockerAll, images, anyVersion);
            var stamp = PackageBinaries.Stamp(SourceTree.ToolVersion);
            var outDir = Path.Combine(Path.GetTempPath(), "esharp-verify-out-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var b in binaries)
                {
                    var target = Targets.Find(b.Target);
                    if (target == null) { Console.WriteLine($"FAIL {b.BinaryTarget}: unknown target '{b.Target}'"); failed++; continue; }
                    if (b.Stamp != stamp)
                    {
                        Console.WriteLine($"FAIL {b.BinaryTarget}: built by E# {b.Stamp}; this esharp is {stamp}, which makes other C: verify with that esharp version");
                        failed++;
                        continue;
                    }
                    if (FindTool(target, tools, out var tool) is { } toolError) { Console.WriteLine($"FAIL {b.BinaryTarget}: {toolError}"); failed++; continue; }
                    if (BuildBinary(library, target, tool!, outDir, out var rebuilt) is { } buildError)
                    {
                        Console.WriteLine($"FAIL {b.BinaryTarget}: rebuild failed: {buildError}");
                        failed++;
                        continue;
                    }
                    var same = File.ReadAllBytes(rebuilt!.Archive!).AsSpan().SequenceEqual(File.ReadAllBytes(b.Archive!));
                    var sameManifest = PackageBinaries.Write(rebuilt with { Folder = null }) == PackageBinaries.Write(b with { Folder = null });
                    if (same && sameManifest) Console.WriteLine($"ok {b.BinaryTarget}: {PackageBinaries.ArchiveName} rebuilt from the package's sources, byte for byte ({new FileInfo(b.Archive!).Length} bytes, {tool!.Describe})");
                    else
                    {
                        Console.WriteLine($"FAIL {b.BinaryTarget}: the rebuilt {(same ? PackageBinaries.ManifestName : PackageBinaries.ArchiveName)} differs from the package's (sha256 {PackageBinaries.Sha256File(b.Archive!)} in the package, {PackageBinaries.Sha256File(rebuilt.Archive!)} rebuilt)");
                        failed++;
                    }
                }
            }
            finally { try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch (IOException) { } }
            Console.WriteLine(failed == 0 ? "VERIFIED" : $"NOT VERIFIED: {failed} problem(s)");
            return failed == 0 ? 0 : 1;
        }
        finally
        {
            if (temp != null) try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

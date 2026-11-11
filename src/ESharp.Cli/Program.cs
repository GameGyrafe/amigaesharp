using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ESharp.Compiler;
using Microsoft.CodeAnalysis;

namespace ESharp.Cli;

internal static class Program
{
    private static readonly string Usage = @"esharp - C# for the Amiga

Usage:
  esharp new <name>                 Create a new hello-world project
  esharp build [path] [options]     Check the subset, emit C, and run the target C compiler
  esharp run [path] [options]       Build for m68k and run it in FS-UAE (or --target sdl: run it through SDL;
                                    --target os4: run it in the AmigaOS 4.1 QEMU VM named by ESHARP_OS4_VM)
  esharp assets [dir|assets.json] [--force]
                                    Convert PNGs to planar chip data: writes Assets.g.cs next to the manifest
                                    (only when stale; build and run do this automatically)
  esharp addr <exe | dir> <hunk> <hex offset> [--docker [image]]
                                    Map a crash address of a built m68k program to the C# file:line (see docs/DEBUGGING.md)
  esharp test [path] [options]      Run the project on the host; with --target m68k --docker also on the Amiga (or
                                    --target os4 --docker also in the AmigaOS 4.1 VM, or --target sdl --docker also
                                    through SDL, headless), and compare

Build options:
  --target <name>   m68k (default), os4, morphos, aros or sdl (desktop, SDL 3)
  -o <dir>          Output directory (default: obj next to the sources)
  --emit-only       Stop after writing the C file
  --docker [image]  Run the C compiler inside the toolchain image (default: esharp-amiga-toolchain; esharp-sdl for sdl)
  --cc <compiler>   Override the target's C compiler
  --cflags <flags>  Override the target's C compiler flags
  --allow-float     Permit float/double
  --chip-budget <n> Fail if constant chip RAM reservations exceed n bytes (k/m suffix ok; 68k)
  --debug           Define DEBUG: bounds-checked arrays and spans, poisoned arena Reset (also defines TRACE)
  --trace           Define TRACE: keep Trace.Write/Line/Value calls (serial output); without it they compile to nothing

Run options (build options also apply; run is m68k, os4 or sdl):
  --headless        No window: run on a virtual display, print the program's output, exit
                    (sdl: SDL's offscreen video and dummy audio, inside the --docker image if given)
                    (os4 runs are always unattended: no window; --screenshot uses the QEMU monitor's
                    screendump, --serial collects the kernel debug lines such as Trace; --input goes through the
                    QEMU monitor, see below)
  --timeout <sec>   Headless time limit (default 120; AROS needs ~20 s to boot)
  --kickstart <rom> Use a real Kickstart ROM instead of FS-UAE's built-in AROS replacement
                    (or set ESHARP_KICKSTART)
  --model <name>    Amiga model for FS-UAE: A1200 (default), A500, A500+, A600 (below the minimum spec)
  --fsuae <path>    FS-UAE executable (or set ESHARP_FSUAE)
  --screenshot <png> Headless only: when the program prints SHOT, save the emulator screen to a PNG (needs xwd)
  --serial <file>   Headless only: write the program's serial output (Trace) to this file, and print it after the run
  --input <script>  Headless only: after SHOT send mouse/keyboard input to the emulator (before the screenshot):
                    ""click X,Y;key 9;key 50+38"" with display pixels and X11 key codes, + holds keys (needs libXtst)
                    os4: the QEMU monitor sends it: click X,Y moves the pointer to X,Y of the screen (from the top-left
                    corner), rel DX,DY moves it, and key N takes an AMIGA RAW key code (Esc is 69, 96+32 is Shift+A)
                    sdl: the same script is replayed by the program itself after SHOT, with X,Y in the program's
                    screen pixels; the screenshot is the program's own frame (both also work without --headless)

Test options (esharp test; a program prints ""ok NAME"" / ""FAIL NAME"" lines):
  Always runs on the host (dotnet, HOST defined). With --target m68k it also builds for m68k and runs headless in
  FS-UAE (--docker, --kickstart, --model, --fsuae, --timeout as for run), then reports checks that differ.
  With --target os4 it builds for os4 and boots the AmigaOS 4.1 VM in ESHARP_OS4_VM (QEMU pegasos2, on a throwaway
  overlay, unattended; tools/os4vm/README.md) and compares the same way; --timeout covers the boot (about 10 s).
  With --target sdl it builds for sdl and runs it headless (in the --docker image if given) and compares the same way.
  Exit code is non-zero on any FAIL, crash, build error or host/Amiga difference.

Targets:
" + string.Join("\n", Targets.All.Select(t => $"  {t.Name,-9} {t.Description}")) + "\n";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.Out.Write(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        return args[0] switch
        {
            "new" => New(args.Skip(1).ToArray()),
            "build" => Build(args.Skip(1).ToArray(), out _),
            "run" => Run(args.Skip(1).ToArray()),
            "addr" => Addr.Run(args.Skip(1).ToArray()),
            "assets" => Assets(args.Skip(1).ToArray()),
            "test" => Test(args.Skip(1).ToArray()),
            _ => Fail($"unknown command '{args[0]}'"),
        };
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"esharp: {message}");
        return 1;
    }

    private static int Test(string[] args)
    {
        int timeout = 120;
        string? kickstart = Environment.GetEnvironmentVariable("ESHARP_KICKSTART");
        string model = "A1200";
        string? fsuae = null;
        bool amiga = false, sdl = false, os4 = false;
        var amigaOnly = new List<string>();   // options that only matter with --target m68k
        string path = ".";
        var buildArgs = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out timeout): i++; break;
                case "--kickstart" when i + 1 < args.Length: amigaOnly.Add("--kickstart"); kickstart = args[++i]; break;
                case "--model" when i + 1 < args.Length: amigaOnly.Add("--model"); model = args[++i]; break;
                case "--fsuae" when i + 1 < args.Length: amigaOnly.Add("--fsuae"); fsuae = args[++i]; break;
                case "--target" when i + 1 < args.Length:
                    var t = args[++i];
                    if (t != "m68k" && t != "sdl" && t != "os4")
                        return Fail("test runs on the host, and with --target m68k also in FS-UAE, with --target os4 also in the AmigaOS 4.1 VM (ESHARP_OS4_VM) or with --target sdl also through SDL (headless); other targets are not supported");
                    if (t == "m68k") amiga = true; else if (t == "os4") os4 = true; else sdl = true;
                    if ((amiga ? 1 : 0) + (sdl ? 1 : 0) + (os4 ? 1 : 0) > 1) return Fail("test takes one --target");
                    buildArgs.Add("--target"); buildArgs.Add(t);
                    break;
                case "--docker":
                    amigaOnly.Add("--docker");
                    buildArgs.Add(args[i]);
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) buildArgs.Add(args[++i]);
                    break;
                case "--allow-float": case "--debug": case "--trace": buildArgs.Add(args[i]); break;
                default:
                    if (args[i].StartsWith("-")) return Fail($"unknown option '{args[i]}'");
                    path = args[i];
                    break;
            }
        }

        if (SyncAssets(path) is { } assetError) return Fail(assetError);

        var files = CollectSources(path);
        if (files.Count == 0) return Fail($"no .cs files found in '{path}'");
        if (sdl || os4) amigaOnly.RemoveAll(o => o == "--docker");
        if (!amiga && amigaOnly.Count > 0)
            Console.Error.WriteLine($"esharp: warning: {string.Join(", ", amigaOnly.Distinct())} ignored, because " +
                (sdl || os4 ? "they only matter for --target m68k" : "the test only runs on the host; add --target m68k to also run in FS-UAE"));
        var repo = SourceTree.Find();
        if (repo == null) return Fail("cannot find the E# source tree (src/ESharp.Target.Host); set ESHARP_ROOT");
        var baseDir = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";

        Console.WriteLine("Host:");
        var host = HostRun.Run(Path.Combine(baseDir, "obj", "host"), repo, files, TimeSpan.FromSeconds(timeout));
        if (host.BuildFailed) return Fail("host build failed");
        Console.Write(host.Output);
        var hostReport = TestReport.Parse(host.Output);
        var failed = false;
        Console.WriteLine($"Host: {hostReport.Summary}" + (host.Exit != 0 ? $", exit code {host.Exit} (crash or error)" : ""));
        if (host.Exit != 0 || hostReport.Failed > 0) failed = true;
        if (hostReport.Checks.Count == 0) { Console.Error.WriteLine("esharp: the program printed no ok/FAIL lines"); failed = true; }

        if (amiga)
        {
            if (kickstart != null)
            {
                kickstart = Path.GetFullPath(kickstart.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
                if (!File.Exists(kickstart)) return Fail($"Kickstart ROM not found: {kickstart}");
            }
            Console.WriteLine("\nAmiga (m68k, FS-UAE):");
            buildArgs.Insert(0, path);
            var rc = Build(buildArgs.ToArray(), out var exe);
            if (rc != 0 || exe == null) return Fail("m68k build failed");
            var captured = new StringWriter();
            var saved = Console.Out;
            int runRc;
            Console.SetOut(captured);
            try { runRc = FsUae.Run(exe, true, TimeSpan.FromSeconds(timeout), kickstart, model, fsuae, null, null); }
            finally { Console.SetOut(saved); }
            Console.Write(captured.ToString());
            if (runRc != 0) return Fail("the Amiga run failed (see above)");
            var amigaReport = TestReport.Parse(captured.ToString());
            Console.WriteLine($"Amiga: {amigaReport.Summary}");
            if (amigaReport.Failed > 0) failed = true;
            var diffs = TestReport.Compare(hostReport, amigaReport);
            if (diffs.Count == 0) Console.WriteLine("Host and Amiga agree on every check.");
            else
            {
                Console.WriteLine($"{diffs.Count} difference(s) between host and Amiga:");
                foreach (var d in diffs) Console.WriteLine("  " + d);
                failed = true;
            }
        }

        if (os4)
        {
            Console.WriteLine("\nAmigaOS 4.1 (os4, QEMU pegasos2 VM):");
            buildArgs.Insert(0, path);
            var rc = Build(buildArgs.ToArray(), out var exe);
            if (rc != 0 || exe == null) return Fail("os4 build failed");
            var (exit, output) = Os4Vm.Run(exe, TimeSpan.FromSeconds(timeout), DockerImage(buildArgs, Targets.Os4), null);
            if (exit != 0) return Fail("the os4 run failed (see above)");
            var os4Report = TestReport.Parse(output);
            Console.WriteLine($"OS4: {os4Report.Summary}");
            if (os4Report.Failed > 0) failed = true;
            var diffs = TestReport.Compare(hostReport, os4Report, "OS4");
            if (diffs.Count == 0) Console.WriteLine("Host and OS4 agree on every check.");
            else
            {
                Console.WriteLine($"{diffs.Count} difference(s) between host and OS4:");
                foreach (var d in diffs) Console.WriteLine("  " + d);
                failed = true;
            }
        }

        if (sdl)
        {
            Console.WriteLine("\nSDL (headless" + (DockerImage(buildArgs, Targets.Sdl) is { } image ? ", in " + image : ", on this machine") + "):");
            buildArgs.Insert(0, path);
            var rc = Build(buildArgs.ToArray(), out var exe);
            if (rc != 0 || exe == null) return Fail("sdl build failed");
            var (exit, output) = SdlRun.Run(exe, true, TimeSpan.FromSeconds(timeout), DockerImage(buildArgs, Targets.Sdl), null, null);
            var sdlReport = TestReport.Parse(output);
            Console.WriteLine($"SDL: {sdlReport.Summary}" + (exit != 0 ? $", exit code {exit} (crash or error)" : ""));
            if (exit != 0 || sdlReport.Failed > 0) failed = true;
            var diffs = TestReport.Compare(hostReport, sdlReport, "SDL");
            if (diffs.Count == 0) Console.WriteLine("Host and SDL agree on every check.");
            else
            {
                Console.WriteLine($"{diffs.Count} difference(s) between host and SDL:");
                foreach (var d in diffs) Console.WriteLine("  " + d);
                failed = true;
            }
        }

        Console.WriteLine(failed ? "TEST FAILED" : "TEST PASSED");
        return failed ? 1 : 0;
    }

    private static int New(string[] args)
    {
        if (args.Length != 1) return Fail("usage: esharp new <name or path>   (the last path segment is the project name)");
        var dir = args[0];
        var name = ProjectTemplate.ProjectName(dir);
        var problem = ProjectTemplate.NameProblem(name);
        if (problem != null) return Fail($"'{name}' cannot be a project name: {problem}. It becomes the C# namespace and the csproj name, so use letters, digits and underscores, e.g. {ProjectTemplate.Suggest(name)}");
        if (Directory.Exists(dir)) return Fail($"'{dir}' already exists");

        Directory.CreateDirectory(dir);
        var root = SourceTree.Find();
        ProjectTemplate.Write(dir, name, root, Path.Combine(AppContext.BaseDirectory, "esharp.dll"));
        Console.WriteLine($"Created {dir}/ (Program.cs, {name}.csproj, .vscode/). Build it with: esharp build {dir}");
        if (root == null)
            Console.Error.WriteLine($"esharp: could not find the E# source tree; set ESHARP_ROOT, or ESharpRoot in {Path.Combine(dir, name + ".csproj")}, before using dotnet build or the editor");
        return 0;
    }

    private static long? ParseSize(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        long scale = 1;
        if (t.EndsWith("k")) { scale = 1024; t = t[..^1]; }
        else if (t.EndsWith("m")) { scale = 1024 * 1024; t = t[..^1]; }
        return long.TryParse(t, out var n) && n >= 0 ? n * scale : null;
    }

    private static void PrintChip(IReadOnlyList<ChipEntry> chip, long? budget)
    {
        if (chip.Count == 0 && budget == null) return;
        Console.WriteLine("Chip RAM reserved at constant sizes (each call site counted once):");
        foreach (var e in chip)
            Console.WriteLine($"  {e.Kind,-7} {e.What,-28} {(e.Bytes < 0 ? "run-time size" : e.Bytes.ToString("N0") + " bytes"),-16} {e.Location}");
        var total = chip.Where(e => e.Bytes >= 0).Sum(e => e.Bytes);
        var unknown = chip.Count(e => e.Bytes < 0);
        Console.WriteLine($"  total   {total:N0} bytes" + (unknown > 0 ? $" (+ {unknown} sized at run time)" : "") +
                          (budget != null ? $", budget {budget:N0} ({total * 100.0 / Math.Max(1, budget.Value):0.#}%)" : ""));
    }

    private static int Build(string[] args, out string? builtExe)
    {
        builtExe = null;
        string path = ".";
        string? outDir = null;
        bool emitOnly = false, allowFloat = false, debug = false, trace = false;
        long? chipBudget = null;
        var target = Targets.M68k;
        string? cc = null, cflags = null, docker = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--emit-only": emitOnly = true; break;
                case "--allow-float": allowFloat = true; break;
                case "--debug": debug = true; break;
                case "--trace": trace = true; break;
                case "--chip-budget" when i + 1 < args.Length:
                    chipBudget = ParseSize(args[++i]);
                    if (chipBudget == null) return Fail($"bad --chip-budget '{args[i]}' (use bytes, or a number followed by k or m)");
                    break;
                case "--target" when i + 1 < args.Length:
                    var found = Targets.Find(args[++i]);
                    if (found == null)
                        return Fail($"unknown target '{args[i]}' (known: {string.Join(", ", Targets.All.Select(t => t.Name))})");
                    target = found;
                    break;
                case "--docker":
                    docker = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[++i] : "";
                    break;
                case "--cc" when i + 1 < args.Length: cc = args[++i]; break;
                case "--cflags" when i + 1 < args.Length: cflags = args[++i]; break;
                default:
                    if (args[i].StartsWith("-")) return Fail($"unknown option '{args[i]}'");
                    path = args[i];
                    break;
            }
        }

        if (docker == "") docker = DefaultImage(target);
        if (SyncAssets(path) is { } assetError) return Fail(assetError);

        var files = CollectSources(path);
        if (files.Count == 0) return Fail($"no .cs files found in '{path}'");

        var baseDir = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var sources = files.Select(f => new SourceFile(Path.GetRelativePath(Directory.GetCurrentDirectory(), f), File.ReadAllText(f))).ToList();
        var result = EsCompiler.Compile(sources, target.ToOptions(allowFloat, debug, chipBudget, trace));

        foreach (var d in result.Diagnostics)
            Console.Error.WriteLine(d.ToString());

        PrintChip(result.Chip, chipBudget);

        if (!result.Success)
        {
            var errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            Console.Error.WriteLine($"Build failed: {errors} error(s)");
            return 1;
        }

        outDir ??= Path.Combine(baseDir, "obj", target.Name);
        Directory.CreateDirectory(outDir);
        var name = Path.GetFileNameWithoutExtension(files[0]);
        if (Directory.Exists(path)) name = new DirectoryInfo(Path.GetFullPath(path)).Name;
        var cFile = Path.Combine(outDir, name + ".c");
        File.WriteAllText(cFile, result.C!);
        Console.WriteLine($"Wrote {cFile}");
        if (emitOnly) return 0;

        cc ??= target.Compiler;
        cflags ??= target.CFlags;
        var exe = Path.Combine(outDir, name);

        // Inside the container the output directory is mounted at /work.
        int Compile(string[] flags, string outFile, bool link)
        {
            var compilerArgs = new List<string>();
            compilerArgs.AddRange(cflags.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            compilerArgs.AddRange(flags);
            compilerArgs.AddRange(new[] { "-o", docker == null ? Path.Combine(outDir, outFile) : outFile, docker == null ? cFile : name + ".c" });
            if (link) compilerArgs.AddRange(target.Libs.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            ProcessStartInfo psi;
            if (target.PkgConfig.Length > 0)
            {
                // The sdl target links SDL 3 statically: the flags come from pkg-config, so the compiler runs under sh.
                var line = string.Join(" ", compilerArgs.Prepend(cc).Select(ShellQuote)) + $" $(pkg-config --static --cflags --libs {target.PkgConfig})";
                psi = new ProcessStartInfo(docker == null ? "sh" : "docker") { UseShellExecute = false };
                if (docker != null)
                    foreach (var a in new[] { "run", "--rm", "-v", $"{Path.GetFullPath(outDir)}:/work", "-w", "/work", docker, "sh" })
                        psi.ArgumentList.Add(a);
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(line);
            }
            else if (docker == null)
            {
                psi = new ProcessStartInfo(cc) { UseShellExecute = false };
                foreach (var a in compilerArgs) psi.ArgumentList.Add(a);
            }
            else
            {
                psi = new ProcessStartInfo("docker") { UseShellExecute = false };
                foreach (var a in new[] { "run", "--rm", "-v", $"{Path.GetFullPath(outDir)}:/work", "-w", "/work", docker, cc })
                    psi.ArgumentList.Add(a);
                foreach (var a in compilerArgs) psi.ArgumentList.Add(a);
            }

            try
            {
                using var proc = Process.Start(psi)!;
                proc.WaitForExit();
                if (proc.ExitCode != 0) return Fail($"{cc} failed with exit code {proc.ExitCode}");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Fail($"'{psi.FileName}' not found. Install {cc} (see docker/Dockerfile), pass --docker, or use --emit-only.");
            }
            return 0;
        }

        if (Compile(Array.Empty<string>(), name, link: true) != 0) return 1;
        // m68k: the same C compiled with -g, for 'esharp addr' (the hunk exe's own debug data is not readable by the binutils).
        if (target.IsM68k && Compile(new[] { "-g", "-c" }, name + ".o", link: false) != 0) return 1;

        Console.WriteLine($"Built {exe}");
        builtExe = exe;
        return 0;
    }

    /// <summary>
    /// Build step: regenerates Assets.g.cs for the project's assets.json (and those of esharp.include directories)
    /// when it is missing or older than the manifest or a PNG. Returns an error message, or null.
    /// </summary>
    /// <summary>Errors a bad assets.json or PNG can raise; reported as a clean message instead of a stack trace.</summary>
    private static bool IsAssetError(Exception e) =>
        e is IOException or InvalidDataException or NotSupportedException or System.Text.Json.JsonException or KeyNotFoundException
            or InvalidOperationException or ArgumentException;   // a wrong JSON type (images not an array), a value out of range

    internal static string? SyncAssets(string path)
    {
        if (File.Exists(path)) path = Path.GetDirectoryName(Path.GetFullPath(path))!;   // esharp build File.cs: its directory
        if (!Directory.Exists(path)) return null;
        var dirs = new List<string> { path };
        var include = Path.Combine(path, "esharp.include");
        if (File.Exists(include))
            foreach (var raw in File.ReadAllLines(include))
            {
                var line = raw.Trim();
                if (line.Length > 0 && line[0] != '#') dirs.Add(Path.Combine(path, line));
            }
        foreach (var dir in dirs)
        {
            var manifest = Path.Combine(dir, "assets.json");
            if (!File.Exists(manifest)) continue;
            try
            {
                var r = ESharp.Assets.AssetBuilder.Sync(manifest);
                if (r.Status is ESharp.Assets.AssetBuilder.SyncStatus.Created or ESharp.Assets.AssetBuilder.SyncStatus.Updated)
                    Console.WriteLine($"Regenerated {r.Target} (assets changed)");
                foreach (var w in r.Output?.Warnings ?? Array.Empty<string>()) Console.WriteLine($"warning: {w}");
            }
            catch (Exception e) when (IsAssetError(e))
            {
                return $"{manifest}: {e.Message}";
            }
        }
        return null;
    }

    private static int Assets(string[] args)
    {
        var path = args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".";
        var manifest = Directory.Exists(path) ? Path.Combine(path, "assets.json") : path;
        if (!File.Exists(manifest)) return Fail($"no assets.json at '{manifest}'");
        try
        {
            var sync = ESharp.Assets.AssetBuilder.Sync(manifest, force: args.Contains("--force"));
            var target = sync.Target;
            if (sync.Output == null) { Console.WriteLine($"{target} is up to date (use --force to rebuild)"); return 0; }
            var output = sync.Output;
            Console.WriteLine(sync.Status == ESharp.Assets.AssetBuilder.SyncStatus.Touched ? $"{target} unchanged" : $"Wrote {target}");
            foreach (var r in output.Reports)
                Console.WriteLine(r.IsSample
                    ? $"  {r.Name,-16} sample, {r.ChipBytes:N0} bytes of chip RAM"
                    : $"  {r.Name,-16} {r.Width}x{r.Height}, {r.Depth} bitplanes, {r.ChipBytes:N0} bytes of chip RAM");
            foreach (var w in output.Warnings ?? Array.Empty<string>()) Console.WriteLine($"warning: {w}");
            Console.WriteLine($"  total            {output.Reports.Sum(r => r.ChipBytes):N0} bytes of chip RAM");
            return 0;
        }
        catch (Exception e) when (IsAssetError(e))
        {
            return Fail(e.Message);
        }
    }

    private static int Run(string[] args)
    {
        bool headless = false;
        int timeout = 120;
        string? kickstart = Environment.GetEnvironmentVariable("ESHARP_KICKSTART");
        string model = "A1200";
        string? fsuae = null;
        string? screenshot = null;
        string? input = null;
        string? serial = null;
        var buildArgs = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless": headless = true; break;
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out timeout): i++; break;
                case "--kickstart" when i + 1 < args.Length: kickstart = args[++i]; break;
                case "--model" when i + 1 < args.Length: model = args[++i]; break;
                case "--fsuae" when i + 1 < args.Length: fsuae = args[++i]; break;
                case "--screenshot" when i + 1 < args.Length: screenshot = args[++i]; break;
                case "--input" when i + 1 < args.Length: input = args[++i]; break;
                case "--serial" when i + 1 < args.Length: serial = args[++i]; break;
                default: buildArgs.Add(args[i]); break;
            }
        }

        var targetIndex = buildArgs.IndexOf("--target");
        var targetName = targetIndex >= 0 && targetIndex + 1 < buildArgs.Count ? buildArgs[targetIndex + 1] : "m68k";
        if (buildArgs.Contains("--emit-only")) return Fail("--emit-only cannot be combined with run");
        if (string.Equals(targetName, "sdl", StringComparison.OrdinalIgnoreCase))
        {
            var sdlRc = Build(buildArgs.ToArray(), out var sdlExe);
            if (sdlRc != 0 || sdlExe == null) return sdlRc;
            var (exit, _) = SdlRun.Run(sdlExe, headless, TimeSpan.FromSeconds(timeout), DockerImage(buildArgs, Targets.Sdl), screenshot, input);
            if (exit != 0) Console.Error.WriteLine($"esharp: the program exited with code {exit}");
            return exit;
        }
        if (string.Equals(targetName, "os4", StringComparison.OrdinalIgnoreCase))
        {
            var os4Rc = Build(buildArgs.ToArray(), out var os4Exe);
            if (os4Rc != 0 || os4Exe == null) return os4Rc;
            var (exit, _) = Os4Vm.Run(os4Exe, TimeSpan.FromSeconds(timeout), DockerImage(buildArgs, Targets.Os4), screenshot, serial, input: input);
            return exit;
        }
        if (targetName != "m68k")
            return Fail("run uses FS-UAE, which emulates 68k Amigas only, an AmigaOS 4.1 QEMU VM (--target os4, ESHARP_OS4_VM), or SDL on this machine; use --target m68k, os4 or sdl");

        if (kickstart != null)
        {
            kickstart = Path.GetFullPath(kickstart.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            if (!File.Exists(kickstart)) return Fail($"Kickstart ROM not found: {kickstart}");
        }

        var rc = Build(buildArgs.ToArray(), out var exe);
        if (rc != 0 || exe == null) return rc;
        return FsUae.Run(exe, headless, TimeSpan.FromSeconds(timeout), kickstart, model, fsuae, screenshot, input, serial);
    }

    /// <summary>The image a build's <c>--docker [image]</c> option names, or null without the option.</summary>
    private static string? DockerImage(IList<string> buildArgs, TargetInfo target)
    {
        var i = buildArgs.IndexOf("--docker");
        if (i < 0) return null;
        return i + 1 < buildArgs.Count && !buildArgs[i + 1].StartsWith("-") ? buildArgs[i + 1] : DefaultImage(target);
    }

    /// <summary>The image <c>--docker</c> without a name uses: esharp-sdl for the sdl target, else the Amiga toolchain.</summary>
    internal static string DefaultImage(TargetInfo target) => target.IsAmiga ? "esharp-amiga-toolchain" : "esharp-sdl";

    private static string ShellQuote(string s) =>
        s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || "-_./=+:,".Contains(c)) ? s : "'" + s.Replace("'", "'\\''") + "'";

    private static List<string> CollectSources(string path)
    {
        if (File.Exists(path)) return new List<string> { path };
        if (!Directory.Exists(path)) return new List<string>();
        var files = SourcesUnder(path);
        // An optional esharp.include file lists more source directories (relative to the sample, one per line, # for comments)
        // so that several samples can share generated code instead of each keeping a copy.
        var include = Path.Combine(path, "esharp.include");
        if (File.Exists(include))
        {
            foreach (var raw in File.ReadAllLines(include))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var dir = Path.GetFullPath(Path.Combine(path, line));
                foreach (var f in SourcesUnder(dir))
                    if (!files.Contains(f)) files.Add(f);
            }
        }
        return files;
    }

    private static List<string> SourcesUnder(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }
}

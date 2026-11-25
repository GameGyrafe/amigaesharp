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
  esharp new <name> [--source [dir] | --package [version]]
                                    Create a new hello-world project. Its csproj references the AmigaESharp.Sdk package
                                    at this tool's version (--package), or, with --source or when this esharp runs from an
                                    E# checkout, that checkout's projects (for contributors; ESHARP_ROOT names one)
  esharp build [path] [options]     Check the subset, emit C, and run the target C compiler
  esharp run [path] [options]       Build for m68k and run it in FS-UAE (or --target sdl: run it through SDL;
                                    --target os4: run it in the AmigaOS 4.1 QEMU VM named by ESHARP_OS4_VM)
  esharp assets [dir|assets.json] [--force]
                                    Convert PNGs to planar chip data: writes Assets.g.cs next to the manifest
                                    (only when stale; build and run do this automatically)
  esharp addr <exe | dir> [<where>] [--target <t>] [--docker [image] | --toolchain <dir>]
                                    Map a crash address to the C# file:line or the nearest symbol: m68k <hunk> <hex offset>,
                                    AROS <segment> <offset>, os4 <section> <offset>, or Function+0x48 (docs/DEBUGGING.md)
  esharp test [path] [options]      Run the project on the host; with --target m68k --docker also on the Amiga (or
                                    --target os4 --docker also in the AmigaOS 4.1 VM, --target aros or aros-i386 also on
                                    hosted AROS in Docker, or --target sdl --docker also through SDL, headless), and compare
  esharp os4vm check|start|install-hook|remove-hook [options]
                                    Helpers for the AmigaOS 4.1 QEMU VM in ESHARP_OS4_VM (python3; see the README.md
                                    in the os4vm folder next to esharp.dll)
  esharp precompile <library dir> --dll <dll> --out <dir> [--targets <a;b>] [--docker [image]] [--image <target>=<image>]
                                    Build the precompiled per-target binaries of an E# library (esharp-bin/<target>/library.a
                                    and binary.json); the AmigaESharp.Sdk pack step runs this at dotnet pack (EsPrecompile)
  esharp verify-package <nupkg | folder> [--docker [image]] [--image <target>=<image>]
                                    Rebuild every precompiled binary of an E# package from the sources it carries and
                                    compare the bytes (exit code 1 when one differs or a binary is not the pack step's)
  esharp doctor [--target <name> [--toolchain <dir>]]
                                    What this machine can do: per target, a build by Docker (image present) and/or by a
                                    local toolchain (compiler, version), and FS-UAE, the OS4 VM and the hosted AROS images.
                                    Offline and read-only; exit code 0
  esharp --version                  Print the version

Build options:
  --target <name>   m68k (default), os4, morphos, aros, aros-i386 or sdl (desktop, SDL 3)
  -o <dir>          Output directory (default: obj next to the sources)
  --emit-only       Stop after writing the C file
  --docker [image]  Run the C compiler inside the toolchain image (default: esharp-amiga-toolchain; esharp-sdl for sdl,
                    esharp-aros-i386 for aros-i386)
  --toolchain <dir> Run the compiler of a local toolchain: <dir> is its install prefix, holding bin/<compiler> (for
                    m68k the folder the pinned image has at /opt/m68k-amigaos). Or set ESHARP_TOOLCHAIN_<TARGET>
                    (ESHARP_TOOLCHAIN_M68K, _OS4, _MORPHOS, _AROS, _AROS_I386, _SDL) or ESHARP_TOOLCHAIN (Amiga targets).
                    Without --docker or a toolchain the compiler is looked up on PATH. An empty <dir> counts as not given
  --sysroot <dir>   AROS: the SDK sysroot of a local toolchain (default: the --toolchain folder for aros, its sdk folder
                    for aros-i386; or ESHARP_SYSROOT_AROS / ESHARP_SYSROOT_AROS_I386)
  --any-cc-version  Build with a local compiler whose version differs from the pinned image's (not tested)
  --cc <compiler>   Override the target's C compiler
  --cflags <flags>  Override the target's C compiler flags
  --allow-float     Permit float/double
  --chip-budget <n> Fail if constant chip RAM reservations exceed n bytes (k/m suffix ok; 68k)
  --debug           Define DEBUG: bounds-checked arrays and spans, poisoned arena Reset (also defines TRACE)
  --trace           Define TRACE: keep Trace.Write/Line/Value calls (serial output); without it they compile to nothing
  --from-source     Compile the E# library packages from their sources even where a precompiled binary for the target fits

Run options (build options also apply; run is m68k, os4 or sdl):
  --headless        No window: run on a virtual display, print the program's output, exit
                    (sdl: SDL's offscreen video and dummy audio, inside the --docker image if given)
                    (os4 runs are always unattended: no window; --screenshot uses the QEMU monitor's
                    screendump, --serial collects the kernel debug lines such as Trace; --input goes through the
                    QEMU monitor, see below)
  --timeout <sec>   Headless time limit (default 120; AROS needs ~20 s to boot)
  --mui <3.8|5>     m68k (FS-UAE) only: mount MUI 3.8 or 5 from the per-user cache (or set ESHARP_MUI); get it first with
                    bash <esharp folder>/mui/fetch-mui.sh <3.8|5> (tools/mui/ in a checkout; shareware, never shipped)
  --workbench <adf> With --mui: your own Workbench 3.x disk 2 (or ESHARP_WORKBENCH), mounted read-only as DF0: for C: and the
                    system libraries MUI opens (asl, commodities, icon, ...); never copied
  --kickstart <rom> Use a real Kickstart ROM instead of FS-UAE's built-in AROS replacement
                    (or set ESHARP_KICKSTART)
  --model <name>    Amiga model for FS-UAE: A1200 (default), A500, A500+, A600 (below the minimum spec)
  --fsuae <path>    FS-UAE executable (or set ESHARP_FSUAE)
  --screenshot <png> Headless only: when the program prints SHOT, save the emulator screen to a PNG (needs xwd)
  --serial <file>   Headless only: write the program's serial output (Trace) to this file, and print it after the run
  --args <line>     Arguments for the program (Args.Count, Args.Get, Args.Value): ""SEED 42 AUTO"" (run: m68k, sdl, os4; test: the host and the target)
  --input <script>  Headless only: after SHOT send mouse/keyboard input to the emulator (before the screenshot):
                    ""click X,Y;key 9;key 50+38"" with display pixels and X11 key codes, + holds keys (needs libXtst);
                    ""rel DX,DY"" moves, ""press"" clicks where the pointer is, ""down""/""up"" hold and release (a drag)
                    os4: the QEMU monitor sends it: click X,Y moves the pointer to X,Y of the screen (from the top-left
                    corner), rel DX,DY moves it, down/up hold and release, and key N takes an AMIGA RAW key code (Esc is 69, 96+32 is Shift+A)
                    sdl: the same script is replayed by the program itself after SHOT, with X,Y in the program's
                    screen pixels; the screenshot is the program's own frame (both also work without --headless)

Test options (esharp test; a program prints ""ok NAME"" / ""FAIL NAME"" lines):
  Always runs on the host (dotnet, HOST defined). With --target m68k it also builds for m68k and runs headless in
  FS-UAE (--docker, --kickstart, --model, --fsuae, --timeout as for run), then reports checks that differ.
  With --target os4 it builds for os4 and boots the AmigaOS 4.1 VM in ESHARP_OS4_VM (QEMU pegasos2, on a throwaway
  overlay, unattended; tools/os4vm/README.md) and compares the same way; --timeout covers the boot (about 10 s).
  With --target sdl it builds for sdl and runs it headless (in the --docker image if given) and compares the same way.
  With --target aros or aros-i386 it builds for that target (--docker: the toolchain image) and boots hosted AROS headless
  in the esharp-aros-hosted (or esharp-aros-hosted-i386) image, built from docker/aros-hosted in the E# repository;
  --aros-image <image> names another. A ""Trap"" line in AROS's debug output counts as a failure.
  The target program's return code is shown (m68k, os4, aros: the shell's RC after it; sdl: the exit code) and fails
  the test when it is non-zero while the host returned 0. morphos has no runner.
  --args ""<line>"" gives the same argument line to the host run and the target run (split at spaces, ""quoted text"" kept).
  Exit code is non-zero on any FAIL, crash, build error, host/target difference or non-zero target return code.

Targets:
" + string.Join("\n", Targets.All.Select(t => $"  {t.Name,-10} {t.Description}")) + "\n";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.Out.Write(Usage);
            return args.Length == 0 ? 1 : 0;
        }
        if (args[0] is "--version" or "version")
        {
            Console.WriteLine(SourceTree.ToolVersion);
            return 0;
        }

        return args[0] switch
        {
            "new" => New(args.Skip(1).ToArray()),
            "build" => Build(args.Skip(1).ToArray(), out _),
            "run" => Run(args.Skip(1).ToArray()),
            "addr" => Addr.Run(args.Skip(1).ToArray()),
            "assets" => Assets(args.Skip(1).ToArray()),
            "test" => Test(args.Skip(1).ToArray()),
            "os4vm" => Os4VmHelper(args.Skip(1).ToArray()),
            "doctor" => Doctor.Run(args.Skip(1).ToArray()),
            "precompile" => Precompile.Run(args.Skip(1).ToArray()),
            "verify-package" => Precompile.Verify(args.Skip(1).ToArray()),
            _ => Fail($"unknown command '{args[0]}'"),
        };
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"esharp: {message}");
        return 1;
    }

    internal static int Test(string[] args)
    {
        int timeout = 120;
        string? kickstart = Environment.GetEnvironmentVariable("ESHARP_KICKSTART");
        string model = "A1200";
        string? fsuae = null;
        bool amiga = false, sdl = false, os4 = false;
        TargetInfo? aros = null;           // aros or aros-i386: hosted AROS in Docker
        string? arosImage = null;
        var amigaOnly = new List<string>();   // options that only matter with --target m68k
        string path = ".";
        string? programArgs = null;
        string? muiOption = null, workbenchOption = null;
        var buildArgs = new List<string>();
        var toolchainOptions = new List<string>();   // only matter with a --target
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out timeout): i++; break;
                case "--args" when i + 1 < args.Length: programArgs = args[++i]; break;
                case "--mui" when i + 1 < args.Length: amigaOnly.Add("--mui"); muiOption = args[++i]; break;
                case "--workbench" when i + 1 < args.Length: amigaOnly.Add("--workbench"); workbenchOption = args[++i]; break;
                case "--kickstart" when i + 1 < args.Length: amigaOnly.Add("--kickstart"); kickstart = args[++i]; break;
                case "--model" when i + 1 < args.Length: amigaOnly.Add("--model"); model = args[++i]; break;
                case "--fsuae" when i + 1 < args.Length: amigaOnly.Add("--fsuae"); fsuae = args[++i]; break;
                case "--aros-image" when i + 1 < args.Length: arosImage = args[++i]; break;
                case "--target" when i + 1 < args.Length:
                    var t = args[++i];
                    if (TestTargetError(t) is { } targetError) return Fail(targetError);
                    if ((amiga || sdl || os4 || aros != null)) return Fail("test takes one --target");
                    if (t == "m68k") amiga = true; else if (t == "os4") os4 = true; else if (t == "sdl") sdl = true;
                    else aros = Targets.Find(t);
                    buildArgs.Add("--target"); buildArgs.Add(t);
                    break;
                case "--docker":
                    amigaOnly.Add("--docker");
                    buildArgs.Add(args[i]);
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) buildArgs.Add(args[++i]);
                    break;
                case "--allow-float": case "--debug": case "--trace": case "--any-cc-version": case "--from-source": buildArgs.Add(args[i]); break;
                case "--toolchain" or "--sysroot" or "--cc" or "--cflags" when i + 1 < args.Length:
                    if (args[i] is "--toolchain" or "--sysroot" && args[i + 1].Length > 0) toolchainOptions.Add(args[i]);
                    buildArgs.Add(args[i]); buildArgs.Add(args[++i]);
                    break;
                default:
                    if (args[i].StartsWith("-")) return Fail($"unknown option '{args[i]}'");
                    path = args[i];
                    break;
            }
        }

        if (Os4Vm.ProgramArgsError(programArgs) is { } testArgsError) return Fail(testArgsError);
        if ((muiOption ?? workbenchOption) != null && (sdl || os4 || aros != null)) return Fail(MuiTargetError(sdl ? "sdl" : os4 ? "os4" : aros!.Name));
        if (arosImage != null && aros == null) return Fail("--aros-image is for --target aros or aros-i386 (the hosted AROS image to run in)");
        string? muiDrawer = null, workbench = null;
        if (amiga && MuiDrawer(muiOption, workbenchOption, out muiDrawer, out workbench) is { } testMuiError) return Fail(testMuiError);
        if (SyncAssets(path) is { } assetError) return Fail(assetError);

        var files = CollectSources(path);
        if (files.Count == 0) return Fail($"no .cs files found in '{path}'");
        if (sdl || os4 || aros != null) amigaOnly.RemoveAll(o => o == "--docker");
        if (!amiga && !sdl && !os4 && aros == null && toolchainOptions.Count > 0)
            Console.Error.WriteLine($"esharp: warning: {string.Join(", ", toolchainOptions.Distinct())} ignored: the test only runs on the host; add a --target to also build and run it there");
        if (!amiga && amigaOnly.Count > 0)
            Console.Error.WriteLine($"esharp: warning: {string.Join(", ", amigaOnly.Distinct())} ignored, because " +
                (sdl || os4 || aros != null ? "they only matter for --target m68k" : "the test only runs on the host; add --target m68k to also run in FS-UAE"));
        var repo = SourceTree.HostSide();
        if (repo == null) return Fail("cannot find the E# host libraries (the host folder next to esharp.dll) or an E# source tree; reinstall AmigaESharp.Tool or set ESHARP_ROOT to a checkout");
        var baseDir = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";

        // E# library packages: the host run references their DLLs (the HOST build each package carries).
        if (PackageStep.Load(path, out var packages) is { } packageError) return Fail(packageError);
        foreach (var w in packages.Warnings) Console.Error.WriteLine($"esharp: warning: {w}");
        // ES0112 from the manifest first: the host run loads the package assemblies, which may need a newer Sdk than this one.
        if (PackageStep.SdkErrors(packages.Packages, SourceTree.ToolVersion) is { Count: > 0 } sdkErrors) return Fail(string.Join("\n", sdkErrors));

        Console.WriteLine("Host:");
        var host = HostRun.Run(Path.Combine(baseDir, "obj", "host"), repo, files, TimeSpan.FromSeconds(timeout),
            programArgs == null ? null : SdlRun.SplitCommandLine(programArgs),
            packages.Packages.Select(p => p.DllPath).OfType<string>().ToList());
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
            var info = new RunInfo();
            Console.SetOut(captured);
            try { runRc = FsUae.Run(exe, true, TimeSpan.FromSeconds(timeout), kickstart, model, fsuae, null, null, null, programArgs, muiDrawer, workbench, info); }
            finally { Console.SetOut(saved); }
            Console.Write(captured.ToString());
            if (runRc != 0) return Fail("the Amiga run failed (see above)");
            if (!CompareWithHost(hostReport, host.Exit, TestReport.Parse(captured.ToString()), "Amiga", info.ReturnCode)) failed = true;
        }

        if (os4)
        {
            Console.WriteLine("\nAmigaOS 4.1 (os4, QEMU pegasos2 VM):");
            buildArgs.Insert(0, path);
            var rc = Build(buildArgs.ToArray(), out var exe);
            if (rc != 0 || exe == null) return Fail("os4 build failed");
            var info = new RunInfo();
            var (exit, output) = Os4Vm.Run(exe, TimeSpan.FromSeconds(timeout), DockerImage(buildArgs, Targets.Os4), null, programArgs: programArgs, info: info, localCc: LastLocalCompiler);
            if (exit != 0) return Fail("the os4 run failed (see above)");
            if (!CompareWithHost(hostReport, host.Exit, TestReport.Parse(output), "OS4", info.ReturnCode)) failed = true;
        }

        if (aros != null)
        {
            var image = arosImage ?? ArosHosted.DefaultImage(aros);
            Console.WriteLine($"\nAROS ({aros.Name}, hosted {ArosHosted.Arch(aros)} in {image}):");
            buildArgs.Insert(0, path);
            var rc = Build(buildArgs.ToArray(), out var exe);
            if (rc != 0 || exe == null) return Fail($"{aros.Name} build failed");
            var run = ArosHosted.Run(exe, aros, TimeSpan.FromSeconds(timeout), image, programArgs);
            Console.Write(run.Output);
            if (run.Exit != 0) return Fail("the hosted AROS run failed (see above)");
            if (!CompareWithHost(hostReport, host.Exit, TestReport.Parse(run.Output), "AROS", run.ReturnCode, run.Traps)) failed = true;
        }

        if (sdl)
        {
            Console.WriteLine("\nSDL (headless" + (DockerImage(buildArgs, Targets.Sdl) is { } image ? ", in " + image : ", on this machine") + "):");
            buildArgs.Insert(0, path);
            var rc = Build(buildArgs.ToArray(), out var exe);
            if (rc != 0 || exe == null) return Fail("sdl build failed");
            var (exit, output) = SdlRun.Run(exe, true, TimeSpan.FromSeconds(timeout), DockerImage(buildArgs, Targets.Sdl), null, null,
                programArgs: programArgs == null ? null : SdlRun.SplitCommandLine(programArgs));
            if (!CompareWithHost(hostReport, host.Exit, TestReport.Parse(output), "SDL", exit)) failed = true;
        }

        Console.WriteLine(failed ? "TEST FAILED" : "TEST PASSED");
        return failed ? 1 : 0;
    }

    /// <summary>Why <c>esharp test --target t</c> cannot run, or null for a target it runs on.</summary>
    internal static string? TestTargetError(string target) => target switch
    {
        "m68k" or "os4" or "sdl" or "aros" or "aros-i386" => null,
        "morphos" => "no runner for morphos: esharp test cannot run MorphOS programs yet (no emulator or VM harness); " +
                     "build them with esharp build --target morphos --docker <image> and run them on a MorphOS machine",
        _ => $"test runs on the host, and with --target m68k also in FS-UAE, os4 in the AmigaOS 4.1 VM (ESHARP_OS4_VM), aros or aros-i386 " +
             $"on hosted AROS in Docker (esharp-aros-hosted images) or sdl through SDL (headless); not '{target}'",
    };

    /// <summary>Prints the target's summary and the differences from the host; false when the target side fails the test.</summary>
    private static bool CompareWithHost(TestReport hostReport, int hostExit, TestReport target, string label, int? returnCode, int? traps = null)
    {
        var verdict = TestReport.Judge(hostReport, hostExit, target, label, returnCode, traps);
        foreach (var line in verdict.Lines) Console.WriteLine(line);
        return verdict.Ok;
    }

    private static int New(string[] args)
    {
        const string usage = "usage: esharp new [--library] <name or path> [--source [E# checkout] | --package [version]]   (the last path segment is the project name; --library writes an E# library with a demo program)";
        string? dir = null, source = null, version = null;
        bool wantSource = false, wantPackage = false, library = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--library": library = true; break;
                case "--source":
                    wantSource = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-") && dir != null) source = args[++i];
                    break;
                case "--package":
                    wantPackage = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-") && dir != null) version = args[++i];
                    break;
                default:
                    if (args[i].StartsWith("-") || dir != null) return Fail(usage);
                    dir = args[i];
                    break;
            }
        }
        if (dir == null || (wantSource && wantPackage)) return Fail(usage);
        var name = ProjectTemplate.ProjectName(dir);
        var problem = ProjectTemplate.NameProblem(name);
        if (problem != null) return Fail($"'{name}' cannot be a project name: {problem}. It becomes the C# namespace and the csproj name, so use letters, digits and underscores, e.g. {ProjectTemplate.Suggest(name)}");
        if (Directory.Exists(dir)) return Fail($"'{dir}' already exists");

        // A checkout: the one named, else ESHARP_ROOT or the checkout this esharp was built in (not the current directory,
        // so an installed tool run inside some checkout still writes a package reference unless asked).
        string? root = null;
        if (source != null)
        {
            if (!SourceTree.IsRoot(source)) return Fail($"'{source}' is not an E# checkout (no src/ESharp.Target.Host and src/ESharp.Analyzers)");
            root = Path.GetFullPath(source);
        }
        else if (!wantPackage)
        {
            root = SourceTree.Find(AppContext.BaseDirectory);
            if (wantSource && root == null) return Fail("--source: cannot find an E# checkout; give its path or set ESHARP_ROOT");
        }

        Directory.CreateDirectory(dir);
        version ??= SourceTree.ToolVersion;
        var cliDll = Path.Combine(AppContext.BaseDirectory, "esharp.dll");
        if (library)
        {
            ProjectTemplate.WriteLibrary(dir, name, root, cliDll, version);
            Console.WriteLine($"Created the library {dir}/{name}/ and its demo {dir}/{name}.Demo/ (README.md, nuget.config, feed/, .vscode/). " +
                              $"Change the PackageId in {name}.csproj, then: dotnet pack {dir}/{name} -o {dir}/feed && dotnet restore {dir}/{name}.Demo && esharp test {dir}/{name}.Demo");
        }
        else
        {
            ProjectTemplate.Write(dir, name, root, cliDll, version);
            Console.WriteLine($"Created {dir}/ (Program.cs, {name}.csproj, .vscode/). Build it with: esharp build {dir}");
        }
        Console.WriteLine(root != null
            ? $"The csproj references the E# checkout at {root} (ESharpRoot)."
            : $"The csproj references the {SourceTree.SdkPackage} package, version {version}.");
        return 0;
    }

    /// <summary>`esharp os4vm ...`: the VM helper script shipped next to the CLI (tools/os4vm/os4vm.py in the repository).</summary>
    private static int Os4VmHelper(string[] args)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "os4vm", "os4vm.py");
        if (!File.Exists(script)) return Fail($"missing {script}; reinstall AmigaESharp.Tool");
        var psi = new ProcessStartInfo("python3") { UseShellExecute = false };
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add(script);
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Fail("python3 not found; esharp os4vm needs Python 3");
        }
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

    /// <summary>The local compiler the last <see cref="Build"/> used (null for a Docker build), for helpers built alongside (os4 estype).</summary>
    internal static string? LastLocalCompiler;

    /// <summary>Gives the linker map the exe's name; with no map from this link, an earlier build's <c>&lt;exe&gt;.map</c> is deleted so addr never reads it.</summary>
    internal static int MoveLinkMap(string outDir, string exe)
    {
        var linkMap = Path.Combine(outDir, "esharp-link.map");
        try
        {
            if (File.Exists(linkMap)) File.Move(linkMap, exe + ".map", true);
            else if (File.Exists(exe + ".map")) File.Delete(exe + ".map");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail("linker map: " + e.Message); }
        return 0;
    }

    internal static int Build(string[] args, out string? builtExe)
    {
        builtExe = null;
        string path = ".";
        string? outDir = null;
        bool emitOnly = false, allowFloat = false, debug = false, trace = false, fromSource = false;
        long? chipBudget = null;
        var target = Targets.M68k;
        string? cc = null, cflags = null, docker = null, toolchain = null, sysrootOption = null;
        bool anyVersion = false;
        LastLocalCompiler = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--emit-only": emitOnly = true; break;
                case "--allow-float": allowFloat = true; break;
                case "--debug": debug = true; break;
                case "--trace": trace = true; break;
                case "--from-source": fromSource = true; break;
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
                // An empty value counts as not given (a VS Code task passes --toolchain ${config:esharp.toolchain} as it is).
                case "--toolchain" when i + 1 < args.Length: toolchain = args[++i]; break;
                case "--sysroot" when i + 1 < args.Length: sysrootOption = args[++i]; break;
                case "--any-cc-version": anyVersion = true; break;
                case "--toolchain" or "--sysroot": return Fail($"{args[i]} needs a folder");
                default:
                    if (args[i].StartsWith("-")) return Fail($"unknown option '{args[i]}'");
                    path = args[i];
                    break;
            }
        }

        if (docker != null && !string.IsNullOrWhiteSpace(toolchain))
            return Fail("--docker and --toolchain both say where the compiler is: use one (--docker runs it in an image, --toolchain from a folder on this machine)");
        if (docker == "") docker = DefaultImage(target);
        if (SyncAssets(path) is { } assetError) return Fail(assetError);

        var files = CollectSources(path);
        if (files.Count == 0) return Fail($"no .cs files found in '{path}'");

        // E# library packages from the project's restore (obj/project.assets.json), compiled with the program.
        if (PackageStep.Load(path, out var packages) is { } packageError) return Fail(packageError);
        foreach (var w in packages.Warnings) Console.Error.WriteLine($"esharp: warning: {w}");
        foreach (var line in PackageStep.Describe(packages.Packages, target.Name)) Console.WriteLine(line);
        if (packages.AllowCopyleft)
            foreach (var p in packages.Packages.Where(p => Licenses.IsCopyleft(p.License)))
                Console.Error.WriteLine($"esharp: warning: package {p.Id} {p.Version} is under {p.License}, compiled into the program (allowed by EsAllowCopyleft)");

        var baseDir = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var sources = files.Select(f => new SourceFile(Path.GetRelativePath(Directory.GetCurrentDirectory(), f), File.ReadAllText(f))).ToList();
        var name = Path.GetFileNameWithoutExtension(files[0]);
        if (Directory.Exists(path)) name = new DirectoryInfo(Path.GetFullPath(path)).Name;
        // cFileName: the #line directives after user functions point back at the C file by this name.
        var options = target.ToOptions(allowFloat, debug, chipBudget, trace, cFileName: name + ".c",
            packages: packages.Packages, allowCopyleft: packages.AllowCopyleft, sdkVersion: SourceTree.ToolVersion);
        // Precompiled package binaries (issue #154): linked when one was built by this E# for the target and this build's configuration.
        var result = EsCompiler.Compile(sources, new CompileOptions
        {
            Defines = options.Defines, AllowFloat = options.AllowFloat, BoundsChecks = options.BoundsChecks, ChipBudget = options.ChipBudget,
            CFileName = options.CFileName, TargetName = options.TargetName, Packages = options.Packages, AllowCopyleft = options.AllowCopyleft,
            SdkVersion = options.SdkVersion, UseBinaries = !fromSource, Stamp = PackageBinaries.Stamp(SourceTree.ToolVersion),
        });

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
        var cFile = Path.Combine(outDir, name + ".c");
        File.WriteAllText(cFile, result.C!);
        Console.WriteLine($"Wrote {cFile}");
        // The packages' C (esharp-native/<id>/), written fresh each build so a file a package no longer has is not compiled.
        var nativeSources = new List<string>();
        try
        {
            var nativeDir = Path.Combine(outDir, PackageNatives.OutputFolder);
            if (Directory.Exists(nativeDir)) Directory.Delete(nativeDir, true);
            foreach (var n in result.Native)
            {
                var file = Path.Combine(outDir, n.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, n.Text);
                if (n.Compile) nativeSources.Add(n.Path);   // relative, with '/': the same in the container and here
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail("package C: " + e.Message); }
        if (nativeSources.Count > 0) Console.WriteLine($"Wrote the packages' C into {Path.Combine(outDir, PackageNatives.OutputFolder)} ({nativeSources.Count} C file(s), compiled with the program)");
        // The precompiled binaries the program links, copied next to its C (a Docker build sees only the output folder).
        var archives = new List<string>();
        try
        {
            var binDir = Path.Combine(outDir, PackageBinaries.FolderName);
            if (Directory.Exists(binDir)) Directory.Delete(binDir, true);
            foreach (var b in result.Linked)
            {
                var rel = PackageBinaries.FolderName + "/" + b.Id + "/" + PackageBinaries.ArchiveName;
                var file = Path.Combine(outDir, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.Copy(b.Archive!, file, true);
                archives.Add(rel);
                Console.WriteLine($"Package {b.Id} {b.Version}: linked from its precompiled {b.BinaryTarget} binary ({b.Objects.Count} object(s){(b.Generics.Count > 0 ? $", {b.Generics.Count} generic copies" : "")}); its sources are not compiled");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail("package binary: " + e.Message); }
        if (emitOnly) return 0;

        var userCc = cc != null;
        string? sysroot;
        if (docker == null)
        {
            // A compiler on this machine: --toolchain, ESHARP_TOOLCHAIN_<TARGET>, ESHARP_TOOLCHAIN, else PATH (Toolchain.cs).
            if (Toolchain.Resolve(target, toolchain, sysrootOption, Environment.GetEnvironmentVariable, out var choice, cc) is { } tcError)
                return Fail(tcError);
            cc = choice!.Compiler;
            sysroot = choice.Sysroot;
            if (!userCc && !anyVersion && Toolchain.VersionProblem(target, cc, Toolchain.ReadVersion(cc)) is { } versionError)
                return Fail(versionError);
            if (choice.Source != "PATH") Console.WriteLine($"Compiler {cc} (from {choice.Source})");
            LastLocalCompiler = cc;
        }
        else
        {
            if (anyVersion) Console.Error.WriteLine("esharp: warning: --any-cc-version ignored with --docker (the image's compiler is used)");
            sysroot = string.IsNullOrWhiteSpace(sysrootOption) ? (target.Sysroot.Length > 0 ? target.Sysroot : null) : sysrootOption;
        }
        cc ??= target.Compiler;
        // --cflags replaces the target's flags; a --sysroot in them wins over the toolchain's.
        if (cflags != null && cflags.Contains("--sysroot")) sysroot = null;
        cflags ??= target.CFlags;
        var exe = Path.Combine(outDir, name);

        // The compiler runs in the output directory with relative file names, inside the container (mounted at /work) or here,
        // so a local build passes the compiler the same command line as a Docker build (and gives the same bytes).
        // withNative: the packages' C files are compiled and linked with the program's (each its own translation unit). Not for the
        // -c object that esharp addr reads (one source file per -c -o): package C lines are not in its line table.
        int Compile(string[] flags, string outFile, bool link, bool withNative = true)
        {
            var compilerArgs = new List<string>();
            if (sysroot != null) compilerArgs.Add("--sysroot=" + sysroot);
            compilerArgs.AddRange(cflags.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            compilerArgs.AddRange(flags);
            // -fno-common: a variable two packages both define (a name a macro made, which ES0120 does not see) is a link error,
            // not one merged variable (the pinned m68k gcc 6.5 defaults to -fcommon).
            var native = withNative && (nativeSources.Count > 0 || archives.Count > 0);
            if (native) compilerArgs.Add(PackageNatives.CFlags);
            compilerArgs.AddRange(new[] { "-o", outFile, name + ".c" });
            if (native) compilerArgs.AddRange(nativeSources);
            // The packages' binaries after the program's C (the linker takes from an archive what the objects before it need, and the
            // archives' own references to the platform code are already defined by then), before the system libraries.
            if (withNative && link) compilerArgs.AddRange(archives);
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
                else psi.WorkingDirectory = Path.GetFullPath(outDir);
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(line);
            }
            else if (docker == null)
            {
                psi = new ProcessStartInfo(cc) { UseShellExecute = false, WorkingDirectory = Path.GetFullPath(outDir) };
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
                // 125: docker itself failed, usually an image that is not there (esharp-amiga-toolchain and esharp-sdl are built locally).
                if (proc.ExitCode == 125 && docker != null)
                    return Fail($"docker could not run image '{docker}' (exit code 125). esharp-amiga-toolchain, esharp-sdl and esharp-aros-i386 are built from docker/, docker/sdl and docker/aros-i386 in the E# repository (https://github.com/GameGyrafe/amigaesharp); for m68k you can also pass --docker {ProjectTemplate.DefaultImage}");
                if (proc.ExitCode != 0) return Fail($"{cc} failed with exit code {proc.ExitCode}");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Fail($"'{psi.FileName}' not found. Install {cc} (see docs/design/TOOLCHAINS.md), pass --docker, --toolchain <dir>, or use --emit-only.");
            }
            return 0;
        }

        // For 'esharp addr' (docs/DEBUGGING.md). m68k: the linker map says where each section of the program went in the hunks,
        // and the same C compiled with -g into <name>.o gives the line table (the hunk exe's own debug data is not readable by the
        // binutils); -fno-reorder-functions keeps all code in one section of the object, so its line table has one address space.
        // ELF targets: <name>.debug is the same program linked with -g (gcc's code does not change with -g).
        var addrFlags = target.IsM68k ? new[] { "-fno-reorder-functions" } : Array.Empty<string>();
        // The map has a fixed name while linking (a comma in the project name would split the -Wl option), then takes the exe's.
        if (Compile(target.IsM68k ? addrFlags.Append("-Wl,-Map,esharp-link.map").ToArray() : addrFlags, name, link: true) != 0) return 1;
        if (target.IsM68k && MoveLinkMap(outDir, exe) != 0) return 1;
        if (target.IsM68k && Compile(addrFlags.Concat(new[] { "-g", "-c" }).ToArray(), name + ".o", link: false, withNative: false) != 0) return 1;
        if (target.IsAmiga && !target.IsM68k && Compile(new[] { "-g", "-gdwarf-4" }, name + ".debug", link: true) != 0) return 1;   // DWARF 4: the os4 binutils read no 5

        Console.WriteLine($"Built {exe}");
        try
        {
            if (IconStep.Write(Directory.Exists(path) ? path : null, outDir, name, target.IsAmiga) is { } icon) Console.WriteLine($"Wrote {icon}");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or ArgumentException)
        {
            return Fail("icon: " + e.Message);
        }
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
        string? programArgs = null;
        string? muiOption = null, workbenchOption = null;
        var buildArgs = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless": headless = true; break;
                case "--mui" when i + 1 < args.Length: muiOption = args[++i]; break;
                case "--workbench" when i + 1 < args.Length: workbenchOption = args[++i]; break;
                case "--args" when i + 1 < args.Length: programArgs = args[++i]; break;
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
        if ((muiOption ?? workbenchOption) != null && targetName != "m68k") return Fail(MuiTargetError(targetName));
        if (string.Equals(targetName, "sdl", StringComparison.OrdinalIgnoreCase))
        {
            var sdlRc = Build(buildArgs.ToArray(), out var sdlExe);
            if (sdlRc != 0 || sdlExe == null) return sdlRc;
            var (exit, _) = SdlRun.Run(sdlExe, headless, TimeSpan.FromSeconds(timeout), DockerImage(buildArgs, Targets.Sdl), screenshot, input,
                programArgs: programArgs == null ? null : SdlRun.SplitCommandLine(programArgs));
            if (exit != 0) Console.Error.WriteLine($"esharp: the program exited with code {exit}");
            return exit;
        }
        if (string.Equals(targetName, "os4", StringComparison.OrdinalIgnoreCase))
        {
            if (Os4Vm.ProgramArgsError(programArgs) is { } os4ArgsError) return Fail(os4ArgsError);
            var os4Rc = Build(buildArgs.ToArray(), out var os4Exe);
            if (os4Rc != 0 || os4Exe == null) return os4Rc;
            var (exit, _) = Os4Vm.Run(os4Exe, TimeSpan.FromSeconds(timeout), DockerImage(buildArgs, Targets.Os4), screenshot, serial, input: input, programArgs: programArgs, localCc: LastLocalCompiler);
            return exit;
        }
        if (targetName != "m68k")
            return Fail("run uses FS-UAE, which emulates 68k Amigas only, an AmigaOS 4.1 QEMU VM (--target os4, ESHARP_OS4_VM), or SDL on this machine; use --target m68k, os4 or sdl" +
                (targetName is "aros" or "aros-i386" ? " (AROS: esharp test --target " + targetName + " runs it on hosted AROS; tools/aros-hosted/aros-hosted.sh in the E# repository adds screenshots and input)" : ""));

        if (kickstart != null)
        {
            kickstart = Path.GetFullPath(kickstart.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            if (!File.Exists(kickstart)) return Fail($"Kickstart ROM not found: {kickstart}");
        }

        if (MuiDrawer(muiOption, workbenchOption, out var muiDrawer, out var workbench) is { } muiError) return Fail(muiError);
        var rc = Build(buildArgs.ToArray(), out var exe);
        if (rc != 0 || exe == null) return rc;
        if (Os4Vm.ProgramArgsError(programArgs) is { } argsError) return Fail(argsError);
        return FsUae.Run(exe, headless, TimeSpan.FromSeconds(timeout), kickstart, model, fsuae, screenshot, input, serial, programArgs, muiDrawer, workbench);
    }

    private static string MuiTargetError(string target) => target switch
    {
        "os4" => "--mui is for the m68k FS-UAE path only: the AmigaOS 4.1 VM has MUI 5 installed (ESHARP_OS4_VM)",
        "aros" or "aros-i386" => "--mui is for the m68k FS-UAE path only: AROS has Zune, its own MUI implementation",
        _ => $"--mui is for the m68k FS-UAE path only (target {target})",
    };

    /// <summary>The MUI drawer for <c>--mui</c> (or ESHARP_MUI) from the cache: null drawer with null error when MUI is not asked for.</summary>
    private static string? MuiDrawer(string? option, string? workbenchOption, out string? drawer, out string? workbench)
    {
        drawer = workbench = null;
        var text = option ?? Environment.GetEnvironmentVariable("ESHARP_MUI");
        if (string.IsNullOrEmpty(text))
            return workbenchOption != null ? "--workbench is only used with --mui" : null;
        var version = Mui.ParseVersion(text);
        if (version == null) return $"--mui takes 3.8 or 5, not '{text}'";
        drawer = Mui.Resolve(version, Mui.CacheRoot(), out var error);
        if (error != null) return error;
        workbench = Mui.ResolveWorkbench(workbenchOption, out error);
        return error;
    }

    /// <summary>The image a build's <c>--docker [image]</c> option names, or null without the option.</summary>
    private static string? DockerImage(IList<string> buildArgs, TargetInfo target)
    {
        var i = buildArgs.IndexOf("--docker");
        if (i < 0) return null;
        return i + 1 < buildArgs.Count && !buildArgs[i + 1].StartsWith("-") ? buildArgs[i + 1] : DefaultImage(target);
    }

    /// <summary>
    /// The image <c>--docker</c> without a name uses: esharp-sdl for the sdl target, esharp-aros-i386 for aros-i386 (its own image,
    /// docker/aros-i386: the i386 AROS toolchain is built from the AROS sources), else the Amiga toolchain.
    /// </summary>
    internal static string DefaultImage(TargetInfo target) =>
        target == Targets.ArosI386 ? "esharp-aros-i386" : target.IsAmiga ? "esharp-amiga-toolchain" : "esharp-sdl";

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
        // obj/ and bin/ below the project folder only: a project that itself lies under some obj/ folder keeps its sources.
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
            .Where(f => sep + Path.GetRelativePath(path, f) is var rel && !rel.Contains($"{sep}obj{sep}") && !rel.Contains($"{sep}bin{sep}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }
}

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
  esharp run [path] [options]       Build for m68k and run it in FS-UAE
  esharp assets [dir|assets.json]   Convert PNGs to planar chip data: writes Assets.g.cs next to the manifest
  esharp test                       Not implemented yet

Build options:
  --target <name>   m68k (default), os4, morphos or aros
  -o <dir>          Output directory (default: obj next to the sources)
  --emit-only       Stop after writing the C file
  --docker [image]  Run the C compiler inside the toolchain image (default: esharp-amiga-toolchain)
  --cc <compiler>   Override the target's C compiler
  --cflags <flags>  Override the target's C compiler flags
  --allow-float     Permit float/double
  --chip-budget <n> Fail if constant chip RAM reservations exceed n bytes (k/m suffix ok; 68k)
  --debug           Define DEBUG: bounds-checked arrays and spans, poisoned arena Reset

Run options (build options also apply; run is m68k only):
  --headless        No window: run on a virtual display, print the program's output, exit
  --timeout <sec>   Headless time limit (default 120; AROS needs ~20 s to boot)
  --kickstart <rom> Use a real Kickstart ROM instead of FS-UAE's built-in AROS replacement
                    (or set ESHARP_KICKSTART)
  --model <name>    Amiga model for FS-UAE: A1200 (default), A500, A500+, A600 (below the minimum spec)
  --fsuae <path>    FS-UAE executable (or set ESHARP_FSUAE)
  --screenshot <png> Headless only: when the program prints SHOT, save the emulator screen to a PNG (needs xwd)
  --input <script>  Headless only: after SHOT send mouse/keyboard input to the emulator (before the screenshot):
                    ""click X,Y;key 9"" with display pixels and X11 key codes (needs libXtst)

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
            "assets" => Assets(args.Skip(1).ToArray()),
            "test" => NotYet(args[0]),
            _ => Fail($"unknown command '{args[0]}'"),
        };
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"esharp: {message}");
        return 1;
    }

    private static int NotYet(string command)
    {
        Console.Error.WriteLine($"esharp: '{command}' is not implemented yet");
        return 2;
    }

    private static int New(string[] args)
    {
        if (args.Length != 1) return Fail("usage: esharp new <name>");
        var name = args[0];
        if (Directory.Exists(name)) return Fail($"'{name}' already exists");

        Directory.CreateDirectory(name);
        File.WriteAllText(Path.Combine(name, "Program.cs"), $@"using ESharp;
using ESharp.Platform;

namespace {name};

public static class Program
{{
    [EntryPoint]
    public static void Main()
    {{
        Dos.PutStr(""Hello from E#!\n"");
    }}
}}
");
        Console.WriteLine($"Created {name}/Program.cs. Build it with: esharp build {name}");
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
        bool emitOnly = false, allowFloat = false, debug = false;
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
                    docker = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[++i] : "esharp-amiga-toolchain";
                    break;
                case "--cc" when i + 1 < args.Length: cc = args[++i]; break;
                case "--cflags" when i + 1 < args.Length: cflags = args[++i]; break;
                default:
                    if (args[i].StartsWith("-")) return Fail($"unknown option '{args[i]}'");
                    path = args[i];
                    break;
            }
        }

        var files = CollectSources(path);
        if (files.Count == 0) return Fail($"no .cs files found in '{path}'");

        var baseDir = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var sources = files.Select(f => new SourceFile(Path.GetRelativePath(Directory.GetCurrentDirectory(), f), File.ReadAllText(f))).ToList();
        var result = EsCompiler.Compile(sources, target.ToOptions(allowFloat, debug, chipBudget));

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
        var compilerArgs = new List<string>();
        compilerArgs.AddRange(cflags.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        compilerArgs.AddRange(new[] { "-o", docker == null ? exe : name, docker == null ? cFile : name + ".c" });
        compilerArgs.AddRange(target.Libs.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        ProcessStartInfo psi;
        if (docker == null)
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

        Console.WriteLine($"Built {exe}");
        builtExe = exe;
        return 0;
    }

    private static int Assets(string[] args)
    {
        var path = args.Length > 0 ? args[0] : ".";
        var manifest = Directory.Exists(path) ? Path.Combine(path, "assets.json") : path;
        if (!File.Exists(manifest)) return Fail($"no assets.json at '{manifest}'");
        try
        {
            var output = ESharp.Assets.AssetBuilder.Build(manifest);
            var target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest))!, "Assets.g.cs");
            File.WriteAllText(target, output.Source);
            Console.WriteLine($"Wrote {target}");
            foreach (var r in output.Reports)
                Console.WriteLine($"  {r.Name,-16} {r.Width}x{r.Height}, {r.Depth} bitplanes, {r.ChipBytes:N0} bytes of chip RAM");
            Console.WriteLine($"  total            {output.Reports.Sum(r => r.ChipBytes):N0} bytes of chip RAM");
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or System.Text.Json.JsonException or KeyNotFoundException)
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
                default: buildArgs.Add(args[i]); break;
            }
        }

        var targetIndex = buildArgs.IndexOf("--target");
        if (targetIndex >= 0 && targetIndex + 1 < buildArgs.Count && buildArgs[targetIndex + 1] != "m68k")
            return Fail("run uses FS-UAE, which emulates 68k Amigas only; use --target m68k");
        if (buildArgs.Contains("--emit-only")) return Fail("--emit-only cannot be combined with run");

        if (kickstart != null)
        {
            kickstart = Path.GetFullPath(kickstart.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            if (!File.Exists(kickstart)) return Fail($"Kickstart ROM not found: {kickstart}");
        }

        var rc = Build(buildArgs.ToArray(), out var exe);
        if (rc != 0 || exe == null) return rc;
        return FsUae.Run(exe, headless, TimeSpan.FromSeconds(timeout), kickstart, model, fsuae, screenshot, input);
    }

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
